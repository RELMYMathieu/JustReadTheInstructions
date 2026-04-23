using System;
using UnityEngine;

namespace JustReadTheInstructions
{
    public class HullCameraWindow
    {
        private readonly HullCameraRenderer _renderer;
        private readonly CameraTelemetry _telemetry;

        private Rect _windowRect;
        private float _windowWidth;
        private float _windowHeight;
        private float _previewWidth;
        private float _previewHeight;
        private float _scale = 1f;
        private bool _isResizing;
        private bool _minimalUI = JRTISettings.MinimalUI;
        private float _currentFOV;
        private float _minFOV;
        private float _maxFOV;

        private const float TitleBarHeight = 20;
        private const float ButtonSize = 18;
        private const float Margin = 2;
        private const float ControlsWidth = 60;
        private const float SliderMinFOV = 5f;
        private const float SliderMaxFOV = 120f;

        private static GUIStyle _titleStyle;
        private static GUIStyle _telemetryStyle;
        private static GUIStyle _buttonStyle;
        private static Font _telemetryFont;
        private static Texture2D _resizeTexture;

        public int WindowId { get; }
        public bool IsOpen { get; private set; } = true;

        public HullCameraWindow(HullCameraRenderer renderer, int windowId)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _telemetry = new CameraTelemetry(renderer.GetVessel());
            WindowId = windowId;
            _minFOV = renderer.GetMinFOV();
            _maxFOV = renderer.GetMaxFOV();
            _currentFOV = _maxFOV > 0f ? _maxFOV : renderer.GetFOV();

            InitializeStyles();
            CalculateInitialSize();

            _windowRect = new Rect(
                Screen.width - _windowWidth - 40,
                100 + (windowId * 30),
                _windowWidth,
                _windowHeight
            );
        }

