using System;

namespace JustReadTheInstructions
{
    internal interface IH264Encoder : IDisposable
    {
        string Description { get; }
        object CopyFrame(byte[] bottomUpRgba);
        void Encode(object frame, long frameIndex);
        void ReleaseFrame(object frame);
        void Finish();
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

        public static bool IsAvailable => IsWindows ? MediaFoundation.IsAvailable : Ffmpeg.Selected != null;

        public static string Status
        {
            get
            {
                if (IsWindows)
                    return MediaFoundation.IsAvailable ? "Encoder: Windows Media Foundation" : "Windows Media Foundation is missing on this system";
                return Ffmpeg.Status;
            }
        }

        public static IH264Encoder Create(string path, int width, int height, int fps)
            => IsWindows ? (IH264Encoder)new MediaFoundationEncoder(path, width, height, fps) : Ffmpeg.CreateEncoder(path, width, height, fps);

        public static uint Bitrate(int width, int height, int fps) => (uint)(width * height * fps * BitsPerPixelPerFrame);
    }
}
