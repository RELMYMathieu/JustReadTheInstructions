using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace JustReadTheInstructions
{
    internal struct PerfStat
    {
        public int Count;
        public double Total;
        public double Max;

        public double Average => Count > 0 ? Total / Count : 0.0;

        public void Add(double value)
        {
            Count++;
            Total += value;
            if (value > Max) Max = value;
        }
    }

    internal enum CameraMetric
    {
        Render,
        CaptureIssue,
        Readback,
        ReadbackCopy,
        EncodeWait,
        Encode,
        JpegKb,
        Count
    }

    internal sealed class CameraPerfSample
    {
        public int Id;
        public string Name;
        public PerfStat[] Stats;
        public int Deferred;
        public double Seconds;
        public bool HasWindow;
        public int StreamClients;
        public int PreviewClients;

        public PerfStat this[CameraMetric metric] => Stats[(int)metric];

        public double PerSecond(double count) => Seconds > 0.0 ? count / Seconds : 0.0;
    }

    internal static class JRTIPerf
    {
        private sealed class CameraCounters
        {
            public readonly string Name;
            private PerfStat[] _stats = new PerfStat[(int)CameraMetric.Count];
            private int _deferred;
            private readonly object _lock = new object();

            public CameraCounters(string name) => Name = name;

            public void Add(CameraMetric metric, double value)
            {
                lock (_lock) _stats[(int)metric].Add(value);
            }

            public void AddDeferred()
            {
                lock (_lock) _deferred++;
            }

            public CameraPerfSample Take(int id)
            {
                lock (_lock)
                {
                    var sample = new CameraPerfSample { Id = id, Name = Name, Stats = _stats, Deferred = _deferred };
                    _stats = new PerfStat[(int)CameraMetric.Count];
                    _deferred = 0;
                    return sample;
                }
            }
        }

        private static readonly ConcurrentDictionary<int, CameraCounters> Cameras
            = new ConcurrentDictionary<int, CameraCounters>();

        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        private static double _frameMainThreadMs;

        public static long Now() => Stopwatch.GetTimestamp();

        public static double MsSince(long startTicks) => (Stopwatch.GetTimestamp() - startTicks) * MsPerTick;

        public static void Register(int cameraId, string name) => Cameras[cameraId] = new CameraCounters(name);

        public static void Unregister(int cameraId) => Cameras.TryRemove(cameraId, out _);

        public static void RecordMainThread(int cameraId, CameraMetric metric, long startTicks)
        {
            double ms = MsSince(startTicks);
            _frameMainThreadMs += ms;
            Record(cameraId, metric, ms);
        }

        public static void RecordSince(int cameraId, CameraMetric metric, long startTicks)
            => Record(cameraId, metric, MsSince(startTicks));

        public static void RecordEncode(int cameraId, long queuedTicks, long startedTicks, int jpegBytes)
        {
            if (!Cameras.TryGetValue(cameraId, out var counters)) return;
            counters.Add(CameraMetric.EncodeWait, (startedTicks - queuedTicks) * MsPerTick);
            counters.Add(CameraMetric.Encode, MsSince(startedTicks));
            counters.Add(CameraMetric.JpegKb, jpegBytes / 1024.0);
        }

        public static void RecordDeferred(int cameraId)
        {
            if (Cameras.TryGetValue(cameraId, out var counters))
                counters.AddDeferred();
        }

        public static double TakeFrameMainThreadMs()
        {
            double ms = _frameMainThreadMs;
            _frameMainThreadMs = 0.0;
            return ms;
        }

        public static List<CameraPerfSample> TakeCameraSamples()
        {
            var samples = new List<CameraPerfSample>(Cameras.Count);
            foreach (var kv in Cameras)
                samples.Add(kv.Value.Take(kv.Key));
            samples.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return samples;
        }

        private static void Record(int cameraId, CameraMetric metric, double ms)
        {
            if (Cameras.TryGetValue(cameraId, out var counters))
                counters.Add(metric, ms);
        }
    }
}
