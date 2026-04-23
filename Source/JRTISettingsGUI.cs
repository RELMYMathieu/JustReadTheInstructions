using KSP.UI.Screens;
using System.Globalization;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class JRTISettingsGUI : MonoBehaviour
    {
        public static JRTISettingsGUI Instance { get; private set; }

        private static readonly string ModVersion =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private bool _isVisible;
        private bool _lastHotkey8State;
        private bool _lastHotkey9State;
        private Rect _windowRect = new Rect(200, 80, 420, 540);
        private Vector2 _scrollPosition;
        private const int WindowId = 1902;

        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _icon;

        private string _renderWidth;
        private string _renderHeight;
        private string _antiAliasing;
        private bool _renderEveryOtherFrame;
        private string _streamPort;

        private bool _enableDockingOverlay;
        private bool _fixedPreviewAspectRatio;
        private bool _minimalUI;

        private string _jpegQuality;
        private string _maxFps;
        private string _maxOpenCameras;

        private bool _secStream = true;
        private bool _secIntegrations = true;
        private bool _secDiagnostics = true;
        private bool _secTroubleshooting = false;

        private float _savedMessageTime = -10f;
        private string _prevFocusedControl = "";

        private GUIStyle _labelStyle;
        private GUIStyle _fieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _descriptionStyle;
        private GUIStyle _noteStyle;
        private GUIStyle _savedStyle;
        private GUIStyle _warningStyle;
        private bool _stylesInitialized;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(this);
        }

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            RemoveToolbarButton();
        }

        private void OnLauncherReady()
        {
            if (_toolbarButton != null) return;

            if (_icon == null)
                _icon = GameDatabase.Instance.GetTexture("JustReadTheInstructions/Textures/icon", false);

            const ApplicationLauncher.AppScenes nonFlightScenes =
                ApplicationLauncher.AppScenes.SPACECENTER |
                ApplicationLauncher.AppScenes.VAB |
                ApplicationLauncher.AppScenes.SPH |
                ApplicationLauncher.AppScenes.TRACKSTATION |
                ApplicationLauncher.AppScenes.MAINMENU;

            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                Toggle, Toggle,
                null, null, null, null,
                nonFlightScenes,
                _icon ?? Texture2D.whiteTexture
            );
        }

        private void OnLauncherDestroyed()
        {
            _toolbarButton = null;
        }

        private void RemoveToolbarButton()
        {
            if (_toolbarButton == null) return;
            if (ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
            _toolbarButton = null;
        }

        public void Toggle()
        {
            if (!_isVisible)
                SyncFromSettings();
            _isVisible = !_isVisible;

            if (_toolbarButton != null)
            {
                if (_isVisible) _toolbarButton.SetTrue(false);
                else _toolbarButton.SetFalse(false);
            }
        }

        void Update()
        {
            bool hotkey8 =
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetKey(KeyCode.F8);

            if (hotkey8 && !_lastHotkey8State) Toggle();
            _lastHotkey8State = hotkey8;

            bool hotkey9 =
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetKey(KeyCode.F9);

            if (hotkey9 && !_lastHotkey9State) Toggle();
            _lastHotkey9State = hotkey9;
        }

        void OnGUI()
        {
            if (!_isVisible) return;

            if (!_stylesInitialized)
                InitStyles();

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "JRTI - Settings & Integrations  (Ctrl+Alt+F8/F9)");
            ClampToScreen();
        }

        private void InitStyles()
        {
            var skin = HighLogic.Skin ?? GUI.skin;
            _labelStyle = new GUIStyle(skin.label) { fontSize = 11, normal = { textColor = Color.white } };
            _fieldStyle = new GUIStyle(skin.textField) { fontSize = 11 };
            _buttonStyle = new GUIStyle(skin.button) { fontSize = 11 };
            _sectionHeaderStyle = new GUIStyle(skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.8f, 0.9f, 1f) }
            };
            _toggleStyle = new GUIStyle(skin.toggle) { fontSize = 11, margin = new RectOffset(4, 4, 4, 4) };
            _descriptionStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = Color.gray }
            };
            _noteStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = new Color(1f, 1f, 0.65f) }
            };
            _savedStyle = new GUIStyle(skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.4f, 1f, 0.4f) }
            };
            _warningStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = new Color(1f, 0.7f, 0.4f) }
            };
            _stylesInitialized = true;
        }

        private void DrawWindow(int id)
        {
            if (Event.current.type == EventType.Repaint)
            {
                string currentFocus = GUI.GetNameOfFocusedControl();
                if (_prevFocusedControl != currentFocus && IsTextFieldControl(_prevFocusedControl))
                    TriggerSave();
                _prevFocusedControl = currentFocus;
            }

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, true);

            DrawSection("▶ Stream / Capture", ref _secStream, DrawStreamSection);
            GUILayout.Space(4);
            DrawSection("▶ Visual Mod Integrations", ref _secIntegrations, DrawIntegrationsSection);
            GUILayout.Space(4);
            DrawSection("▶ Diagnostics", ref _secDiagnostics, DrawDiagnosticsSection);
            GUILayout.Space(4);
            DrawSection("▶ Troubleshooting", ref _secTroubleshooting, DrawTroubleshootingSection);

            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close", _buttonStyle)) Toggle();
            GUILayout.FlexibleSpace();
            if (Time.realtimeSinceStartup - _savedMessageTime < 2f)
                GUILayout.Label("✓ Saved", _savedStyle);
            GUILayout.Label($"v{ModVersion}", _noteStyle);
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawSection(string title, ref bool open, System.Action drawContents)
        {
            if (GUILayout.Button(title, _sectionHeaderStyle))
                open = !open;

            if (open)
            {
                GUILayout.BeginVertical("box");
                drawContents();
                GUILayout.EndVertical();
            }
        }

        private void DrawStreamSection()
        {
            DrawField("Port", ref _streamPort, "f_port");
            DrawField("JPEG Quality  (1-100)", ref _jpegQuality, "f_quality");
            DrawField("Max FPS", ref _maxFps, "f_fps");
            GUILayout.Space(4);
            DrawField("Render Width", ref _renderWidth, "f_width");
            DrawField("Render Height", ref _renderHeight, "f_height");
            DrawField("Anti-Aliasing  (0=off / 1/2/4/8)", ref _antiAliasing, "f_aa");
            DrawField("Max Open Cameras", ref _maxOpenCameras, "f_cameras");
            GUILayout.Space(4);

            DrawToggle(ref _renderEveryOtherFrame, "Render every other frame (recommended)");
            if (!_renderEveryOtherFrame)
                GUILayout.Label("⚠ Rendering every frame doubles per-camera cost. Only viable on a top-tier GPU.", _warningStyle);
            GUILayout.Space(4);
            DrawToggle(ref _enableDockingOverlay, "Render overlay with telemetry on docking cameras");
            DrawToggle(ref _fixedPreviewAspectRatio, "Fixed preview aspect ratio (square preview window)");
            DrawToggle(ref _minimalUI, "Minimal UI by default (double-click preview to toggle)");
            GUILayout.Label("Render resolution and AA apply on next camera open.", _noteStyle);
            GUILayout.Label("Stream port change requires game restart.", _noteStyle);
        }

        private void DrawIntegrationsSection()
        {
            DrawIntegrationToggle(
                "Deferred Rendering",
                JRTISettings.EnableDeferred,
                v => JRTISettings.EnableDeferred = v,
                DeferredIntegration.IsAvailable,
                "Deferred shading pipeline for JRTI cameras"
            );
            GUILayout.Space(3);
            DrawIntegrationToggle(
                "TUFX Post-Processing",
                JRTISettings.EnableTUFX,
                v => JRTISettings.EnableTUFX = v,
                TUFXIntegration.IsAvailable,
                "TUFX post-processing effects (bloom, tone-mapping, etc.)"
            );
            GUILayout.Space(3);
            DrawIntegrationToggle(
                "Scatterer (Ocean & Atmosphere)",
                JRTISettings.EnableScatterer,
                v => JRTISettings.EnableScatterer = v,
                ScattererIntegration.IsAvailable,
                "Scatterer atmospheric scattering - disables MSAA on JRTI cameras"
            );
            GUILayout.Space(6);
            DrawIntegrationToggle(
                "EVE (Clouds & Water)",
                JRTISettings.EnableEVE,
                v => JRTISettings.EnableEVE = v,
                EVEIntegration.IsAvailable,
                "Environmental Visual Enhancements - clouds, water, atmospheric effects"
            );
            GUILayout.Space(3);
            DrawIntegrationToggle(
                "Parallax (Terrain Scatter)",
                JRTISettings.EnableParallax,
                v => JRTISettings.EnableParallax = v,
                ParallaxIntegration.IsAvailable,
                "Parallax-Continued - grass, rocks, trees near camera (can be heavy)"
            );
            GUILayout.Space(3);
            DrawIntegrationToggle(
                "Firefly (Re-entry Effects)",
                JRTISettings.EnableFirefly,
                v => JRTISettings.EnableFirefly = v,
                FireflyIntegration.IsAvailable,
                "Firefly - atmospheric re-entry plasma effects near camera"
            );
            GUILayout.Space(6);
            DrawIntegrationToggle(
                "HullcamVDS Camera Filter",
                JRTISettings.EnableHullcamFilter,
                v => JRTISettings.EnableHullcamFilter = v,
                HullcamFilterIntegration.IsAvailable,
                "Applies the active Hullcam filter/overlay (night-vision, CRT, etc.) to the stream frame"
            );
            GUILayout.Space(4);
            GUILayout.Label("Changes apply immediately to all open cameras. Re-open a camera to fully reinitialize its integration stack.", _noteStyle);
        }

        private void DrawDiagnosticsSection()
        {
            int openCameras = HullCameraManager.Instance?.GetOpenCameraCount() ?? 0;
            GUILayout.Label($"Open cameras: {openCameras}", _labelStyle);

            if (ParallaxIntegration.IsAvailable)
                GUILayout.Label($"Parallax scatters active: {ParallaxIntegration.HasActiveScatters()}", _labelStyle);

            GUILayout.Label($"HullcamFilter discovered: {HullcamFilterIntegration.IsAvailable}", _labelStyle);

            GUILayout.Space(6);
            if (GUILayout.Button("Print Diagnostics to Log", _buttonStyle))
                PrintDiagnostics();

            GUILayout.Space(4);
            GUILayout.Label("Output goes to KSP.log.", _noteStyle);
        }

        private void DrawTroubleshootingSection()
        {
            GUILayout.Label(
                "If a visual integration doesn't appear correctly in the stream, try:",
                _warningStyle
            );
            GUILayout.Label("  • Returning to the launchpad and re-launching", _descriptionStyle);
            GUILayout.Label("  • Disabling and re-enabling the stream feed", _descriptionStyle);
            GUILayout.Label("  • Closing and re-opening the camera window", _descriptionStyle);
            GUILayout.Space(6);
            GUILayout.Label(
                "Some integrations (Scatterer, EVE) hook directly into Unity camera setup and cannot be hot-swapped without reopening the camera.",
                _noteStyle
            );
            GUILayout.Space(6);
            if (GUILayout.Button("Reload Integrations (all cameras)", _buttonStyle))
            {
                HullCameraManager.Instance?.UpdateAllCameraVisualEffects();
                Debug.Log("[JRTI]: Integration reload requested from settings menu");
            }
        }

        private void DrawIntegrationToggle(string name, bool enabled, System.Action<bool> setEnabled, bool available, string description)
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();

            bool newValue = GUILayout.Toggle(enabled, name, _toggleStyle);
            if (newValue != enabled)
            {
                setEnabled(newValue);
                Debug.Log($"[JRTI]: {name} {(newValue ? "enabled" : "disabled")}");
                HullCameraManager.Instance?.UpdateAllCameraVisualEffects();
                TriggerSave();
            }

            GUILayout.FlexibleSpace();

            if (available)
            {
                GUI.color = Color.green;
                GUILayout.Label("✓", _labelStyle, GUILayout.Width(20));
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label("✗", _labelStyle, GUILayout.Width(20));
                GUI.color = Color.white;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(description, _descriptionStyle);
            GUILayout.EndVertical();
        }

        private void DrawField(string label, ref string value, string controlName)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle);
            GUI.SetNextControlName(controlName);
            value = GUILayout.TextField(value, _fieldStyle, GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }

        private void DrawToggle(ref bool value, string label)
        {
            bool newValue = GUILayout.Toggle(value, label, _toggleStyle);
            if (newValue != value)
            {
                value = newValue;
                TriggerSave();
            }
        }

        private void TriggerSave()
        {
            ApplyAndSave();
            _savedMessageTime = Time.realtimeSinceStartup;
        }

        private static bool IsTextFieldControl(string name) =>
            name == "f_port" || name == "f_quality" || name == "f_fps" ||
            name == "f_width" || name == "f_height" || name == "f_aa" || name == "f_cameras";

        private void ApplyAndSave()
        {
            if (int.TryParse(_renderWidth, out int w)) JRTISettings.RenderWidth = w;
            if (int.TryParse(_renderHeight, out int h)) JRTISettings.RenderHeight = h;
            if (int.TryParse(_antiAliasing, out int aa)) JRTISettings.AntiAliasing = aa;
            JRTISettings.RenderEveryOtherFrame = _renderEveryOtherFrame;
            JRTISettings.EnableDockingOverlay = _enableDockingOverlay;
            JRTISettings.FixedPreviewAspectRatio = _fixedPreviewAspectRatio;
            JRTISettings.MinimalUI = _minimalUI;
            if (int.TryParse(_streamPort, out int port)) JRTISettings.StreamPort = port;
            if (int.TryParse(_jpegQuality, out int q)) JRTISettings.StreamJpegQuality = q;
            if (int.TryParse(_maxFps, out int fps)) JRTISettings.StreamMaxFps = fps;
            if (uint.TryParse(_maxOpenCameras, out uint maxCams))
                JRTISettings.MaxOpenCameras = (uint)Mathf.Clamp((int)maxCams, 1, 64);

            JRTISettings.Save();
            SyncFromSettings();
        }

        private void SyncFromSettings()
        {
            _renderWidth = JRTISettings.RenderWidth.ToString();
            _renderHeight = JRTISettings.RenderHeight.ToString();
            _antiAliasing = JRTISettings.AntiAliasing.ToString();
            _renderEveryOtherFrame = JRTISettings.RenderEveryOtherFrame;
            _enableDockingOverlay = JRTISettings.EnableDockingOverlay;
            _fixedPreviewAspectRatio = JRTISettings.FixedPreviewAspectRatio;
            _minimalUI = JRTISettings.MinimalUI;
            _streamPort = JRTISettings.StreamPort.ToString();
            _jpegQuality = JRTISettings.StreamJpegQuality.ToString();
            _maxFps = JRTISettings.StreamMaxFps.ToString();
            _maxOpenCameras = JRTISettings.MaxOpenCameras.ToString();
        }

        private void PrintDiagnostics()
        {
            Debug.Log("[JRTI-Diag]: ===== Diagnostics =====");
            Debug.Log($"[JRTI-Diag]: Deferred: Available={DeferredIntegration.IsAvailable}, Enabled={JRTISettings.EnableDeferred}");
            Debug.Log($"[JRTI-Diag]: TUFX:     Available={TUFXIntegration.IsAvailable}, Enabled={JRTISettings.EnableTUFX}");
            Debug.Log($"[JRTI-Diag]: EVE:      Available={EVEIntegration.IsAvailable}, Enabled={JRTISettings.EnableEVE}");
            Debug.Log($"[JRTI-Diag]: Parallax: Available={ParallaxIntegration.IsAvailable}, Enabled={JRTISettings.EnableParallax}");
            Debug.Log($"[JRTI-Diag]: Firefly:  Available={FireflyIntegration.IsAvailable}, Enabled={JRTISettings.EnableFirefly}");
            Debug.Log($"[JRTI-Diag]: Scatterer:Available={ScattererIntegration.IsAvailable}, Enabled={JRTISettings.EnableScatterer}");
            Debug.Log($"[JRTI-Diag]: HullcamFilter: Available={HullcamFilterIntegration.IsAvailable}, Enabled={JRTISettings.EnableHullcamFilter}");
            if (ParallaxIntegration.IsAvailable)
                Debug.Log($"[JRTI-Diag]: Parallax scatters active: {ParallaxIntegration.HasActiveScatters()}");
            Debug.Log($"[JRTI-Diag]: Open cameras: {HullCameraManager.Instance?.GetOpenCameraCount() ?? 0}");
            Debug.Log("[JRTI-Diag]: =======================");
        }

        private void ClampToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }
    }
}
