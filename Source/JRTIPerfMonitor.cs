using System;
using System.Threading;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class JRTIPerfMonitor : MonoBehaviour
    {
        public static JRTIPerfMonitor Instance { get; private set; }

        private const float SampleIntervalSeconds = 1f;
        private const int WindowId = 1903;
        private const int NameColumnChars = 28;

        private static readonly (string Title, float Width)[] OverlayColumns =
        {
            ("Camera", 190f), ("Mode", 52f), ("Rend/s", 48f), ("Render ms", 72f), ("FPS", 36f),
            ("Capt ms", 52f), ("Readback", 64f), ("Copy ms", 52f), ("Enc wait", 60f), ("Enc ms", 64f),
            ("KB", 40f), ("Defer/s", 50f), ("Clients", 50f)
        };

        private static readonly string[] OverlayHeader = Array.ConvertAll(OverlayColumns, c => c.Title);

        private volatile PerfSnapshot _latest;
        internal PerfSnapshot Latest => _latest;

        private float _windowStart;
        private int _frames;
        private PerfStat _frameMs;
        private PerfStat _jrtiMs;
        private double _gcFrameMaxMs;
        private bool _previousFrameCollected;
        private int _lastGcCount;
        private int _gcBaseline;
        private long _recordedBytesBaseline;

        private JRTIPerfLog _log;
        private bool _overlayVisible;
        private bool _lastHotkeyState;
        private Rect _windowRect;
        private string[] _overlayLines = new string[0];
        private string[][] _overlayRows = new string[0][];
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            _windowRect = new Rect(Mathf.Max(0f, Screen.width - 850f), 60f, 830f, 160f);
            ResetWindow(Time.unscaledTime);
        }

        void OnDestroy()
        {
            StopLog();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            bool hotkey =
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetKey(KeyCode.F7);

            if (hotkey && !_lastHotkeyState) ToggleOverlay();
            _lastHotkeyState = hotkey;
        }

        void LateUpdate()
        {
            SampleFrame();
            if (Time.unscaledTime - _windowStart >= SampleIntervalSeconds)
                PublishSnapshot();
        }

        void OnGUI()
        {
            if (!_overlayVisible) return;
            if (_labelStyle == null) InitStyles();
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawOverlay, "JRTI Performance  (Ctrl+Alt+F7)");
        }

        public void ToggleOverlay()
        {
            _overlayVisible = !_overlayVisible;
            if (_overlayVisible && _latest != null)
                BuildOverlay(_latest);
        }

        public bool IsLogging => _log != null;

        public void StartLog()
        {
            if (_log != null) return;
            try
            {
                _log = JRTIPerfLog.Start();
                Debug.Log($"[JRTI-Perf]: CSV log started: {_log.BaseName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Perf]: Could not start CSV log: {ex.Message}");
            }
        }

        public void StopLog()
        {
            if (_log == null) return;
            Debug.Log($"[JRTI-Perf]: CSV log stopped: {_log.BaseName} ({_log.Rows} rows)");
            _log.Dispose();
            _log = null;
        }

        private void SampleFrame()
        {
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            _frames++;
            _frameMs.Add(frameMs);
            _jrtiMs.Add(JRTIPerf.TakeFrameMainThreadMs());

            int gcCount = GC.CollectionCount(0);
            bool collected = gcCount != _lastGcCount;
            if (collected || _previousFrameCollected)
                _gcFrameMaxMs = Math.Max(_gcFrameMaxMs, frameMs);
            _previousFrameCollected = collected;
            _lastGcCount = gcCount;
        }

        private void PublishSnapshot()
        {
            float now = Time.unscaledTime;
            var snapshot = BuildSnapshot(now - _windowStart);
            _latest = snapshot;

            if (_log != null)
            {
                try { _log.Write(snapshot); }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-Perf]: CSV log write failed: {ex.Message}");
                    StopLog();
                }
            }

            if (_overlayVisible)
                BuildOverlay(snapshot);

            ResetWindow(now);
        }

        private PerfSnapshot BuildSnapshot(double seconds)
        {
            var server = JRTIStreamServer.Instance;
            var manager = HullCameraManager.Instance;
            var cameras = JRTIPerf.TakeCameraSamples();

            foreach (var camera in cameras)
            {
                camera.Seconds = seconds;
                camera.HasWindow = manager != null && manager.HasWindow(camera.Id);
                if (server != null)
                    server.GetClientCounts(camera.Id, out camera.StreamClients, out camera.PreviewClients);
            }

            ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
            ThreadPool.GetAvailableThreads(out int freeWorkers, out int freeIo);
            ThreadPool.GetMinThreads(out int minWorkers, out _);

            int streamClients = 0, previewClients = 0;
            foreach (var camera in cameras)
            {
                streamClients += camera.StreamClients;
                previewClients += camera.PreviewClients;
            }

            long recordedBytes = JRTIStreamServer.RecordedBytesTotal;

            return new PerfSnapshot
            {
                Utc = DateTime.UtcNow,
                Seconds = seconds,
                Frames = _frames,
                FrameMs = _frameMs,
                JrtiMs = _jrtiMs,
                GcCollections = _lastGcCount - _gcBaseline,
                GcFrameMaxMs = _gcFrameMaxMs,
                HeapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
                PoolBusy = maxWorkers - freeWorkers,
                PoolMin = minWorkers,
                PoolIoBusy = maxIo - freeIo,
                StreamClients = streamClients,
                PreviewClients = previewClients,
                Recordings = server?.RecordingCount ?? 0,
                RecordingKbps = seconds > 0.0 ? (recordedBytes - _recordedBytesBaseline) / 1024.0 / seconds : 0.0,
                SpreadCaptures = JRTISettings.SpreadCaptures,
                MaxFps = JRTISettings.StreamMaxFps,
                Cameras = cameras
            };
        }

        private void ResetWindow(float now)
        {
            _windowStart = now;
            _frames = 0;
            _frameMs = default(PerfStat);
            _jrtiMs = default(PerfStat);
            _gcFrameMaxMs = 0.0;
            _lastGcCount = GC.CollectionCount(0);
            _gcBaseline = _lastGcCount;
            _recordedBytesBaseline = JRTIStreamServer.RecordedBytesTotal;
        }

        private void BuildOverlay(PerfSnapshot s)
        {
            _overlayLines = new[]
            {
                $"FPS {s.PerSecond(s.Frames):0.0}    frame {s.FrameMs.Average:0.0} ms (max {s.FrameMs.Max:0.0})    " +
                $"JRTI main thread {s.JrtiMs.Average:0.00} ms/frame (max {s.JrtiMs.Max:0.00})",
                $"GC {s.GcCollections} in {s.Seconds:0.0}s (worst GC frame {s.GcFrameMaxMs:0} ms)    heap {s.HeapMb:0} MB    " +
                $"pool threads busy {s.PoolBusy} (min {s.PoolMin}, IO {s.PoolIoBusy})",
                $"Clients {s.StreamClients} stream + {s.PreviewClients} preview    recordings {s.Recordings} ({s.RecordingKbps:0} KB/s)    " +
                $"Max FPS {s.MaxFps}"
            };

            _overlayRows = new string[s.Cameras.Count][];
            for (int i = 0; i < s.Cameras.Count; i++)
                _overlayRows[i] = FormatCameraRow(s.Cameras[i]);
        }

        private static string[] FormatCameraRow(CameraPerfSample c)
        {
            var render = c[CameraMetric.Render];
            var encode = c[CameraMetric.Encode];
            return new[]
            {
                c.Name.Length > NameColumnChars ? c.Name.Substring(0, NameColumnChars - 1) + "…" : c.Name,
                c.HasWindow ? "window" : "stream",
                $"{c.PerSecond(render.Count):0}",
                $"{render.Average:0.0}/{render.Max:0.0}",
                $"{c.PerSecond(encode.Count):0}",
                $"{c[CameraMetric.CaptureIssue].Average:0.00}",
                $"{c[CameraMetric.Readback].Average:0}",
                $"{c[CameraMetric.ReadbackCopy].Average:0.00}",
                $"{c[CameraMetric.EncodeWait].Average:0.0}",
                $"{encode.Average:0.0}/{encode.Max:0.0}",
                $"{c[CameraMetric.JpegKb].Average:0}",
                $"{c.PerSecond(c.Deferred):0}",
                $"{c.StreamClients}+{c.PreviewClients}"
            };
        }

        private void InitStyles()
        {
            var skin = HighLogic.Skin ?? GUI.skin;
            _labelStyle = new GUIStyle(skin.label) { fontSize = 11, wordWrap = false, normal = { textColor = Color.white } };
            _headerStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
        }

        private void DrawOverlay(int id)
        {
            foreach (var line in _overlayLines)
                GUILayout.Label(line, _labelStyle);

            GUILayout.Space(4);
            DrawRow(OverlayHeader, _headerStyle);
            foreach (var row in _overlayRows)
                DrawRow(row, _labelStyle);
            if (_overlayRows.Length == 0)
                GUILayout.Label("No open cameras", _labelStyle);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            bool spread = GUILayout.Toggle(JRTISettings.SpreadCaptures, "Spread camera renders across frames");
            if (spread != JRTISettings.SpreadCaptures)
            {
                JRTISettings.SpreadCaptures = spread;
                JRTISettings.Save();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(IsLogging ? $"Stop CSV log ({_log.Rows} rows)" : "Start CSV log", GUILayout.Width(170f)))
            {
                if (IsLogging) StopLog();
                else StartLog();
            }
            if (GUILayout.Button("Close", GUILayout.Width(60f)))
                _overlayVisible = false;
            GUILayout.EndHorizontal();

            if (IsLogging)
                GUILayout.Label($"Logging to PluginData/PerfLogs/{_log.BaseName}-*.csv", _labelStyle);

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawRow(string[] cells, GUIStyle style)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < cells.Length && i < OverlayColumns.Length; i++)
                GUILayout.Label(cells[i], style, GUILayout.Width(OverlayColumns[i].Width));
            GUILayout.EndHorizontal();
        }
    }
}
