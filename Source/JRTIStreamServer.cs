using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public partial class JRTIStreamServer : MonoBehaviour
    {
        public static JRTIStreamServer Instance { get; private set; }
        public string LaunchId { get; private set; }

        private HttpListener _listener;
        private Thread _listenerThread;
        private Thread _watchdogThread;
        private volatile bool _running;

        private static readonly TimeSpan RecordingIdleTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<int, CameraStreamState> _states
            = new ConcurrentDictionary<int, CameraStreamState>();
        private readonly ConcurrentDictionary<string, RecordingSession> _recordings
            = new ConcurrentDictionary<string, RecordingSession>();
        private readonly ConcurrentDictionary<string, DateTime> _finalizedSessions
            = new ConcurrentDictionary<string, DateTime>();

        private static readonly TimeSpan FinalizedRetention = TimeSpan.FromSeconds(60);

        private const int PortCheckTimeoutMs = 3000;
        private const string SteamPortHint = "(Steam's web inspector often takes 8080)";
        private const string PortFixHint = "Pick another Port in JRTI settings (Ctrl+Alt+F8), then restart the game.";

        private enum PortCheckResult { ThisServer, OtherProgram, NoAnswer }

        public static string PortWarning { get; private set; }
        private volatile bool _portWarningPending;

        private static long _recordedBytesTotal;
        internal static long RecordedBytesTotal => Interlocked.Read(ref _recordedBytesTotal);
        internal int RecordingCount
        {
            get
            {
                int count = _recordings.Count;
                foreach (var state in _states.Values)
                    if (state.Recorder != null) count++;
                return count;
            }
        }

        void Awake()
        {
            if (!JRTISettings.EnableStreamServer)
            {
                Debug.Log("[JRTI-Stream]: Web server disabled in settings - in-game windows only");
                Destroy(this);
                return;
            }
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            LaunchId = Guid.NewGuid().ToString("N");
            EnsureRecordingsDirectory();
        }

        void Start() => StartServer();

        void Update()
        {
            if (!_portWarningPending) return;
            _portWarningPending = false;
            ShowPortWarning();
        }

        void OnDestroy()
        {
            StopServer();
            FinalizeAllRecordings();
            if (Instance == this) Instance = null;
        }

        private static void EnsureRecordingsDirectory()
        {
            try { Directory.CreateDirectory(RecordingsRoot); }
            catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: Could not create recordings directory: {ex.Message}"); }
        }

        public void RegisterCamera(int cameraId, int frameWidth, int frameHeight)
            => _states.GetOrAdd(cameraId, _ => new CameraStreamState(frameWidth, frameHeight));

        public void UnregisterCamera(int cameraId)
        {
            if (_states.TryRemove(cameraId, out var state))
                state.Dispose();
        }

        public void PublishCameraInfo(int cameraId, string displayName, float fov, float fovMin, float fovMax)
        {
            if (_states.TryGetValue(cameraId, out var s))
                s.PublishInfo(displayName, fov, fovMin, fovMax);
        }

        public bool TryTakePendingFov(int cameraId, out float fov)
        {
            fov = 0f;
            return _states.TryGetValue(cameraId, out var s) && s.TryTakePendingFov(out fov);
        }

        public bool IsStreaming(int cameraId)
            => _states.TryGetValue(cameraId, out var s) && s.MjpegClientCount > 0;

        public bool TryGetCaptureOverdue(int cameraId, float now, out float overdue)
        {
            overdue = float.NegativeInfinity;
            if (!_states.TryGetValue(cameraId, out var s) || !s.HasActiveClients)
                return false;
            overdue = s.CaptureOverdue(now);
            return true;
        }

        public void GetClientCounts(int cameraId, out int streamClients, out int previewClients)
        {
            streamClients = 0;
            previewClients = 0;
            if (!_states.TryGetValue(cameraId, out var s)) return;
            streamClients = s.MjpegClients.Count;
            previewClients = s.PreviewClients.Count;
        }

        public void CaptureFrame(int cameraId, RenderTexture renderTexture, bool rephase)
        {
            if (!_states.TryGetValue(cameraId, out var state))
                return;

            long start = JRTIPerf.Now();
            long sequence = state.BeginCapture(Time.unscaledTime, JRTISettings.FramePeriod, rephase);

            int width = renderTexture.width;
            int height = renderTexture.height;
            bool rgba = state.Recorder != null;
            var format = rgba ? TextureFormat.RGBA32 : TextureFormat.RGB24;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                var raw = ReadPixelsSync(state, renderTexture, format);
                EncodeAndPublish(cameraId, state, new CapturedFrame(raw, width, height, rgba, start, sequence));
                JRTIPerf.RecordSince(cameraId, CameraMetric.CaptureIssue, start);
                return;
            }

            AsyncGPUReadback.Request(renderTexture, 0, format, request =>
            {
                if (request.hasError || !_states.ContainsKey(cameraId))
                {
                    state.EndCapture(null);
                    return;
                }

                JRTIPerf.RecordSince(cameraId, CameraMetric.Readback, start);
                long copyStart = JRTIPerf.Now();
                var data = request.GetData<byte>();
                var raw = state.RentFrameBuffer(data.Length);
                data.CopyTo(raw);
                EncodeAndPublish(cameraId, state, new CapturedFrame(raw, width, height, rgba, start, sequence));
                JRTIPerf.RecordMainThread(cameraId, CameraMetric.ReadbackCopy, copyStart);
            });
            JRTIPerf.RecordSince(cameraId, CameraMetric.CaptureIssue, start);
        }

        private static byte[] ReadPixelsSync(CameraStreamState state, RenderTexture renderTexture, TextureFormat format)
        {
            var tex = state.GetReadbackTexture(renderTexture.width, renderTexture.height, format);

            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            RenderTexture.active = previous;

            var data = tex.GetRawTextureData<byte>();
            var raw = state.RentFrameBuffer(data.Length);
            data.CopyTo(raw);
            return raw;
        }

        private static void EncodeAndPublish(int cameraId, CameraStreamState state, CapturedFrame frame)
        {
            byte[] lut = state.HasAdjustment ? state.GetLut() : null;
            long queued = JRTIPerf.Now();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    long started = JRTIPerf.Now();
                    if (lut != null) CameraImageAdjust.Apply(frame.Raw, lut);
                    if (frame.Rgba) state.Recorder?.Write(frame.Raw, frame.CapturedAt);
                    if (!state.NeedsJpeg) return;

                    var jpeg = ImageConversion.EncodeArrayToJPG(
                        frame.Raw, frame.Format,
                        (uint)frame.Width, (uint)frame.Height, 0, JRTISettings.StreamJpegQuality);

                    if (jpeg == null) return;
                    state.PushFrame(jpeg, frame.Sequence);
                    JRTIPerf.RecordEncode(cameraId, queued, started, jpeg.Length);
                }
                finally
                {
                    state.EndCapture(frame.Raw);
                }
            });
        }

        private void StartServer()
        {
            if (!HttpListener.IsSupported)
            {
                Debug.LogWarning("[JRTI-Stream]: HttpListener not supported on this platform");
                return;
            }

            _listener = new HttpListener();
            int port = JRTISettings.StreamPort;

            bool started = TryBind($"http://*:{port}/")
                        || TryBind($"http://localhost:{port}/");

            if (!started)
            {
                PortWarning = $"Web UI could not start: port {port} is already used by another program {SteamPortHint}. {PortFixHint}";
                ShowPortWarning();
                return;
            }

            PortWarning = null;
            new Thread(() => VerifyPortOwnership(port)) { IsBackground = true, Name = "JRTI-PortCheck" }.Start();
            VideoEncoders.Prepare();

            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "JRTI-StreamServer" };
            _listenerThread.Start();
            _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "JRTI-RecordingWatchdog" };
            _watchdogThread.Start();

            Debug.Log($"[JRTI-Stream]: Web UI at http://localhost:{JRTISettings.StreamPort}/");
        }

        private static void ShowPortWarning()
        {
            Debug.LogError($"[JRTI-Stream]: {PortWarning}");
            ScreenMessages.PostScreenMessage($"[JRTI] {PortWarning}", 12f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void VerifyPortOwnership(int port)
        {
            switch (CheckPort(port))
            {
                case PortCheckResult.ThisServer:
                    return;
                case PortCheckResult.OtherProgram:
                    PortWarning = $"Another program also answers on port {port} {SteamPortHint}, so localhost:{port} may show its page instead of JRTI's. {PortFixHint}";
                    break;
                default:
                    PortWarning = $"Could not confirm the web UI on port {port}: either the port check timed out or the port is busy {SteamPortHint}. If the browser does not show JRTI's page: {PortFixHint}";
                    break;
            }
            _portWarningPending = true;
        }

        private PortCheckResult CheckPort(int port)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{port}/session");
                request.Proxy = null;
                request.Timeout = PortCheckTimeoutMs;
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd().Contains(LaunchId) ? PortCheckResult.ThisServer : PortCheckResult.OtherProgram;
            }
            catch (WebException ex) when (ex.Response != null)
            {
                ex.Response.Close();
                return PortCheckResult.OtherProgram;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Stream]: Port check on 127.0.0.1:{port} got no answer: {ex.Message}");
                return PortCheckResult.NoAnswer;
            }
        }

        private bool TryBind(string prefix)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                Debug.Log($"[JRTI-Stream]: Listening on {prefix}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Stream]: Could not bind {prefix}: {ex.Message}");
                return false;
            }
        }

        private void StopServer()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listenerThread?.Join(2000);
            _watchdogThread?.Join(2000);
            foreach (var state in _states.Values)
                state.Dispose();
            _states.Clear();
        }

        private void WatchdogLoop()
        {
            while (_running)
            {
                try
                {
                    Thread.Sleep(WatchdogInterval);
                    if (!_running) break;

                    PruneFinalizedSessions();

                    var cutoff = DateTime.UtcNow - RecordingIdleTimeout;
                    foreach (var kv in _recordings)
                    {
                        if (kv.Value.LastActivityUtc >= cutoff) continue;

                        _finalizedSessions[kv.Key] = DateTime.UtcNow;
                        if (_recordings.TryRemove(kv.Key, out var session))
                        {
                            try
                            {
                                session.Dispose();
                                if (session.BytesWritten == 0)
                                {
                                    try { File.Delete(session.DisplayPath); } catch { }
                                    Debug.Log($"[JRTI-Stream]: Recording auto-finalized (idle, deleted empty): {session.DisplayPath}");
                                }
                                else
                                {
                                    if (session.DisplayPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                                        FixMp4(session.DisplayPath);
                                    else if (session.DisplayPath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                                        FixWebm(session.DisplayPath);
                                    Debug.Log($"[JRTI-Stream]: Recording auto-finalized (idle): {session.DisplayPath} ({session.BytesWritten} bytes)");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[JRTI-Stream]: Watchdog finalize error: {ex.Message}");
                            }
                        }
                    }
                }
                catch (ThreadInterruptedException) { break; }
                catch (Exception ex) { if (_running) Debug.LogError($"[JRTI-Stream]: Watchdog error: {ex.Message}"); }
            }
        }

        private void PruneFinalizedSessions()
        {
            var staleCutoff = DateTime.UtcNow - FinalizedRetention;
            foreach (var kv in _finalizedSessions)
            {
                if (kv.Value < staleCutoff)
                    _finalizedSessions.TryRemove(kv.Key, out _);
            }
        }

        private void FinalizeAllRecordings()
        {
            foreach (var kv in _recordings)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _recordings.Clear();
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (HttpListenerException) when (!_running) { break; }
                catch (Exception ex) { if (_running) Debug.LogError($"[JRTI-Stream]: Accept error: {ex.Message}"); }
            }
        }

        private void ServeSession(HttpListenerContext ctx)
        {
            string inGameRecording = InGameRecordingAvailable ? "true" : "false";
            ServeText(ctx, $"{{\"launchId\":\"{LaunchId}\",\"inGameRecording\":{inGameRecording},\"codecs\":{CodecsJson()}}}", "application/json");
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                var trimmed = path == "/" ? "" : path.TrimEnd('/');

                if (trimmed == "" || trimmed == "/index.html") { ServeStaticFile(ctx, "index.html"); return; }
                if (trimmed == "/cameras") { ServeCameraList(ctx); return; }
                if (trimmed == "/streams") { ServeMultiStream(ctx); return; }
                if (trimmed == "/session") { ServeSession(ctx); return; }
                if (trimmed == "/debug/stats") { ServeDebugStats(ctx); return; }
                if (trimmed.StartsWith("/recordings/")) { HandleRecordingEndpoint(ctx, trimmed); return; }
                if (trimmed.StartsWith("/camera/")) { ServeCameraEndpoint(ctx, trimmed); return; }

                var relative = trimmed.TrimStart('/');
                if (!string.IsNullOrEmpty(relative)) { ServeStaticFile(ctx, relative); return; }

                ServeError(ctx, 404, "Not found");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Stream]: Request handler error: {ex.Message}");
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}