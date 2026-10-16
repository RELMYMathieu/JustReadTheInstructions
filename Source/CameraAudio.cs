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
        private const float MinVoiceVolume = 0.0001f;
        private const float MaxBoomControlMach = 4f;
        private const float BoomPitchJitter = 0.04f;
        private const float MinBoomPitch = 0.7f;
        private const float MaxBoomPitch = 1.3f;
        private const long FirstOneShotId = (long)int.MaxValue + 1;
        private const long FirstShipLayerId = 1L << 48;
        private static readonly AirSimProfile ShipEffectsAirSim = new AirSimProfile(0f, 1f, 0f);

        private readonly CameraAudioMixer _mixer = new CameraAudioMixer();
        private readonly Dictionary<AudioClip, ClipPcm> _clips = new Dictionary<AudioClip, ClipPcm>();
        private readonly Dictionary<int, SoundSource> _sources = new Dictionary<int, SoundSource>();
        private readonly List<OneShot> _oneShots = new List<OneShot>();
        private readonly List<SoundHooks.Fired> _fired = new List<SoundHooks.Fired>();
        private readonly List<VoiceState> _voices = new List<VoiceState>();
        private readonly List<int> _staleSources = new List<int>();
        private readonly SonicBoomDetector _boomDetector = new SonicBoomDetector();
        private readonly List<BoomListener> _boomListeners = new List<BoomListener>();
        private readonly List<BoomHit> _boomHits = new List<BoomHit>();
        private readonly List<Boom> _booms = new List<Boom>();
        private readonly VesselLengths _lengths = new VesselLengths();
        private readonly Dictionary<long, double> _loopPositions = new Dictionary<long, double>();
        private readonly HashSet<long> _liveLoops = new HashSet<long>();
        private readonly List<long> _endedLoops = new List<long>();
        private readonly Queue<AudioClip> _clipsToCapture = new Queue<AudioClip>();
        private readonly List<AudioSource> _createdSources = new List<AudioSource>();
        private readonly EngineModeSwitches _engineSwitches = new EngineModeSwitches();
        private ClipCapture _capture;
        private float _refreshSourcesAt;
        private long _nextOneShotId = FirstOneShotId;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            _mixer.Start();
            SoundHooks.TryInstall();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            SoundHooks.Listening = false;
            if (_capture != null) Destroy(_capture.gameObject);
            _mixer.Dispose();
        }

        internal void Subscribe(int cameraId, IAudioSink sink) => _mixer.Subscribe(cameraId, sink);

        void LateUpdate()
        {
            var cameraIds = _mixer.ListenedCameraIds;
            SoundHooks.Listening = cameraIds.Length > 0;
            if (cameraIds.Length == 0)
            {
                _oneShots.Clear();
                _booms.Clear();
                _loopPositions.Clear();
                return;
            }

            long start = JRTIPerf.Now();
            RefreshSources();
            TakeCreatedSources();
            CaptureCompressedClips();
            TakeOneShots();
            _engineSwitches.Update();
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

            foreach (var effect in FindObjectsOfType<AudioFX>())
            {
                var clip = string.IsNullOrEmpty(effect.clip) ? null : GameDatabase.Instance.GetAudioClip(effect.clip);
                if (clip != null && clip.loadType != AudioClipLoadType.DecompressOnLoad) ReadClip(clip);
            }

            _staleSources.Clear();
            foreach (var kv in _sources)
                if (kv.Value.Source == null) _staleSources.Add(kv.Key);
            foreach (int id in _staleSources)
                _sources.Remove(id);
            _engineSwitches.Forget();
        }

        private void TakeCreatedSources()
        {
            _createdSources.Clear();
            SoundHooks.TakeCreated(_createdSources);
            foreach (var source in _createdSources)
                if (source != null) EntryFor(source).LinkPlayerNeutralMinDistance();
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
            SoundHooks.TakePending(_fired);
            foreach (var fired in _fired)
            {
                if (fired.Source == null) continue;
                var entry = EntryFor(fired.Source);
                var pcm = entry.ReplacedByJrti ? null : ReadClip(fired.Clip);
                if (pcm != null) _oneShots.Add(new OneShot(_nextOneShotId++, entry, fired.Clip, pcm, fired.VolumeScale, Time.unscaledTime));
            }
        }

        private void DetectBooms(int[] cameraIds, Listener[] listeners)
        {
            _boomListeners.Clear();
            for (int i = 0; i < listeners.Length; i++)
                if (listeners[i].View != null) _boomListeners.Add(new BoomListener(cameraIds[i], listeners[i].View.position, listeners[i].Vessel));

            _boomHits.Clear();
            _boomDetector.Update(_boomListeners, _boomHits, Time.unscaledTime);
            float exterior = RSEIntegration.ExteriorVolume() * GameSettings.SHIP_VOLUME;
            foreach (var hit in _boomHits)
            {
                var source = hit.Source;
                if (!HasShipSounds(source)) continue;
                var listener = listeners[Array.IndexOf(cameraIds, hit.CameraId)];
                float pan = PanOf(hit.SourcePosition - listener.View.position, hit.Distance, listener.View, 1f);
                float control = Mathf.Min(RSEIntegration.EffectiveMach(source), MaxBoomControlMach);
                float mass = (float)source.totalMass;
                float length = _lengths.Of(source);
                float stretch = SoundPaths.BoomStretch(length, hit.Distance);
                float spacing = SoundPaths.ShockSpacing(length, (float)(source.srfSpeed / hit.SpeedOfSound), hit.Distance, hit.SpeedOfSound);
                int shocks = SoundPaths.ShockCount(length);
                float arrival = Time.unscaledTime + hit.TravelSeconds;
                bool physicalMic = listener.Mic != CameraMic.Game;
                var layers = RSEIntegration.ShipLayers(source, RSEIntegration.SonicBoomGroup);

                for (int shock = 0; shock < shocks; shock++)
                {
                    float jitter = UnityEngine.Random.Range(1f - BoomPitchJitter, 1f + BoomPitchJitter);
                    float startsAt = arrival + SoundPaths.ShockDelay(shock, spacing);
                    foreach (var layer in layers)
                    {
                        var pcm = ReadClip(layer.Clips[UnityEngine.Random.Range(0, layer.Clips.Length)]);
                        float gain = layer.Volume(control, mass) * layer.Rolloff(hit.Distance, physicalMic) * exterior * SoundPaths.ShockGain(shock);
                        if (pcm == null || gain <= MinVoiceVolume) continue;

                        var path = SoundPaths.Boom(listener.Mic, gain, pan, listener.AirFactor);
                        float pitch = Mathf.Clamp(layer.Pitch(control, mass) / stretch, MinBoomPitch, MaxBoomPitch) * jitter;
                        _booms.Add(new Boom(_nextOneShotId++, hit.CameraId, pcm, pitch, path, startsAt));
                    }
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
                cameras[i] = new CameraMix(cameraIds[i], Mathf.Pow(10f, sound.GainDb / 20f), sound.AutoGain, sound.Mastering);
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
                    AddVoice(shot.Id, Emitter.Of(shot.Entry, shot.Entry.Volume * shot.VolumeScale), shot.Pcm, (int)position, pitch, false, listeners);
            }

            for (int i = _booms.Count - 1; i >= 0; i--)
            {
                var boom = _booms[i];
                double position = (now - boom.StartsAt) * boom.Pcm.Frequency * boom.Pitch;
                if (position >= boom.Pcm.Samples.Length) _booms.RemoveAt(i);
                else if (position >= 0 && !paused) _voices.Add(boom.VoiceAt(cameras, (int)position));
            }

            foreach (var entry in _sources.Values)
            {
                var source = entry.Source;
                if (source == null || entry.ReplacedByJrti || _engineSwitches.Mutes(source)) continue;
                if (_engineSwitches.Bridges(source, out float gain))
                {
                    var held = entry.Heard.HasValue && !paused ? ReadClip(source.clip) : null;
                    if (held != null)
                    {
                        var heard = entry.Heard.Value;
                        AddVoice(entry.Id, Emitter.Of(entry, heard.Volume * gain), held, heard.TimeSamplesAt(now, held), heard.Pitch, true, listeners);
                    }
                    continue;
                }
                if (!entry.IsPlayingClip(paused) || HasOneShot(entry, source.clip)) continue;
                var pcm = ReadClip(source.clip);
                if (pcm == null) continue;
                entry.Heard = new HeardState(source.timeSamples, entry.Pitch, entry.Volume, now);
                AddVoice(entry.Id, Emitter.Of(entry, entry.Volume), pcm, source.timeSamples, entry.Pitch, source.loop, listeners);
            }

            if (!paused) AddReentryVoices(listeners);

            long ticks = Stopwatch.GetTimestamp();
            return new AudioSnapshot(ticks, PhysicsStepTicks(ticks), cameras, _voices.ToArray(), RSEIntegration.Mastering());
        }

        private static long PhysicsStepTicks(long now)
        {
            if (Time.timeScale <= 0f) return now;
            double secondsSinceStep = Time.realtimeSinceStartup - Time.unscaledTime + (Time.time - Time.fixedTime) / Time.timeScale;
            return now - (long)(secondsSinceStep * Stopwatch.Frequency);
        }

        private void AddReentryVoices(Listener[] listeners)
        {
            _liveLoops.Clear();
            float exterior = RSEIntegration.ExteriorVolume() * GameSettings.SHIP_VOLUME;
            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (!HasShipSounds(vessel)) continue;
                var layers = RSEIntegration.ShipLayers(vessel, RSEIntegration.ReentryHeatGroup);
                if (layers.Count == 0) continue;

                float control = AeroHeat.ReentryControl(vessel);
                if (control <= 0f) continue;

                float mass = (float)vessel.totalMass;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    float volume = layer.Volume(control, mass) * exterior;
                    var pcm = volume > MinVoiceVolume ? ReadClip(layer.Clips[(int)(vessel.persistentId % (uint)layer.Clips.Length)]) : null;
                    if (pcm == null) continue;

                    long id = FirstShipLayerId + ((long)vessel.persistentId << 4) + i;
                    float pitch = layer.Pitch(control, mass);
                    _liveLoops.Add(id);
                    var emitter = new Emitter(layer, (Vector3)vessel.CoMD, vessel, volume, 1f, false, false, 0f, false, ShipEffectsAirSim);
                    AddVoice(id, emitter, pcm, LoopPosition(id, pcm, pitch, vessel.persistentId), pitch, true, listeners);
                }
            }

            _endedLoops.Clear();
            foreach (long id in _loopPositions.Keys)
                if (!_liveLoops.Contains(id)) _endedLoops.Add(id);
            foreach (long id in _endedLoops)
                _loopPositions.Remove(id);
        }

        private int LoopPosition(long id, ClipPcm pcm, float pitch, uint seed)
        {
            int length = pcm.Samples.Length;
            if (!_loopPositions.TryGetValue(id, out double position))
                position = seed * 2654435761u % (uint)Math.Max(length, 1);
            position = (position + Time.unscaledDeltaTime * pitch * pcm.Frequency) % Math.Max(length, 1);
            _loopPositions[id] = position;
            return (int)position;
        }

        private static bool HasShipSounds(Vessel vessel)
            => vessel != null && vessel.loaded && !vessel.packed && vessel.Parts.Count > 1
               && vessel.vesselType != VesselType.Debris && vessel.vesselType != VesselType.SpaceObject && vessel.vesselType != VesselType.DroppedPart;

        private bool HasOneShot(SoundSource entry, AudioClip clip)
        {
            foreach (var shot in _oneShots)
                if (shot.Entry == entry && ReferenceEquals(shot.Clip, clip)) return true;
            return false;
        }

        private void AddVoice(long id, Emitter emitter, ClipPcm pcm, int timeSamples, float pitch, bool loop, Listener[] listeners)
        {
            var paths = new VoicePath[listeners.Length];
            float now = Time.unscaledTime;
            for (int i = 0; i < listeners.Length; i++)
                paths[i] = emitter.PathTo(listeners[i], _boomDetector, now);
            _voices.Add(new VoiceState(id, pcm, timeSamples, pitch * pcm.Frequency, loop, false, paths));
        }

        private ClipPcm ReadClip(AudioClip clip)
        {
            if (_clips.TryGetValue(clip, out var pcm)) return pcm;

            bool readable = clip.loadType == AudioClipLoadType.DecompressOnLoad;
            if (readable && clip.loadState != AudioDataLoadState.Loaded) return null;

            pcm = (readable ? ClipPcm.Read(clip) : null) ?? GameDataSounds.ReadWav(clip.name);
            if (pcm == null && !readable) _clipsToCapture.Enqueue(clip);
            else if (pcm == null) SkipSound(clip, "Unity could not read it");
            _clips[clip] = pcm;
            return pcm;
        }

        private void CaptureCompressedClips()
        {
            if (_capture != null)
            {
                if (!_capture.IsDone) return;
                var clip = _capture.Clip;
                var pcm = _capture.Finish();
                _capture = null;
                if (pcm == null) SkipSound(clip, $"Unity keeps it compressed ({clip.loadType}) and capturing it failed");
                else
                {
                    _clips[clip] = pcm;
                    Debug.Log($"[JRTI-Audio]: Captured compressed sound '{clip.name}' ({pcm.Samples.Length / (float)pcm.Frequency:F1} s)");
                }
            }
            if (_clipsToCapture.Count > 0) _capture = ClipCapture.Begin(_clipsToCapture.Dequeue());
        }

        private static void SkipSound(AudioClip clip, string reason)
        {
            AudioPerf.RecordSkippedSound();
            Debug.LogWarning($"[JRTI-Audio]: Skipping sound '{clip.name}': {reason}");
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
            private const double MinHeadingSpeed = 1.0;

            private readonly IEmitterShape _shape;
            private readonly Vector3 _position;
            private readonly Vessel _vessel;
            private readonly float _volume;
            private readonly float _spatialBlend;
            private readonly bool _interior;
            private readonly bool _muted;
            private readonly float _thrustKn;
            private readonly bool _loudnessScalesWithThrust;
            private readonly float _reach;
            private readonly AirSimProfile _airSim;
            private readonly float _airFactor;
            private readonly float _speedOfSound;
            private readonly Vector3 _heading;
            private readonly float _mach;

            public Emitter(IEmitterShape shape, Vector3 position, Vessel vessel, float volume, float spatialBlend, bool interior, bool muted,
                float thrustKn, bool loudnessScalesWithThrust, AirSimProfile airSim)
            {
                _shape = shape;
                _position = position;
                _vessel = vessel;
                _volume = volume;
                _spatialBlend = spatialBlend;
                _interior = interior;
                _muted = muted;
                _thrustKn = thrustKn;
                _loudnessScalesWithThrust = loudnessScalesWithThrust;
                _reach = SoundPaths.Reach(thrustKn, loudnessScalesWithThrust);
                _airSim = airSim;
                _airFactor = vessel != null ? SoundPaths.AirFactor(vessel.atmDensity) : 1f;
                _speedOfSound = vessel != null ? (float)vessel.speedOfSound : 0f;
                bool moving = vessel != null && vessel.srfSpeed > MinHeadingSpeed;
                _heading = moving ? ((Vector3)vessel.srf_velocity).normalized : Vector3.zero;
                _mach = moving ? RSEIntegration.EffectiveMach(vessel) : 0f;
            }

            public static Emitter Of(SoundSource entry, float volume)
                => new Emitter(entry, entry.Source.transform.position, entry.Vessel, volume, entry.Source.spatialBlend, entry.IsInterior,
                    entry.Source.mute, entry.ThrustKn, entry.LoudnessScalesWithThrust, entry.AirSim);

            public VoicePath PathTo(Listener listener, SonicBoomDetector shocks, float now)
            {
                if (listener.View == null) return VoicePath.Silent;

                var offset = _position - listener.View.position;
                float separation = offset.magnitude;
                float distance = _interior ? 0f : separation;
                float viewAngle = _heading == Vector3.zero || separation < MinPanDistance
                    ? 90f
                    : SoundPaths.ViewAngleDegrees(Vector3.Dot(-offset / separation, _heading));
                return SoundPaths.For(listener.Mic, new PathInputs
                {
                    Volume = _volume,
                    SpatialBlend = _spatialBlend,
                    GameRolloff = _shape.GameRolloff(distance / _reach),
                    Distance = distance,
                    ThrustKn = _thrustKn,
                    LoudnessScalesWithThrust = _loudnessScalesWithThrust,
                    Pan = PanOf(offset, distance, listener.View, _spatialBlend) * (1f - _shape.Width(distance)),
                    AirFactor = _vessel != null ? Mathf.Min(listener.AirFactor, _airFactor) : listener.AirFactor,
                    SpeedOfSound = SoundPaths.SpeedOfSoundBetween(_speedOfSound, listener.SpeedOfSound),
                    SameVessel = _vessel != null && _vessel == listener.Vessel,
                    Interior = _interior,
                    Muted = _muted,
                    AirSim = _airSim,
                    Behind = 1f - viewAngle / 180f,
                    AheadOfCone = SoundPaths.AheadOfCone(viewAngle, _mach),
                    Mach = _mach,
                    AheadOfShock = _vessel != null && shocks.Silences(_vessel, listener.CameraId, now),
                });
            }
        }

        private sealed class Boom
        {
            public readonly long Id;
            public readonly int CameraId;
            public readonly ClipPcm Pcm;
            public readonly float Pitch;
            public readonly VoicePath Path;
            public readonly float StartsAt;

            public Boom(long id, int cameraId, ClipPcm pcm, float pitch, VoicePath path, float startsAt)
            {
                Id = id;
                CameraId = cameraId;
                Pcm = pcm;
                Pitch = pitch;
                Path = path;
                StartsAt = startsAt;
            }

            public VoiceState VoiceAt(CameraMix[] cameras, int timeSamples)
            {
                var paths = new VoicePath[cameras.Length];
                for (int i = 0; i < cameras.Length; i++)
                    paths[i] = cameras[i].CameraId == CameraId ? Path : VoicePath.Silent;
                return new VoiceState(Id, Pcm, timeSamples, Pcm.Frequency * Pitch, false, true, paths);
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

        private readonly struct HeardState
        {
            public readonly int TimeSamples;
            public readonly float Pitch;
            public readonly float Volume;
            public readonly float At;

            public HeardState(int timeSamples, float pitch, float volume, float at)
            {
                TimeSamples = timeSamples;
                Pitch = pitch;
                Volume = volume;
                At = at;
            }

            public int TimeSamplesAt(float now, ClipPcm pcm)
                => (int)((TimeSamples + (now - At) * Pitch * pcm.Frequency) % Math.Max(pcm.Samples.Length, 1));
        }

        private sealed class SoundSource : IEmitterShape
        {
            private const float MinDoppler = 0.01f;
            private const float MinRolloffDistance = 0.01f;
            private const float WidthPerSpreadUnit = 2f;

            public readonly AudioSource Source;
            public readonly int Id;
            public readonly bool ReplacedByJrti;
            public HeardState? Heard;
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
                ReplacedByJrti = RSEIntegration.IsReplacedByJrti(source);
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

            public AirSimProfile AirSim => _rse?.AirSim ?? AirSimProfile.Full;

            public bool LoudnessScalesWithThrust => _rse != null && _rse.LoudnessScalesWithThrust;

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
