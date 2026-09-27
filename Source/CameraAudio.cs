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

        private readonly CameraAudioMixer _mixer = new CameraAudioMixer();
        private readonly Dictionary<AudioClip, ClipPcm> _clips = new Dictionary<AudioClip, ClipPcm>();
        private readonly List<VoiceState> _voices = new List<VoiceState>();
        private AudioSource[] _sources = new AudioSource[0];
        private float _refreshSourcesAt;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            _mixer.Start();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            _mixer.Dispose();
        }

        internal void Subscribe(int cameraId, IAudioSink sink) => _mixer.Subscribe(cameraId, sink);

        void LateUpdate()
        {
            var cameraIds = _mixer.ListenedCameraIds();
            if (cameraIds.Length == 0) return;

            RefreshSources();
            _mixer.Publish(BuildSnapshot(cameraIds));
        }

        private void RefreshSources()
        {
            if (Time.unscaledTime < _refreshSourcesAt) return;
            _refreshSourcesAt = Time.unscaledTime + SourceRefreshSeconds;
            _sources = FindObjectsOfType<AudioSource>();
        }

        private AudioSnapshot BuildSnapshot(int[] cameraIds)
        {
            var listeners = new Transform[cameraIds.Length];
            for (int i = 0; i < cameraIds.Length; i++)
                listeners[i] = HullCameraManager.Instance != null ? HullCameraManager.Instance.GetViewTransform(cameraIds[i]) : null;

            bool paused = AudioListener.pause;
            _voices.Clear();
            foreach (var source in _sources)
            {
                if (!IsAudible(source, paused)) continue;
                var clip = ReadClip(source.clip);
                if (clip == null) continue;

                var gains = new StereoGain[listeners.Length];
                var rolloff = RolloffFor(source);
                for (int i = 0; i < listeners.Length; i++)
                    gains[i] = GainAt(source, rolloff, listeners[i]);

                _voices.Add(new VoiceState(source.GetInstanceID(), clip, source.timeSamples,
                    source.pitch * clip.Frequency, source.loop, gains));
            }

            return new AudioSnapshot(Stopwatch.GetTimestamp(), cameraIds, _voices.ToArray());
        }

        private static bool IsAudible(AudioSource source, bool listenerPaused)
            => source != null
               && source.isActiveAndEnabled
               && source.isPlaying
               && !source.mute
               && source.clip != null
               && source.volume > 0f
               && source.spatialBlend > 0f
               && (!listenerPaused || source.ignoreListenerPause);

        private ClipPcm ReadClip(AudioClip clip)
        {
            if (_clips.TryGetValue(clip, out var pcm)) return pcm;

            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                Debug.LogWarning($"[JRTI-Audio]: Skipping sound '{clip.name}': Unity can't read its samples (load type {clip.loadType})");
                _clips[clip] = null;
                return null;
            }
            if (clip.loadState != AudioDataLoadState.Loaded) return null;

            pcm = ClipPcm.Read(clip);
            if (pcm == null) Debug.LogWarning($"[JRTI-Audio]: Skipping sound '{clip.name}': reading its samples failed");
            _clips[clip] = pcm;
            return pcm;
        }

        private static Func<float, float> RolloffFor(AudioSource source)
        {
            float min = Mathf.Max(source.minDistance, 0.01f);
            float max = Mathf.Max(source.maxDistance, min);

            switch (source.rolloffMode)
            {
                case AudioRolloffMode.Linear:
                    return distance => max > min ? Mathf.Clamp01((max - distance) / (max - min)) : 1f;
                case AudioRolloffMode.Custom:
                    var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    return distance => curve.Evaluate(distance / max);
                default:
                    return distance => min / Mathf.Clamp(distance, min, max);
            }
        }

        private static StereoGain GainAt(AudioSource source, Func<float, float> rolloff, Transform listener)
        {
            if (listener == null) return default;

            var offset = source.transform.position - listener.position;
            float distance = offset.magnitude;
            float blend = source.spatialBlend;
            float gain = source.volume * Mathf.Lerp(1f, rolloff(distance), blend);
            float pan = distance > MinPanDistance ? Vector3.Dot(offset, listener.right) / distance * blend : 0f;
            float angle = (pan + 1f) * Mathf.PI / 4f;
            return new StereoGain(gain * Mathf.Cos(angle), gain * Mathf.Sin(angle));
        }
    }
}
