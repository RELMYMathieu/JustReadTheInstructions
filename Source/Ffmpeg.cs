using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace JustReadTheInstructions
{
    internal static class Ffmpeg
    {
        private const int ProbeTimeoutMs = 10_000;
        private const string RenderNodeFolder = "/dev/dri";
        private static readonly string[] ExtraSearchFolders = { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin", "/snap/bin" };

        internal sealed class Codec
        {
            public readonly string Name;
            public readonly string DeviceArguments;
            public readonly string Filter;
            public readonly string EncoderArguments;

            public Codec(string name, string deviceArguments, string filter, string encoderArguments)
            {
                Name = name;
                DeviceArguments = deviceArguments;
                Filter = filter;
                EncoderArguments = encoderArguments;
            }
        }

        private static int _probeStarted;
        private static volatile Codec _selected;
        private static volatile string _status = "Checking for ffmpeg...";
        private static volatile string _executable;

        public static Codec Selected => _selected;
        public static string Status => _status;

        public static void ProbeInBackground()
        {
            if (Interlocked.Exchange(ref _probeStarted, 1) != 0) return;
            new Thread(Probe) { IsBackground = true, Name = "JRTI-FfmpegProbe" }.Start();
        }

        public static IH264Encoder CreateEncoder(string path, int width, int height, int fps)
        {
            var codec = _selected;
            if (codec == null) throw new InvalidOperationException(_status);
            return new FfmpegEncoder(_executable, EncodeArguments(codec, path, width, height, fps), $"{codec.Name} (ffmpeg)", width * height * 4);
        }

        private static void Probe()
        {
            try
            {
                _executable = FindExecutable();
                if (_executable == null)
                {
                    _status = "ffmpeg not found: install ffmpeg to record in game (the browser records until then)";
                    return;
                }

                foreach (var codec in Candidates())
                {
                    if (!Works(codec)) continue;
                    _selected = codec;
                    _status = $"Encoder: {codec.Name} (ffmpeg)";
                    return;
                }
                _status = "ffmpeg has no working H.264 encoder: recordings use the browser";
            }
            catch (Exception ex)
            {
                _status = $"ffmpeg check failed: {ex.Message}";
            }
        }

        private static IEnumerable<Codec> Candidates()
        {
            yield return new Codec("h264_nvenc", "", "vflip", "-c:v h264_nvenc -profile:v high");
            foreach (var node in RenderNodes())
                yield return new Codec("h264_vaapi", $"-vaapi_device {node}", "vflip,format=nv12,hwupload", "-c:v h264_vaapi -profile:v high");
            yield return new Codec("h264_videotoolbox", "", "vflip,format=nv12", "-c:v h264_videotoolbox -profile:v high");
            yield return new Codec("h264_qsv", "", "vflip,format=nv12", "-c:v h264_qsv -profile:v high");
            yield return new Codec("libx264", "", "vflip,format=yuv420p", "-c:v libx264 -preset veryfast -profile:v high");
            yield return new Codec("libopenh264", "", "vflip,format=yuv420p", "-c:v libopenh264");
        }

        private static IEnumerable<string> RenderNodes()
        {
            if (!Directory.Exists(RenderNodeFolder)) return new string[0];
            var nodes = new List<string>(Directory.GetFiles(RenderNodeFolder, "renderD*"));
            nodes.Sort(StringComparer.Ordinal);
            return nodes;
        }

        private static string EncodeArguments(Codec codec, string path, int width, int height, int fps)
        {
            uint bitrate = VideoEncoders.Bitrate(width, height, fps);
            return $"-hide_banner -loglevel error -nostats {codec.DeviceArguments} " +
                   $"-f rawvideo -pix_fmt rgba -video_size {width}x{height} -framerate {fps} -i pipe:0 " +
                   $"-vf {codec.Filter} {codec.EncoderArguments} -b:v {bitrate} -maxrate {bitrate * 3 / 2} -bufsize {bitrate * 2} " +
                   $"-g {fps * VideoEncoders.GopSeconds} -movflags +frag_keyframe+empty_moov+default_base_moof -f mp4 -y \"{path}\"";
        }

        private static string ProbeArguments(Codec codec)
            => $"-hide_banner -loglevel error -nostats {codec.DeviceArguments} " +
               "-f lavfi -i color=c=gray:s=256x144:r=30,format=rgba -frames:v 3 " +
               $"-vf {codec.Filter} {codec.EncoderArguments} -b:v 500k -f null -";

        private static bool Works(Codec codec)
        {
            try
            {
                using (var process = Start(_executable, ProbeArguments(codec), redirectInput: false))
                {
                    process.BeginErrorReadLine();
                    if (process.WaitForExit(ProbeTimeoutMs)) return process.ExitCode == 0;
                    process.Kill();
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static Process Start(string executable, string arguments, bool redirectInput)
        {
            var info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectInput,
                RedirectStandardError = true,
            };
            var process = new Process { StartInfo = info };
            process.ErrorDataReceived += (sender, e) => { };
            process.Start();
            return process;
        }

        private static string FindExecutable()
        {
            string name = Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg";
            var folders = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            folders.AddRange(ExtraSearchFolders);

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                try
                {
                    var candidate = Path.Combine(folder.Trim().Trim('"'), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }
    }
}
