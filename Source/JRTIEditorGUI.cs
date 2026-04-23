using System.Collections.Generic;
using System.Linq;
using HullcamVDS;
using KSP.UI.Screens;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class JRTIEditorGUI : MonoBehaviour
    {
        public static JRTIEditorGUI Instance { get; private set; }

        private const int WindowId = 1903;

        private bool _isVisible;
        private Rect _windowRect = new Rect(200, 80, 530, 420);
        private Vector2 _scrollPosition;

        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _icon;

        private readonly Dictionary<uint, string> _draftNames = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> _draftIds = new Dictionary<uint, string>();

        private string _feedbackText = "";
        private float _feedbackUntil;

        private GUIStyle _labelStyle;
        private GUIStyle _dimLabelStyle;
        private GUIStyle _fieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _successStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInitialized;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
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
            if (Instance == this) Instance = null;
        }

        private void OnLauncherReady()
        {
            if (_toolbarButton != null) return;

            if (_icon == null)
                _icon = GameDatabase.Instance.GetTexture("JustReadTheInstructions/Textures/icon", false);

            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                Toggle, Toggle,
                null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
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
            _isVisible = !_isVisible;
            if (_isVisible) RefreshDraftState();

            if (_toolbarButton != null)
            {
                if (_isVisible) _toolbarButton.SetTrue(false);
                else _toolbarButton.SetFalse(false);
            }
        }

        void OnGUI()
        {
            if (!_isVisible) return;
            if (!_stylesInitialized) InitStyles();
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "JRTI — Camera IDs & Names");
            ClampToScreen();
        }

        private void InitStyles()
        {
            var skin = HighLogic.Skin ?? GUI.skin;
            _labelStyle = new GUIStyle(skin.label) { fontSize = 11, normal = { textColor = Color.white } };
            _dimLabelStyle = new GUIStyle(skin.label) { fontSize = 10, normal = { textColor = Color.gray } };
            _fieldStyle = new GUIStyle(skin.textField) { fontSize = 11 };
            _buttonStyle = new GUIStyle(skin.button) { fontSize = 11 };
            _warningStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(1f, 0.4f, 0.4f) }
            };
            _successStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.4f, 1f, 0.4f) }
            };
            _headerStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.7f, 0.85f, 1f) }
            };
            _stylesInitialized = true;
        }

        private void DrawWindow(int id)
        {
            var cameras = GetShipCameras();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, true);
            GUILayout.BeginVertical();

            if (cameras.Count == 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("No cameras on this craft.", _dimLabelStyle);
            }
            else
            {
                GUILayout.Label($"{cameras.Count} camera{(cameras.Count == 1 ? "" : "s")} on this craft", _dimLabelStyle);
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Part / Camera", _headerStyle, GUILayout.Width(160));
                GUILayout.Label("Custom Name", _headerStyle, GUILayout.Width(150));
                GUILayout.Label("ID", _headerStyle, GUILayout.Width(40));
                GUILayout.Label("", _headerStyle, GUILayout.Width(130));
                GUILayout.EndHorizontal();

                foreach (var camera in cameras)
                    DrawCameraRow(camera, cameras);
            }

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-assign All", _buttonStyle))
                AutoAssignAll(cameras);
            GUILayout.FlexibleSpace();
            if (Time.realtimeSinceStartup < _feedbackUntil)
                GUILayout.Label(_feedbackText, _successStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _buttonStyle))
                Toggle();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawCameraRow(MuMechModuleHullCamera camera, List<MuMechModuleHullCamera> allCameras)
        {
            uint pid = camera.part.persistentId;
            if (!_draftNames.ContainsKey(pid)) _draftNames[pid] = "";
            if (!_draftIds.ContainsKey(pid)) _draftIds[pid] = "";

            bool idIsValid = int.TryParse(_draftIds[pid], out int parsedId) && parsedId >= 1;

            bool sameCraftDuplicate = idIsValid && allCameras
                .Any(c => c.part.persistentId != pid &&
                          _draftIds.TryGetValue(c.part.persistentId, out string s) &&
                          int.TryParse(s, out int otherId) && otherId == parsedId);

            bool externalConflict = idIsValid && JRTICameraRuntime.IsIdTaken(pid, parsedId);
            bool idIsTaken = sameCraftDuplicate || externalConflict;

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label(camera.part.partInfo?.title ?? camera.part.name, _labelStyle);
            GUILayout.Label(camera.cameraName, _dimLabelStyle);
            GUILayout.EndVertical();

            _draftNames[pid] = GUILayout.TextField(_draftNames[pid], _fieldStyle, GUILayout.Width(150));

            if (idIsTaken) GUI.color = new Color(1f, 0.4f, 0.4f);
            _draftIds[pid] = GUILayout.TextField(_draftIds[pid], _fieldStyle, GUILayout.Width(40));
            GUI.color = Color.white;

            if (idIsTaken)
            {
                string warning = (externalConflict && !sameCraftDuplicate) ? "! other craft" : "! duplicate";
                GUILayout.Label(warning, _warningStyle, GUILayout.Width(75));
            }
            else
            {
                GUILayout.Space(75);
            }

            GUI.enabled = idIsValid && !idIsTaken;
            if (GUILayout.Button("Apply", _buttonStyle, GUILayout.Width(50)))
                ApplyCameraRow(camera);
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }

        private void ApplyCameraRow(MuMechModuleHullCamera camera)
        {
            uint pid = camera.part.persistentId;
            if (!_draftIds.TryGetValue(pid, out string idStr)) return;
            if (!int.TryParse(idStr, out int id) || id < 1) return;

            string name = _draftNames.TryGetValue(pid, out string n) ? n : "";
            JRTICameraRuntime.SetEntry(pid, name, id);

            string displayName = string.IsNullOrEmpty(name) ? camera.cameraName : name;
            _feedbackText = $"✓  {displayName}  (ID {id})";
            _feedbackUntil = Time.realtimeSinceStartup + 3f;
        }

        private void AutoAssignAll(List<MuMechModuleHullCamera> cameras)
        {
            foreach (var camera in cameras)
                JRTICameraRuntime.ResolveId(camera.part.persistentId);
            RefreshDraftState();

            _feedbackText = "✓  All cameras assigned";
            _feedbackUntil = Time.realtimeSinceStartup + 3f;
        }

        private void RefreshDraftState()
        {
            _draftNames.Clear();
            _draftIds.Clear();

            foreach (var camera in GetShipCameras())
            {
                uint pid = camera.part.persistentId;
                _draftNames[pid] = JRTICameraRuntime.GetStoredName(pid);
                int storedId = JRTICameraRuntime.GetStoredId(pid);
                _draftIds[pid] = storedId > 0 ? storedId.ToString() : "";
            }
        }

        private static List<MuMechModuleHullCamera> GetShipCameras()
        {
            var ship = EditorLogic.fetch?.ship;
            if (ship == null) return new List<MuMechModuleHullCamera>();
            return ship.parts
                .SelectMany(p => p.Modules.OfType<MuMechModuleHullCamera>())
                .ToList();
        }

        private void ClampToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }
    }
}
