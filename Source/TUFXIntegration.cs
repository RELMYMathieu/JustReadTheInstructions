using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class TUFXIntegration
    {
        private const string PostProcessing = "UnityEngine.Rendering.PostProcessing.";
        private const int TufxVolumeLayer = 0;
        private const int JrtiVolumeLayer = 31;
        private const float JrtiVolumePriority = 100f;

        private static bool? _isAvailable;
        private static Type _postProcessLayerType;
        private static Type _effectSettingsType;
        private static Type _motionBlurType;
        private static MethodInfo _initMethod;
        private static MethodInfo _quickVolumeMethod;
        private static PropertyInfo _resourcesProperty;
        private static PropertyInfo _managerProperty;
        private static FieldInfo _volumeLayerField;
        private static Component _motionBlurOffVolume;

        public static bool IsAvailable
        {
            get
            {
                if (!_isAvailable.HasValue) _isAvailable = Load();
                return _isAvailable.Value;
            }
        }

        public static void ApplyToCamera(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return;

            try
            {
                var resources = _resourcesProperty.GetValue(null);
                if (resources == null)
                {
                    Debug.LogWarning($"[JRTI-TUFX]: TUFX resources not loaded - no post-processing on {camera.name}");
                    return;
                }

                var layer = camera.gameObject.GetComponent(_postProcessLayerType);
                if (layer == null) layer = camera.gameObject.AddComponent(_postProcessLayerType);
                _initMethod.Invoke(layer, new[] { resources });
                _volumeLayerField.SetValue(layer, (LayerMask)(1 << TufxVolumeLayer | 1 << JrtiVolumeLayer));
                EnsureMotionBlurOff();

                Debug.Log($"[JRTI-TUFX]: Applied post-processing to {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-TUFX]: Failed to apply to {camera.name}: {(ex.InnerException ?? ex).Message}");
                RemoveFromCamera(camera);
            }
        }

        public static void RemoveFromCamera(Camera camera)
        {
            if (camera == null || _postProcessLayerType == null)
                return;

            var layer = camera.gameObject.GetComponent(_postProcessLayerType);
            if (layer == null)
                return;

            UnityEngine.Object.Destroy(layer);
            Debug.Log($"[JRTI-TUFX]: Removed from {camera.name}");
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return "TUFX not available";

            bool hasLayer = camera.GetComponent(_postProcessLayerType) != null;
            return $"TUFX Integration for {camera.name}:\n"
                   + $"- PostProcessLayer: {(hasLayer ? "Present" : "Missing")}\n"
                   + $"- Motion blur off for JRTI: {(_motionBlurOffVolume != null ? "Yes" : "No")}\n";
        }

        private static void EnsureMotionBlurOff()
        {
            if (_motionBlurOffVolume != null)
                return;

            var settings = Array.CreateInstance(_effectSettingsType, 1);
            settings.SetValue(ScriptableObject.CreateInstance(_motionBlurType), 0);
            var manager = _managerProperty.GetValue(null);
            _motionBlurOffVolume = (Component)_quickVolumeMethod.Invoke(manager, new object[] { JrtiVolumeLayer, JrtiVolumePriority, settings });
        }

        private static bool Load()
        {
            try
            {
                var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name == "TUFX")?.assembly;
                if (assembly == null)
                {
                    Debug.Log("[JRTI-TUFX]: TUFX not found - post-processing disabled");
                    return false;
                }

                _postProcessLayerType = assembly.GetType(PostProcessing + "PostProcessLayer");
                _effectSettingsType = assembly.GetType(PostProcessing + "PostProcessEffectSettings");
                _motionBlurType = assembly.GetType(PostProcessing + "MotionBlur");
                var managerType = assembly.GetType(PostProcessing + "PostProcessManager");
                var resourcesType = assembly.GetType(PostProcessing + "PostProcessResources");

                _initMethod = _postProcessLayerType?.GetMethod("Init", new[] { resourcesType });
                _volumeLayerField = _postProcessLayerType?.GetField("volumeLayer");
                _managerProperty = managerType?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                _quickVolumeMethod = managerType?.GetMethod("QuickVolume");
                _resourcesProperty = assembly.GetType("TUFX.TexturesUnlimitedFXLoader")?.GetProperty("Resources", BindingFlags.Public | BindingFlags.Static);

                if (_initMethod == null || _volumeLayerField == null || _managerProperty == null || _quickVolumeMethod == null
                    || _resourcesProperty == null || _effectSettingsType == null || _motionBlurType == null)
                {
                    Debug.LogWarning("[JRTI-TUFX]: TUFX types not found - incompatible version?");
                    return false;
                }

                Debug.Log("[JRTI-TUFX]: Integration enabled");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-TUFX]: Error checking availability: {ex.Message}");
                return false;
            }
        }
    }
}
