using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class ParallaxIntegration
    {
        private static bool? _isAvailable;
        private static Assembly _parallaxAssembly;
        private static Type _scatterManagerType;
        private static Type _scatterRendererType;
        private static FieldInfo _instanceField;
        private static FieldInfo _activeScatterRenderersField;
        private static MethodInfo _renderInCamerasMethod;
        private static int _lastLogFrame = -999;

        private const int ParallaxLayer = 15;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    Debug.Log("[JRTI-Parallax]: Searching for Parallax assembly...");

                    _parallaxAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "Parallax")?.assembly;

                    if (_parallaxAssembly == null)
                    {
                        _parallaxAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => !a.IsDynamic && a.GetName().Name.Contains("Parallax"));
                    }

                    if (_parallaxAssembly == null)
                    {
                        Debug.Log("[JRTI-Parallax]: Parallax-Continued not found - terrain scatter disabled");
                        _isAvailable = false;
                        return false;
                    }

                    Debug.Log($"[JRTI-Parallax]: Found assembly: {_parallaxAssembly.GetName().Name}");

                    _scatterManagerType = _parallaxAssembly.GetType("Parallax.ScatterManager");
                    _scatterRendererType = _parallaxAssembly.GetType("Parallax.ScatterRenderer");

                    if (_scatterManagerType == null || _scatterRendererType == null)
                    {
                        Debug.LogWarning("[JRTI-Parallax]: Required types not found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _instanceField = _scatterManagerType.GetField("Instance",
                        BindingFlags.Public | BindingFlags.Static);

                    _activeScatterRenderersField = _scatterManagerType.GetField("activeScatterRenderers",
                        BindingFlags.Public | BindingFlags.Instance);

                    _renderInCamerasMethod = _scatterRendererType.GetMethod("RenderInCameras",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_instanceField == null || _activeScatterRenderersField == null || _renderInCamerasMethod == null)
                    {
                        Debug.LogWarning("[JRTI-Parallax]: Required members not found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _isAvailable = true;
                    Debug.Log("[JRTI-Parallax]: Integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-Parallax]: Error checking availability: {ex.Message}\n{ex.StackTrace}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        //public static void ApplyToCamera(Camera camera)
        //{
        //    if (!IsAvailable || camera == null)
        //        return;

        //    try
        //    {
        //        int originalMask = camera.cullingMask;

        //        camera.cullingMask |= (1 << ParallaxLayer);

        //        if (originalMask != camera.cullingMask)
        //        {
        //            Debug.Log($"[JRTI-Parallax]: Updated {camera.name} culling mask (added layer {ParallaxLayer})");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.LogError($"[JRTI-Parallax]: Failed to apply to {camera.name}: {ex.Message}");
        //    }
        //}

        public static void ApplyToCamera(Camera camera) { }

        //public static void RenderToCamera(params Camera[] cameras)
        //{
        //    if (!IsAvailable || cameras == null || cameras.Length == 0)
        //        return;

        //    try
        //    {
        //        var instance = _instanceField.GetValue(null);
        //        if (instance == null)
        //            return;

        //        var activeRenderers = _activeScatterRenderersField.GetValue(instance) as System.Collections.IList;
        //        if (activeRenderers == null || activeRenderers.Count == 0)
        //            return;

        //        if (Time.frameCount - _lastLogFrame > 10000 || _lastLogFrame == -999)
        //        {
        //            Debug.Log($"[JRTI-Parallax]: Rendering {activeRenderers.Count} scatters to {cameras.Length} cameras");
        //            _lastLogFrame = Time.frameCount;
        //        }

        //        foreach (var renderer in activeRenderers)
        //        {
        //            if (renderer != null)
        //            {
        //                _renderInCamerasMethod.Invoke(renderer, new object[] { cameras });
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        if (Time.frameCount - _lastLogFrame > 1000)
        //        {
        //            Debug.LogError($"[JRTI-Parallax]: Failed to render scatter: {ex.Message}");
        //            _lastLogFrame = Time.frameCount;
        //        }
        //    }
        //}
        public static void RenderToCamera(params Camera[] cameras) { }

        //public static void RemoveFromCamera(Camera camera)
        //{
        //    if (camera == null)
        //        return;

        //    try
        //    {
        //        Debug.Log($"[JRTI-Parallax]: Disabled scatter rendering for {camera.name}");
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.LogError($"[JRTI-Parallax]: Error removing from {camera.name}: {ex.Message}");
        //    }
        //}
        public static void RemoveFromCamera(Camera camera) { }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable)
                return "Parallax not available";

            if (camera == null)
                return "Camera is null";

            var info = $"Parallax Integration for {camera.name}:\n";

            bool hasLayer = (camera.cullingMask & (1 << ParallaxLayer)) != 0;
            info += $"- Culling mask includes layer {ParallaxLayer}: {hasLayer}\n";

            try
            {
                var instance = _instanceField.GetValue(null);
                if (instance == null)
                {
                    info += "- ScatterManager instance: Not found\n";
                    return info;
                }

                var activeRenderers = _activeScatterRenderersField.GetValue(instance) as System.Collections.IList;
                if (activeRenderers == null)
                {
                    info += "- Active renderers list: Not found\n";
                    return info;
                }

                info += $"- Active scatter renderers: {activeRenderers.Count}\n";

                if (activeRenderers.Count == 0)
                {
                    info += "- Note: No scatters are currently active (may be normal depending on location)\n";
                }
            }
            catch (Exception ex)
            {
                info += $"- Error getting diagnostic info: {ex.Message}\n";
            }

            return info;
        }

        public static bool HasActiveScatters()
        {
            if (!IsAvailable)
                return false;

            try
            {
                var instance = _instanceField.GetValue(null);
                if (instance == null)
                    return false;

                var activeRenderers = _activeScatterRenderersField.GetValue(instance) as System.Collections.IList;
                return activeRenderers != null && activeRenderers.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
