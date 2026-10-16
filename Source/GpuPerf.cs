using System.Collections.Generic;
using System.Threading;

namespace JustReadTheInstructions
{
    internal enum GpuSection
    {
        Setup,
        Near,
        FarTerrain,
        Scaled,
        Galaxy,
        Finish,
        Count
    }

    internal sealed class GpuPerfSample
    {
        public string Status = "off";
        public int Frames;
        public int Dropped;
        public PerfStat SpanMs;
        public PerfStat GameMs;
        public PerfStat JrtiMs;
        public readonly double[] SectionTotalMs = new double[(int)GpuSection.Count];
        public double VramUsedMb = -1.0;
        public double VramBudgetMb = -1.0;
        public double VramSharedMb = -1.0;
        public double JrtiTargetsMb;

        public double SectionMsPerFrame(GpuSection section)
            => Frames > 0 ? SectionTotalMs[(int)section] / Frames : 0.0;
    }

    internal static class GpuPerf
    {
        public const string StatusOn = "on";

        public static readonly string[] SectionKeys = { "setup", "near", "far_terrain", "scaled", "galaxy", "finish" };

        private const int MaxCameras = 64;
        private const int Sections = (int)GpuSection.Count;
        private const int GameTag = 0;
        private const int IdleTag = 1;
        private const int FirstCameraTag = 2;
        private const double BytesPerMb = 1024.0 * 1024.0;

        private static readonly Dictionary<int, int> SlotByCamera = new Dictionary<int, int>();
        private static readonly bool[] SlotUsed = new bool[MaxCameras];
        private static readonly int[] CameraBySlot = new int[MaxCameras];
        private static int _nextSlot;

        private static readonly double[] FrameSectionMs = new double[MaxCameras * Sections];
        private static readonly double[] FrameSectionTotals = new double[Sections];
        private static readonly bool[] FrameTouched = new bool[MaxCameras];
        private static readonly int[] FrameSlots = new int[MaxCameras];

        private static readonly object Lock = new object();
        private static GpuPerfSample _current = new GpuPerfSample();

        private static bool _running;
        private static bool _frameOpen;

        public static bool Wanted { get; set; }

        private static string Status
        {
            get
            {
                if (GpuTimer.Failure != null) return "failed: " + GpuTimer.Failure;
                if (_running) return StatusOn;
                return D3D11Native.Error ?? "off";
            }
        }

        public static void BeginFrame()
        {
            _frameOpen = false;
            if (Wanted != _running)
                _running = Wanted && GpuTimer.TryStart(OnFrame);
            if (!_running || GpuTimer.Failure != null) return;

            GpuTimer.BeginFrame(GameTag);
            _frameOpen = true;
        }

        public static void Mark(int cameraId, GpuSection section)
        {
            if (!_frameOpen) return;
            int slot = SlotFor(cameraId);
            if (slot >= 0) GpuTimer.Mark(FirstCameraTag + slot * Sections + (int)section);
        }

        public static void EndCamera()
        {
            if (_frameOpen) GpuTimer.Mark(IdleTag);
        }

        public static void EndFrame()
        {
            if (!_frameOpen) return;
            _frameOpen = false;
            GpuTimer.EndFrame();
        }

        public static void Release(int cameraId)
        {
            if (!SlotByCamera.TryGetValue(cameraId, out int slot)) return;
            SlotByCamera.Remove(cameraId);
            SlotUsed[slot] = false;
        }

        public static GpuPerfSample Take(long jrtiTargetBytes)
        {
            GpuPerfSample sample;
            lock (Lock)
            {
                sample = _current;
                _current = new GpuPerfSample();
            }

            sample.Status = Status;
            sample.Dropped = GpuTimer.TakeDropped();
            sample.JrtiTargetsMb = jrtiTargetBytes / BytesPerMb;

            if (!_running) return sample;
            if (D3D11Native.TryGetLocalVideoMemory(out var local))
            {
                sample.VramUsedMb = local.CurrentUsage / BytesPerMb;
                sample.VramBudgetMb = local.Budget / BytesPerMb;
            }
            if (D3D11Native.TryGetSharedVideoMemory(out var shared))
                sample.VramSharedMb = shared.CurrentUsage / BytesPerMb;
            return sample;
        }

        private static int SlotFor(int cameraId)
        {
            if (SlotByCamera.TryGetValue(cameraId, out int slot)) return slot;

            for (int i = 0; i < MaxCameras; i++)
            {
                int candidate = (_nextSlot + i) % MaxCameras;
                if (SlotUsed[candidate]) continue;

                SlotUsed[candidate] = true;
                Volatile.Write(ref CameraBySlot[candidate], cameraId);
                SlotByCamera[cameraId] = candidate;
                _nextSlot = (candidate + 1) % MaxCameras;
                return candidate;
            }
            return -1;
        }

        private static void OnFrame(int[] tags, double[] milliseconds, int intervals)
        {
            double span = 0.0, game = 0.0, jrti = 0.0;
            int touched = 0;

            for (int i = 0; i < intervals; i++)
            {
                int tag = tags[i];
                double ms = milliseconds[i];
                span += ms;

                if (tag == GameTag) game += ms;
                if (tag < FirstCameraTag) continue;

                int index = tag - FirstCameraTag;
                int slot = index / Sections;
                if (!FrameTouched[slot])
                {
                    FrameTouched[slot] = true;
                    FrameSlots[touched++] = slot;
                }
                FrameSectionMs[index] += ms;
                jrti += ms;
            }

            for (int i = 0; i < touched; i++)
                RecordCamera(FrameSlots[i]);

            lock (Lock)
            {
                _current.Frames++;
                _current.SpanMs.Add(span);
                _current.GameMs.Add(game);
                _current.JrtiMs.Add(jrti);
                for (int s = 0; s < Sections; s++)
                {
                    _current.SectionTotalMs[s] += FrameSectionTotals[s];
                    FrameSectionTotals[s] = 0.0;
                }
            }
        }

        private static void RecordCamera(int slot)
        {
            int cameraId = Volatile.Read(ref CameraBySlot[slot]);
            double total = 0.0;

            for (int s = 0; s < Sections; s++)
            {
                int index = slot * Sections + s;
                double ms = FrameSectionMs[index];
                FrameSectionMs[index] = 0.0;
                total += ms;
                FrameSectionTotals[s] += ms;
                JRTIPerf.Record(cameraId, CameraMetric.GpuSetup + s, ms);
            }

            JRTIPerf.Record(cameraId, CameraMetric.Gpu, total);
            FrameTouched[slot] = false;
        }
    }
}
