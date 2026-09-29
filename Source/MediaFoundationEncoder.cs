using System;

namespace JustReadTheInstructions
{
    internal sealed class MediaFoundationEncoder : IVideoEncoder
    {
        private const uint HighProfile = 100;
        private const uint UnconstrainedVbr = 2;
        private const int BytesPerPixel = 4;

        private readonly int _frameBytes;
        private readonly long _frameDuration;
        private IntPtr _writer;
        private IntPtr _deviceManager;
        private uint _stream;
        private bool _threadStarted;

        public string Description { get; }

        public MediaFoundationEncoder(string path, int width, int height, int fps)
        {
            _frameBytes = width * height * BytesPerPixel;
            _frameDuration = 10_000_000L / fps;
            try
            {
                MediaFoundation.StartThread();
                _threadStarted = true;
                _deviceManager = TryCreateDeviceManager();
                Description = _deviceManager != IntPtr.Zero
                    ? "Windows Media Foundation, GPU color conversion"
                    : "Windows Media Foundation";
                _writer = CreateWriter(path, width, height, fps);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public object CopyFrame(byte[] bottomUpRgba) => MediaFoundation.CreateBuffer(bottomUpRgba, _frameBytes);

        public void Encode(object frame, long frameIndex)
        {
            var sample = MediaFoundation.CreateSample((IntPtr)frame, frameIndex * _frameDuration, _frameDuration);
            try { MediaFoundation.WriteSample(_writer, _stream, sample); }
            finally { MediaFoundation.Release(sample); }
        }

        public void ReleaseFrame(object frame) => MediaFoundation.Release((IntPtr)frame);

        public void Finish() => MediaFoundation.FinalizeWriter(_writer);

        public void Dispose()
        {
            MediaFoundation.Release(_writer);
            _writer = IntPtr.Zero;
            MediaFoundation.Release(_deviceManager);
            _deviceManager = IntPtr.Zero;
            if (_threadStarted) MediaFoundation.EndThread();
            _threadStarted = false;
        }

        private static IntPtr TryCreateDeviceManager()
        {
            try { return MediaFoundation.CreateGpuDeviceManager(); }
            catch (Exception) { return IntPtr.Zero; }
        }

        private IntPtr CreateWriter(string path, int width, int height, int fps)
        {
            var attributes = IntPtr.Zero;
            var outputType = IntPtr.Zero;
            var inputType = IntPtr.Zero;
            var encoderSettings = IntPtr.Zero;
            var writer = IntPtr.Zero;
            try
            {
                attributes = MediaFoundation.CreateAttributes(3);
                MediaFoundation.SetUInt32(attributes, MediaFoundation.EnableHardwareTransforms, 1);
                MediaFoundation.SetGuid(attributes, MediaFoundation.ContainerType, MediaFoundation.ContainerFragmentedMpeg4);
                if (_deviceManager != IntPtr.Zero)
                    MediaFoundation.SetUnknown(attributes, MediaFoundation.SinkWriterDeviceManager, _deviceManager);
                writer = MediaFoundation.CreateSinkWriter(path, attributes);

                uint bitrate = VideoEncoders.Bitrate(width, height, fps);
                outputType = MediaFoundation.CreateVideoType(MediaFoundation.VideoFormatH264, width, height, fps);
                MediaFoundation.SetUInt32(outputType, MediaFoundation.AverageBitrate, bitrate);
                MediaFoundation.SetUInt32(outputType, MediaFoundation.Mpeg2Profile, HighProfile);
                _stream = MediaFoundation.AddStream(writer, outputType);

                inputType = MediaFoundation.CreateVideoType(MediaFoundation.VideoFormatAbgr32, width, height, fps);
                MediaFoundation.SetUInt32(inputType, MediaFoundation.DefaultStride, unchecked((uint)(-width * BytesPerPixel)));
                MediaFoundation.SetUInt32(inputType, MediaFoundation.AllSamplesIndependent, 1);

                encoderSettings = MediaFoundation.CreateAttributes(3);
                MediaFoundation.SetUInt32(encoderSettings, MediaFoundation.EncoderRateControlMode, UnconstrainedVbr);
                MediaFoundation.SetUInt32(encoderSettings, MediaFoundation.EncoderMeanBitrate, bitrate);
                MediaFoundation.SetUInt32(encoderSettings, MediaFoundation.EncoderGopSize, (uint)(fps * VideoEncoders.GopSeconds));

                if (MediaFoundation.TrySetInputType(writer, _stream, inputType, encoderSettings) < 0)
                    MediaFoundation.Check(MediaFoundation.TrySetInputType(writer, _stream, inputType, IntPtr.Zero), "SetInputMediaType");

                MediaFoundation.BeginWriting(writer);
                var ready = writer;
                writer = IntPtr.Zero;
                return ready;
            }
            finally
            {
                MediaFoundation.Release(attributes);
                MediaFoundation.Release(outputType);
                MediaFoundation.Release(inputType);
                MediaFoundation.Release(encoderSettings);
                MediaFoundation.Release(writer);
            }
        }
    }
}
