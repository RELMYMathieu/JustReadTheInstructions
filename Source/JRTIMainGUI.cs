using System.Collections.Generic;
using HullcamVDS;
using KSP.UI.Screens;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class JRTIMainGUI : MonoBehaviour
    {
        private const int WindowId = 1900;
        private const float WindowWidth = 500;
        private const float MaxCameraListHeight = 280f;
        private const float EntryHeight = 28f;
        private const float CameraListRefreshInterval = 1f;

        private static Texture2D _appIcon;
        private static ApplicationLauncherButton _toolbarButton;
        private static bool _hasAddedButton;

        private bool _isVisible;
        private bool _uiHidden;
        private bool _stylesInitialized;
        private Rect _windowRect;
        private Vector2 _cameraListScroll;

        private GUIStyle _titleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _streamBtnStyle;
        private GUIStyle _stopBtnStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _dimLabelStyle;
        private GUIStyle _separatorStyle;

        private List<MuMechModuleHullCamera> _cachedAllCameras = new List<MuMechModuleHullCamera>();
        private List<MuMechModuleHullCamera> _cachedAvailableCameras = new List<MuMechModuleHullCamera>();
        private float _lastCameraRefresh;

        void Start()
        {
            _windowRect = new Rect(Screen.width - WindowWidth - 50, 100, WindowWidth, 100);

            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            AddToolbarButton();
            Debug.Log("[JRTI]: Main GUI initialized");
        }

        void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            RemoveToolbarButton();
        }

        void Update()
        {
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetKeyDown(KeyCode.F7))
            {
                _isVisible = !_isVisible;
            }

            if (_isVisible && Time.unscaledTime - _lastCameraRefresh > CameraListRefreshInterval)
            {
                RefreshCameraList();
                _lastCameraRefresh = Time.unscaledTime;
            }
        }

        void OnGUI()
        {
            if (!_isVisible || _uiHidden) return;

            if (!_stylesInitialized)
                InitStyles();

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "Just Read The Instructions  (Ctrl+Alt+F7)", GUILayout.Width(WindowWidth));
            ClampToScreen();
        }

        private void InitStyles()
        {
            var skin = HighLogic.Skin ?? GUI.skin;

            _titleStyle = new GUIStyle(skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            _buttonStyle = new GUIStyle(skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 4, 4)
            };

            _streamBtnStyle = new GUIStyle(skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 4, 4),
                normal = { textColor = new Color(0.4f, 1f, 0.4f) },
                hover = { textColor = new Color(0.4f, 1f, 0.4f) }
            };

            _stopBtnStyle = new GUIStyle(skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 4, 4),
                normal = { textColor = new Color(1f, 0.4f, 0.4f) },
                hover = { textColor = new Color(1f, 0.4f, 0.4f) }
            };

            _labelStyle = new GUIStyle(skin.label)
            {
                fontSize = 11,
                normal = { textColor = Color.white }
            };

            _dimLabelStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.gray }
            };

            _separatorStyle = new GUIStyle(skin.label)
            {
                fontSize = 1,
                margin = new RectOffset(0, 0, 4, 4),
                normal = { textColor = Color.gray }
            };

            _stylesInitialized = true;
        }

        private void DrawWindow(int windowId)
        {
            int openCount = HullCameraManager.Instance?.GetOpenCameraCount() ?? 0;
            int totalCount = _cachedAllCameras.Count;

            GUILayout.Space(2);
            GUILayout.Label($"Cameras: {openCount} open / {totalCount} total", _dimLabelStyle);
            GUILayout.Space(4);

            DrawCameraList();

            GUILayout.Space(6);

            DrawActionButtons(openCount);

            GUILayout.Space(4);
            GUILayout.Label($"localhost:{JRTISettings.StreamPort}", _dimLabelStyle);
            GUILayout.Space(4);

            GUI.DragWindow(new Rect(0, 0, WindowWidth, 24));
        }

        private void DrawCameraList()
        {
            if (_cachedAvailableCameras.Count == 0)
            {
                GUILayout.Label("No cameras available.", _dimLabelStyle);
                return;
            }

            float contentHeight = _cachedAvailableCameras.Count * EntryHeight;
            float viewHeight = Mathf.Min(contentHeight, MaxCameraListHeight);

            _cameraListScroll = GUILayout.BeginScrollView(
                _cameraListScroll,
                false,
                contentHeight > MaxCameraListHeight,
                GUILayout.Height(viewHeight)
            );

            foreach (var camera in _cachedAvailableCameras)
                DrawCameraRow(camera);

            GUILayout.EndScrollView();
        }

        private void DrawCameraRow(MuMechModuleHullCamera camera)
        {
            if (camera == null || camera.vessel == null) return;

            int stableId = HullCameraRenderer.GetStableId(camera);
            string vesselName = camera.vessel.GetDisplayName();
            string resolvedName = JRTICameraRuntime.ResolveName(camera.part.persistentId, camera.cameraName);
            string displayName = $"{vesselName}.{resolvedName}";
            bool streamOnly = HullCameraManager.Instance?.IsStreamOnly(camera) ?? false;
            bool streaming = JRTIStreamServer.Instance?.IsStreaming(stableId) ?? false;

            GUILayout.BeginHorizontal(GUILayout.Height(EntryHeight));

            if (GUILayout.Button(displayName, _buttonStyle))
                HullCameraManager.Instance?.OpenCamera(camera);

            if (streamOnly)
            {
                if (GUILayout.Button("■ Stop", streaming ? _streamBtnStyle : _stopBtnStyle, GUILayout.Width(72)))
                    HullCameraManager.Instance?.StopStream(stableId);
            }
            else
            {
                string streamLabel = streaming ? "● Stream" : "○ Stream";
                GUIStyle style = streaming ? _streamBtnStyle : _buttonStyle;
                if (GUILayout.Button(streamLabel, style, GUILayout.Width(72)))
                {
                    HullCameraManager.Instance?.StreamCamera(camera);
                    GUIUtility.systemCopyBuffer = $"http://localhost:{JRTISettings.StreamPort}/camera/{stableId}/stream";
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawActionButtons(int openCount)
        {
            if (_cachedAvailableCameras.Count > 1)
            {
                if (GUILayout.Button("Stream All", _buttonStyle))
                    StreamAllCameras();
            }

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy Index URL", _buttonStyle))
                GUIUtility.systemCopyBuffer = $"http://localhost:{JRTISettings.StreamPort}/";

            if (openCount > 0)
            {
                if (GUILayout.Button("Close All", _stopBtnStyle))
                    HullCameraManager.Instance?.CloseAllCameras();
            }

            GUILayout.EndHorizontal();

            if (GUILayout.Button("Settings", _buttonStyle))
                JRTISettingsGUI.Instance?.Toggle();
        }

        private void RefreshCameraList()
        {
            _cachedAllCameras = HullCameraManager.GetAllAvailableCameras();
            _cachedAvailableCameras.Clear();

            foreach (var camera in _cachedAllCameras)
            {
                if (camera == null || camera.vessel == null) continue;

                bool isOpen = HullCameraManager.Instance?.IsCameraOpen(camera) ?? false;
                bool isStreamOnly = HullCameraManager.Instance?.IsStreamOnly(camera) ?? false;

                if (!isOpen || isStreamOnly)
                    _cachedAvailableCameras.Add(camera);
            }

            _lastCameraRefresh = Time.unscaledTime;
        }

        private void StreamAllCameras()
        {
            foreach (var camera in _cachedAvailableCameras)
            {
                if (camera == null || camera.vessel == null) continue;
                HullCameraManager.Instance?.StreamCamera(camera);
            }
        }

        private void ClampToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }

        private void AddToolbarButton()
        {
            if (_hasAddedButton) return;

            _appIcon = GameDatabase.Instance.GetTexture("JustReadTheInstructions/Textures/icon", false);

            if (_appIcon == null)
                _appIcon = Texture2D.whiteTexture;

            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                OnToolbarButtonToggle,
                OnToolbarButtonToggle,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT,
                _appIcon
            );

            _hasAddedButton = true;
        }

        private void RemoveToolbarButton()
        {
            if (_toolbarButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
                _toolbarButton = null;
                _hasAddedButton = false;
            }
        }

        private void OnToolbarButtonToggle() => _isVisible = !_isVisible;
        private void OnHideUI() => _uiHidden = true;
        private void OnShowUI() => _uiHidden = false;
    }
}