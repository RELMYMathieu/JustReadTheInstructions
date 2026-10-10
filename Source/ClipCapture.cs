using System;
using System.Collections.Generic;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class ClipCapture : MonoBehaviour
    {
        private const float GraceSeconds = 1f;

        private readonly List<float> _samples = new List<float>();
        private AudioSource _source;
        private int _channels;
        private float _giveUpAt;

        public AudioClip Clip { get; private set; }

        public bool IsDone => _source == null || !_source.isPlaying || Time.unscaledTime > _giveUpAt;

        public static ClipCapture Begin(AudioClip clip)
        {
            var host = new GameObject("JRTI capture " + clip.name);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.pitch = 1f;
            source.priority = 0;
            source.bypassEffects = false;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            source.ignoreListenerPause = true;
            source.ignoreListenerVolume = true;

            var capture = host.AddComponent<ClipCapture>();
            capture._source = source;
            capture.Clip = clip;
            capture._giveUpAt = Time.unscaledTime + clip.length + GraceSeconds;
            source.Play();
            return capture;
        }

        public ClipPcm Finish()
        {
            float[] interleaved;
            int channels;
            lock (_samples)
            {
                interleaved = _samples.ToArray();
                channels = _channels;
            }
            Destroy(gameObject);
            return channels > 0 && interleaved.Length > 0 ? ClipPcm.FromInterleaved(interleaved, channels, AudioSettings.outputSampleRate) : null;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_samples)
            {
                _samples.AddRange(data);
                _channels = channels;
            }
            Array.Clear(data, 0, data.Length);
        }
    }
}
