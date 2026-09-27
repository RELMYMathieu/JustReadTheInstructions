using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace JustReadTheInstructions
{
    internal sealed class CameraAudioMixer : IDisposable
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;
        private const int BytesPerSample = 2;
        public const int BytesPerFrame = Channels * BytesPerSample;
        private const int BlockFrames = SampleRate / 50;
        private const int MaxLagFrames = SampleRate / 4;
        private const int IdleSleepMs = 50;
        private const int TickSleepMs = 5;

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<IAudioSink, bool>> _sinks
            = new ConcurrentDictionary<int, ConcurrentDictionary<IAudioSink, bool>>();
        private readonly Dictionary<int, MixVoice> _voices = new Dictionary<int, MixVoice>();
        private readonly Dictionary<int, MixBus> _buses = new Dictionary<int, MixBus>();
        private readonly List<int> _finishedVoices = new List<int>();
        private readonly float[] _voiceBuffer = new float[BlockFrames];

        private AudioSnapshot _latest;
        private AudioSnapshot _applied;
        private Thread _thread;
        private volatile bool _running;

        public void Start()
        {
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "JRTI-CameraAudio" };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(1000);
            _sinks.Clear();
        }

        public void Subscribe(int cameraId, IAudioSink sink)
            => _sinks.GetOrAdd(cameraId, _ => new ConcurrentDictionary<IAudioSink, bool>())[sink] = true;

        public int[] ListenedCameraIds()
            => _sinks.Where(kv => kv.Value.Keys.Any(sink => sink.IsOpen)).Select(kv => kv.Key).ToArray();

        public void Publish(AudioSnapshot snapshot) => Volatile.Write(ref _latest, snapshot);

        public static byte[] WavStreamHeader()
        {
            var header = new byte[44];
            WriteAscii(header, 0, "RIFF");
            WriteUInt32(header, 4, uint.MaxValue);
            WriteAscii(header, 8, "WAVEfmt ");
            WriteUInt32(header, 16, 16);
            WriteUInt16(header, 20, 1);
            WriteUInt16(header, 22, Channels);
            WriteUInt32(header, 24, SampleRate);
            WriteUInt32(header, 28, SampleRate * BytesPerFrame);
            WriteUInt16(header, 32, BytesPerFrame);
            WriteUInt16(header, 34, BytesPerSample * 8);
            WriteAscii(header, 36, "data");
            WriteUInt32(header, 40, uint.MaxValue);
            return header;
        }

        private void Run()
        {
            long startTicks = 0;
            long framesMixed = 0;
            bool idle = true;

            while (_running)
            {
                try
                {
                    if (ListenedCameraIds().Length == 0)
                    {
                        if (!idle) Reset();
                        idle = true;
                        Thread.Sleep(IdleSleepMs);
                        continue;
                    }

                    if (idle)
                    {
                        idle = false;
                        startTicks = Stopwatch.GetTimestamp();
                        framesMixed = 0;
                    }

                    long due = (Stopwatch.GetTimestamp() - startTicks) * SampleRate / Stopwatch.Frequency;
                    if (due - framesMixed > MaxLagFrames) framesMixed = due - BlockFrames;
                    while (framesMixed + BlockFrames <= due)
                    {
                        MixBlock(startTicks + framesMixed * Stopwatch.Frequency / SampleRate);
                        framesMixed += BlockFrames;
                    }
                    Thread.Sleep(TickSleepMs);
                }
                catch (ThreadInterruptedException) { break; }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-Audio]: Mixer error: {ex}");
                    Reset();
                    Thread.Sleep(IdleSleepMs);
                }
            }
        }

        private void Reset()
        {
            _voices.Clear();
            _buses.Clear();
            _applied = null;
        }

        private void MixBlock(long blockTicks)
        {
            ApplyLatestSnapshot();
            SyncBuses();

            foreach (var bus in _buses.Values)
                bus.Clear();

            _finishedVoices.Clear();
            foreach (var kv in _voices)
            {
                var voice = kv.Value;
                bool playing = voice.Render(_voiceBuffer);
                foreach (var bus in _buses.Values)
                    bus.Add(kv.Key, voice.TargetFor(bus.CameraId), _voiceBuffer);
                if (!playing || voice.Ending) _finishedVoices.Add(kv.Key);
            }

            foreach (int id in _finishedVoices)
            {
                _voices.Remove(id);
                foreach (var bus in _buses.Values)
                    bus.Forget(id);
            }

            foreach (var bus in _buses.Values)
            {
                if (!_sinks.TryGetValue(bus.CameraId, out var sinks)) continue;
                var block = new AudioBlock(bus.ToPcm16(), blockTicks);
                foreach (var sink in sinks.Keys)
                {
                    if (sink.IsOpen) sink.Push(block);
                    else sinks.TryRemove(sink, out _);
                }
            }
        }

        private void ApplyLatestSnapshot()
        {
            var snapshot = Volatile.Read(ref _latest);
            if (snapshot == null || snapshot == _applied) return;
            _applied = snapshot;

            double elapsedSeconds = (Stopwatch.GetTimestamp() - snapshot.Ticks) / (double)Stopwatch.Frequency;

            foreach (var voice in _voices.Values)
                voice.Ending = true;

            foreach (var state in snapshot.Voices)
            {
                if (!_voices.TryGetValue(state.SourceId, out var voice) || voice.Clip != state.Clip)
                    _voices[state.SourceId] = voice = new MixVoice(state.Clip);
                voice.Sync(state, snapshot.CameraIds, elapsedSeconds);
            }
        }

        private void SyncBuses()
        {
            var listened = ListenedCameraIds();
            foreach (int id in listened)
                if (!_buses.ContainsKey(id)) _buses[id] = new MixBus(id, BlockFrames);

            if (_buses.Count == listened.Length) return;
            foreach (int id in _buses.Keys.Except(listened).ToArray())
                _buses.Remove(id);
        }

        private static void WriteAscii(byte[] buffer, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++)
                buffer[offset + i] = (byte)text[i];
        }

        private static void WriteUInt16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            WriteUInt16(buffer, offset, (int)(value & 0xFFFF));
            WriteUInt16(buffer, offset + 2, (int)(value >> 16));
        }

        private sealed class MixVoice
        {
            private const double ResyncSeconds = 0.1;

            public readonly ClipPcm Clip;
            public bool Ending;
            private double _cursor;
            private double _step;
            private bool _loop;
            private bool _synced;
            private int[] _cameraIds;
            private StereoGain[] _targets;

            public MixVoice(ClipPcm clip) => Clip = clip;

            public void Sync(VoiceState state, int[] cameraIds, double elapsedSeconds)
            {
                Ending = false;
                _loop = state.Loop;
                _step = Math.Max(0.0, state.ClipSamplesPerSecond / SampleRate);
                _cameraIds = cameraIds;
                _targets = state.Gains;

                int length = Clip.Samples.Length;
                double expected = state.TimeSamples + elapsedSeconds * state.ClipSamplesPerSecond;
                if (_loop && length > 0) expected %= length;

                double drift = expected - _cursor;
                if (_loop && length > 0)
                {
                    if (drift > length / 2.0) drift -= length;
                    else if (drift < -length / 2.0) drift += length;
                }

                if (!_synced || Math.Abs(drift) > ResyncSeconds * Clip.Frequency)
                    _cursor = expected;
                _synced = true;
            }

            public StereoGain TargetFor(int cameraId)
            {
                if (Ending) return default;
                int slot = Array.IndexOf(_cameraIds, cameraId);
                return slot >= 0 ? _targets[slot] : default;
            }

            public bool Render(float[] output)
            {
                var samples = Clip.Samples;
                int length = samples.Length;

                for (int i = 0; i < output.Length; i++)
                {
                    if (_cursor >= length)
                    {
                        if (!_loop || length == 0)
                        {
                            Array.Clear(output, i, output.Length - i);
                            return false;
                        }
                        _cursor %= length;
                    }

                    int index = (int)_cursor;
                    int next = index + 1 < length ? index + 1 : _loop ? 0 : index;
                    float fraction = (float)(_cursor - index);
                    output[i] = samples[index] + (samples[next] - samples[index]) * fraction;
                    _cursor += _step;
                }
                return true;
            }
        }

        private sealed class MixBus
        {
            public readonly int CameraId;
            private readonly float[] _mix;
            private readonly Dictionary<int, StereoGain> _gains = new Dictionary<int, StereoGain>();

            public MixBus(int cameraId, int frames)
            {
                CameraId = cameraId;
                _mix = new float[frames * Channels];
            }

            public void Clear() => Array.Clear(_mix, 0, _mix.Length);

            public void Forget(int voiceId) => _gains.Remove(voiceId);

            public void Add(int voiceId, StereoGain target, float[] voice)
            {
                _gains.TryGetValue(voiceId, out var from);
                _gains[voiceId] = target;
                if (from.IsSilent && target.IsSilent) return;

                float step = 1f / voice.Length;
                for (int i = 0; i < voice.Length; i++)
                {
                    float t = i * step;
                    _mix[i * 2] += voice[i] * (from.Left + (target.Left - from.Left) * t);
                    _mix[i * 2 + 1] += voice[i] * (from.Right + (target.Right - from.Right) * t);
                }
            }

            public byte[] ToPcm16()
            {
                var pcm = new byte[_mix.Length * BytesPerSample];
                for (int i = 0; i < _mix.Length; i++)
                {
                    float clamped = _mix[i] > 1f ? 1f : _mix[i] < -1f ? -1f : _mix[i];
                    short sample = (short)(clamped * short.MaxValue);
                    pcm[i * 2] = (byte)sample;
                    pcm[i * 2 + 1] = (byte)(sample >> 8);
                }
                return pcm;
            }
        }
    }

    internal sealed class ClipPcm
    {
        public readonly float[] Samples;
        public readonly int Frequency;

        private ClipPcm(float[] samples, int frequency)
        {
            Samples = samples;
            Frequency = frequency;
        }

        public static ClipPcm Read(UnityEngine.AudioClip clip)
        {
            int channels = clip.channels;
            var interleaved = new float[clip.samples * channels];
            if (channels <= 0 || !clip.GetData(interleaved, 0)) return null;

            var mono = new float[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return new ClipPcm(mono, clip.frequency);
        }
    }

    internal readonly struct StereoGain
    {
        public readonly float Left;
        public readonly float Right;

        public StereoGain(float left, float right)
        {
            Left = left;
            Right = right;
        }

        public bool IsSilent => Left == 0f && Right == 0f;
    }

    internal sealed class VoiceState
    {
        public readonly int SourceId;
        public readonly ClipPcm Clip;
        public readonly int TimeSamples;
        public readonly double ClipSamplesPerSecond;
        public readonly bool Loop;
        public readonly StereoGain[] Gains;

        public VoiceState(int sourceId, ClipPcm clip, int timeSamples, double clipSamplesPerSecond, bool loop, StereoGain[] gains)
        {
            SourceId = sourceId;
            Clip = clip;
            TimeSamples = timeSamples;
            ClipSamplesPerSecond = clipSamplesPerSecond;
            Loop = loop;
            Gains = gains;
        }
    }

    internal sealed class AudioSnapshot
    {
        public readonly long Ticks;
        public readonly int[] CameraIds;
        public readonly VoiceState[] Voices;

        public AudioSnapshot(long ticks, int[] cameraIds, VoiceState[] voices)
        {
            Ticks = ticks;
            CameraIds = cameraIds;
            Voices = voices;
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
                Monitor.PulseAll(_blocks);
            }
        }
    }
}
