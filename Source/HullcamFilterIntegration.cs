using System;
using System.Linq;
using System.Reflection;
using HullcamVDS;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class HullcamFilterIntegration
    {
        private const float DefaultNightVisionAmbience = 0.7f;

        private static bool? _isAvailable;
        private static FieldInfo _defaultAmbienceField;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    _isAvailable = AssemblyLoader.loadedAssemblies.Any(a =>
                        a.name.Equals("HullcamVDS", StringComparison.OrdinalIgnoreCase) ||
                        a.name.Equals("HullcamVDSContinued", StringComparison.OrdinalIgnoreCase));

                    Debug.Log(_isAvailable.Value
                        ? "[JRTI-HullcamFilter]: Integration enabled"
                        : "[JRTI-HullcamFilter]: HullcamVDS not found");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-HullcamFilter]: Error checking availability: {ex.Message}");
                    _isAvailable = false;
                }

                return _isAvailable.Value;
            }
        }

        public static void SyncToCamera(Camera targetCamera, MuMechModuleHullCamera hullCamera)
        {
            if (!IsAvailable || targetCamera == null || hullCamera == null)
                return;

            var mode = (CameraFilter.eCameraMode)(int)hullCamera.cameraMode;
            var filter = targetCamera.GetComponent<HullcamFeedFilter>();

            if (mode == CameraFilter.eCameraMode.Normal)
            {
                if (filter != null)
                    UnityEngine.Object.Destroy(filter);
                return;
            }

            if (filter == null)
            {
                filter = targetCamera.gameObject.AddComponent<HullcamFeedFilter>();
                filter.enabled = false;
            }

            filter.SetMode(mode);
        }

        public static void RemoveFromCamera(Camera targetCamera)
        {
            if (targetCamera == null)
                return;

            var filter = targetCamera.GetComponent<HullcamFeedFilter>();
            if (filter != null)
                UnityEngine.Object.Destroy(filter);
        }

        public static void RenderWithFilter(Camera targetCamera)
        {
            if (targetCamera == null)
                return;

            var filter = targetCamera.GetComponent<HullcamFeedFilter>();
            if (filter == null || !filter.IsReady)
            {
                targetCamera.Render();
                return;
            }

            bool nightVision = filter.Mode == CameraFilter.eCameraMode.NightVision;
            var saved = default(FeedAmbient.Saved);

            if (nightVision)
            {
                float level = filter.NightVisionAmbience ?? DefaultNightVisionAmbience;
                saved = FeedAmbient.Override(new Color(level, level, level, 1f));
            }

            filter.enabled = true;
            targetCamera.Render();
            filter.enabled = false;

            if (nightVision)
                FeedAmbient.Restore(saved);
        }

        public static bool TryGetUnboostedAmbient(out Color ambient)
        {
            ambient = default;
            if (!IsAvailable || !MainViewInNightVision())
                return false;

            var kspAmbient = FeedAmbient.KspAmbient();
            if (kspAmbient.HasValue)
            {
                ambient = kspAmbient.Value;
                return true;
            }

            var level = MainViewDefaultAmbience();
            if (!level.HasValue)
                return false;

            ambient = new Color(level.Value, level.Value, level.Value, 1f);
            return true;
        }

        private static bool MainViewInNightVision()
        {
            var current = MuMechModuleHullCamera.sCurrentCamera;
            return current != null && (CameraFilter.eCameraMode)(int)current.cameraMode == CameraFilter.eCameraMode.NightVision;
        }

        private static float? MainViewDefaultAmbience()
        {
            var mainView = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            var filter = mainView != null ? mainView.GetComponent<MovieTimeFilter>() : null;
            if (filter == null || !(filter.GetFilter() is CameraFilterNightVision nightVision))
                return null;

            if (_defaultAmbienceField == null)
                _defaultAmbienceField = typeof(CameraFilterNightVision)
                    .GetField("defaultAmbienceLevel", BindingFlags.NonPublic | BindingFlags.Instance);

            return _defaultAmbienceField?.GetValue(nightVision) as float?;
        }

        public static string GetDiagnosticInfo(Camera nearCamera)
        {
            if (!IsAvailable)
                return "HullcamFilter: unavailable\n";

            var filter = nearCamera != null ? nearCamera.GetComponent<HullcamFeedFilter>() : null;
            if (filter == null)
                return "HullcamFilter: none\n";

            return filter.IsReady
                ? $"HullcamFilter: {filter.Mode}, own material: {filter.HasOwnMaterial}\n"
                : $"HullcamFilter: {filter.Mode}, not ready\n";
        }
    }

    public class HullcamFeedFilter : MonoBehaviour
    {
        private const string TitleTextureFile = "dockingdisplay.png";

        private static readonly FieldInfo SharedMaterialField = typeof(CameraFilter)
            .GetField("mtShader", BindingFlags.NonPublic | BindingFlags.Static);

        private static Texture2D _titleTexture;
        private static FieldInfo _ambienceLevelField;

        private CameraFilter _filter;
        private Material _material;

        public CameraFilter.eCameraMode Mode { get; private set; } = CameraFilter.eCameraMode.Normal;
        public bool IsReady => _filter != null;
        public bool HasOwnMaterial => _material != null;

        public float? NightVisionAmbience
        {
            get
            {
                if (!(_filter is CameraFilterNightVision))
                    return null;

                if (_ambienceLevelField == null)
                    _ambienceLevelField = typeof(CameraFilterNightVision)
                        .GetField("ambienceLevel", BindingFlags.NonPublic | BindingFlags.Instance);

                return _ambienceLevelField?.GetValue(_filter) as float?;
            }
        }

        public void SetMode(CameraFilter.eCameraMode mode)
        {
            if (mode == Mode && _filter != null)
                return;

            try
            {
                var filter = CameraFilter.CreateFilter(mode);
                if (filter == null || !filter.Activate())
                {
                    Debug.LogWarning($"[JRTI-HullcamFilter]: Could not create filter for mode {mode}");
                    return;
                }

                _filter = filter;
                Mode = mode;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-HullcamFilter]: Failed to create filter for mode {mode}: {ex.Message}");
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture target)
        {
            var shared = SharedMaterialField?.GetValue(null) as Material;
            if (_filter == null || shared == null)
            {
                Graphics.Blit(source, target);
                return;
            }

            if (_material == null)
                _material = CreateOwnMaterial(shared);

            SharedMaterialField.SetValue(null, _material);
            try
            {
                _filter.RenderTitlePage(true, TitleTexture);
                _filter.RenderImageWithFilter(source, target);
            }
            finally
            {
                SharedMaterialField.SetValue(null, shared);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        private static Texture2D TitleTexture
        {
            get
            {
                if (_titleTexture == null)
                {
                    _titleTexture = CameraFilter.LoadTextureFile(TitleTextureFile);
                    if (_titleTexture != null)
                        _titleTexture.wrapMode = TextureWrapMode.Clamp;
                }
                return _titleTexture;
            }
        }

        private static Material CreateOwnMaterial(Material shared)
        {
            var material = new Material(shared.shader);
            SetTextureFrom(material, "_VignetteTex", "filmVignette");
            SetTextureFrom(material, "_Overlay1Tex", "nvMesh");
            SetTextureFrom(material, "_Overlay2Tex", "noise");
            return material;
        }

        private static void SetTextureFrom(Material material, string property, string sharedField)
        {
            var texture = typeof(CameraFilter)
                .GetField(sharedField, BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as Texture2D;

            if (texture != null)
                material.SetTexture(property, texture);
        }
    }
}
