using System.Collections.Generic;
using System.Linq;
using HullcamVDS;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class HullCameraManager : MonoBehaviour
    {
        public static HullCameraManager Instance { get; private set; }

        private readonly Dictionary<int, HullCameraRenderer> _renderers = new Dictionary<int, HullCameraRenderer>();
        private readonly Dictionary<int, HullCameraWindow> _windows = new Dictionary<int, HullCameraWindow>();
        private readonly HashSet<int> _streamOnlyRenderers = new HashSet<int>();
        private int _nextWindowId = 2000;

        private const float FrameTimeSmoothing = 0.1f;
        private const float MapExitRebuildDelaySeconds = 0.5f;

        private readonly struct DueRender
        {
            public readonly HullCameraRenderer Renderer;
            public readonly float Overdue;
            public readonly bool Capture;

            public DueRender(HullCameraRenderer renderer, float overdue, bool capture)
            {
                Renderer = renderer;
                Overdue = overdue;
                Capture = capture;
            }
        }

        private readonly List<DueRender> _dueRenders = new List<DueRender>();
        private readonly HashSet<int> _deferredRenders = new HashSet<int>();
        private readonly Dictionary<int, FrameSchedule> _windowSchedules = new Dictionary<int, FrameSchedule>();
        private float _smoothedFrameTime;
        private float _rebuildCamerasAt = -1f;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            JRTICameraRuntime.Reset();
            GameEvents.OnMapExited.Add(OnMapExited);
            Debug.Log("[JRTI]: Camera Manager initialized");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                GameEvents.OnMapExited.Remove(OnMapExited);
                CloseAllCameras();
                HullCameraWindow.DestroyStaticResources();
                Instance = null;
            }
        }

        void Update()
        {
            RebuildCamerasAfterMapView();
            UpdateAllRenderers();
            SyncStreamServerState();
            if (Time.frameCount % 60 == 0)
                CleanupInvalidCameras();
        }

        void LateUpdate() => UpdateAllWindows();

        void OnGUI()
        {
            CheckAllWindowResize();
            DrawAllWindows();
        }

        private void OnMapExited() => _rebuildCamerasAt = Time.unscaledTime + MapExitRebuildDelaySeconds;

        private void RebuildCamerasAfterMapView()
        {
            if (_rebuildCamerasAt < 0f || Time.unscaledTime < _rebuildCamerasAt || MapView.MapIsEnabled)
                return;

            _rebuildCamerasAt = -1f;
            foreach (var renderer in _renderers.Values)
                renderer.RebuildCameras();
        }

        private void UpdateAllRenderers()
        {
            var server = JRTIStreamServer.Instance;
            float now = Time.unscaledTime;
            UpdateSmoothedFrameTime();
            float earlyTolerance = _smoothedFrameTime * 0.5f;
            int scheduledCameras = 0;
            _dueRenders.Clear();

            foreach (var kvp in _renderers)
            {
                float overdue = float.NegativeInfinity;
                bool capture = server != null && server.TryGetCaptureOverdue(kvp.Key, now, out overdue);
                if (!capture)
                {
                    if (!_windows.ContainsKey(kvp.Key)) continue;
                    overdue = WindowSchedule(kvp.Key).Overdue(now);
                }

                scheduledCameras++;
                if (overdue >= -earlyTolerance) InsertByOverdue(new DueRender(kvp.Value, overdue, capture));
            }

            GrantDueRenders(RenderBudget(scheduledCameras), now);
        }

        private FrameSchedule WindowSchedule(int cameraId)
        {
            if (!_windowSchedules.TryGetValue(cameraId, out var schedule))
                _windowSchedules[cameraId] = schedule = new FrameSchedule();
            return schedule;
        }

        private void UpdateSmoothedFrameTime()
        {
            float frameTime = Time.unscaledDeltaTime;
            _smoothedFrameTime = _smoothedFrameTime > 0f
                ? Mathf.Lerp(_smoothedFrameTime, frameTime, FrameTimeSmoothing)
                : frameTime;
        }

        private int RenderBudget(int scheduledCameras)
        {
            if (!JRTISettings.SpreadCaptures || scheduledCameras <= 1)
                return scheduledCameras;

            float rendersPerFrame = scheduledCameras * JRTISettings.StreamMaxFps * _smoothedFrameTime;
            return Mathf.Clamp(Mathf.CeilToInt(rendersPerFrame), 1, scheduledCameras);
        }

        private void InsertByOverdue(DueRender render)
        {
            int index = _dueRenders.Count;
            while (index > 0 && _dueRenders[index - 1].Overdue < render.Overdue)
                index--;
            _dueRenders.Insert(index, render);
        }

        private void GrantDueRenders(int budget, float now)
        {
            for (int i = 0; i < _dueRenders.Count; i++)
            {
                var due = _dueRenders[i];
                int id = due.Renderer.InstanceId;

                if (i >= budget)
                {
                    _deferredRenders.Add(id);
                    JRTIPerf.RecordDeferred(id);
                    continue;
                }

                bool rephase = _deferredRenders.Remove(id);
                if (!due.Capture) WindowSchedule(id).Advance(now, JRTISettings.FramePeriod, rephase);
                RenderCamera(due.Renderer, due.Capture, rephase);
            }
        }

        private static void RenderCamera(HullCameraRenderer renderer, bool capture, bool rephaseCapture)
        {
            long start = JRTIPerf.Now();
            renderer.Render(capture, rephaseCapture);
            JRTIPerf.RecordMainThread(renderer.InstanceId, CameraMetric.Render, start);
        }

        private void UpdateAllWindows()
        {
            var closedWindows = new List<int>();

            foreach (var kvp in _windows)
            {
                kvp.Value.Update();
                if (!kvp.Value.IsOpen)
                    closedWindows.Add(kvp.Key);
            }

            foreach (var id in closedWindows)
            {
                _windows.Remove(id);
                _windowSchedules.Remove(id);
                _streamOnlyRenderers.Add(id);
            }
        }

        private void CheckAllWindowResize()
        {
            foreach (var window in _windows.Values)
                window.CheckResize();
        }

        private void DrawAllWindows()
        {
            foreach (var window in _windows.Values)
                window.Draw();
        }

        private void SyncStreamServerState()
        {
            var server = JRTIStreamServer.Instance;
            if (server == null) return;

            bool publishInfo = Time.frameCount % 30 == 0;
            foreach (var kvp in _renderers)
            {
                var renderer = kvp.Value;
                if (server.TryTakePendingFov(kvp.Key, out float fov))
                    renderer.SetFieldOfView(Mathf.Clamp(fov, renderer.GetMinFOV(), renderer.GetMaxFOV()));

                if (publishInfo)
                    server.PublishCameraInfo(kvp.Key, renderer.GetDisplayName(),
                        renderer.GetFOV(), renderer.GetMinFOV(), renderer.GetMaxFOV());
            }
        }

        private void CleanupInvalidCameras()
        {
            // Ensure timewarping oddness doesn't cause us to dispose renderers that are just paused
            if (TimeWarp.CurrentRate > 1.0f) return;
            var invalidIds = new List<int>();
            foreach (var kvp in _renderers)
            {
                if (!kvp.Value.IsValid())
                    invalidIds.Add(kvp.Key);
            }

            foreach (var id in invalidIds)
                CloseCamera(id);

            PruneRuntimeIds();
        }

        private void PruneRuntimeIds()
        {
            if (!FlightGlobals.ready) return;

            var live = new HashSet<(uint, int)>();
            foreach (var camera in GetAllAvailableCameras())
            {
                if (camera?.part != null)
                    live.Add((camera.part.persistentId, HullCameraRenderer.GetCameraIndex(camera)));
            }

            JRTICameraRuntime.RetainOnly(live);
        }

        public void OpenCamera(MuMechModuleHullCamera hullCamera)
        {
            if (hullCamera == null) return;

            int stableId = HullCameraRenderer.GetStableId(hullCamera);

            if (_renderers.TryGetValue(stableId, out var existingRenderer))
            {
                if (!_windows.ContainsKey(stableId))
                {
                    var window = new HullCameraWindow(existingRenderer, _nextWindowId++);
                    _windows.Add(stableId, window);
                    _streamOnlyRenderers.Remove(stableId);
                    Debug.Log($"[JRTI]: Opened window for existing stream '{existingRenderer.GetDisplayName()}'");
                }
                return;
            }

            uint maxOpenCameras = JRTISettings.MaxOpenCameras;
            if ((uint)_renderers.Count >= maxOpenCameras)
            {
                Debug.LogWarning($"[JRTI]: Cannot open camera - limit of {maxOpenCameras} reached");
                ScreenMessages.PostScreenMessage($"[JRTI] Camera limit reached ({maxOpenCameras})", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            try
            {
                var renderer = new HullCameraRenderer(hullCamera);
                var window = new HullCameraWindow(renderer, _nextWindowId++);
                _renderers.Add(stableId, renderer);
                _windows.Add(stableId, window);
                Debug.Log($"[JRTI]: Opened camera '{renderer.GetDisplayName()}'");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[JRTI]: Failed to open camera: {ex.Message}");
            }
        }

        public void StreamCamera(MuMechModuleHullCamera hullCamera)
        {
            if (hullCamera == null) return;

            int stableId = HullCameraRenderer.GetStableId(hullCamera);
            if (_renderers.ContainsKey(stableId)) return;

            uint maxOpenCameras = JRTISettings.MaxOpenCameras;
            if ((uint)_renderers.Count >= maxOpenCameras)
            {
                Debug.LogWarning($"[JRTI]: Cannot stream camera - limit of {maxOpenCameras} reached");
                ScreenMessages.PostScreenMessage($"[JRTI] Camera limit reached ({maxOpenCameras})", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            try
            {
                var renderer = new HullCameraRenderer(hullCamera);
                _renderers.Add(stableId, renderer);
                _streamOnlyRenderers.Add(stableId);
                Debug.Log($"[JRTI]: Streaming (no window) '{renderer.GetDisplayName()}'");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[JRTI]: Failed to start stream: {ex.Message}");
            }
        }

        public void CloseCamera(int stableId)
        {
            if (_renderers.TryGetValue(stableId, out var renderer))
            {
                renderer.Dispose();
                _renderers.Remove(stableId);
            }

            if (_windows.TryGetValue(stableId, out var window))
            {
                window.Close();
                _windows.Remove(stableId);
            }

            _streamOnlyRenderers.Remove(stableId);
            _deferredRenders.Remove(stableId);
            _windowSchedules.Remove(stableId);
        }

        public void StopStream(int stableId)
        {
            if (_streamOnlyRenderers.Contains(stableId))
                CloseCamera(stableId);
        }

        public void CloseAllCameras()
        {
            foreach (var id in _renderers.Keys.ToList())
                CloseCamera(id);

            Debug.Log("[JRTI]: All cameras closed");
        }

        public bool IsCameraOpen(MuMechModuleHullCamera hullCamera)
        {
            if (hullCamera == null) return false;
            return _renderers.ContainsKey(HullCameraRenderer.GetStableId(hullCamera));
        }

        public bool IsStreamOnly(MuMechModuleHullCamera hullCamera)
        {
            if (hullCamera == null) return false;
            return _streamOnlyRenderers.Contains(HullCameraRenderer.GetStableId(hullCamera));
        }

        public int GetOpenCameraCount() => _renderers.Count;

        public bool HasWindow(int stableId) => _windows.ContainsKey(stableId);

        public void UpdateAllCameraVisualEffects()
        {
            foreach (var renderer in _renderers.Values)
                renderer.UpdateVisualEffects();
        }

        public static List<MuMechModuleHullCamera> GetAllAvailableCameras()
        {
            var cameras = new List<MuMechModuleHullCamera>();

            if (!FlightGlobals.ready)
                return cameras;

            foreach (var vessel in FlightGlobals.VesselsLoaded)
                cameras.AddRange(vessel.FindPartModulesImplementing<MuMechModuleHullCamera>());

            return cameras;
        }
    }
}
