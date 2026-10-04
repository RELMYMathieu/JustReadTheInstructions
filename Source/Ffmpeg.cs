using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace JustReadTheInstructions
{
    internal static class Ffmpeg
    {
        private const int ProbeTimeoutMs = 10_000;
        private const string RenderNodeFolder = "/dev/dri";
        private static readonly string ExecutableName = Environment.OSVersion.Platform == PlatformID.Win32NT && !Wine.IsRunning ? "ffmpeg.exe" : "ffmpeg";
        private static readonly string ProvidedFolder = Path.GetFullPath(KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/PluginData/ffmpeg");
        private static readonly string[] ExtraSearchFolders = { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin", "/snap/bin" };

        private const int ProbeWidth = 256;
        private const int ProbeHeight = 144;
        private const int ProbeFps = 30;
        private const string StartCheckArguments = "-hide_banner -loglevel error -nostdin -f lavfi -i nullsrc=s=16x16 -frames:v 1 -f null -";
        private const string RawInputOptions = "-probesize 32 -analyzeduration 0 -thread_queue_size 64";
        private const string Bt709Tags = "-colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv";
        private const string VulkanDevice = "-init_hw_device vulkan=vk -filter_hw_device vk";

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
            return new FfmpegEncoder(_executable,
                (videoUrl, audioUrl) => EncodeArguments(encoder, path, width, height, fps, videoUrl, audioUrl),
                $"{encoder.Name} (ffmpeg), with sound", width * height * 4, fps);
        }

        private static void Probe()
        {
            try
            {
                _executable = FindExecutable();
                if (_executable == null)
                {
                    _status = Wine.IsRunning
                        ? "Proton detected: put a Linux build of ffmpeg in GameData/JustReadTheInstructions/PluginData/ffmpeg/ to record in game (see the README; the browser records until then)"
                        : "ffmpeg not found: install ffmpeg to record in game (the browser records until then)";
                    return;
                }

                if (!Succeeds(StartCheckArguments, out string errors))
                {
                    _status = $"ffmpeg did not start ({_executable}): {errors}";
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
                if (encoder.Codec == codec && Succeeds(ProbeArguments(encoder), out _)) return encoder;
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

            yield return new Encoder(VideoCodec.H264, "h264_nvenc", "", Bt709("nv12"), "-c:v h264_nvenc -profile:v high");
            yield return new Encoder(VideoCodec.H264, "h264_amf", "", Bt709("nv12"), "-c:v h264_amf -rc vbr_peak -profile:v high");
            foreach (var node in nodes)
                yield return new Encoder(VideoCodec.H264, "h264_vaapi", $"-vaapi_device {node}", Bt709("nv12") + ",hwupload", "-c:v h264_vaapi -profile:v high");
            yield return new Encoder(VideoCodec.H264, "h264_videotoolbox", "", Bt709("nv12"), "-c:v h264_videotoolbox -profile:v high");
            yield return new Encoder(VideoCodec.H264, "h264_qsv", "", Bt709("nv12"), "-c:v h264_qsv -profile:v high");
            yield return new Encoder(VideoCodec.H264, "h264_vulkan", VulkanDevice, Bt709("nv12") + ",hwupload", "-c:v h264_vulkan");
            yield return new Encoder(VideoCodec.H264, "libx264", "", Bt709("yuv420p"), "-c:v libx264 -preset veryfast -profile:v high");
            yield return new Encoder(VideoCodec.H264, "libopenh264", "", Bt709("yuv420p"), "-c:v libopenh264");

            yield return new Encoder(VideoCodec.AV1, "av1_nvenc", "", Bt709("nv12"), "-c:v av1_nvenc");
            yield return new Encoder(VideoCodec.AV1, "av1_amf", "", Bt709("nv12"), "-c:v av1_amf -rc vbr_peak");
            foreach (var node in nodes)
                yield return new Encoder(VideoCodec.AV1, "av1_vaapi", $"-vaapi_device {node}", Bt709("nv12") + ",hwupload", "-c:v av1_vaapi");
            yield return new Encoder(VideoCodec.AV1, "av1_qsv", "", Bt709("nv12"), "-c:v av1_qsv");
            yield return new Encoder(VideoCodec.AV1, "av1_vulkan", VulkanDevice, Bt709("nv12") + ",hwupload", "-c:v av1_vulkan");
            yield return new Encoder(VideoCodec.AV1, "libsvtav1", "", Bt709("yuv420p"), "-c:v libsvtav1 -preset 10", capsBitrate: false);
        }

        private static string Bt709(string pixelFormat)
            => $"vflip,scale=out_color_matrix=bt709:out_range=tv,format={pixelFormat},setparams=color_primaries=bt709:color_trc=bt709";

        private static IEnumerable<string> RenderNodes()
        {
            string folder = Wine.IsRunning ? Wine.WindowsPath(RenderNodeFolder) : RenderNodeFolder;
            if (folder == null || !Directory.Exists(folder)) return new string[0];
            var nodes = new List<string>();
            foreach (var node in Directory.GetFiles(folder, "renderD*"))
                nodes.Add(RenderNodeFolder + "/" + Path.GetFileName(node));
            nodes.Sort(StringComparer.Ordinal);
            return nodes;
        }

        private static string EncodeArguments(Encoder encoder, string path, int width, int height, int fps, string videoUrl, string audioUrl)
            => Arguments(encoder,
                         $"{RawInputOptions} -f rawvideo -pix_fmt rgba -video_size {width}x{height} -framerate {fps} -i {videoUrl} " +
                         $"{RawInputOptions} -f s16le -ar {CameraAudioMixer.SampleRate} -ac {CameraAudioMixer.Channels} -i {audioUrl}",
                         width, height, fps,
                         $"-map 0:v -map 1:a -c:a aac -b:a {VideoEncoders.AudioBitsPerSecond} " +
                         $"-movflags +frag_keyframe+empty_moov+default_base_moof -f mp4 -y \"{PathForFfmpeg(path)}\"");

        private static string ProbeArguments(Encoder encoder)
            => Arguments(encoder, $"-f lavfi -i color=c=gray:s={ProbeWidth}x{ProbeHeight}:r={ProbeFps},format=rgba -frames:v 3", ProbeWidth, ProbeHeight, ProbeFps,
                         "-f null -");

        private static string Arguments(Encoder encoder, string input, int width, int height, int fps, string output)
            => $"-hide_banner -loglevel error -nostats -nostdin {encoder.DeviceArguments} {input} " +
               $"-vf {encoder.Filter} {encoder.EncoderArguments} {Bt709Tags} {RateArguments(encoder, VideoEncoders.Bitrate(width, height, fps))} " +
               $"-g {fps * VideoEncoders.GopSeconds} {output}";

        private static string RateArguments(Encoder encoder, uint bitrate)
            => encoder.CapsBitrate ? $"-b:v {bitrate} -maxrate {bitrate * 3 / 2} -bufsize {bitrate * 2}" : $"-b:v {bitrate}";

        private static string PathForFfmpeg(string path) => Wine.IsRunning ? Wine.UnixPath(path) : path;

        private static bool Succeeds(string arguments, out string errors)
        {
            try
            {
                using (var process = FfmpegProcess.Start(_executable, arguments, null))
                {
                    bool succeeded = process.WaitForExit(ProbeTimeoutMs) && process.ExitCode == 0;
                    errors = succeeded ? "" : process.ErrorTail;
                    return succeeded;
                }
            }
            catch (Exception ex)
            {
                errors = ex.Message;
                return false;
            }
        }

        private static string FindExecutable()
        {
            var provided = FindIn(ProvidedFolder);
            if (provided != null || Wine.IsRunning) return provided;

            var folders = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            folders.AddRange(ExtraSearchFolders);

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                try
                {
                    var candidate = Path.Combine(folder.Trim().Trim('"'), ExecutableName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        private static string FindIn(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var found = new List<string>();
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                if (string.Equals(Path.GetFileName(file), ExecutableName, StringComparison.OrdinalIgnoreCase)) found.Add(file);
            found.Sort(StringComparer.Ordinal);
            return found.Count > 0 ? found[0] : null;
        }
    }
}
