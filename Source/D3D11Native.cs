using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    internal static class D3D11Native
    {
        public const int QueryTimestamp = 2;
        public const int QueryTimestampDisjoint = 3;

        private const int UnknownQueryInterface = 0;
        private const int UnknownRelease = 2;
        private const int DeviceChildGetDevice = 3;
        private const int DeviceCreateQuery = 24;
        private const int DeviceGetImmediateContext = 40;
        private const int ContextBegin = 27;
        private const int ContextEnd = 28;
        private const int ContextGetData = 29;
        private const int DxgiDeviceGetAdapter = 7;
        private const int DxgiAdapter3QueryVideoMemoryInfo = 14;
        private const uint GetDataDoNotFlush = 1;
        private const int SegmentLocal = 0;
        private const int SegmentNonLocal = 1;

        private static readonly Guid DxgiDeviceIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid DxgiAdapter3Iid = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");

        [StructLayout(LayoutKind.Sequential)]
        private struct QueryDesc
        {
            public int Query;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VideoMemoryInfo
        {
            public ulong Budget;
            public ulong CurrentUsage;
            public ulong AvailableForReservation;
            public ulong CurrentReservation;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetObjectFn(IntPtr self, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetObjectResultFn(IntPtr self, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateQueryFn(IntPtr self, ref QueryDesc desc, out IntPtr query);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void AsyncFn(IntPtr self, IntPtr query);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDataFn(IntPtr self, IntPtr query, IntPtr data, uint size, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryVideoMemoryInfoFn(IntPtr self, uint node, int segment, out VideoMemoryInfo info);

        private static IntPtr _device;
        private static IntPtr _context;
        private static IntPtr _adapter;
        private static CreateQueryFn _createQuery;
        private static AsyncFn _begin;
        private static AsyncFn _end;
        private static GetDataFn _getData;
        private static QueryVideoMemoryInfoFn _queryVideoMemory;

        public static string Error { get; private set; }

        public static bool TryInitialize()
        {
            if (_context != IntPtr.Zero) return true;
            if (Error != null) return false;

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                Error = $"needs DirectX 11, game runs {SystemInfo.graphicsDeviceType}";
                return false;
            }

            try
            {
                IntPtr texture = Texture2D.whiteTexture.GetNativeTexturePtr();
                Method<GetObjectFn>(texture, DeviceChildGetDevice)(texture, out _device);
                Method<GetObjectFn>(_device, DeviceGetImmediateContext)(_device, out var context);

                _createQuery = Method<CreateQueryFn>(_device, DeviceCreateQuery);
                _begin = Method<AsyncFn>(context, ContextBegin);
                _end = Method<AsyncFn>(context, ContextEnd);
                _getData = Method<GetDataFn>(context, ContextGetData);

                _adapter = QueryAdapter3(_device);
                if (_adapter != IntPtr.Zero)
                    _queryVideoMemory = Method<QueryVideoMemoryInfoFn>(_adapter, DxgiAdapter3QueryVideoMemoryInfo);

                _context = context;
                return true;
            }
            catch (Exception ex)
            {
                Error = "DirectX 11 setup failed: " + ex.Message;
                Debug.LogError($"[JRTI-Perf]: {Error}");
                return false;
            }
        }

        public static IntPtr CreateQuery(int type)
        {
            var desc = new QueryDesc { Query = type };
            return _createQuery(_device, ref desc, out var query) < 0 ? IntPtr.Zero : query;
        }

        public static void Begin(IntPtr query) => _begin(_context, query);

        public static void End(IntPtr query) => _end(_context, query);

        public static bool TryGetData(IntPtr query, IntPtr buffer, int size)
            => _getData(_context, query, buffer, (uint)size, GetDataDoNotFlush) == 0;

        public static bool TryGetLocalVideoMemory(out VideoMemoryInfo info) => TryGetVideoMemory(SegmentLocal, out info);

        public static bool TryGetSharedVideoMemory(out VideoMemoryInfo info) => TryGetVideoMemory(SegmentNonLocal, out info);

        private static bool TryGetVideoMemory(int segment, out VideoMemoryInfo info)
        {
            info = default(VideoMemoryInfo);
            return _queryVideoMemory != null && _queryVideoMemory(_adapter, 0, segment, out info) >= 0;
        }

        private static IntPtr QueryAdapter3(IntPtr device)
        {
            if (!TryQueryInterface(device, DxgiDeviceIid, out var dxgiDevice)) return IntPtr.Zero;
            try
            {
                if (Method<GetObjectResultFn>(dxgiDevice, DxgiDeviceGetAdapter)(dxgiDevice, out var adapter) < 0)
                    return IntPtr.Zero;
                try
                {
                    return TryQueryInterface(adapter, DxgiAdapter3Iid, out var adapter3) ? adapter3 : IntPtr.Zero;
                }
                finally
                {
                    Release(adapter);
                }
            }
            finally
            {
                Release(dxgiDevice);
            }
        }

        private static bool TryQueryInterface(IntPtr unknown, Guid iid, out IntPtr result)
            => Method<QueryInterfaceFn>(unknown, UnknownQueryInterface)(unknown, ref iid, out result) >= 0;

        private static void Release(IntPtr unknown) => Method<ReleaseFn>(unknown, UnknownRelease)(unknown);

        private static T Method<T>(IntPtr comObject, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(function, typeof(T));
        }
    }
}
