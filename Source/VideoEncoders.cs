using System;
using System.Collections.Generic;

namespace JustReadTheInstructions
{
    internal interface IVideoEncoder : IDisposable
    {
        string Description { get; }
        object CopyFrame(byte[] bottomUpRgba);
        void Encode(object frame, long frameIndex);
        void ReleaseFrame(object frame);
        void Finish();
    }

    internal enum VideoCodec
    {
        H264,
        AV1,
    }

    internal static class VideoEncoders
    {
        public const int GopSeconds = 2;
        private const double BitsPerPixelPerFrame = 0.2;

        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static void Prepare()
        {
            if (!IsWindows) Ffmpeg.ProbeInBackground();
        }

        public static bool IsAvailable => Supports(VideoCodec.H264);

        public static bool Supports(VideoCodec codec)
            => IsWindows ? codec == VideoCodec.H264 && MediaFoundation.IsAvailable : Ffmpeg.Selected(codec) != null;

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
                if (IsWindows)
                    return MediaFoundation.IsAvailable ? "Encoder: Windows Media Foundation" : "Windows Media Foundation is missing on this system";
                return Ffmpeg.Status;
            }
        }

        public static IVideoEncoder Create(VideoCodec codec, string path, int width, int height, int fps)
            => IsWindows ? (IVideoEncoder)new MediaFoundationEncoder(path, width, height, fps) : Ffmpeg.CreateEncoder(codec, path, width, height, fps);

        public static uint Bitrate(int width, int height, int fps) => (uint)(width * height * fps * BitsPerPixelPerFrame);
    }
}
