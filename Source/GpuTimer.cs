using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class GpuTimer
    {
        public delegate void FrameHandler(int[] tags, double[] milliseconds, int intervals);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RenderEventFn(int eventId);

        public const int MaxMarks = 256;

        private const int RingSize = 6;
        private const int TagBits = 24;
        private const int TagMask = (1 << TagBits) - 1;
        private const int EndTag = -1;
        private const int DisjointDataBytes = 16;

        private enum Kind
        {
            Reset = 1,
            Begin,
            Mark,
            End
        }

        private sealed class Frame
        {
            public IntPtr Disjoint;
            public readonly IntPtr[] Stamps = new IntPtr[MaxMarks];
            public readonly int[] Tags = new int[MaxMarks];
            public int Count;
            public bool Pending;
        }

        private static readonly RenderEventFn Callback = OnRenderEvent;
        private static readonly IntPtr CallbackPointer = Marshal.GetFunctionPointerForDelegate(Callback);
        private static readonly Frame[] Frames = CreateFrames();
        private static readonly double[] Milliseconds = new double[MaxMarks];

        private static IntPtr _scratch;
        private static FrameHandler _handler;
        private static int _open = -1;
        private static int _next;
        private static int _dropped;
        private static volatile string _failure;

        public static string Failure => _failure;

        public static bool TryStart(FrameHandler handler)
        {
            if (!D3D11Native.TryInitialize()) return false;
            if (_scratch == IntPtr.Zero) _scratch = Marshal.AllocHGlobal(DisjointDataBytes);
            _handler = handler;
            Issue(Kind.Reset, 0);
            return true;
        }

        public static void BeginFrame(int tag) => Issue(Kind.Begin, tag);

        public static void Mark(int tag) => Issue(Kind.Mark, tag);

        public static void EndFrame() => Issue(Kind.End, 0);

        public static int TakeDropped() => Interlocked.Exchange(ref _dropped, 0);

        private static Frame[] CreateFrames()
        {
            var frames = new Frame[RingSize];
            for (int i = 0; i < RingSize; i++) frames[i] = new Frame();
            return frames;
        }

        private static void Issue(Kind kind, int tag)
            => GL.IssuePluginEvent(CallbackPointer, ((int)kind << TagBits) | (tag & TagMask));

        private static void OnRenderEvent(int eventId)
        {
            if (_failure != null) return;
            try
            {
                int tag = eventId & TagMask;
                switch ((Kind)(eventId >> TagBits))
                {
                    case Kind.Reset: Reset(); break;
                    case Kind.Begin: Begin(tag); break;
                    case Kind.Mark: Stamp(tag); break;
                    case Kind.End: End(); break;
                }
            }
            catch (Exception ex)
            {
                _failure = ex.Message;
            }
        }

        private static void Reset()
        {
            CloseOpenFrame();
            foreach (var frame in Frames) frame.Pending = false;
        }

        private static void Begin(int tag)
        {
            CloseOpenFrame();
            ResolveFinished();

            var frame = Frames[_next];
            if (frame.Pending)
            {
                frame.Pending = false;
                Interlocked.Increment(ref _dropped);
            }
            if (frame.Disjoint == IntPtr.Zero)
                frame.Disjoint = D3D11Native.CreateQuery(D3D11Native.QueryTimestampDisjoint);
            if (frame.Disjoint == IntPtr.Zero)
                throw new InvalidOperationException("could not create a GPU timer");

            D3D11Native.Begin(frame.Disjoint);
            frame.Count = 0;
            _open = _next;
            _next = (_next + 1) % RingSize;
            Stamp(tag);
        }

        private static void Stamp(int tag)
        {
            if (_open < 0) return;
            var frame = Frames[_open];
            if (frame.Count >= MaxMarks - 1) return;
            Write(frame, tag);
        }

        private static void End()
        {
            if (_open < 0) return;
            var frame = Frames[_open];
            Write(frame, EndTag);
            D3D11Native.End(frame.Disjoint);
            frame.Pending = true;
            _open = -1;
        }

        private static void Write(Frame frame, int tag)
        {
            int index = frame.Count;
            if (frame.Stamps[index] == IntPtr.Zero)
                frame.Stamps[index] = D3D11Native.CreateQuery(D3D11Native.QueryTimestamp);
            if (frame.Stamps[index] == IntPtr.Zero)
                throw new InvalidOperationException("could not create a GPU timestamp");

            D3D11Native.End(frame.Stamps[index]);
            frame.Tags[index] = tag;
            frame.Count++;
        }

        private static void CloseOpenFrame()
        {
            if (_open < 0) return;
            D3D11Native.End(Frames[_open].Disjoint);
            _open = -1;
        }

        private static void ResolveFinished()
        {
            for (int i = 0; i < RingSize; i++)
            {
                var frame = Frames[(_next + i) % RingSize];
                if (frame.Pending && !TryResolve(frame)) return;
            }
        }

        private static bool TryResolve(Frame frame)
        {
            if (!D3D11Native.TryGetData(frame.Disjoint, _scratch, DisjointDataBytes)) return false;
            frame.Pending = false;

            long frequency = Marshal.ReadInt64(_scratch, 0);
            bool disjoint = Marshal.ReadInt32(_scratch, 8) != 0;
            if (disjoint || frequency <= 0)
            {
                Interlocked.Increment(ref _dropped);
                return true;
            }

            long previous = 0;
            double msPerTick = 1000.0 / frequency;
            for (int i = 0; i < frame.Count; i++)
            {
                if (!D3D11Native.TryGetData(frame.Stamps[i], _scratch, sizeof(long)))
                {
                    Interlocked.Increment(ref _dropped);
                    return true;
                }
                long ticks = Marshal.ReadInt64(_scratch, 0);
                if (i > 0) Milliseconds[i - 1] = (ticks - previous) * msPerTick;
                previous = ticks;
            }

            _handler(frame.Tags, Milliseconds, frame.Count - 1);
            return true;
        }
    }
}
