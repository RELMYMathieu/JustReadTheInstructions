using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    public static class EVEIntegration
    {
        private static bool? _isAvailable;
        private static Assembly _eveAssembly;

        private static Type _wetSurfacesRendererType;
        private static Type _screenSpaceShadowsRendererType;
        private static Type _volumetricCloudsRendererType;
        private static Type _particleFieldRendererType;

        private static Type _deferredCameraBufferType;
        private static Type _deferredVolumetricCloudsRendererType;

        private static readonly CameraEvent[] NewEveEvents =
        {
            CameraEvent.BeforeReflections,
            CameraEvent.BeforeLighting,
            CameraEvent.AfterLighting,
            CameraEvent.BeforeImageEffects
        };

        private static readonly CameraEvent[] OldEveEvents =
        {
            CameraEvent.AfterForwardAlpha,
            CameraEvent.AfterForwardOpaque
        };

        private static bool IsNewEve => _wetSurfacesRendererType != null
                                     || _volumetricCloudsRendererType != null
                                     || _particleFieldRendererType != null;

        private static bool IsOldEve => _deferredCameraBufferType != null
                                     || _deferredVolumetricCloudsRendererType != null;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                    _eveAssembly = allAssemblies.FirstOrDefault(a =>
                        !a.IsDynamic &&
                        a.GetTypes().Any(t => t.Namespace == "Atmosphere"));

                    if (_eveAssembly == null)
                    {
                        Debug.Log("[JRTI-EVE]: EVE not found - water and cloud effects disabled");
                        _isAvailable = false;
                        return false;
                    }

                    Debug.Log($"[JRTI-EVE]: Found EVE assembly: {_eveAssembly.GetName().Name}");

                    _wetSurfacesRendererType = _eveAssembly.GetType("Atmosphere.WetSurfacesPerCameraRenderer");
                    _screenSpaceShadowsRendererType = _eveAssembly.GetType("Atmosphere.ScreenSpaceShadowsRenderer");
                    _volumetricCloudsRendererType = _eveAssembly.GetType("Atmosphere.DeferredRaymarchedVolumetricCloudsRenderer");
                    _particleFieldRendererType = _eveAssembly.GetType("Atmosphere.ParticleField+ParticleFieldRenderer");

                    _deferredCameraBufferType = _eveAssembly.GetType("Utils.DeferredCameraBuffer");
                    _deferredVolumetricCloudsRendererType = _eveAssembly.GetType("Atmosphere.DeferredVolumetricCloudsRenderer");

                    if (!IsNewEve && !IsOldEve)
                    {
                        Debug.LogWarning("[JRTI-EVE]: No known EVE component types found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _isAvailable = true;
                    Debug.Log(IsNewEve
                        ? "[JRTI-EVE]: Integration enabled (new EVE / Patreon)"
                        : "[JRTI-EVE]: Integration enabled (EVE Redux legacy)");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-EVE]: Error checking availability: {ex.Message}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        public static void ApplyToCamera(Camera targetCamera, Camera referenceCamera = null, bool includeLocalEffects = true)
        {
            if (!IsAvailable || targetCamera == null)
                return;

            try
            {
                if (referenceCamera == null)
                    referenceCamera = Camera.allCameras.FirstOrDefault(c => c.name == "Camera 00");

                if (referenceCamera == null)
                {
                    Debug.LogWarning("[JRTI-EVE]: No reference camera found");
                    return;
                }

                if (IsNewEve)
                    ApplyNewEve(targetCamera, referenceCamera, includeLocalEffects);
                else
                    ApplyOldEve(targetCamera, referenceCamera);

                Debug.Log($"[JRTI-EVE]: Applied effects to {targetCamera.name} (localEffects={includeLocalEffects})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-EVE]: Failed to apply to {targetCamera.name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void ApplyNewEve(Camera targetCamera, Camera referenceCamera, bool includeLocalEffects)
        {
            if (includeLocalEffects)
            {
                AddEVEComponent(targetCamera, referenceCamera, _wetSurfacesRendererType, "WetSurfacesRenderer");
                AddEVEComponent(targetCamera, referenceCamera, _particleFieldRendererType, "ParticleFieldRenderer");
            }

            AddEVEComponent(targetCamera, referenceCamera, _volumetricCloudsRendererType, "VolumetricCloudsRenderer");
            CopyEVECommandBuffers(referenceCamera, targetCamera, NewEveEvents, namedOnly: true);
        }

        private static void ApplyOldEve(Camera targetCamera, Camera referenceCamera)
        {
            AddEVEComponent(targetCamera, referenceCamera, _deferredCameraBufferType, "DeferredCameraBuffer");
            AddEVEComponent(targetCamera, referenceCamera, _deferredVolumetricCloudsRendererType, "DeferredVolumetricCloudsRenderer");
            CopyEVECommandBuffers(referenceCamera, targetCamera, OldEveEvents, namedOnly: false);
        }

        private static void AddEVEComponent(Camera targetCamera, Camera referenceCamera, Type componentType, string componentName)
        {
            if (componentType == null)
                return;

            try
            {
                if (targetCamera.gameObject.GetComponent(componentType) != null)
                    return;

                var referenceComponent = referenceCamera.gameObject.GetComponent(componentType);
                if (referenceComponent == null)
                    return;

                var newComponent = targetCamera.gameObject.AddComponent(componentType);

                if (newComponent != null)
                {
                    CopyComponentFields(referenceComponent, newComponent);
                    Debug.Log($"[JRTI-EVE]: Added {componentName} to {targetCamera.name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-EVE]: Could not add {componentName}: {ex.Message}");
            }
        }

        private static void CopyEVECommandBuffers(Camera source, Camera target, CameraEvent[] events, bool namedOnly)
        {
            try
            {
                int buffersCopied = 0;

                foreach (var evt in events)
                {
                    var buffers = source.GetCommandBuffers(evt);
                    if (buffers == null || buffers.Length == 0)
                        continue;

                    foreach (var buffer in buffers)
                    {
                        if (namedOnly && !buffer.name.Contains("EVE") &&
                            !buffer.name.Contains("Wet") &&
                            !buffer.name.Contains("Atmosphere"))
                            continue;

                        var existingBuffers = target.GetCommandBuffers(evt);
                        if (existingBuffers.Any(b => b == buffer))
                            continue;

                        target.AddCommandBuffer(evt, buffer);
                        buffersCopied++;
                        Debug.Log($"[JRTI-EVE]: Copied buffer '{buffer.name}' at {evt}");
                    }
                }

                if (buffersCopied > 0)
                    Debug.Log($"[JRTI-EVE]: Copied {buffersCopied} command buffers to {target.name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-EVE]: Error copying command buffers: {ex.Message}");
            }
        }

        private static void CopyComponentFields(Component source, Component target)
        {
            var type = source.GetType();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (!field.IsLiteral && !field.IsInitOnly)
                        field.SetValue(target, field.GetValue(source));
                }
                catch { }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (property.CanWrite && property.CanRead
                        && property.Name != "name"
                        && property.Name != "tag"
                        && property.Name != "hideFlags")
                        property.SetValue(target, property.GetValue(source));
                }
                catch { }
            }
        }

        public static void RemoveFromCamera(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return;

            try
            {
                RemoveComponentIfExists(camera, _wetSurfacesRendererType);
                RemoveComponentIfExists(camera, _screenSpaceShadowsRendererType);
                RemoveComponentIfExists(camera, _volumetricCloudsRendererType);
                RemoveComponentIfExists(camera, _particleFieldRendererType);
                RemoveComponentIfExists(camera, _deferredCameraBufferType);
                RemoveComponentIfExists(camera, _deferredVolumetricCloudsRendererType);

                int buffersRemoved = RemoveEVECommandBuffers(camera, NewEveEvents, namedOnly: true);
                buffersRemoved += RemoveEVECommandBuffers(camera, OldEveEvents, namedOnly: false);

                Debug.Log($"[JRTI-EVE]: Removed from {camera.name} ({buffersRemoved} buffers)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-EVE]: Error removing from {camera.name}: {ex.Message}");
            }
        }

        private static int RemoveEVECommandBuffers(Camera camera, CameraEvent[] events, bool namedOnly)
        {
            int count = 0;
            foreach (var evt in events)
            {
                foreach (var buffer in camera.GetCommandBuffers(evt).ToArray())
                {
                    if (namedOnly && !buffer.name.Contains("EVE") &&
                        !buffer.name.Contains("Wet") &&
                        !buffer.name.Contains("Atmosphere"))
                        continue;

                    camera.RemoveCommandBuffer(evt, buffer);
                    count++;
                }
            }
            return count;
        }

        private static void RemoveComponentIfExists(Camera camera, Type componentType)
        {
            if (componentType == null)
                return;

            var component = camera.gameObject.GetComponent(componentType);
            if (component != null)
                UnityEngine.Object.Destroy(component);
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return "EVE not available";

            var info = $"EVE Integration for {camera.name} ({(IsNewEve ? "new" : "legacy")}):\n";

            CheckComponent(camera, _wetSurfacesRendererType, "WetSurfacesRenderer", ref info);
            CheckComponent(camera, _screenSpaceShadowsRendererType, "ScreenSpaceShadowsRenderer", ref info);
            CheckComponent(camera, _volumetricCloudsRendererType, "VolumetricCloudsRenderer", ref info);
            CheckComponent(camera, _particleFieldRendererType, "ParticleFieldRenderer", ref info);
            CheckComponent(camera, _deferredCameraBufferType, "DeferredCameraBuffer", ref info);
            CheckComponent(camera, _deferredVolumetricCloudsRendererType, "DeferredVolumetricCloudsRenderer", ref info);

            var allEvents = NewEveEvents.Concat(OldEveEvents);
            int bufferCount = 0;
            foreach (var evt in allEvents)
            {
                foreach (var buffer in camera.GetCommandBuffers(evt))
                {
                    bufferCount++;
                    info += $"- CommandBuffer: '{buffer.name}' at {evt}\n";
                }
            }

            if (bufferCount == 0)
                info += "- No EVE command buffers found\n";

            return info;
        }

        private static void CheckComponent(Camera camera, Type componentType, string componentName, ref string info)
        {
            if (componentType == null)
                return;

            var component = camera.GetComponent(componentType);
            info += component != null
                ? $"- {componentName}: Present\n"
                : $"- {componentName}: Missing\n";
        }
    }
}