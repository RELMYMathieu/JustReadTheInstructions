using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class ScattererIntegration
    {
        private const string MainDepthFlagField = "useDbufferOnCamera";

        private static bool? _isAvailable;
        private static Type[] _hookTypes;
        private static readonly Dictionary<int, int> _hookSources = new Dictionary<int, int>();
        private static readonly List<Component> _sourceHooks = new List<Component>();
        private static readonly List<Component> _targetHooks = new List<Component>();

        private static readonly string[] _hookTypeNames =
        {
            "CameraRenderingHook",
            "SunflareCameraHook",
        };

        private static bool IsClonableHook(Type type)
        {
            foreach (var name in _hookTypeNames)
                if (type.Name.Contains(name))
                    return true;
            return false;
        }

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

                    _hookTypes = loaded.assembly.GetTypes()
                        .Where(t => !t.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(t) && IsClonableHook(t))
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

            SyncHooks(mainCam, camera);
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

            SyncHooks(scaledCam, camera);
        }

        public static void RemoveFromCamera(Camera camera)
        {
            if (!IsAvailable || camera == null || _hookTypes == null)
                return;

            foreach (var hookType in _hookTypes)
            {
                camera.GetComponents(hookType, _targetHooks);
                foreach (var hook in _targetHooks)
                {
                    _hookSources.Remove(hook.GetInstanceID());
                    UnityEngine.Object.Destroy(hook);
                }
            }
        }

        public static void SyncHooks(Camera source, Camera target)
        {
            if (!JRTISettings.EnableScatterer || _hookTypes == null || source == null || target == null)
                return;

            foreach (var hookType in _hookTypes)
            {
                source.GetComponents(hookType, _sourceHooks);
                target.GetComponents(hookType, _targetHooks);

                for (int i = 0; i < _sourceHooks.Count; i++)
                {
                    var sourceHook = (Behaviour)_sourceHooks[i];
                    var targetHook = i < _targetHooks.Count ? (Behaviour)_targetHooks[i] : AddHook(target, hookType);
                    if (targetHook != null)
                        Mirror(sourceHook, targetHook);
                }

                for (int i = _sourceHooks.Count; i < _targetHooks.Count; i++)
                    ((Behaviour)_targetHooks[i]).enabled = false;
            }
        }

        private static void Mirror(Behaviour sourceHook, Behaviour targetHook)
        {
            int targetId = targetHook.GetInstanceID();
            int sourceId = sourceHook.GetInstanceID();
            if (!_hookSources.TryGetValue(targetId, out var copiedFrom) || copiedFrom != sourceId)
            {
                CopyComponentFields(sourceHook, targetHook);
                targetHook.GetType().GetField(MainDepthFlagField)?.SetValue(targetHook, 0f);
                _hookSources[targetId] = sourceId;
            }

            targetHook.enabled = sourceHook.enabled;
        }

        private static Behaviour AddHook(Camera target, Type hookType)
        {
            try
            {
                var hook = target.gameObject.AddComponent(hookType) as Behaviour;
                Debug.Log($"[JRTI-Scatterer]: Added {hookType.Name} to {target.name}");
                return hook;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Scatterer]: Failed to add {hookType.Name}: {ex.Message}");
                return null;
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