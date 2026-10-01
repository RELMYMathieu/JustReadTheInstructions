using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CameraAudio : MonoBehaviour
    {
        internal static CameraAudio Instance { get; private set; }

        private const float SourceRefreshSeconds = 0.25f;
        private const float MinPanDistance = 0.001f;
        private const long FirstOneShotId = (long)int.MaxValue + 1;

        private readonly CameraAudioMixer _mixer = new CameraAudioMixer();
        private readonly Dictionary<AudioClip, ClipPcm> _clips = new Dictionary<AudioClip, ClipPcm>();
        private readonly Dictionary<int, SoundSource> _sources = new Dictionary<int, SoundSource>();
        private readonly List<OneShot> _oneShots = new List<OneShot>();
        private readonly List<OneShotHook.Fired> _fired = new List<OneShotHook.Fired>();
        private readonly List<VoiceState> _voices = new List<VoiceState>();
        private readonly List<int> _staleSources = new List<int>();
        private readonly SonicBoomDetector _boomDetector = new SonicBoomDetector();
        private readonly List<BoomListener> _boomListeners = new List<BoomListener>();
        private readonly List<BoomHit> _boomHits = new List<BoomHit>();
        private readonly List<Boom> _booms = new List<Boom>();
        private float _refreshSourcesAt;
        private long _nextOneShotId = FirstOneShotId;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            _mixer.Start();
            OneShotHook.TryInstall();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            OneShotHook.Listening = false;
            _mixer.Dispose();
        }

        internal void Subscribe(int cameraId, IAudioSink sink) => _mixer.Subscribe(cameraId, sink);

        void LateUpdate()
        {
            var cameraIds = _mixer.ListenedCameraIds;
            OneShotHook.Listening = cameraIds.Length > 0;
            if (cameraIds.Length == 0)
            {
                _oneShots.Clear();
                _booms.Clear();
                return;
            }

            long start = JRTIPerf.Now();
            RefreshSources();
            TakeOneShots();
            var listeners = Listeners(cameraIds, out var cameras);
            if (!AudioListener.pause) DetectBooms(cameraIds, listeners);
            _mixer.Publish(BuildSnapshot(listeners, cameras));
            AudioPerf.RecordSnapshot(start, cameraIds.Length, _voices.Count);
        }

        private void RefreshSources()
        {
            if (Time.unscaledTime < _refreshSourcesAt) return;
            _refreshSourcesAt = Time.unscaledTime + SourceRefreshSeconds;

            foreach (var source in FindObjectsOfType<AudioSource>())
                EntryFor(source).LinkPlayerNeutralMinDistance();

            _staleSources.Clear();
            foreach (var kv in _sources)
                if (kv.Value.Source == null) _staleSources.Add(kv.Key);
            foreach (int id in _staleSources)
                _sources.Remove(id);
        }

        private SoundSource EntryFor(AudioSource source)
        {
            int id = source.GetInstanceID();
            if (!_sources.TryGetValue(id, out var entry))
                _sources[id] = entry = new SoundSource(source);
            return entry;
        }

        private void TakeOneShots()
        {
            _fired.Clear();
            OneShotHook.TakePending(_fired);
            foreach (var fired in _fired)
            {
                if (fired.Source == null) continue;
                var entry = EntryFor(fired.Source);
                var pcm = entry.FollowsPlayerCamera ? null : ReadClip(fired.Clip);
                if (pcm != null) _oneShots.Add(new OneShot(_nextOneShotId++, entry, fired.Clip, pcm, fired.VolumeScale, Time.unscaledTime));
            }
        }

        private void DetectBooms(int[] cameraIds, Listener[] listeners)
        {
            _boomListeners.Clear();
            for (int i = 0; i < listeners.Length; i++)
                if (listeners[i].View != null) _boomListeners.Add(new BoomListener(cameraIds[i], listeners[i].View.position, listeners[i].Vessel));

            _boomHits.Clear();
            _boomDetector.Update(_boomListeners, _boomHits);
            foreach (var hit in _boomHits)
            {
                var listener = listeners[Array.IndexOf(cameraIds, hit.CameraId)];
                float pan = PanOf(hit.SourcePosition - listener.View.position, hit.Distance, listener.View, 1f);
                foreach (var layer in RSEIntegration.SonicBoomLayers(hit.Source))
                {
                    var pcm = ReadClip(layer.Clips[UnityEngine.Random.Range(0, layer.Clips.Length)]);
                    float gain = layer.Gain(hit.Source.mach, hit.Distance) * GameSettings.SHIP_VOLUME;
                    if (pcm != null && gain > 0f)
                        _booms.Add(new Boom(_nextOneShotId++, hit.CameraId, pcm, SoundPaths.Boom(listener.Mic, gain, pan, listener.AirFactor), Time.unscaledTime));
                }
            }
        }

        private static Listener[] Listeners(int[] cameraIds, out CameraMix[] cameras)
        {
            var listeners = new Listener[cameraIds.Length];
            cameras = new CameraMix[cameraIds.Length];
            var manager = HullCameraManager.Instance;
            var server = JRTIStreamServer.Instance;

            for (int i = 0; i < cameraIds.Length; i++)
            {
                Transform view = null;
                Vessel vessel = null;
                if (manager != null) manager.TryGetListener(cameraIds[i], out view, out vessel);

                var sound = server != null ? server.SoundSettingsOf(cameraIds[i]) : SoundSettings.Default;
                listeners[i] = new Listener(cameraIds[i], view, vessel, sound.Mic);
                cameras[i] = new CameraMix(cameraIds[i], Mathf.Pow(10f, sound.GainDb / 20f), sound.AutoGain);
            }
            return listeners;
        }

        private AudioSnapshot BuildSnapshot(Listener[] listeners, CameraMix[] cameras)
        {
            bool paused = AudioListener.pause;
            float now = Time.unscaledTime;
            _voices.Clear();

            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                var shot = _oneShots[i];
                if (shot.Entry.Source == null)
                {
                    _oneShots.RemoveAt(i);
                    continue;
                }

                float pitch = shot.Entry.Pitch;
                double position = (now - shot.StartedAt) * pitch * shot.Pcm.Frequency;
                if (position >= shot.Pcm.Samples.Length)
                {
                    _oneShots.RemoveAt(i);
                    continue;
                }
                if (shot.Entry.CanBeHeard(paused))
                    AddVoice(shot.Id, shot.Entry, shot.Pcm, (int)position, pitch, false, shot.Entry.Volume * shot.VolumeScale, listeners);
            }

            for (int i = _booms.Count - 1; i >= 0; i--)
            {
                var boom = _booms[i];
                double position = (now - boom.StartedAt) * boom.Pcm.Frequency;
                if (position >= boom.Pcm.Samples.Length) _booms.RemoveAt(i);
                else if (!paused) _voices.Add(boom.VoiceAt(cameras, (int)position));
            }

            foreach (var entry in _sources.Values)
            {
                var source = entry.Source;
                if (source == null || !entry.IsPlayingClip(paused) || HasOneShot(entry, source.clip)) continue;
                var pcm = ReadClip(source.clip);
                if (pcm != null) AddVoice(entry.Id, entry, pcm, source.timeSamples, entry.Pitch, source.loop, entry.Volume, listeners);
            }

            return new AudioSnapshot(Stopwatch.GetTimestamp(), cameras, _voices.ToArray());
        }

        private bool HasOneShot(SoundSource entry, AudioClip clip)
        {
            foreach (var shot in _oneShots)
                if (shot.Entry == entry && ReferenceEquals(shot.Clip, clip)) return true;
            return false;
        }

        private void AddVoice(long id, SoundSource entry, ClipPcm pcm, int timeSamples, float pitch, bool loop, float volume, Listener[] listeners)
        {
            var emitter = new Emitter(entry, volume);
            var paths = new VoicePath[listeners.Length];
            for (int i = 0; i < listeners.Length; i++)
                paths[i] = emitter.PathTo(listeners[i], _boomDetector);
            _voices.Add(new VoiceState(id, pcm, timeSamples, pitch * pcm.Frequency, loop, paths));
        }

        private ClipPcm ReadClip(AudioClip clip)
        {
            if (_clips.TryGetValue(clip, out var pcm)) return pcm;

            bool readable = clip.loadType == AudioClipLoadType.DecompressOnLoad;
            if (readable && clip.loadState != AudioDataLoadState.Loaded) return null;

            pcm = (readable ? ClipPcm.Read(clip) : null) ?? GameDataSounds.ReadWav(clip.name);
            if (pcm == null)
            {
                AudioPerf.RecordSkippedSound();
                Debug.LogWarning($"[JRTI-Audio]: Skipping sound '{clip.name}': Unity keeps it compressed ({clip.loadType}) and no WAV file with that name is in GameData");
            }
            _clips[clip] = pcm;
            return pcm;
        }

        private static float PanOf(Vector3 offset, float distance, Transform view, float blend)
            => distance > MinPanDistance ? Vector3.Dot(offset, view.right) / distance * blend : 0f;

        private readonly struct Listener
        {
            public readonly int CameraId;
            public readonly Transform View;
            public readonly Vessel Vessel;
            public readonly CameraMic Mic;
            public readonly float AirFactor;
            public readonly float SpeedOfSound;

            public Listener(int cameraId, Transform view, Vessel vessel, CameraMic mic)
            {
                CameraId = cameraId;
                View = view;
                Vessel = vessel;
                Mic = mic;
                AirFactor = vessel != null ? SoundPaths.AirFactor(vessel.atmDensity) : 1f;
                SpeedOfSound = vessel != null ? (float)vessel.speedOfSound : 0f;
            }
        }

        private readonly struct Emitter
        {
            private readonly SoundSource _entry;
            private readonly Vector3 _position;
            private readonly float _volume;
            private readonly bool _interior;
            private readonly Vessel _vessel;
            private readonly float _airFactor;
            private readonly float _thrustKn;
            private readonly float _reach;

            public Emitter(SoundSource entry, float volume)
            {
                _entry = entry;
                _position = entry.Source.transform.position;
                _volume = volume;
                _interior = entry.IsInterior;
                _vessel = entry.Vessel;
                _airFactor = _vessel != null ? SoundPaths.AirFactor(_vessel.atmDensity) : 1f;
                _thrustKn = entry.ThrustKn;
                _reach = SoundPaths.Reach(_thrustKn);
            }

            public VoicePath PathTo(Listener listener, SonicBoomDetector booms)
            {
                if (listener.View == null) return VoicePath.Silent;

                var offset = _position - listener.View.position;
                float distance = _interior ? 0f : offset.magnitude;
                float blend = _entry.Source.spatialBlend;
                return SoundPaths.For(listener.Mic, new PathInputs
                {
                    Volume = _volume,
                    SpatialBlend = blend,
                    GameRolloff = _entry.GameRolloff(distance / _reach),
                    Distance = distance,
                    ThrustKn = _thrustKn,
                    Pan = PanOf(offset, distance, listener.View, blend) * (1f - _entry.Width(distance)),
                    AirFactor = _vessel != null ? Mathf.Min(listener.AirFactor, _airFactor) : listener.AirFactor,
                    SpeedOfSound = listener.SpeedOfSound,
                    SameVessel = _vessel != null && _vessel == listener.Vessel,
                    Interior = _interior,
                    Muted = _entry.Source.mute,
                    AheadOfShock = _vessel != null && booms.IsAheadOfShock(_vessel, listener.CameraId),
                });
            }
        }

        private sealed class Boom
        {
            public readonly long Id;
            public readonly int CameraId;
            public readonly ClipPcm Pcm;
            public readonly VoicePath Path;
            public readonly float StartedAt;

            public Boom(long id, int cameraId, ClipPcm pcm, VoicePath path, float startedAt)
            {
                Id = id;
                CameraId = cameraId;
                Pcm = pcm;
                Path = path;
                StartedAt = startedAt;
            }

            public VoiceState VoiceAt(CameraMix[] cameras, int timeSamples)
            {
                var paths = new VoicePath[cameras.Length];
                for (int i = 0; i < cameras.Length; i++)
                    paths[i] = cameras[i].CameraId == CameraId ? Path : VoicePath.Silent;
                return new VoiceState(Id, Pcm, timeSamples, Pcm.Frequency, false, paths);
            }
        }

        private sealed class OneShot
        {
            public readonly long Id;
            public readonly SoundSource Entry;
            public readonly AudioClip Clip;
            public readonly ClipPcm Pcm;
            public readonly float VolumeScale;
            public readonly float StartedAt;

            public OneShot(long id, SoundSource entry, AudioClip clip, ClipPcm pcm, float volumeScale, float startedAt)
            {
                Id = id;
                Entry = entry;
                Clip = clip;
                Pcm = pcm;
                VolumeScale = volumeScale;
                StartedAt = startedAt;
            }
        }

        private sealed class SoundSource
        {
            private const float MinDoppler = 0.01f;
            private const float MinRolloffDistance = 0.01f;
            private const float WidthPerSpreadUnit = 2f;

            public readonly AudioSource Source;
            public readonly int Id;
            public readonly bool FollowsPlayerCamera;
            private readonly Part _part;
            private readonly List<ModuleEngines> _engines;
            private readonly bool _inInternalSpace;
            private readonly RsePlayerEffects _rse;
            private readonly AnimationCurve _rolloffCurve;
            private readonly AnimationCurve _spreadCurve;
            private Func<float> _playerNeutralMinDistance;

            public SoundSource(AudioSource source)
            {
                Source = source;
                Id = source.GetInstanceID();
                var model = source.GetComponentInParent<InternalModel>();
                var part = source.GetComponentInParent<Part>();
                _inInternalSpace = model != null;
                _part = part != null ? part : _inInternalSpace ? model.part : null;
                _engines = _part != null ? _part.FindModulesImplementing<ModuleEngines>() : null;
                FollowsPlayerCamera = RSEIntegration.FollowsPlayerCamera(source);
                _rse = RSEIntegration.PlayerEffectsOf(source, _part);
                _rolloffCurve = source.rolloffMode == AudioRolloffMode.Custom ? source.GetCustomCurve(AudioSourceCurveType.CustomRolloff) : null;
                _spreadCurve = source.GetCustomCurve(AudioSourceCurveType.Spread);
                LinkPlayerNeutralMinDistance();
            }

            public void LinkPlayerNeutralMinDistance()
            {
                if (_playerNeutralMinDistance == null)
                    _playerNeutralMinDistance = RSEIntegration.PlayerNeutralMinDistanceOf(Source, _part);
            }

            public float GameRolloff(float distance)
            {
                float min = Mathf.Max(_playerNeutralMinDistance?.Invoke() ?? Source.minDistance, MinRolloffDistance);
                float max = Mathf.Max(Source.maxDistance, min);
                switch (Source.rolloffMode)
                {
                    case AudioRolloffMode.Linear: return max > min ? Mathf.Clamp01((max - distance) / (max - min)) : 1f;
                    case AudioRolloffMode.Custom: return _rolloffCurve != null ? _rolloffCurve.Evaluate(distance / max) : 1f;
                    default: return min / Mathf.Clamp(distance, min, max);
                }
            }

            public float Width(float distance)
                => Mathf.Clamp01(_spreadCurve.Evaluate(distance / Mathf.Max(Source.maxDistance, MinRolloffDistance)) * WidthPerSpreadUnit);

            public Vessel Vessel => _part != null ? _part.vessel : null;

            public float ThrustKn
            {
                get
                {
                    float thrust = 0f;
                    if (_engines != null)
                        foreach (var engine in _engines)
                            if (engine != null) thrust += engine.finalThrust;
                    return thrust;
                }
            }

            public bool IsInterior => _inInternalSpace || RSEIntegration.IsInterior(Source.outputAudioMixerGroup);

            public float Pitch
            {
                get
                {
                    float doppler = _rse?.Doppler ?? 1f;
                    return doppler > MinDoppler ? Source.pitch / doppler : Source.pitch;
                }
            }

            public float Volume => IsInterior || _rse == null ? Source.volume : Source.volume * _rse.VolumeCorrection;

            public bool CanBeHeard(bool listenerPaused)
                => Source.isActiveAndEnabled
                   && Source.spatialBlend > 0f
                   && (!Source.mute || IsInterior)
                   && (!listenerPaused || Source.ignoreListenerPause);

            public bool IsPlayingClip(bool listenerPaused)
                => Source.isPlaying && Source.clip != null && Source.volume > 0f && CanBeHeard(listenerPaused);
        }
    }
}
