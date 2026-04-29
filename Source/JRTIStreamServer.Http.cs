using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static readonly string WebRoot =
            KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/Web/";
        private static readonly string RecordingsRoot = Path.Combine(WebRoot, "recordings");
        private static readonly string DefaultLosPath = Path.Combine(WebRoot, "images", "los.png");
        private static readonly string CustomLosPath = Path.Combine(WebRoot, "images", "customlos.png");

        private static readonly StringComparison PathComparison =
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private void ServeStaticFile(HttpListenerContext ctx, string relativePath)
        {
            var webRootFull = Path.GetFullPath(WebRoot);
            var candidate = Path.GetFullPath(Path.Combine(WebRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!candidate.StartsWith(webRootFull, StringComparison.Ordinal))
            {
                ServeError(ctx, 403, "Forbidden");
                return;
            }

            if (PathsEqual(candidate, DefaultLosPath) && File.Exists(CustomLosPath))
                candidate = CustomLosPath;

            if (!File.Exists(candidate))
            {
                ServeError(ctx, 404, "Not found");
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(candidate);
                ctx.Response.ContentType = GetContentType(candidate);
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch (Exception ex) { ServeError(ctx, 500, $"Read failed: {ex.Message}"); }
        }

        private void ServeCameraList(HttpListenerContext ctx)
        {
            var sb = new StringBuilder("[");
            bool first = true;

            foreach (var kv in _states)
            {
                if (HullCameraManager.Instance != null && !HullCameraManager.Instance.HasCamera(kv.Key))
                    continue;

                if (!first) sb.Append(',');
                int id = kv.Key;
                string name = HullCameraManager.Instance?.GetCameraDisplayName(id) ?? id.ToString();
                sb.Append($"{{\"id\":{id},\"name\":\"{EscapeJson(name)}\",\"streaming\":true,\"viewerCount\":{kv.Value.MjpegClientCount},")
                  .Append($"\"snapshotUrl\":\"/camera/{id}/snapshot\",\"streamUrl\":\"/viewer.html?id={id}\"}}");
                first = false;
            }

            sb.Append(']');
            ServeText(ctx, sb.ToString(), "application/json");
        }

        private void ServeCameraEndpoint(HttpListenerContext ctx, string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[1], out int cameraId))
            {
                ServeError(ctx, 400, "Invalid camera ID");
                return;
            }

            if (parts.Length == 2)
            {
                ctx.Response.Redirect($"/viewer.html?id={cameraId}");
                ctx.Response.Close();
                return;
            }

            if (!_states.TryGetValue(cameraId, out var state))
            {
                ServeError(ctx, 404, "Camera not found");
                return;
            }

            switch (parts[2])
            {
                case "snapshot": ServeSnapshot(ctx, state); break;
                case "stream": ServeMjpeg(ctx, state); break;
                case "preview": ServePreviewMjpeg(ctx, state); break;
                case "status": ServeText(ctx, "ok", "text/plain"); break;
                case "settings": ServeOrUpdateSettings(ctx, cameraId, state); break;
                default: ServeError(ctx, 404, "Unknown action"); break;
            }
        }

        private static void ServeSnapshot(HttpListenerContext ctx, CameraStreamState state)
        {
            state.MarkSnapshotInterest();

            byte[] jpeg;
            lock (state.JpegLock)
                jpeg = state.LatestJpeg;

            if (jpeg == null)
            {
                ServeError(ctx, 503, "No frame available yet");
                return;
            }

            ctx.Response.ContentType = "image/jpeg";
            ctx.Response.ContentLength64 = jpeg.Length;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.OutputStream.Write(jpeg, 0, jpeg.Length);
            ctx.Response.Close();
        }

        private static void ServeMjpeg(HttpListenerContext ctx, CameraStreamState state)
            => ServeToClientDict(ctx, state.MjpegClients);

        private static void ServePreviewMjpeg(HttpListenerContext ctx, CameraStreamState state)
            => ServeToClientDict(ctx, state.PreviewClients);

        private static void ServeToClientDict(HttpListenerContext ctx, System.Collections.Concurrent.ConcurrentDictionary<Guid, LatestFrameSlot> clients)
        {
            const string boundary = "jrtiboundary";
            ctx.Response.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";
            ctx.Response.SendChunked = true;

            var clientId = Guid.NewGuid();
            var slot = new LatestFrameSlot();
            clients[clientId] = slot;

            try
            {
                var outStream = ctx.Response.OutputStream;
                var boundaryHdr = Encoding.ASCII.GetBytes($"--{boundary}\r\n");
                var crlf = Encoding.ASCII.GetBytes("\r\n");
                var hdrPrefix = Encoding.ASCII.GetBytes("Content-Type: image/jpeg\r\nContent-Length: ");
                var hdrSuffix = Encoding.ASCII.GetBytes("\r\n\r\n");

                while (true)
                {
                    var jpeg = slot.Take(30_000);
                    if (jpeg == null) break;

                    var lenBytes = Encoding.ASCII.GetBytes(jpeg.Length.ToString());
                    outStream.Write(boundaryHdr, 0, boundaryHdr.Length);
                    outStream.Write(hdrPrefix, 0, hdrPrefix.Length);
                    outStream.Write(lenBytes, 0, lenBytes.Length);
                    outStream.Write(hdrSuffix, 0, hdrSuffix.Length);
                    outStream.Write(jpeg, 0, jpeg.Length);
                    outStream.Write(crlf, 0, crlf.Length);
                    outStream.Flush();
                }
            }
            catch { }
            finally
            {
                clients.TryRemove(clientId, out _);
                slot.Dispose();
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static void ServeOrUpdateSettings(HttpListenerContext ctx, int cameraId, CameraStreamState state)
        {
            if (ctx.Request.HttpMethod == "POST")
            {
                string body;
                using (var reader = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();

                if (TryParseJsonFloat(body, "brightness", out var b))
                    state.Brightness = UnityEngine.Mathf.Clamp(b, -1f, 1f);
                if (TryParseJsonFloat(body, "contrast", out var c))
                    state.Contrast = UnityEngine.Mathf.Clamp(c, 0f, 3f);
                if (TryParseJsonFloat(body, "gamma", out var g))
                    state.Gamma = UnityEngine.Mathf.Clamp(g, 0.1f, 5f);
                if (TryParseJsonFloat(body, "fov", out var fov))
                    HullCameraManager.Instance?.SetFov(cameraId, fov);

                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                return;
            }

            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{");
            sb.Append($"\"brightness\":{state.Brightness.ToString("F2", ic)},");
            sb.Append($"\"contrast\":{state.Contrast.ToString("F2", ic)},");
            sb.Append($"\"gamma\":{state.Gamma.ToString("F2", ic)}");

            float? fovVal = HullCameraManager.Instance?.GetFov(cameraId);
            float? fovMin = HullCameraManager.Instance?.GetFovMin(cameraId);
            float? fovMax = HullCameraManager.Instance?.GetFovMax(cameraId);
            if (fovVal.HasValue && fovMax.GetValueOrDefault() > fovMin.GetValueOrDefault())
            {
                sb.Append($",\"fov\":{fovVal.Value.ToString("F1", ic)}");
                sb.Append($",\"fovMin\":{fovMin.Value.ToString("F1", ic)}");
                sb.Append($",\"fovMax\":{fovMax.Value.ToString("F1", ic)}");
            }

            sb.Append("}");
            ServeText(ctx, sb.ToString(), "application/json");
        }

        private static bool TryParseJsonFloat(string json, string key, out float value)
        {
            value = 0f;
            var pattern = $"\"{key}\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == ':')) idx++;
            int start = idx;
            while (idx < json.Length && (json[idx] == '-' || json[idx] == '.' || char.IsDigit(json[idx]))) idx++;
            return float.TryParse(
                json.Substring(start, idx - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static void ServeText(HttpListenerContext ctx, string text, string contentType)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            ctx.Response.ContentType = contentType + (contentType.Contains("charset") ? "" : "; charset=utf-8");
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void ServeError(HttpListenerContext ctx, int code, string message)
        {
            ctx.Response.StatusCode = code;
            ServeText(ctx, message, "text/plain");
        }

        private static bool PathsEqual(string a, string b)
            => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

        private static string GetContentType(string fullPath)
        {
            switch (Path.GetExtension(fullPath).ToLowerInvariant())
            {
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": case ".mjs": return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".webm": return "video/webm";
                case ".mp4": return "video/mp4";
                case ".mkv": return "video/x-matroska";
                case ".txt": return "text/plain; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }
}