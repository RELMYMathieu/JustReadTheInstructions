using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private void HandleRecordingEndpoint(HttpListenerContext ctx, string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                ServeError(ctx, 400, "Expected /recordings/<sessionId>/<action>");
                return;
            }

            var sessionId = parts[1];
            var action = parts[2];

            if (!IsSafeId(sessionId))
            {
                ServeError(ctx, 400, "Invalid session id");
                return;
            }

            var name = ctx.Request.QueryString["name"];
            if (string.IsNullOrEmpty(name))
            {
                ServeError(ctx, 400, "Missing name parameter");
                return;
            }

            var safeName = SanitizeRecordingFilename(name);
            if (string.IsNullOrEmpty(safeName))
            {
                ServeError(ctx, 400, "Invalid filename");
                return;
            }

            switch (action)
            {
                case "append": AppendRecordingChunk(ctx, sessionId, safeName); break;
                case "finalize": FinalizeRecordingSession(ctx, sessionId); break;
                case "abort": AbortRecordingSession(ctx, sessionId); break;
                default: ServeError(ctx, 404, "Unknown recording action"); break;
            }
        }

        private void AppendRecordingChunk(HttpListenerContext ctx, string sessionId, string safeName)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ServeError(ctx, 405, "POST required");
                return;
            }

            if (_finalizedSessions.ContainsKey(sessionId))
            {
                ServeError(ctx, 410, "Session closed");
                return;
            }

            RecordingSession session;
            try
            {
                session = _recordings.GetOrAdd(sessionId, id =>
                    RecordingSession.Create(id, Path.Combine(RecordingsRoot, safeName)));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Stream]: Could not create session {sessionId}: {ex.Message}");
                ServeError(ctx, 500, "Session create failed");
                return;
            }

            if (_finalizedSessions.ContainsKey(sessionId))
            {
                if (_recordings.TryRemove(sessionId, out var zombie) && ReferenceEquals(zombie, session))
                    try { zombie.DisposeAndDelete(); } catch { }
                ServeError(ctx, 410, "Session closed");
                return;
            }

            try
            {
                session.AppendFromStream(ctx.Request.InputStream);
                ServeText(ctx, "ok", "text/plain");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Stream]: Append failed for {sessionId}: {ex.Message}");
                ServeError(ctx, 500, "Append failed");
            }
        }

        private void FinalizeRecordingSession(HttpListenerContext ctx, string sessionId)
        {
            _finalizedSessions.TryAdd(sessionId, 0);
            if (_recordings.TryRemove(sessionId, out var session))
            {
                try
                {
                    session.Dispose();
                    // Hacky fix to make sure we don't end up with empty files or broken mp4/webm files that can't be played
                    // Otherwise we as usual execute our respective fixers to make sure the output's playable
                    if (session.BytesWritten == 0)
                    {
                        try { File.Delete(session.DisplayPath); } catch { }
                    }
                    else if (session.DisplayPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        FixMp4(session.DisplayPath);
                    else if (session.DisplayPath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                        FixWebm(session.DisplayPath);
                    Debug.Log($"[JRTI-Stream]: Recording saved: {session.DisplayPath} ({session.BytesWritten} bytes)");
                }
                catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: Finalize error:\n{ex}"); }
            }
            ServeText(ctx, "ok", "text/plain");
        }

        private void AbortRecordingSession(HttpListenerContext ctx, string sessionId)
        {
            _finalizedSessions.TryAdd(sessionId, 0);
            if (_recordings.TryRemove(sessionId, out var session))
            {
                try
                {
                    session.DisposeAndDelete();
                    Debug.Log($"[JRTI-Stream]: Recording aborted: {session.DisplayPath}");
                }
                catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: Abort error: {ex.Message}"); }
            }
            ServeText(ctx, "ok", "text/plain");
        }

        private static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            foreach (var c in id)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
            return true;
        }

        private static string SanitizeRecordingFilename(string requested)
        {
            if (string.IsNullOrEmpty(requested)) return null;

            var name = Path.GetFileName(requested);
            if (string.IsNullOrEmpty(name)) return null;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c < 32 ? '_' : c);

            var result = sb.ToString();
            if (result.Length > 200) result = result.Substring(0, 200);

            var ext = Path.GetExtension(result).ToLowerInvariant();
            if (ext != ".webm" && ext != ".mp4" && ext != ".mkv")
                result += ".webm";

            return result;
        }
    }
}