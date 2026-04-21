using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class ScattererIntegration
    {
        private static bool? _isAvailable;
        private static Type[] _scattererMonoBehaviourTypes;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    var loaded = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "Scatterer");

                    if (loaded == null)
                    {
                        Debug.Log("[JRTI-Scatterer]: Not found");
                        _isAvailable = false;
                        return false;
                    }

                    _scattererMonoBehaviourTypes = loaded.assembly.GetTypes()
                        .Where(t => !t.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(t))
                        .ToArray();

                    _isAvailable = true;
                    Debug.Log("[JRTI-Scatterer]: Detected");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-Scatterer]: Error checking availability: {ex.Message}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        public static void ApplyToCamera(Camera camera)
        {
            if (camera == null) return;
            camera.allowMSAA = false;
            if (!IsAvailable) return;

            var mainCam = Camera.allCameras.FirstOrDefault(c => c.name == "Camera 00");
            if (mainCam == null)
            {
                Debug.LogWarning("[JRTI-Scatterer]: Camera 00 not found");
                return;
            }

            CopyCameraRenderingHooks(mainCam, camera);
        }

        public static void ApplyToScaledCamera(Camera camera)
        {
            if (!IsAvailable || camera == null) return;

            var scaledCam = Camera.allCameras.FirstOrDefault(c => c.name == "Camera ScaledSpace");
            if (scaledCam == null)
            {
                Debug.LogWarning("[JRTI-Scatterer]: Camera ScaledSpace not found");
                return;
            }

            CopyCameraRenderingHooks(scaledCam, camera);
        }

        public static void RemoveFromCamera(Camera camera)
        {
            if (!IsAvailable || camera == null || _scattererMonoBehaviourTypes == null)
                return;

            foreach (var hookType in _scattererMonoBehaviourTypes)
            {
                if (!hookType.Name.Contains("CameraRenderingHook"))
                    continue;

                var hook = camera.gameObject.GetComponent(hookType);
                if (hook != null)
                    UnityEngine.Object.Destroy(hook);
            }
        }

        private static void CopyCameraRenderingHooks(Camera source, Camera target)
        {
            if (_scattererMonoBehaviourTypes == null)
                return;

            foreach (var hookType in _scattererMonoBehaviourTypes)
            {
                if (!hookType.Name.Contains("CameraRenderingHook"))
                    continue;

                if (target.gameObject.GetComponent(hookType) != null)
                    continue;

                var sourceHook = source.gameObject.GetComponent(hookType);
                if (sourceHook == null)
                    continue;

                try
                {
                    var newHook = target.gameObject.AddComponent(hookType);
                    if (newHook != null)
                    {
                        CopyComponentFields(sourceHook, newHook);
                        Debug.Log($"[JRTI-Scatterer]: Added {hookType.Name} to {target.name}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[JRTI-Scatterer]: Failed to add {hookType.Name}: {ex.Message}");
                }
            }
        }

        private static void CopyComponentFields(Component source, Component target)
        {
            var type = source.GetType();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(target, field.GetValue(source)); }
                catch { }
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (prop.CanRead && prop.CanWrite
                        && prop.Name != "name"
                        && prop.Name != "tag"
                        && prop.Name != "hideFlags")
                        prop.SetValue(target, prop.GetValue(source));
                }
                catch { }
            }
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return "Scatterer not available\n";

            return $"Scatterer for {camera.name}:\n" +
                   $"- allowMSAA: {camera.allowMSAA}\n";
        }
    }
}