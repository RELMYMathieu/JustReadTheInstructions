using System;
using System.Collections.Generic;

namespace JustReadTheInstructions
{
    internal interface IVideoEncoder : IDisposable
    {
        string Description { get; }
        IAudioEncoder Audio { get; }
        object CopyFrame(byte[] bottomUpRgba);
        void Encode(object frame, long frameIndex);
        void ReleaseFrame(object frame);
        void Finish();
    }

    internal interface IAudioEncoder
    {
        void EncodeAudio(byte[] pcm, int offset, int frames, long firstFrame);
    }

    internal enum VideoCodec
    {
        H264,
        AV1,
    }

    internal static class VideoEncoders
    {
        public const int GopSeconds = 2;
        public const int AudioBitsPerSecond = 192_000;
        private const double BitsPerPixelPerFrame = 0.2;

        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT && !Wine.IsRunning;

        public static void Prepare() => Ffmpeg.ProbeInBackground();

        public static bool IsAvailable => Supports(VideoCodec.H264);

        public static bool Supports(VideoCodec codec) => MediaFoundationRecords(codec) || Ffmpeg.Selected(codec) != null;

        private static bool MediaFoundationRecords(VideoCodec codec)
            => IsWindows && codec == VideoCodec.H264 && MediaFoundation.IsAvailable;

        public static IEnumerable<VideoCodec> Available
        {
            get
            {
                foreach (VideoCodec codec in Enum.GetValues(typeof(VideoCodec)))
                    if (Supports(codec)) yield return codec;
            }
        }

        public static string Id(VideoCodec codec) => codec.ToString().ToLowerInvariant();

        public static bool TryParse(string id, out VideoCodec codec)
            => Enum.TryParse(id, ignoreCase: true, out codec) && Enum.IsDefined(typeof(VideoCodec), codec);

        public static string Status
        {
            get
            {
                if (!MediaFoundationRecords(VideoCodec.H264)) return Ffmpeg.Status;
                var av1 = Ffmpeg.Selected(VideoCodec.AV1);
                return av1 == null
                    ? "Encoder: Windows Media Foundation (add ffmpeg for AV1)"
                    : $"Encoders: Windows Media Foundation, {av1.Name} (ffmpeg) for AV1";
            }
        }

        public static IVideoEncoder Create(VideoCodec codec, string path, int width, int height, int fps)
            => MediaFoundationRecords(codec)
                ? (IVideoEncoder)new MediaFoundationEncoder(path, width, height, fps)
                : Ffmpeg.CreateEncoder(codec, path, width, height, fps);

        public static uint Bitrate(int width, int height, int fps) => (uint)(width * height * fps * BitsPerPixelPerFrame);
    }
}