        private static void InitializeStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(HighLogic.Skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = Color.white }
            };

            _telemetryFont = Font.CreateDynamicFontFromOSFont("Consolas", 14);
            if (_telemetryFont == null)
                _telemetryFont = Font.CreateDynamicFontFromOSFont("Courier New", 14);

            _telemetryStyle = new GUIStyle
            {
                alignment = TextAnchor.LowerCenter,
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                font = _telemetryFont
            };

            _buttonStyle = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 3, 3)
            };

            _resizeTexture = GameDatabase.Instance.GetTexture("JustReadTheInstructions/Textures/resizeSquare", false);
        }

        private void CalculateInitialSize()
        {
            float maxSize = JRTISettings.MaxPreviewSize;

            if (JRTISettings.FixedPreviewAspectRatio)
            {
                _previewWidth = _previewHeight = maxSize;
            }
            else
            {
                float aspectRatio = (float)JRTISettings.RenderWidth / JRTISettings.RenderHeight;
                if (aspectRatio >= 1f)
                {
                    _previewWidth = maxSize;
                    _previewHeight = maxSize / aspectRatio;
                }
                else
                {
                    _previewHeight = maxSize;
                    _previewWidth = maxSize * aspectRatio;
                }
            }

            RecalculateWindowSize();
        }

        private void RecalculateWindowSize()
        {
            float scaledWidth = _previewWidth * _scale;
            float scaledHeight = _previewHeight * _scale;

            if (_minimalUI)
            {
                _windowWidth = scaledWidth + 2 * Margin;
            }
            else
            {
                _windowWidth = scaledWidth + ControlsWidth + 3 * Margin;
            }

            _windowHeight = scaledHeight + TitleBarHeight + Margin;
        }

        public void Update()
        {
            if (!IsOpen || !_renderer.IsValid())
            {
                IsOpen = false;
                return;
            }

            _telemetry.Update();
        }

        public void CheckResize()
        {
            if (Event.current.type == EventType.MouseUp && _isResizing)
            {
                _isResizing = false;
            }
        }

        public void Draw()
        {
            if (!IsOpen) return;

            _windowRect = GUI.Window(
                WindowId,
                _windowRect,
                DrawWindow,
                _renderer.GetDisplayName(),
                HighLogic.Skin.window
            );

            ClampWindowToScreen();
        }

        private void DrawWindow(int windowId)
        {
            if (GUI.Button(
                new Rect(_windowWidth - ButtonSize - 2, 2, ButtonSize, ButtonSize),
                "×",
                HighLogic.Skin.button))
            {
                IsOpen = false;
                return;
            }

            bool streaming = JRTIStreamServer.Instance?.IsStreaming(_renderer.InstanceId) ?? false;
            GUI.color = streaming ? Color.green : Color.gray;
            GUI.Label(new Rect(_windowWidth - ButtonSize * 4, 2, ButtonSize * 3, ButtonSize), streaming ? "● LIVE" : "○ OFFLINE", _titleStyle);
            GUI.color = Color.white;

            float scaledWidth = _previewWidth * _scale;
            float scaledHeight = _previewHeight * _scale;
            Rect previewRect = new Rect(Margin, TitleBarHeight, scaledWidth, scaledHeight);

            GUI.DrawTexture(
                previewRect,
                _renderer.TargetTexture,
                ScaleMode.ScaleAndCrop,
                false
            );

            if (Event.current.type == EventType.MouseDown &&
                Event.current.clickCount == 2 &&
                previewRect.Contains(Event.current.mousePosition))
            {
                _minimalUI = !_minimalUI;
                RecalculateWindowSize();
                _windowRect.width = _windowWidth;
                _windowRect.height = _windowHeight;

                Event.current.Use();
            }

            if (!_minimalUI)
            {
                DrawTelemetry(previewRect);
                DrawControls(scaledWidth);
            }

            HandleResize();

            Rect dragRect = new Rect(0, 0, _windowWidth - ButtonSize - 4, TitleBarHeight);

            dragRect = Rect.MinMaxRect(
                0,
                0,
                Mathf.Max(dragRect.xMax, previewRect.xMax),
                Mathf.Max(dragRect.yMax, previewRect.yMax)
            );

            if (!(Event.current.type == EventType.MouseDown && Event.current.clickCount == 2))
            {
                GUI.DragWindow(dragRect);
            }
        }

        private void DrawTelemetry(Rect previewRect)
        {
            int fontSize = (int)Mathf.Clamp(12 * _scale, 8, 16);
            _telemetryStyle.fontSize = fontSize;

            Rect telemetryRect = new Rect(
                previewRect.x,
                previewRect.yMax - (fontSize * 2.5f),
                previewRect.width,
                fontSize * 2.5f
            );

            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(telemetryRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(telemetryRect, _telemetry.GetFormattedTelemetry(), _telemetryStyle);
        }

        private void DrawControls(float scaledWidth)
        {
            float controlX = scaledWidth + 2 * Margin;
            float controlY = TitleBarHeight + Margin;


            GUI.Label(
                new Rect(controlX, controlY, ControlsWidth, 20),
                "FOV",
                _titleStyle
            );
            controlY += 22;

            float newFOV = GUI.VerticalSlider(
                new Rect(controlX + 20, controlY, 20, 100),
                _currentFOV,
                SliderMaxFOV,
                SliderMinFOV
            );

            if (Math.Abs(newFOV - _currentFOV) > 0.1f)
            {
                _currentFOV = newFOV;
                _renderer.SetFieldOfView(_currentFOV);
            }

            bool outsideIntended = _minFOV > 0f && _maxFOV > 0f &&
                (_currentFOV < _minFOV || _currentFOV > _maxFOV);
            if (outsideIntended) GUI.color = new Color(1f, 0.65f, 0f);
            GUI.Label(
                new Rect(controlX, controlY + 105, ControlsWidth, 20),
                outsideIntended ? $"{_currentFOV:F0}° !" : $"{_currentFOV:F0}°",
                _titleStyle
            );
            if (outsideIntended) GUI.color = Color.white;

            float urlButtonY = controlY + 130;

            bool streaming = JRTIStreamServer.Instance?.IsStreaming(_renderer.InstanceId) ?? false;
            GUI.color = streaming ? Color.green : Color.gray;
            GUI.Label(new Rect(controlX, urlButtonY, ControlsWidth, 16), streaming ? "● LIVE" : "○ IDLE", _titleStyle);
            GUI.color = Color.white;

            if (GUI.Button(new Rect(controlX, urlButtonY + 18, ControlsWidth, 18), "Copy URL", _buttonStyle))
            {
                GUIUtility.systemCopyBuffer = $"http://localhost:{JRTISettings.StreamPort}/camera/{_renderer.InstanceId}/stream";
            }
        }

        private void HandleResize()
        {
            Rect resizeRect = new Rect(
                _windowWidth - ButtonSize,
                _windowHeight - ButtonSize,
                ButtonSize,
                ButtonSize
            );

            if (_resizeTexture != null)
            {
                GUI.DrawTexture(resizeRect, _resizeTexture, ScaleMode.StretchToFill, true);
            }
            else
            {
                GUI.Box(resizeRect, "⋰");
            }

            if (Event.current.type == EventType.MouseDown &&
                resizeRect.Contains(Event.current.mousePosition))
            {
                _isResizing = true;
                Event.current.Use();
            }

            if (_isResizing && Event.current.type == EventType.Repaint)
            {
                if (Math.Abs(Mouse.delta.x) > 0.01f || Math.Abs(Mouse.delta.y) > 0.01f)
                {
                    float diff = Mouse.delta.x + Mouse.delta.y;

                    float scaleDiff = diff / (_windowRect.width + _windowRect.height) * 100 * 0.01f;

                    if (Math.Abs(scaleDiff) < 0.01f)
                    {
                        scaleDiff = scaleDiff > 0 ? 0.01f : -0.01f;
                    }

                    _scale = Mathf.Clamp(
                        _scale + scaleDiff,
                        JRTISettings.MinWindowScale,
                        JRTISettings.MaxWindowScale
                    );

                    RecalculateWindowSize();
                    _windowRect.width = _windowWidth;
                    _windowRect.height = _windowHeight;
                }
            }
        }

        private void ClampWindowToScreen()
        {
            if (_windowRect.x < 0)
                _windowRect.x = 0;
            if (_windowRect.y < 0)
                _windowRect.y = 0;

            if (_windowRect.xMax > Screen.width)
                _windowRect.x = Screen.width - _windowRect.width;
            if (_windowRect.yMax > Screen.height)
                _windowRect.y = Screen.height - _windowRect.height;
        }

        public static void DestroyStaticResources()
        {
            if (_telemetryFont != null)
            {
                UnityEngine.Object.Destroy(_telemetryFont);
                _telemetryFont = null;
            }
            _titleStyle = null;
            _telemetryStyle = null;
            _buttonStyle = null;
            _resizeTexture = null;
        }

        public void Close()
        {
            IsOpen = false;
        }
    }
}
