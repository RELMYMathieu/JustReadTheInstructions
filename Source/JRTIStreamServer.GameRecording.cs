using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static readonly Regex UnsafeFileNameChars = new Regex(@"[\\/:*?""<>|\s]+");

        internal static bool InGameRecordingAvailable => JRTISettings.InGameRecording && VideoEncoders.IsAvailable;

        private static void HandleGameRecording(HttpListenerContext ctx, int cameraId, CameraStreamState state, string action)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ServeError(ctx, 405, "POST required");
                return;
            }

            switch (action)
            {
                case "start": StartGameRecording(ctx, cameraId, state); break;
                case "stop": StopGameRecording(ctx, state, discard: false); break;
                case "discard": StopGameRecording(ctx, state, discard: true); break;
                case "pause": state.Recorder?.Pause(); ServeText(ctx, RecordingJson(state), "application/json"); break;
                case "resume": state.Recorder?.Resume(); ServeText(ctx, RecordingJson(state), "application/json"); break;
                default: ServeError(ctx, 404, "Unknown recording action"); break;
            }
        }

        private static void StartGameRecording(HttpListenerContext ctx, int cameraId, CameraStreamState state)
        {
            if (!InGameRecordingAvailable)
            {
                ServeError(ctx, 409, "In-game recording is not available");
                return;
            }
            if (state.FrameWidth % 2 != 0 || state.FrameHeight % 2 != 0)
            {
                ServeError(ctx, 409, "In-game recording needs an even Render Width and Height");
                return;
            }
            var requestedCodec = ctx.Request.QueryString["codec"] ?? VideoEncoders.Id(VideoCodec.H264);
            if (!VideoEncoders.TryParse(requestedCodec, out var codec) || !VideoEncoders.Supports(codec))
            {
                ServeError(ctx, 409, $"The {requestedCodec} codec is not available on this system");
                return;
            }

            lock (state.RecordingLock)
            {
                if (state.Recorder == null)
                {
                    var path = RecordingSession.ResolveUniquePath(Path.Combine(RecordingsRoot, RecordingFileName(state.DisplayName, cameraId)));
                    int width = state.FrameWidth, height = state.FrameHeight, fps = JRTISettings.StreamMaxFps;
                    try
                    {
                        var recorder = new Mp4Recorder(path, width, height, fps,
                            () => VideoEncoders.Create(codec, path, width, height, fps),
                            message => Debug.LogWarning($"[JRTI-Stream]: {message}"));
                        state.SetRecorder(recorder);
                        Debug.Log($"[JRTI-Stream]: In-game recording started with {recorder.EncoderDescription}: {path}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[JRTI-Stream]: In-game recording could not start: {ex.Message}");
                        ServeError(ctx, 500, ex.Message);
                        return;
                    }
                }
            }

            ServeText(ctx, RecordingJson(state), "application/json");
        }

        private static void StopGameRecording(HttpListenerContext ctx, CameraStreamState state, bool discard)
        {
            var recorder = state.TakeRecorder();
            if (recorder != null)
            {
                if (discard) recorder.Discard();
                else recorder.Stop();
                Debug.Log($"[JRTI-Stream]: In-game recording {(discard ? "discarded" : "saved")}: {recorder.FilePath} " +
                          $"({recorder.FramesWritten} frames, {recorder.FramesDropped} dropped)");
            }
            ServeText(ctx, RecordingJson(state), "application/json");
        }

        private static string RecordingFileName(string cameraName, int cameraId)
        {
            var safe = UnsafeFileNameChars.Replace(cameraName ?? "", "_").Trim('_');
            if (safe.Length > 80) safe = safe.Substring(0, 80);
            if (safe.Length == 0) safe = "camera";
            return $"{safe}__cam{cameraId}__{DateTime.Now:yyyy-MM-dd_HHmmss}.mp4";
        }

        private static string CodecsJson()
        {
            var ids = new List<string>();
            foreach (var codec in VideoEncoders.Available)
                ids.Add($"\"{VideoEncoders.Id(codec)}\"");
            return $"[{string.Join(",", ids)}]";
        }

        internal static string RecordingJson(CameraStreamState state)
        {
            var recorder = state.Recorder;
            if (recorder == null) return "null";

            long bytes = 0;
            try { bytes = new FileInfo(recorder.FilePath).Length; } catch { }
            long elapsedMs = (long)(DateTime.UtcNow - state.RecordingStartedUtc).TotalMilliseconds;

            var sb = new StringBuilder("{");
            sb.Append($"\"file\":\"{EscapeJson(Path.GetFileName(recorder.FilePath))}\",");
            sb.Append($"\"paused\":{(recorder.IsPaused ? "true" : "false")},");
            sb.Append($"\"elapsedMs\":{elapsedMs},");
            sb.Append($"\"bytes\":{bytes},");
            sb.Append($"\"framesWritten\":{recorder.FramesWritten},");
            sb.Append($"\"framesDropped\":{recorder.FramesDropped},");
            sb.Append(recorder.Error == null ? "\"error\":null" : $"\"error\":\"{EscapeJson(recorder.Error)}\"");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
