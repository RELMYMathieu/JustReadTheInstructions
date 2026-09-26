using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace JustReadTheInstructions
{
    internal static class MediaFoundation
    {
        private const uint Version = 0x00020070;
        private const uint StartupNoSockets = 1;
        private const uint ComMultithreaded = 0;
        private const int D3DDriverHardware = 1;
        private const uint D3DDeviceFlags = 0x20 | 0x800;
        private const uint D3DSdkVersion = 7;

        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid AverageBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid InterlaceMode = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspectRatio = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid Mpeg2Profile = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid AllSamplesIndependent = new Guid("c9173739-5e56-461c-b713-46fb995cb95f");
        public static readonly Guid EnableHardwareTransforms = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
        public static readonly Guid ContainerType = new Guid("150ff23f-4abc-478b-ac4f-e1916fba1cca");
        public static readonly Guid ContainerMpeg4 = new Guid("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
        public static readonly Guid ContainerFragmentedMpeg4 = new Guid("9ba876f1-419f-4b77-a1e0-35959d9d4004");
        public static readonly Guid MediaTypeVideo = new Guid("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid VideoFormatH264 = new Guid("34363248-0000-0010-8000-00aa00389b71");
        public static readonly Guid VideoFormatAbgr32 = new Guid("00000020-0000-0010-8000-00aa00389b71");
        public static readonly Guid EncoderRateControlMode = new Guid("1c0608e9-370c-4710-8a58-cb6181c42423");
        public static readonly Guid EncoderMeanBitrate = new Guid("f7222374-2144-4815-b550-a37f8e12ee52");
        public static readonly Guid EncoderGopSize = new Guid("95f31b26-95a4-41aa-9303-246a7fc6eef1");
        public static readonly Guid SinkWriterDeviceManager = new Guid("ec822da2-e1e9-4b29-a0d8-563c719f5269");
        private static readonly Guid D3DMultithreadInterface = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");

        private const int QueryInterfaceSlot = 0;
        private const int ReleaseSlot = 2;
        private const int SetUnknownSlot = 27;
        private const int SetMultithreadProtectedSlot = 5;
        private const int ResetDeviceSlot = 7;
        private const int SetUInt32Slot = 21;
        private const int SetUInt64Slot = 22;
        private const int SetGuidSlot = 24;
        private const int AddStreamSlot = 3;
        private const int SetInputMediaTypeSlot = 4;
        private const int BeginWritingSlot = 5;
        private const int WriteSampleSlot = 6;
        private const int FinalizeSlot = 11;
        private const int LockSlot = 3;
        private const int UnlockSlot = 4;
        private const int SetCurrentLengthSlot = 6;
        private const int SetSampleTimeSlot = 36;
        private const int SetSampleDurationSlot = 38;
        private const int AddBufferSlot = 42;

        [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] private static extern int MFShutdown();
        [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);
        [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr mediaType);
        [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IntPtr sample);
        [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr sinkWriter);
        [DllImport("mfplat.dll")] private static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr manager);
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
            IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, IntPtr featureLevel, IntPtr immediateContext);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUnknownFn(IntPtr self, ref Guid key, IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetBoolFn(IntPtr self, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResetDeviceFn(IntPtr self, IntPtr device, uint resetToken);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUInt32Fn(IntPtr self, ref Guid key, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUInt64Fn(IntPtr self, ref Guid key, ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddStreamFn(IntPtr self, IntPtr mediaType, out uint streamIndex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetInputMediaTypeFn(IntPtr self, uint streamIndex, IntPtr mediaType, IntPtr encodingParameters);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgumentFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WriteSampleFn(IntPtr self, uint streamIndex, IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockFn(IntPtr self, out IntPtr data, IntPtr maxLength, IntPtr currentLength);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLengthFn(IntPtr self, uint length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetTimeFn(IntPtr self, long hundredNanoseconds);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddBufferFn(IntPtr self, IntPtr buffer);

        private static readonly ConcurrentDictionary<(IntPtr, Type), Delegate> Methods
            = new ConcurrentDictionary<(IntPtr, Type), Delegate>();

        private static bool? _available;

        public static bool IsAvailable
        {
            get
            {
                if (_available == null) _available = ProbeAvailability();
                return _available.Value;
            }
        }

        private static bool ProbeAvailability()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            try
            {
                if (MFStartup(Version, StartupNoSockets) < 0) return false;
                MFShutdown();
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        public static void StartThread()
        {
            CoInitializeEx(IntPtr.Zero, ComMultithreaded);
            Check(MFStartup(Version, StartupNoSockets), "MFStartup");
        }

        public static void EndThread()
        {
            MFShutdown();
            CoUninitialize();
        }

        public static IntPtr CreateAttributes(uint count)
        {
            Check(MFCreateAttributes(out var attributes, count), "MFCreateAttributes");
            return attributes;
        }

        public static IntPtr CreateVideoType(Guid subtype, int width, int height, int fps)
        {
            Check(MFCreateMediaType(out var mediaType), "MFCreateMediaType");
            SetGuid(mediaType, MajorType, MediaTypeVideo);
            SetGuid(mediaType, Subtype, subtype);
            SetUInt32(mediaType, InterlaceMode, 2);
            SetRatio(mediaType, FrameSize, (uint)width, (uint)height);
            SetRatio(mediaType, FrameRate, (uint)fps, 1);
            SetRatio(mediaType, PixelAspectRatio, 1, 1);
            return mediaType;
        }

        public static IntPtr CreateGpuDeviceManager()
        {
            Check(D3D11CreateDevice(IntPtr.Zero, D3DDriverHardware, IntPtr.Zero, D3DDeviceFlags,
                IntPtr.Zero, 0, D3DSdkVersion, out var device, IntPtr.Zero, IntPtr.Zero), "D3D11CreateDevice");
            var manager = IntPtr.Zero;
            try
            {
                var multithreadId = D3DMultithreadInterface;
                if (Method<QueryInterfaceFn>(device, QueryInterfaceSlot)(device, ref multithreadId, out var multithread) >= 0)
                {
                    Method<SetBoolFn>(multithread, SetMultithreadProtectedSlot)(multithread, 1);
                    Release(multithread);
                }

                Check(MFCreateDXGIDeviceManager(out uint resetToken, out manager), "MFCreateDXGIDeviceManager");
                Check(Method<ResetDeviceFn>(manager, ResetDeviceSlot)(manager, device, resetToken), "ResetDevice");
                var ready = manager;
                manager = IntPtr.Zero;
                return ready;
            }
            finally
            {
                Release(manager);
                Release(device);
            }
        }

        public static IntPtr CreateSinkWriter(string path, IntPtr attributes)
        {
            Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out var writer), "MFCreateSinkWriterFromURL");
            return writer;
        }

        public static uint AddStream(IntPtr writer, IntPtr outputType)
        {
            Check(Method<AddStreamFn>(writer, AddStreamSlot)(writer, outputType, out var streamIndex), "AddStream");
            return streamIndex;
        }

        public static int TrySetInputType(IntPtr writer, uint streamIndex, IntPtr inputType, IntPtr encoderSettings)
            => Method<SetInputMediaTypeFn>(writer, SetInputMediaTypeSlot)(writer, streamIndex, inputType, encoderSettings);

        public static void BeginWriting(IntPtr writer)
            => Check(Method<NoArgumentFn>(writer, BeginWritingSlot)(writer), "BeginWriting");

        public static void WriteSample(IntPtr writer, uint streamIndex, IntPtr sample)
            => Check(Method<WriteSampleFn>(writer, WriteSampleSlot)(writer, streamIndex, sample), "WriteSample");

        public static void FinalizeWriter(IntPtr writer)
            => Check(Method<NoArgumentFn>(writer, FinalizeSlot)(writer), "Finalize");

        public static IntPtr CreateBuffer(byte[] data, int length)
        {
            Check(MFCreateMemoryBuffer((uint)length, out var buffer), "MFCreateMemoryBuffer");
            try
            {
                Check(Method<LockFn>(buffer, LockSlot)(buffer, out var destination, IntPtr.Zero, IntPtr.Zero), "Lock");
                Marshal.Copy(data, 0, destination, length);
                Check(Method<NoArgumentFn>(buffer, UnlockSlot)(buffer), "Unlock");
                Check(Method<SetLengthFn>(buffer, SetCurrentLengthSlot)(buffer, (uint)length), "SetCurrentLength");
                return buffer;
            }
            catch
            {
                Release(buffer);
                throw;
            }
        }

        public static IntPtr CreateSample(IntPtr buffer, long time, long duration)
        {
            Check(MFCreateSample(out var sample), "MFCreateSample");
            try
            {
                Check(Method<AddBufferFn>(sample, AddBufferSlot)(sample, buffer), "AddBuffer");
                Check(Method<SetTimeFn>(sample, SetSampleTimeSlot)(sample, time), "SetSampleTime");
                Check(Method<SetTimeFn>(sample, SetSampleDurationSlot)(sample, duration), "SetSampleDuration");
                return sample;
            }
            catch
            {
                Release(sample);
                throw;
            }
        }

        public static void SetUInt32(IntPtr attributes, Guid key, uint value)
            => Check(Method<SetUInt32Fn>(attributes, SetUInt32Slot)(attributes, ref key, value), "SetUINT32");

        public static void SetUnknown(IntPtr attributes, Guid key, IntPtr value)
            => Check(Method<SetUnknownFn>(attributes, SetUnknownSlot)(attributes, ref key, value), "SetUnknown");

        public static void SetGuid(IntPtr attributes, Guid key, Guid value)
            => Check(Method<SetGuidFn>(attributes, SetGuidSlot)(attributes, ref key, ref value), "SetGUID");

        public static void SetRatio(IntPtr attributes, Guid key, uint high, uint low)
        {
            ulong packed = ((ulong)high << 32) | low;
            Check(Method<SetUInt64Fn>(attributes, SetUInt64Slot)(attributes, ref key, packed), "SetUINT64");
        }

        public static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero)
                Method<ReleaseFn>(instance, ReleaseSlot)(instance);
        }

        public static void Check(int hresult, string call)
        {
            if (hresult < 0)
                throw new InvalidOperationException($"{call} failed with 0x{hresult:X8}");
        }

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return (T)(object)Methods.GetOrAdd((function, typeof(T)), key => Marshal.GetDelegateForFunctionPointer(key.Item1, key.Item2));
        }
    }
}
