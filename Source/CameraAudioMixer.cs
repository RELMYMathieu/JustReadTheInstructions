using System;
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
        public const int BlockFrames = SampleRate / 50;
        public const float MaxDelaySeconds = 8f;
        private const int MaxLagFrames = SampleRate / 4;
        private const int IdleSleepMs = 50;
        private const int TickSleepMs = 5;

        private static readonly Predicate<IAudioSink> IsClosed = sink => !sink.IsOpen;

        private readonly Dictionary<int, List<IAudioSink>> _sinks = new Dictionary<int, List<IAudioSink>>();
        private readonly Dictionary<long, MixVoice> _voices = new Dictionary<long, MixVoice>();
        private readonly Dictionary<int, MixBus> _buses = new Dictionary<int, MixBus>();
        private readonly List<long> _expiredVoices = new List<long>();

        private int[] _listened = new int[0];
        private AudioSnapshot _latest;
        private AudioSnapshot _applied;
        private Thread _thread;
        private volatile bool _running;

        public int[] ListenedCameraIds => Volatile.Read(ref _listened);

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
            lock (_sinks)
            {
                _sinks.Clear();
                RefreshListened();
            }
        }

        public void Subscribe(int cameraId, IAudioSink sink)
        {
            lock (_sinks)
            {
                if (!_sinks.TryGetValue(cameraId, out var sinks)) _sinks[cameraId] = sinks = new List<IAudioSink>();
                sinks.Add(sink);
                RefreshListened();
            }
        }

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
                    DropClosedSinks();
                    if (ListenedCameraIds.Length == 0)
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
                    if (due - framesMixed > MaxLagFrames) framesMixed = (due / BlockFrames - 1) * BlockFrames;
                    while (framesMixed + BlockFrames <= due)
                    {
                        long mixStart = Stopwatch.GetTimestamp();
                        MixBlock(framesMixed, startTicks + framesMixed * Stopwatch.Frequency / SampleRate);
                        AudioPerf.RecordMix(mixStart);
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

        private void DropClosedSinks()
        {
            lock (_sinks)
            {
                int dropped = 0;
                foreach (var sinks in _sinks.Values)
                    dropped += sinks.RemoveAll(IsClosed);
                if (dropped > 0) RefreshListened();
            }
        }

        private void RefreshListened()
            => Volatile.Write(ref _listened, _sinks.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToArray());

        private void MixBlock(long blockStart, long blockTicks)
        {
            long blockEnd = blockStart + BlockFrames;
            ApplyLatestSnapshot();
            SyncBuses();

            foreach (var bus in _buses.Values)
                bus.Begin(_applied?.CameraFor(bus.CameraId) ?? CameraMix.Default(bus.CameraId));

            _expiredVoices.Clear();
            foreach (var kv in _voices)
            {
                var voice = kv.Value;
                voice.Advance(blockStart);
                foreach (var bus in _buses.Values)
                    bus.Add(kv.Key, voice, voice.PathFor(bus.CameraId), blockStart);
                if (voice.HasExpired(blockEnd)) _expiredVoices.Add(kv.Key);
            }

            foreach (long id in _expiredVoices)
            {
                _voices.Remove(id);
                foreach (var bus in _buses.Values)
                    bus.Forget(id);
            }

            foreach (var bus in _buses.Values)
            {
                var block = new AudioBlock(bus.Finish(), blockTicks);
                lock (_sinks)
                {
                    if (!_sinks.TryGetValue(bus.CameraId, out var sinks)) continue;
                    foreach (var sink in sinks) sink.Push(block);
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
                voice.MarkEnding();

            foreach (var state in snapshot.Voices)
            {
                if (!_voices.TryGetValue(state.VoiceId, out var voice) || voice.Clip != state.Clip)
                    _voices[state.VoiceId] = voice = new MixVoice(state.Clip);
                voice.Sync(state, snapshot.CameraIds, elapsedSeconds);
            }
        }

        private void SyncBuses()
        {
            var listened = ListenedCameraIds;
            foreach (int id in listened)
                if (!_buses.ContainsKey(id)) _buses[id] = new MixBus(id);

            if (_buses.Count == listened.Length) return;
            foreach (int id in _buses.Keys.Except(listened).ToArray())
                _buses.Remove(id);
        }

        private static double DelayFrames(VoicePath path) => Math.Min(path.DelaySeconds, MaxDelaySeconds) * SampleRate;

        private static double EchoFrames(VoicePath path) => path.EchoDelaySeconds * SampleRate;

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
            private const int HistoryBlocks = 512;
            private const int HistoryMask = HistoryBlocks - 1;
            private const double BlocksPerFrame = 1.0 / BlockFrames;

            private struct Segment
            {
                public long Block;
                public double Cursor;
                public double Step;
                public float From;
                public float To;
            }

            public readonly ClipPcm Clip;
            private readonly Segment[] _history = new Segment[HistoryBlocks];
            private double _cursor;
            private double _step;
            private bool _loop;
            private bool _synced;
            private bool _ending;
            private float _level;
            private long _silentFrom = -1;
            private double _maxDelayFrames;
            private int[] _cameraIds = new int[0];
            private VoicePath[] _paths = new VoicePath[0];

            public MixVoice(ClipPcm clip) => Clip = clip;

            public void MarkEnding() => _ending = true;

            public void Sync(VoiceState state, int[] cameraIds, double elapsedSeconds)
            {
                _ending = false;
                _silentFrom = -1;
                _loop = state.Loop;
                _step = Math.Max(0.0, state.ClipSamplesPerSecond / SampleRate);
                _cameraIds = cameraIds;
                _paths = state.Paths;

                _maxDelayFrames = 0;
                foreach (var path in _paths)
                    _maxDelayFrames = Math.Max(_maxDelayFrames, DelayFrames(path) + EchoFrames(path));

                SyncCursor(state, elapsedSeconds);
            }

            public VoicePath PathFor(int cameraId)
            {
                int slot = Array.IndexOf(_cameraIds, cameraId);
                return slot >= 0 ? _paths[slot] : VoicePath.Silent;
            }

            public bool HasExpired(long now) => _silentFrom >= 0 && now - _silentFrom > _maxDelayFrames + BlockFrames;

            public void Advance(long blockStart)
            {
                long block = blockStart / BlockFrames;
                int length = Clip.Samples.Length;
                bool clipDone = !_loop && _cursor >= length;
                float to = _ending || clipDone ? 0f : 1f;

                _history[block & HistoryMask] = new Segment { Block = block, Cursor = _cursor, Step = _step, From = _level, To = to };
                if (_level == 0f && to == 0f && _silentFrom < 0) _silentFrom = blockStart;

                _level = to;
                _cursor += _step * BlockFrames;
                if (_loop && length > 0 && _cursor >= length) _cursor %= length;
            }

            public float Read(double frame)
            {
                if (frame < 0) return 0f;
                double blocks = frame * BlocksPerFrame;
                long block = (long)blocks;
                ref var segment = ref _history[block & HistoryMask];
                if (segment.Block != block) return 0f;

                float progress = (float)(blocks - block);
                float level = segment.From + (segment.To - segment.From) * progress;
                return level == 0f ? 0f : SampleAt(segment.Cursor + progress * BlockFrames * segment.Step) * level;
            }

            private float SampleAt(double cursor)
            {
                var samples = Clip.Samples;
                int length = samples.Length;
                if (cursor >= length)
                {
                    if (!_loop || length == 0) return 0f;
                    do cursor -= length; while (cursor >= length);
                }

                int index = (int)cursor;
                int next = index + 1 < length ? index + 1 : _loop ? 0 : index;
                float fraction = (float)(cursor - index);
                return (samples[index] + (samples[next] - samples[index]) * fraction) * ClipPcm.SampleScale;
            }

            private void SyncCursor(VoiceState state, double elapsedSeconds)
            {
                int length = Clip.Samples.Length;
                double expected = state.TimeSamples + elapsedSeconds * state.ClipSamplesPerSecond;
                if (_loop && length > 0) expected %= length;

                double drift = expected - _cursor;
                if (_loop && length > 0)
                {
                    if (drift > length / 2.0) drift -= length;
                    else if (drift < -length / 2.0) drift += length;
                }

                if (!_synced || Math.Abs(drift) > ResyncSeconds * Clip.Frequency) _cursor = expected;
                _synced = true;
            }
        }

        private sealed class PathState
        {
            private const double MaxDelaySlewPerFrame = 0.5;
            private const double SnapDelayFrames = SampleRate;
            private const double CutoffSmoothing = 0.5;
            private const float DistortionDrive = 3f;

            private readonly Biquad _filter = new Biquad();
            private float _left;
            private float _right;
            private float _cutoff;
            private double _delay;
            private double _echoDelay;
            private float _echoMix;
            private float _distortion;

            public PathState(VoicePath initial)
            {
                _cutoff = initial.Cutoff;
                _delay = DelayFrames(initial);
                _echoDelay = EchoFrames(initial);
            }

            public void Mix(MixVoice voice, VoicePath target, long blockStart, float[] mix)
            {
                double targetDelay = DelayFrames(target);
                if (Math.Abs(targetDelay - _delay) > SnapDelayFrames) _delay = targetDelay;
                double slope = Math.Max(-MaxDelaySlewPerFrame, Math.Min(MaxDelaySlewPerFrame, (targetDelay - _delay) / BlockFrames));

                double targetEcho = EchoFrames(target);
                if (_left == 0f && _right == 0f && target.IsSilent)
                {
                    _delay += slope * BlockFrames;
                    _cutoff = target.Cutoff;
                    _echoDelay = targetEcho;
                    _echoMix = target.EchoMix;
                    _distortion = target.Distortion;
                    _filter.Reset();
                    return;
                }

                _cutoff = SmoothCutoff(_cutoff, target.Cutoff);
                bool filtered = _cutoff < Biquad.BypassHz;
                if (filtered) _filter.SetLowpass(_cutoff);

                bool echoed = _echoMix > 0f || target.EchoMix > 0f;
                bool distorted = _distortion > 0f || target.Distortion > 0f;
                float step = 1f / BlockFrames;
                for (int i = 0; i < BlockFrames; i++)
                {
                    float t = i * step;
                    double read = blockStart + i - (_delay + slope * i);
                    float x = voice.Read(read);
                    if (echoed)
                        x += voice.Read(read - (_echoDelay + (targetEcho - _echoDelay) * t)) * (_echoMix + (target.EchoMix - _echoMix) * t);

                    float y = filtered ? _filter.Process(x) : _filter.Pass(x);
                    if (distorted)
                        y /= 1f + (_distortion + (target.Distortion - _distortion) * t) * DistortionDrive * Math.Abs(y);

                    mix[i * 2] += y * (_left + (target.Left - _left) * t);
                    mix[i * 2 + 1] += y * (_right + (target.Right - _right) * t);
                }

                _delay += slope * BlockFrames;
                _left = target.Left;
                _right = target.Right;
                _echoDelay = targetEcho;
                _echoMix = target.EchoMix;
                _distortion = target.Distortion;
            }

            private static float SmoothCutoff(float current, float target)
                => (float)Math.Exp(Math.Log(current) + (Math.Log(target) - Math.Log(current)) * CutoffSmoothing);
        }

        private sealed class MixBus
        {
            public readonly int CameraId;
            private readonly float[] _mix = new float[BlockFrames * Channels];
            private readonly Dictionary<long, PathState> _paths = new Dictionary<long, PathState>();
            private readonly AutoGain _autoGain = new AutoGain();
            private readonly Limiter _limiter = new Limiter();
            private CameraMix _settings;
            private float _gain = 1f;

            public MixBus(int cameraId)
            {
                CameraId = cameraId;
                _settings = CameraMix.Default(cameraId);
            }

            public void Begin(CameraMix settings)
            {
                _settings = settings;
                Array.Clear(_mix, 0, _mix.Length);
            }

            public void Forget(long voiceId) => _paths.Remove(voiceId);

            public void Add(long voiceId, MixVoice voice, VoicePath target, long blockStart)
            {
                if (!_paths.TryGetValue(voiceId, out var path))
                    _paths[voiceId] = path = new PathState(target);
                path.Mix(voice, target, blockStart, _mix);
            }

            public byte[] Finish()
            {
                float gain = _settings.Gain * _autoGain.Next(_mix, _settings.Gain, _settings.AutoGain);
                _limiter.Process(_mix, _gain, gain);
                _gain = gain;

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
}
