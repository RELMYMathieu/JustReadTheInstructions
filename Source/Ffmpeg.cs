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

        private const int ProbeWidth = 256;
        private const int ProbeHeight = 144;
        private const int ProbeFps = 30;

        internal sealed class Encoder
        {
            public readonly VideoCodec Codec;
            public readonly string Name;
            public readonly string DeviceArguments;
            public readonly string Filter;
            public readonly string EncoderArguments;
            public readonly bool CapsBitrate;

            public Encoder(VideoCodec codec, string name, string deviceArguments, string filter, string encoderArguments, bool capsBitrate = true)
            {
                Codec = codec;
                Name = name;
                DeviceArguments = deviceArguments;
                Filter = filter;
                EncoderArguments = encoderArguments;
                CapsBitrate = capsBitrate;
            }
        }

        private static int _probeStarted;
        private static readonly Encoder[] _selected = new Encoder[Enum.GetValues(typeof(VideoCodec)).Length];
        private static volatile string _status = "Checking for ffmpeg...";
        private static volatile string _executable;

        public static Encoder Selected(VideoCodec codec) => Volatile.Read(ref _selected[(int)codec]);
        public static string Status => _status;

        public static void ProbeInBackground()
        {
            if (Interlocked.Exchange(ref _probeStarted, 1) != 0) return;
            new Thread(Probe) { IsBackground = true, Name = "JRTI-FfmpegProbe" }.Start();
        }

        public static IVideoEncoder CreateEncoder(VideoCodec codec, string path, int width, int height, int fps)
        {
            var encoder = Selected(codec);
            if (encoder == null) throw new InvalidOperationException($"ffmpeg has no working {codec} encoder");
            return new FfmpegEncoder(_executable, EncodeArguments(encoder, path, width, height, fps), $"{encoder.Name} (ffmpeg)", width * height * 4);
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

                foreach (VideoCodec codec in Enum.GetValues(typeof(VideoCodec)))
                {
                    var encoder = FirstWorking(codec);
                    if (encoder == null) continue;
                    Volatile.Write(ref _selected[(int)codec], encoder);
                    _status = DescribeSelected();
                }
                if (Selected(VideoCodec.H264) == null)
                    _status = "ffmpeg has no working H.264 encoder: recordings use the browser";
            }
            catch (Exception ex)
            {
                _status = $"ffmpeg check failed: {ex.Message}";
            }
        }

        private static Encoder FirstWorking(VideoCodec codec)
        {
            foreach (var encoder in Candidates())
                if (encoder.Codec == codec && Works(encoder)) return encoder;
            return null;
        }

        private static string DescribeSelected()
        {
            var names = new List<string>();
            foreach (var encoder in _selected)
                if (encoder != null) names.Add(encoder.Name);
            return $"Encoders: {string.Join(", ", names)} (ffmpeg)";
        }

        private static IEnumerable<Encoder> Candidates()
        {
            var nodes = RenderNodes();

            yield return new Encoder(VideoCodec.H264, "h264_nvenc", "", "vflip", "-c:v h264_nvenc -profile:v high");
            foreach (var node in nodes)
                yield return new Encoder(VideoCodec.H264, "h264_vaapi", $"-vaapi_device {node}", "vflip,format=nv12,hwupload", "-c:v h264_vaapi -profile:v high");
            yield return new Encoder(VideoCodec.H264, "h264_videotoolbox", "", "vflip,format=nv12", "-c:v h264_videotoolbox -profile:v high");
            yield return new Encoder(VideoCodec.H264, "h264_qsv", "", "vflip,format=nv12", "-c:v h264_qsv -profile:v high");
            yield return new Encoder(VideoCodec.H264, "libx264", "", "vflip,format=yuv420p", "-c:v libx264 -preset veryfast -profile:v high");
            yield return new Encoder(VideoCodec.H264, "libopenh264", "", "vflip,format=yuv420p", "-c:v libopenh264");

            yield return new Encoder(VideoCodec.AV1, "av1_nvenc", "", "vflip", "-c:v av1_nvenc");
            foreach (var node in nodes)
                yield return new Encoder(VideoCodec.AV1, "av1_vaapi", $"-vaapi_device {node}", "vflip,format=nv12,hwupload", "-c:v av1_vaapi");
            yield return new Encoder(VideoCodec.AV1, "av1_qsv", "", "vflip,format=nv12", "-c:v av1_qsv");
            yield return new Encoder(VideoCodec.AV1, "libsvtav1", "", "vflip,format=yuv420p", "-c:v libsvtav1 -preset 10", capsBitrate: false);
        }

        private static IEnumerable<string> RenderNodes()
        {
            if (!Directory.Exists(RenderNodeFolder)) return new string[0];
            var nodes = new List<string>(Directory.GetFiles(RenderNodeFolder, "renderD*"));
            nodes.Sort(StringComparer.Ordinal);
            return nodes;
        }

        private static string EncodeArguments(Encoder encoder, string path, int width, int height, int fps)
            => Arguments(encoder, $"-f rawvideo -pix_fmt rgba -video_size {width}x{height} -framerate {fps} -i pipe:0", width, height, fps,
                         $"-movflags +frag_keyframe+empty_moov+default_base_moof -f mp4 -y \"{path}\"");

        private static string ProbeArguments(Encoder encoder)
            => Arguments(encoder, $"-f lavfi -i color=c=gray:s={ProbeWidth}x{ProbeHeight}:r={ProbeFps},format=rgba -frames:v 3", ProbeWidth, ProbeHeight, ProbeFps,
                         "-f null -");

        private static string Arguments(Encoder encoder, string input, int width, int height, int fps, string output)
            => $"-hide_banner -loglevel error -nostats {encoder.DeviceArguments} {input} " +
               $"-vf {encoder.Filter} {encoder.EncoderArguments} {RateArguments(encoder, VideoEncoders.Bitrate(width, height, fps))} " +
               $"-g {fps * VideoEncoders.GopSeconds} {output}";

        private static string RateArguments(Encoder encoder, uint bitrate)
            => encoder.CapsBitrate ? $"-b:v {bitrate} -maxrate {bitrate * 3 / 2} -bufsize {bitrate * 2}" : $"-b:v {bitrate}";

        private static bool Works(Encoder encoder)
        {
            try
            {
                using (var process = Start(_executable, ProbeArguments(encoder), redirectInput: false))
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
