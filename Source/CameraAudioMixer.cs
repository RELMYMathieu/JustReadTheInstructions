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
        public const float MaxDelaySeconds = 18f;
        private const int HistoryBlocks = 1024;
        private const int HistoryMask = HistoryBlocks - 1;
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
            ApplyLatestSnapshot(blockStart, blockTicks);
            SyncBuses();

            var mastering = _applied?.Mastering ?? MasteringSettings.RseDefault;
            foreach (var bus in _buses.Values)
                bus.Begin(_applied?.CameraFor(bus.CameraId) ?? CameraMix.Default(bus.CameraId), mastering);

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

        private void ApplyLatestSnapshot(long blockStart, long blockTicks)
        {
            var snapshot = Volatile.Read(ref _latest);
            if (snapshot == null || snapshot == _applied) return;
            _applied = snapshot;

            double elapsedSeconds = (Stopwatch.GetTimestamp() - snapshot.Ticks) / (double)Stopwatch.Frequency;
            double takenAt = blockStart + (snapshot.Ticks - blockTicks) * (double)SampleRate / Stopwatch.Frequency;

            foreach (var voice in _voices.Values)
                voice.MarkEnding();

            foreach (var state in snapshot.Voices)
            {
                if (!_voices.TryGetValue(state.VoiceId, out var voice) || voice.Clip != state.Clip)
                    _voices[state.VoiceId] = voice = new MixVoice(state.Clip);
                voice.Sync(state, snapshot.CameraIds, elapsedSeconds, takenAt);
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

            public double TakenAt { get; private set; }

            public void MarkEnding() => _ending = true;

            public void Sync(VoiceState state, int[] cameraIds, double elapsedSeconds, double takenAt)
            {
                TakenAt = takenAt;
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
            private const double CutoffSmoothing = 0.5;
            private const float DistortionDrive = 3f;
            private const double SilentBelowRate = 0.1;
            private const double FullFromRate = 0.2;
            private const double FullUpToRate = 3.0;
            private const double SilentAboveRate = 6.0;
            private static readonly float CentrePan = (float)Math.Sqrt(0.5);

            private struct Emission
            {
                public long Block;
                public float Delay;
                public float Level;

                public double Arrival => (Block + 1) * (double)BlockFrames + Delay;
            }

            private readonly Biquad _filter = new Biquad();
            private readonly Biquad _highpass = new Biquad();
            private readonly Emission[] _emissions = new Emission[HistoryBlocks];
            private readonly DelayCurve _delays = new DelayCurve();
            private long _first;
            private long _lastAudible = long.MinValue;
            private long _lastFold = long.MinValue;
            private long _guessedFrom = long.MaxValue;
            private float _panLeft;
            private float _panRight;
            private float _panTargetLeft = CentrePan;
            private float _panTargetRight = CentrePan;
            private float _cutoff;
            private float _highpassCutoff;
            private double _echoDelay;
            private float _echoMix;
            private float _distortion;

            public PathState(VoicePath initial, double takenAt, long blockStart)
            {
                _cutoff = initial.Cutoff;
                _highpassCutoff = initial.Highpass;
                _echoDelay = EchoFrames(initial);
                double delay = DelayFrames(initial);
                _delays.Add(takenAt, delay);
                long block = blockStart / BlockFrames;
                long inFlight = Math.Min(HistoryBlocks - 2, (long)(delay / BlockFrames) + 2);
                for (long b = block - inFlight; b < block; b++)
                    Record(b, initial, delay);
                _first = block - inFlight + 1;
                _guessedFrom = block - 1;
            }

            public void Mix(MixVoice voice, VoicePath target, long blockStart, float[] dry, float[] mix)
            {
                long block = blockStart / BlockFrames;
                _delays.Add(voice.TakenAt, DelayFrames(target));
                if (!_delays.HasSlope) _guessedFrom = Math.Min(_guessedFrom, block);
                else RetimeGuesses(block);
                Record(block, target, _delays.At((block + 1) * (double)BlockFrames));
                AimPan(target);

                double targetEcho = EchoFrames(target);
                if (!Gather(voice, block, blockStart, targetEcho, target.EchoMix, dry))
                {
                    _panLeft = 0f;
                    _panRight = 0f;
                    _cutoff = target.Cutoff;
                    _echoDelay = targetEcho;
                    _echoMix = target.EchoMix;
                    _distortion = target.Distortion;
                    _highpassCutoff = target.Highpass;
                    _filter.Reset();
                    _highpass.Reset();
                    return;
                }

                _cutoff = SmoothCutoff(_cutoff, target.Cutoff);
                bool filtered = _cutoff < Biquad.BypassHz;
                if (filtered) _filter.SetLowpass(_cutoff);
                _highpassCutoff += (target.Highpass - _highpassCutoff) * (float)CutoffSmoothing;
                bool thinned = _highpassCutoff > Biquad.HighpassOffHz;
                if (thinned) _highpass.SetHighpass(_highpassCutoff);

                bool distorted = _distortion > 0f || target.Distortion > 0f;
                float step = 1f / BlockFrames;
                for (int i = 0; i < BlockFrames; i++)
                {
                    float t = i * step;
                    float y = filtered ? _filter.Process(dry[i]) : _filter.Pass(dry[i]);
                    y = thinned ? _highpass.Process(y) : _highpass.Pass(y);
                    if (distorted)
                        y /= 1f + (_distortion + (target.Distortion - _distortion) * t) * DistortionDrive * Math.Abs(y);

                    mix[i * 2] += y * (_panLeft + (_panTargetLeft - _panLeft) * t);
                    mix[i * 2 + 1] += y * (_panRight + (_panTargetRight - _panRight) * t);
                }

                _panLeft = _panTargetLeft;
                _panRight = _panTargetRight;
                _echoDelay = targetEcho;
                _echoMix = target.EchoMix;
                _distortion = target.Distortion;
            }

            private void Record(long block, VoicePath path, double delay)
            {
                float level = LevelOf(path);
                var emission = new Emission { Block = block, Delay = (float)delay, Level = level };
                ref var previous = ref _emissions[(block - 1) & HistoryMask];
                if (previous.Block == block - 1 && emission.Arrival < previous.Arrival) _lastFold = block;
                _emissions[block & HistoryMask] = emission;
                if (level > 0f) _lastAudible = block;
            }

            private void RetimeGuesses(long block)
            {
                for (long b = _guessedFrom; b < block; b++)
                {
                    ref var emission = ref _emissions[b & HistoryMask];
                    if (emission.Block != b) continue;
                    emission.Delay = (float)_delays.At((b + 1) * (double)BlockFrames);
                    if (_emissions[(b - 1) & HistoryMask].Block == b - 1 && emission.Arrival < _emissions[(b - 1) & HistoryMask].Arrival)
                        _lastFold = Math.Max(_lastFold, b);
                }
                _guessedFrom = long.MaxValue;
            }

            private void AimPan(VoicePath target)
            {
                float level = LevelOf(target);
                if (level <= 0f) return;
                _panTargetLeft = target.Left / level;
                _panTargetRight = target.Right / level;
            }

            private bool Gather(MixVoice voice, long block, long blockStart, double targetEcho, float targetEchoMix, float[] dry)
            {
                _first = Math.Max(_first, block - HistoryBlocks + 2);
                while (_first <= block && HasPassed(_first, blockStart)) _first++;
                if (_first > _lastAudible + 1) return false;

                Array.Clear(dry, 0, BlockFrames);
                bool echoed = _echoMix > 0f || targetEchoMix > 0f;
                float step = 1f / BlockFrames;
                long blockEnd = blockStart + BlockFrames;
                bool heard = false;
                for (long j = _first; j <= block; j++)
                {
                    ref var from = ref _emissions[(j - 1) & HistoryMask];
                    ref var to = ref _emissions[j & HistoryMask];
                    if (from.Block != j - 1 || to.Block != j) continue;
                    double arrivesFrom = from.Arrival;
                    double arrivesTo = to.Arrival;
                    double earliest = Math.Min(arrivesFrom, arrivesTo);
                    if (earliest >= blockEnd && j > _lastFold) break;

                    double span = arrivesTo - arrivesFrom;
                    float weight = span == 0.0 ? 0f : RateWeight(BlockFrames / Math.Abs(span));
                    if (weight == 0f || (from.Level == 0f && to.Level == 0f)) continue;

                    int start = (int)Math.Max(0.0, Math.Ceiling(earliest - blockStart));
                    int end = (int)Math.Min(BlockFrames, Math.Ceiling(Math.Max(arrivesFrom, arrivesTo) - blockStart));
                    double emittedFrom = j * (double)BlockFrames;
                    for (int i = start; i < end; i++)
                    {
                        double u = (blockStart + i - arrivesFrom) / span;
                        double emitted = emittedFrom + u * BlockFrames;
                        float x = voice.Read(emitted);
                        if (echoed)
                        {
                            float t = i * step;
                            x += voice.Read(emitted - (_echoDelay + (targetEcho - _echoDelay) * t)) * (_echoMix + (targetEchoMix - _echoMix) * t);
                        }
                        dry[i] += x * (from.Level + (to.Level - from.Level) * (float)u) * weight;
                        heard = true;
                    }
                }
                return heard;
            }

            private bool HasPassed(long segment, long blockStart)
            {
                ref var from = ref _emissions[(segment - 1) & HistoryMask];
                ref var to = ref _emissions[segment & HistoryMask];
                return from.Block != segment - 1 || to.Block != segment || Math.Max(from.Arrival, to.Arrival) <= blockStart;
            }

            private static float LevelOf(VoicePath path) => (float)Math.Sqrt(path.Left * path.Left + path.Right * path.Right);

            private static float RateWeight(double rate)
            {
                if (rate <= SilentBelowRate || rate >= SilentAboveRate) return 0f;
                if (rate < FullFromRate) return (float)((rate - SilentBelowRate) / (FullFromRate - SilentBelowRate));
                if (rate > FullUpToRate) return (float)((SilentAboveRate - rate) / (SilentAboveRate - FullUpToRate));
                return 1f;
            }

            private static float SmoothCutoff(float current, float target)
                => (float)Math.Exp(Math.Log(current) + (Math.Log(target) - Math.Log(current)) * CutoffSmoothing);
        }

        private sealed class DelayCurve
        {
            private const int Capacity = 4;
            private const double SpacingDecay = 0.98;
            private const double SpacingsBehind = 1.25;
            private const double LatencySlew = 0.02;
            private const double MaxLatencyFrames = SampleRate / 5;

            private readonly double[] _takenAt = new double[Capacity];
            private readonly double[] _delays = new double[Capacity];
            private int _count;
            private int _newest;
            private double _spacing = BlockFrames;
            private double _latency = BlockFrames * 2;

            public bool HasSlope => _count >= 2;

            public void Add(double takenAt, double delay)
            {
                if (_count > 0 && takenAt <= _takenAt[_newest])
                {
                    _delays[_newest] = delay;
                    return;
                }
                if (_count > 0)
                {
                    double gap = takenAt - _takenAt[_newest];
                    _spacing = Math.Max(Math.Min(gap, MaxLatencyFrames), _spacing * SpacingDecay);
                    double wanted = Math.Min(MaxLatencyFrames, BlockFrames + _spacing * SpacingsBehind);
                    double slew = gap * LatencySlew;
                    _latency += _count == 1 ? wanted - _latency : Math.Max(-slew, Math.Min(slew, wanted - _latency));
                }
                _newest = (_newest + 1) % Capacity;
                _takenAt[_newest] = takenAt;
                _delays[_newest] = delay;
                _count = Math.Min(_count + 1, Capacity);
            }

            public double At(double frame)
            {
                if (_count < 2) return _delays[_newest];
                double t = frame - _latency;
                int later = _newest;
                int earlier = Before(later);
                if (t >= _takenAt[later]) return Along(earlier, later, Math.Min(t, _takenAt[later] + _latency));
                for (int pair = 1; pair < _count - 1 && t < _takenAt[earlier]; pair++)
                {
                    later = earlier;
                    earlier = Before(earlier);
                }
                return Along(earlier, later, Math.Max(t, _takenAt[earlier] - _latency));
            }

            private static int Before(int slot) => (slot + Capacity - 1) % Capacity;

            private double Along(int earlier, int later, double t)
                => _delays[earlier] + (_delays[later] - _delays[earlier]) * (t - _takenAt[earlier]) / (_takenAt[later] - _takenAt[earlier]);
        }

        private sealed class MixBus
        {
            public readonly int CameraId;
            private readonly float[] _mix = new float[BlockFrames * Channels];
            private readonly float[] _dry = new float[BlockFrames];
            private readonly Dictionary<long, PathState> _paths = new Dictionary<long, PathState>();
            private readonly AutoGain _autoGain = new AutoGain();
            private readonly Compressor _compressor = new Compressor();
            private readonly Limiter _limiter = new Limiter();
            private CameraMix _settings;
            private MasteringSettings _mastering;
            private float _gain = 1f;

            public MixBus(int cameraId)
            {
                CameraId = cameraId;
                _settings = CameraMix.Default(cameraId);
            }

            public void Begin(CameraMix settings, MasteringSettings mastering)
            {
                _settings = settings;
                _mastering = mastering;
                Array.Clear(_mix, 0, _mix.Length);
            }

            public void Forget(long voiceId) => _paths.Remove(voiceId);

            public void Add(long voiceId, MixVoice voice, VoicePath target, long blockStart)
            {
                if (!_paths.TryGetValue(voiceId, out var path))
                    _paths[voiceId] = path = new PathState(target, voice.TakenAt, blockStart);
                path.Mix(voice, target, blockStart, _dry, _mix);
            }

            public byte[] Finish()
            {
                float gain = _settings.Gain * _autoGain.Next(_mix, _settings.Gain, _settings.AutoGain);
                GainRamp.Apply(_mix, _gain, gain);
                _gain = gain;
                if (_settings.Mastering) _compressor.Process(_mix, _mastering);
                _limiter.Process(_mix);

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
