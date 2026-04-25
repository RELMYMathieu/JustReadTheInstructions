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

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            Debug.Log("[JRTI]: Camera Manager initialized");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                CloseAllCameras();
                HullCameraWindow.DestroyStaticResources();
                Instance = null;
            }
        }

        void Update()
        {
            UpdateAllRenderers();
            if (Time.frameCount % 60 == 0)
                CleanupInvalidCameras();
        }

        void LateUpdate() => UpdateAllWindows();

        void OnGUI()
        {
            CheckAllWindowResize();
            DrawAllWindows();
        }

        private void UpdateAllRenderers()
        {
            foreach (var kvp in _renderers)
            {
                bool hasWindow = _windows.ContainsKey(kvp.Key);
                kvp.Value.Update(hasWindow);
            }
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

        public bool HasCamera(int stableId) => _renderers.ContainsKey(stableId);

        public float? GetFov(int stableId)
            => _renderers.TryGetValue(stableId, out var r) ? r.GetFOV() : (float?)null;

        public float? GetFovMin(int stableId)
            => _renderers.TryGetValue(stableId, out var r) ? r.GetMinFOV() : (float?)null;

        public float? GetFovMax(int stableId)
            => _renderers.TryGetValue(stableId, out var r) ? r.GetMaxFOV() : (float?)null;

        public void SetFov(int stableId, float fov)
        {
            if (!_renderers.TryGetValue(stableId, out var r)) return;
            r.SetFieldOfView(Mathf.Clamp(fov, r.GetMinFOV(), r.GetMaxFOV()));
        }

        public int GetOpenCameraCount() => _renderers.Count;

        public string GetCameraDisplayName(int stableId)
            => _renderers.TryGetValue(stableId, out var r) ? r.GetDisplayName() : null;

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