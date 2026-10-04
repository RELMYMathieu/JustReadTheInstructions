using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static readonly string[] RecordingExtensions = { ".mp4", ".webm", ".mkv" };
        private static readonly Regex ByteRange = new Regex(@"^bytes=(\d*)-(\d*)$");
        private static readonly Regex NonAsciiOrQuote = new Regex(@"[^\x20-\x7E]|""");
        private const int FileCopyBufferBytes = 256 * 1024;

        private void ServeRecordingList(HttpListenerContext ctx)
        {
            var active = ActiveRecordingPaths();
            var files = Directory.Exists(RecordingsRoot)
                ? new DirectoryInfo(RecordingsRoot).GetFiles().Where(f => IsRecordingFileName(f.Name)).OrderByDescending(f => f.LastWriteTimeUtc)
                : Enumerable.Empty<FileInfo>();

            var entries = files.Select(f =>
                $"{{\"file\":\"{EscapeJson(f.Name)}\",\"bytes\":{f.Length}," +
                $"\"modified\":\"{f.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\"," +
                $"\"recording\":{(active.Contains(f.FullName) ? "true" : "false")}}}");

            ServeText(ctx, $"[{string.Join(",", entries)}]", "application/json");
        }

        private HashSet<string> ActiveRecordingPaths()
        {
            var comparer = PathComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var paths = new HashSet<string>(comparer);
            foreach (var state in _states.Values)
            {
                var recorder = state.Recorder;
                if (recorder != null) paths.Add(Path.GetFullPath(recorder.FilePath));
            }
            return paths;
        }

        private static bool IsRecordingFileName(string name)
            => !string.IsNullOrEmpty(name)
               && name == Path.GetFileName(name)
               && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && RecordingExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());

        private static void ServeRecordingFile(HttpListenerContext ctx, string encodedName)
        {
            var name = Uri.UnescapeDataString(encodedName);
            var path = Path.Combine(RecordingsRoot, name);
            if (!IsRecordingFileName(name) || !File.Exists(path))
            {
                ServeError(ctx, 404, "Recording not found");
                return;
            }

            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = file.Length;
                    var response = ctx.Response;
                    response.ContentType = GetContentType(path);
                    response.Headers.Add("Accept-Ranges", "bytes");
                    response.Headers.Add("Cache-Control", "no-cache");
                    if (ctx.Request.QueryString["download"] == "1")
                        response.Headers.Add("Content-Disposition", AttachmentHeader(name));

                    long start = 0, end = length - 1;
                    if (TryParseRange(ctx.Request.Headers["Range"], length, ref start, ref end))
                    {
                        response.StatusCode = 206;
                        response.Headers.Add("Content-Range", $"bytes {start}-{end}/{length}");
                    }

                    response.ContentLength64 = end - start + 1;
                    file.Position = start;
                    CopyBytes(file, response.OutputStream, end - start + 1);
                    response.Close();
                }
            }
            catch (HttpListenerException) { try { ctx.Response.Abort(); } catch { } }
            catch (IOException) { try { ctx.Response.Abort(); } catch { } }
        }

        private static string AttachmentHeader(string name)
            => $"attachment; filename=\"{NonAsciiOrQuote.Replace(name, "_")}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";

        private static bool TryParseRange(string header, long length, ref long start, ref long end)
        {
            var match = header == null ? null : ByteRange.Match(header.Trim());
            if (match == null || !match.Success || length == 0) return false;

            bool hasStart = long.TryParse(match.Groups[1].Value, out long first);
            bool hasEnd = long.TryParse(match.Groups[2].Value, out long last);
            if (!hasStart && !hasEnd) return false;

            if (hasStart)
            {
                if (first >= length) return false;
                start = first;
                end = hasEnd ? Math.Min(last, length - 1) : length - 1;
            }
            else
            {
                start = Math.Max(0, length - last);
                end = length - 1;
            }
            return start <= end;
        }

        private static void CopyBytes(Stream input, Stream output, long count)
        {
            var buffer = new byte[FileCopyBufferBytes];
            while (count > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read <= 0) break;
                output.Write(buffer, 0, read);
                count -= read;
            }
        }
    }
}
