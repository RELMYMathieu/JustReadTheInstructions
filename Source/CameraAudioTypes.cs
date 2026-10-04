using System;
using System.Collections.Generic;
using System.Threading;

namespace JustReadTheInstructions
{
    internal sealed class ClipPcm
    {
        public const float SampleScale = 1f / 32768f;

        public readonly short[] Samples;
        public readonly int Frequency;

        private ClipPcm(short[] samples, int frequency)
        {
            Samples = samples;
            Frequency = frequency;
        }

        public static ClipPcm Read(UnityEngine.AudioClip clip)
        {
            int channels = clip.channels;
            if (channels <= 0) return null;
            var interleaved = new float[clip.samples * channels];
            if (!clip.GetData(interleaved, 0)) return null;

            var mono = new short[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[i * channels + c];
                float sample = sum / channels;
                mono[i] = (short)(Math.Max(-1f, Math.Min(1f, sample)) * short.MaxValue);
            }
            return new ClipPcm(mono, clip.frequency);
        }

        public static ClipPcm FromWav(byte[] wav)
        {
            if (wav.Length < 12 || Ascii(wav, 0) != "RIFF" || Ascii(wav, 8) != "WAVE") return null;

            int format = 0, channels = 0, frequency = 0, bits = 0, dataOffset = -1, dataLength = 0;
            for (int position = 12; position + 8 <= wav.Length;)
            {
                string chunk = Ascii(wav, position);
                int size = BitConverter.ToInt32(wav, position + 4);
                int body = position + 8;
                if (chunk == "fmt " && size >= 16)
                {
                    format = BitConverter.ToUInt16(wav, body);
                    channels = BitConverter.ToUInt16(wav, body + 2);
                    frequency = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToUInt16(wav, body + 14);
                    if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(wav, body + 24);
                }
                else if (chunk == "data")
                {
                    dataOffset = body;
                    dataLength = size <= 0 || size > wav.Length - body ? wav.Length - body : size;
                    break;
                }
                if (size < 0 || size > wav.Length - body) return null;
                position = body + size + (size & 1);
            }

            int bytesPerSample = bits / 8;
            bool supported = (format == 1 && bits >= 8 && bits <= 32 && bits % 8 == 0) || (format == 3 && bits == 32);
            if (!supported || dataOffset < 0 || channels <= 0 || frequency <= 0) return null;

            var mono = new short[dataLength / (bytesPerSample * channels)];
            for (int frame = 0; frame < mono.Length; frame++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += WavSample(wav, dataOffset + (frame * channels + c) * bytesPerSample, format, bits);
                mono[frame] = (short)(Math.Max(-1f, Math.Min(1f, sum / channels)) * short.MaxValue);
            }
            return new ClipPcm(mono, frequency);
        }

        private static float WavSample(byte[] wav, int offset, int format, int bits)
        {
            if (format == 3) return BitConverter.ToSingle(wav, offset);
            switch (bits)
            {
                case 8: return (wav[offset] - 128) / 128f;
                case 16: return BitConverter.ToInt16(wav, offset) / 32768f;
                case 24: return ((wav[offset] | wav[offset + 1] << 8 | wav[offset + 2] << 16) << 8 >> 8) / 8388608f;
                default: return BitConverter.ToInt32(wav, offset) / 2147483648f;
            }
        }

        private static string Ascii(byte[] bytes, int offset)
            => offset + 4 <= bytes.Length ? System.Text.Encoding.ASCII.GetString(bytes, offset, 4) : "";
    }

    internal readonly struct CameraMix
    {
        public readonly int CameraId;
        public readonly float Gain;
        public readonly bool AutoGain;
        public readonly bool Mastering;

        public CameraMix(int cameraId, float gain, bool autoGain, bool mastering)
        {
            CameraId = cameraId;
            Gain = gain;
            AutoGain = autoGain;
            Mastering = mastering;
        }

        public static CameraMix Default(int cameraId) => new CameraMix(cameraId, 1f, false, false);
    }

    internal sealed class VoiceState
    {
        public readonly long VoiceId;
        public readonly ClipPcm Clip;
        public readonly int TimeSamples;
        public readonly double ClipSamplesPerSecond;
        public readonly bool Loop;
        public readonly VoicePath[] Paths;

        public VoiceState(long voiceId, ClipPcm clip, int timeSamples, double clipSamplesPerSecond, bool loop, VoicePath[] paths)
        {
            VoiceId = voiceId;
            Clip = clip;
            TimeSamples = timeSamples;
            ClipSamplesPerSecond = clipSamplesPerSecond;
            Loop = loop;
            Paths = paths;
        }
    }

    internal sealed class AudioSnapshot
    {
        public readonly long Ticks;
        public readonly CameraMix[] Cameras;
        public readonly int[] CameraIds;
        public readonly VoiceState[] Voices;
        public readonly MasteringSettings Mastering;

        public AudioSnapshot(long ticks, CameraMix[] cameras, VoiceState[] voices, MasteringSettings mastering)
        {
            Ticks = ticks;
            Cameras = cameras;
            Voices = voices;
            Mastering = mastering;
            CameraIds = new int[cameras.Length];
            for (int i = 0; i < cameras.Length; i++)
                CameraIds[i] = cameras[i].CameraId;
        }

        public CameraMix CameraFor(int cameraId)
        {
            int slot = Array.IndexOf(CameraIds, cameraId);
            return slot >= 0 ? Cameras[slot] : CameraMix.Default(cameraId);
        }
    }

    internal sealed class AudioBlock
    {
        public readonly byte[] Pcm;
        public readonly long Ticks;

        public AudioBlock(byte[] pcm, long ticks)
        {
            Pcm = pcm;
            Ticks = ticks;
        }
    }

    internal interface IEmitterShape
    {
        float GameRolloff(float distance);
        float Width(float distance);
    }

    internal interface IAudioSink
    {
        bool IsOpen { get; }
        void Push(AudioBlock block);
    }

    internal sealed class AudioClient : IAudioSink, IDisposable
    {
        private const int MaxQueuedBlocks = 25;

        private readonly Queue<AudioBlock> _blocks = new Queue<AudioBlock>();
        private volatile bool _disposed;

        public bool IsOpen => !_disposed;

        public void Push(AudioBlock block)
        {
            lock (_blocks)
            {
                if (_disposed) return;
                if (_blocks.Count == MaxQueuedBlocks) _blocks.Dequeue();
                _blocks.Enqueue(block);
                Monitor.Pulse(_blocks);
            }
        }

        public AudioBlock Take(int timeoutMs)
        {
            lock (_blocks)
            {
                while (_blocks.Count == 0)
                {
                    if (_disposed || !Monitor.Wait(_blocks, timeoutMs)) return null;
                }
                return _disposed ? null : _blocks.Dequeue();
            }
        }

        public void Dispose()
        {
            lock (_blocks)
            {
                _disposed = true;
                _blocks.Clear();
                Monitor.PulseAll(_blocks);
            }
        }
    }
}
