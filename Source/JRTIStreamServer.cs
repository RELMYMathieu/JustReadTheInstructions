using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        private readonly ConcurrentDictionary<int, float> _lastCaptureTimes
            = new ConcurrentDictionary<int, float>();
        private readonly ConcurrentDictionary<int, bool> _captureInFlight
            = new ConcurrentDictionary<int, bool>();
        private readonly ConcurrentDictionary<string, RecordingSession> _recordings
            = new ConcurrentDictionary<string, RecordingSession>();
        private readonly ConcurrentDictionary<string, byte> _finalizedSessions
            = new ConcurrentDictionary<string, byte>();

        private float MinCapturePeriod => 1f / Mathf.Max(1, JRTISettings.StreamMaxFps);

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            LaunchId = Guid.NewGuid().ToString("N");
            EnsureRecordingsDirectory();
        }

        void Start() => StartServer();

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

        public void RegisterCamera(int cameraId)
            => _states.GetOrAdd(cameraId, _ => new CameraStreamState());

        public void UnregisterCamera(int cameraId)
        {
            if (_states.TryRemove(cameraId, out var state))
                state.Dispose();
            _lastCaptureTimes.TryRemove(cameraId, out _);
            _captureInFlight.TryRemove(cameraId, out _);
        }

        public bool IsStreaming(int cameraId)
            => _states.TryGetValue(cameraId, out var s) && s.MjpegClientCount > 0;

        public bool HasActiveClients(int cameraId)
            => _states.TryGetValue(cameraId, out var s) && s.HasActiveClients;

        public void TryCaptureFrame(int cameraId, RenderTexture renderTexture)
        {
            if (!_states.TryGetValue(cameraId, out var state) || !state.HasActiveClients)
                return;

            float now = Time.unscaledTime;
            _lastCaptureTimes.TryGetValue(cameraId, out float last);
            if (now - last < MinCapturePeriod)
                return;

            _captureInFlight.TryGetValue(cameraId, out bool inFlight);
            if (inFlight)
                return;

            _lastCaptureTimes[cameraId] = now;
            _captureInFlight[cameraId] = true;

            int rtWidth = renderTexture.width;
            int rtHeight = renderTexture.height;
            int quality = JRTISettings.StreamJpegQuality;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                CaptureFrameSync(cameraId, renderTexture, rtWidth, rtHeight, quality);
                return;
            }

            AsyncGPUReadback.Request(renderTexture, 0, TextureFormat.RGB24, (request) =>
            {
                _captureInFlight[cameraId] = false;

                if (request.hasError || !_states.TryGetValue(cameraId, out var s))
                    return;

                var raw = request.GetData<byte>().ToArray();
                byte[] lut = s.HasAdjustment ? s.GetLut() : null;

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (lut != null) CameraImageAdjust.Apply(raw, lut);

                    var jpeg = ImageConversion.EncodeArrayToJPG(
                        raw, GraphicsFormat.R8G8B8_UNorm,
                        (uint)rtWidth, (uint)rtHeight, 0, quality);

                    if (jpeg != null && _states.TryGetValue(cameraId, out var s2))
                        s2.PushFrame(jpeg);
                });
            });
        }

        private void CaptureFrameSync(int cameraId, RenderTexture renderTexture, int rtWidth, int rtHeight, int quality)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            var tex = new Texture2D(rtWidth, rtHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rtWidth, rtHeight), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;

            var raw = tex.GetRawTextureData();
            Destroy(tex);

            _captureInFlight[cameraId] = false;

            if (!_states.TryGetValue(cameraId, out var state))
                return;

            byte[] lut = state.HasAdjustment ? state.GetLut() : null;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (lut != null) CameraImageAdjust.Apply(raw, lut);

                var jpeg = ImageConversion.EncodeArrayToJPG(
                    raw, GraphicsFormat.R8G8B8_UNorm,
                    (uint)rtWidth, (uint)rtHeight, 0, quality);

                if (jpeg != null && _states.TryGetValue(cameraId, out var s2))
                    s2.PushFrame(jpeg);
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

            bool started = TryBind($"http://*:{JRTISettings.StreamPort}/")
                        || TryBind($"http://localhost:{JRTISettings.StreamPort}/");

            if (!started)
            {
                Debug.LogError("[JRTI-Stream]: Could not bind to any address. Streaming disabled.");
                return;
            }

            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "JRTI-StreamServer" };
            _listenerThread.Start();
            _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "JRTI-RecordingWatchdog" };
            _watchdogThread.Start();

            Debug.Log($"[JRTI-Stream]: Web UI at http://localhost:{JRTISettings.StreamPort}/");
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

                    var cutoff = DateTime.UtcNow - RecordingIdleTimeout;
                    foreach (var kv in _recordings)
                    {
                        if (kv.Value.LastActivityUtc >= cutoff) continue;

                        _finalizedSessions.TryAdd(kv.Key, 0);
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

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                var trimmed = path == "/" ? "" : path.TrimEnd('/');

                if (trimmed == "" || trimmed == "/index.html") { ServeStaticFile(ctx, "index.html"); return; }
                if (trimmed == "/cameras") { ServeCameraList(ctx); return; }
                if (trimmed == "/session") { ServeText(ctx, $"{{\"launchId\":\"{LaunchId}\"}}", "application/json"); return; }
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