using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
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

        private const string MjpegBoundary = "jrtiboundary";
        private const int StreamIdleTimeoutMs = 30_000;
        private static readonly byte[] MjpegPartEnd = Encoding.ASCII.GetBytes("\r\n");

        private void ServeStaticFile(HttpListenerContext ctx, string relativePath)
        {
            var webRootFull = Path.GetFullPath(WebRoot);
            if (!webRootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                webRootFull += Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(WebRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!candidate.StartsWith(webRootFull, PathComparison))
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
                ctx.Response.Headers.Add("Cache-Control", IsFont(candidate) ? "max-age=604800" : "no-cache");
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
                if (!first) sb.Append(',');
                int id = kv.Key;
                string name = kv.Value.DisplayName ?? id.ToString();
                sb.Append($"{{\"id\":{id},\"name\":\"{EscapeJson(name)}\",\"streaming\":true,\"viewerCount\":{kv.Value.MjpegClientCount},")
                  .Append($"\"snapshotUrl\":\"/camera/{id}/snapshot\",\"streamUrl\":\"/viewer.html?id={id}\",\"mic\":\"{CameraMics.Id(kv.Value.Mic)}\",")
                  .Append($"\"canPan\":{(kv.Value.Aim.CanPan ? "true" : "false")},\"track\":\"{CameraTracks.Id(kv.Value.Aim.Track)}\",")
                  .Append($"\"recording\":{RecordingJson(kv.Value)}}}");
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
                case "audio": ServeCameraAudio(ctx, cameraId, state); break;
                case "status": ServeText(ctx, "ok", "text/plain"); break;
                case "settings": ServeOrUpdateSettings(ctx, cameraId, state); break;
                case "recording": HandleGameRecording(ctx, cameraId, state, parts.Length > 3 ? parts[3] : ""); break;
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

        private static void ServeToClientDict(HttpListenerContext ctx, ConcurrentDictionary<Guid, LatestFrameSlot> clients)
        {
            BeginMjpegResponse(ctx);

            var clientId = Guid.NewGuid();
            var slot = new LatestFrameSlot();
            clients[clientId] = slot;

            try
            {
                var output = ctx.Response.OutputStream;
                byte[] jpeg;
                while ((jpeg = slot.Take(StreamIdleTimeoutMs)) != null)
                {
                    WriteMjpegPart(output, jpeg, "");
                    output.Flush();
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

        private void ServeMultiStream(HttpListenerContext ctx)
        {
            var clientId = Guid.NewGuid();
            var signal = new ManualResetEventSlim(false);
            bool preview = ctx.Request.QueryString["preview"] == "1";
            var sources = SubscribeStreamClients(ctx.Request.QueryString["ids"], preview, clientId, signal);

            if (sources.Count == 0)
            {
                ServeError(ctx, 404, "No matching cameras");
                return;
            }

            BeginMjpegResponse(ctx);

            try
            {
                var output = ctx.Response.OutputStream;
                while (signal.Wait(StreamIdleTimeoutMs))
                {
                    signal.Reset();
                    bool anyOpen = false;
                    foreach (var source in sources)
                    {
                        if (source.Slot.IsDisposed) continue;
                        anyOpen = true;
                        var jpeg = source.Slot.TakeReady();
                        if (jpeg != null) WriteMjpegPart(output, jpeg, source.PartHeaders);
                    }
                    if (!anyOpen) break;
                    output.Flush();
                }
            }
            catch { }
            finally
            {
                foreach (var source in sources)
                {
                    source.Clients.TryRemove(clientId, out _);
                    source.Slot.Dispose();
                }
                try { ctx.Response.Close(); } catch { }
            }
        }

        private List<(ConcurrentDictionary<Guid, LatestFrameSlot> Clients, LatestFrameSlot Slot, string PartHeaders)> SubscribeStreamClients(
            string idList, bool preview, Guid clientId, ManualResetEventSlim signal)
        {
            var sources = new List<(ConcurrentDictionary<Guid, LatestFrameSlot>, LatestFrameSlot, string)>();
            foreach (var token in (idList ?? "").Split(','))
            {
                if (!int.TryParse(token, out int cameraId) || !_states.TryGetValue(cameraId, out var state)) continue;

                var clients = preview ? state.PreviewClients : state.MjpegClients;
                if (clients.ContainsKey(clientId)) continue;

                var slot = new LatestFrameSlot(signal);
                clients[clientId] = slot;
                sources.Add((clients, slot, $"X-Camera-Id: {cameraId}\r\n"));
            }
            return sources;
        }

        private static void BeginMjpegResponse(HttpListenerContext ctx)
        {
            ctx.Response.ContentType = $"multipart/x-mixed-replace; boundary={MjpegBoundary}";
            ctx.Response.SendChunked = true;
        }

        private static void WriteMjpegPart(Stream output, byte[] jpeg, string extraHeaders)
        {
            var header = Encoding.ASCII.GetBytes(
                $"--{MjpegBoundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n{extraHeaders}\r\n");
            output.Write(header, 0, header.Length);
            output.Write(jpeg, 0, jpeg.Length);
            output.Write(MjpegPartEnd, 0, MjpegPartEnd.Length);
        }

        private static void ServeDebugStats(HttpListenerContext ctx)
        {
            var snapshot = JRTIPerfMonitor.Instance?.Latest;
            if (snapshot == null)
            {
                ServeError(ctx, 503, "No performance sample yet");
                return;
            }
            ServeText(ctx, PerfFormat.ToJson(snapshot), "application/json");
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
                    state.SetPendingFov(fov);
                if (TryParseJsonString(body, "mic", out var mic) && CameraMics.TryParse(mic, out var parsedMic))
                    state.Mic = parsedMic;
                if (TryParseJsonFloat(body, "soundGain", out var soundGain))
                    state.SoundGainDb = UnityEngine.Mathf.Clamp(soundGain, -24f, 24f);
                if (TryParseJsonBool(body, "autoGain", out var autoGain))
                    state.AutoGain = autoGain;
                if (TryParseJsonBool(body, "mastering", out var mastering))
                    state.Mastering = mastering;
                UpdateAim(body, state.Aim);

                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                return;
            }

            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{");
            sb.Append($"\"brightness\":{state.Brightness.ToString("F2", ic)},");
            sb.Append($"\"contrast\":{state.Contrast.ToString("F2", ic)},");
            sb.Append($"\"gamma\":{state.Gamma.ToString("F2", ic)},");
            sb.Append($"\"mic\":\"{CameraMics.Id(state.Mic)}\",");
            sb.Append($"\"soundGain\":{state.SoundGainDb.ToString("F1", ic)},");
            sb.Append($"\"autoGain\":{(state.AutoGain ? "true" : "false")},");
            sb.Append($"\"mastering\":{(state.Mastering ? "true" : "false")}");

            if (state.HasFov)
            {
                sb.Append($",\"fov\":{state.Fov.ToString("0.##", ic)}");
                sb.Append($",\"fovMin\":{state.FovMin.ToString("F1", ic)}");
                sb.Append($",\"fovMax\":{state.FovMax.ToString("F1", ic)}");
                sb.Append($",\"fovLimitMin\":{HullCameraRenderer.FovLimitMin.ToString("0.##", ic)}");
                sb.Append($",\"fovLimitMax\":{HullCameraRenderer.FovLimitMax.ToString("F0", ic)}");
            }

            state.Aim.AppendJson(sb);
            sb.Append("}");
            ServeText(ctx, sb.ToString(), "application/json");
        }

        private static void UpdateAim(string body, CameraAimControl aim)
        {
            if (TryParseJsonFloat(body, "zoomRate", out var zoomRate))
                aim.SetZoomRate(zoomRate);

            if (!aim.CanPan) return;

            bool hasYaw = TryParseJsonFloat(body, "panYaw", out var yaw);
            bool hasPitch = TryParseJsonFloat(body, "panPitch", out var pitch);
            if (hasYaw || hasPitch)
                aim.SetTarget(hasYaw ? yaw : (float?)null, hasPitch ? pitch : (float?)null);

            bool hasYawRate = TryParseJsonFloat(body, "panYawRate", out var yawRate);
            bool hasPitchRate = TryParseJsonFloat(body, "panPitchRate", out var pitchRate);
            if (hasYawRate || hasPitchRate)
                aim.SetPanRate(hasYawRate ? yawRate : (float?)null, hasPitchRate ? pitchRate : (float?)null);

            if (TryParseJsonString(body, "track", out var track) && CameraTracks.TryParse(track, out var parsedTrack))
                aim.SetTrack(parsedTrack);
        }

        private static int JsonValueStart(string json, string key)
        {
            var pattern = $"\"{key}\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return -1;
            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == ':')) idx++;
            return idx;
        }

        private static bool TryParseJsonFloat(string json, string key, out float value)
        {
            value = 0f;
            int idx = JsonValueStart(json, key);
            if (idx < 0) return false;
            int start = idx;
            while (idx < json.Length && (json[idx] == '-' || json[idx] == '.' || char.IsDigit(json[idx]))) idx++;
            return float.TryParse(
                json.Substring(start, idx - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static bool TryParseJsonString(string json, string key, out string value)
        {
            value = null;
            int idx = JsonValueStart(json, key);
            if (idx < 0 || idx >= json.Length || json[idx] != '"') return false;
            int end = json.IndexOf('"', idx + 1);
            if (end < 0) return false;
            value = json.Substring(idx + 1, end - idx - 1);
            return true;
        }

        private static bool TryParseJsonBool(string json, string key, out bool value)
        {
            value = false;
            int idx = JsonValueStart(json, key);
            if (idx < 0) return false;
            value = string.CompareOrdinal(json, idx, "true", 0, 4) == 0;
            return value || string.CompareOrdinal(json, idx, "false", 0, 5) == 0;
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

        private static bool IsFont(string path)
            => GetContentType(path).StartsWith("font/", StringComparison.Ordinal);

        private static bool PathsEqual(string a, string b)
            => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

        internal static string EscapeJson(string s)
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
                case ".otf": return "font/otf";
                case ".ttf": return "font/ttf";
                case ".woff2": return "font/woff2";
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