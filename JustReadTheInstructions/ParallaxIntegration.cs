using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class ParallaxIntegration
    {
        private static bool? _isAvailable;
        private static Assembly _parallaxAssembly;

        private static FieldInfo _smInstanceField;
        private static FieldInfo _smActiveRenderersField;
        private static MethodInfo _srPreRenderMethod;
        private static MethodInfo _srRenderInCamerasMethod;

        private static FieldInfo _roVectorCameraPosField;
        private static FieldInfo _roFloatFrustumPlanesField;

        private static FieldInfo _scScatterQuadDataField;
        private static FieldInfo _sqdQuadField;
        private static FieldInfo _sqdIgnoreRendererVisibilityField;
        private static FieldInfo _sqdCameraDistanceField;
        private static MethodInfo _sqdEvaluateQuadMethod;

        private const int ParallaxLayer = 15;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;

                try
                {
                    _parallaxAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "Parallax")?.assembly
                        ?? AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => !a.IsDynamic && a.GetName().Name.Contains("Parallax"));

                    if (_parallaxAssembly == null)
                    {
                        Debug.Log("[JRTI-Parallax]: Parallax-Continued not found");
                        _isAvailable = false;
                        return false;
                    }

                    var smType = _parallaxAssembly.GetType("Parallax.ScatterManager");
                    var srType = _parallaxAssembly.GetType("Parallax.ScatterRenderer");
                    var roType = _parallaxAssembly.GetType("Parallax.RuntimeOperations");
                    var scType = _parallaxAssembly.GetType("Parallax.ScatterComponent");
                    var sqdType = _parallaxAssembly.GetType("Parallax.ScatterSystemQuadData");

                    if (smType == null || srType == null || roType == null || scType == null || sqdType == null)
                    {
                        Debug.LogWarning("[JRTI-Parallax]: Required types not found");
                        _isAvailable = false;
                        return false;
                    }

                    _smInstanceField = smType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    _smActiveRenderersField = smType.GetField("activeScatterRenderers", BindingFlags.Public | BindingFlags.Instance);
                    _srPreRenderMethod = srType.GetMethod("PreRender", BindingFlags.Public | BindingFlags.Instance);
                    _srRenderInCamerasMethod = srType.GetMethod("RenderInCameras", BindingFlags.Public | BindingFlags.Instance);

                    _roVectorCameraPosField = roType.GetField("vectorCameraPos", BindingFlags.Public | BindingFlags.Static);
                    _roFloatFrustumPlanesField = roType.GetField("floatCameraFrustumPlanes", BindingFlags.Public | BindingFlags.Static);

                    _scScatterQuadDataField = scType.GetField("scatterQuadData", BindingFlags.Public | BindingFlags.Static);
                    _sqdQuadField = sqdType.GetField("quad", BindingFlags.Public | BindingFlags.Instance);
                    _sqdIgnoreRendererVisibilityField = sqdType.GetField("ignoreRendererVisibility", BindingFlags.Public | BindingFlags.Instance);
                    _sqdCameraDistanceField = sqdType.GetField("cameraDistance", BindingFlags.Public | BindingFlags.Instance);
                    _sqdEvaluateQuadMethod = sqdType.GetMethod("EvaluateQuad", BindingFlags.Public | BindingFlags.Instance);

                    if (_smInstanceField == null || _smActiveRenderersField == null ||
                        _srPreRenderMethod == null || _srRenderInCamerasMethod == null ||
                        _roVectorCameraPosField == null || _roFloatFrustumPlanesField == null ||
                        _scScatterQuadDataField == null || _sqdQuadField == null ||
                        _sqdCameraDistanceField == null || _sqdEvaluateQuadMethod == null)
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

        public static void ApplyToCamera(Camera camera)
        {
            if (!IsAvailable || camera == null) return;
            camera.cullingMask |= (1 << ParallaxLayer);
        }

        public static void RemoveFromCamera(Camera camera) { }

        public static void RenderToCamera(Camera camera)
        {
            if (!IsAvailable || camera == null) return;

            try
            {
                var smInstance = _smInstanceField.GetValue(null);
                if (smInstance == null) return;

                var activeRenderers = _smActiveRenderersField.GetValue(smInstance) as IList;
                if (activeRenderers == null || activeRenderers.Count == 0) return;

                var scatterQuadData = _scScatterQuadDataField.GetValue(null) as IDictionary;
                if (scatterQuadData == null) return;

                var savedPos = (Vector3)_roVectorCameraPosField.GetValue(null);
                var savedFrustum = (float[])_roFloatFrustumPlanesField.GetValue(null);

                var cameraPos = camera.transform.position;
                _roVectorCameraPosField.SetValue(null, cameraPos);
                _roFloatFrustumPlanesField.SetValue(null, ComputeFrustumPlanes(camera));

                foreach (var renderer in activeRenderers)
                    _srPreRenderMethod.Invoke(renderer, null);

                foreach (DictionaryEntry entry in scatterQuadData)
                {
                    var quadData = entry.Value;
                    var quad = _sqdQuadField.GetValue(quadData) as PQ;
                    if (quad == null || !quad.isVisible) continue;

                    bool ignoreVisibility = _sqdIgnoreRendererVisibilityField != null && (bool)_sqdIgnoreRendererVisibilityField.GetValue(quadData);
                    if (!ignoreVisibility && !quad.meshRenderer.isVisible) continue;

                    float sqrDist = ((Vector3)quad.meshRenderer.localToWorldMatrix.GetColumn(3) - cameraPos).sqrMagnitude;
                    _sqdCameraDistanceField.SetValue(quadData, sqrDist);

                    _sqdEvaluateQuadMethod.Invoke(quadData, null);
                }

                var cameraArray = new Camera[] { camera };
                foreach (var renderer in activeRenderers)
                    _srRenderInCamerasMethod.Invoke(renderer, new object[] { cameraArray });

                _roVectorCameraPosField.SetValue(null, savedPos);
                _roFloatFrustumPlanesField.SetValue(null, savedFrustum);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Parallax]: RenderToCamera failed: {ex.Message}");
            }
        }

        private static float[] ComputeFrustumPlanes(Camera camera)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var result = new float[planes.Length * 4];
            for (int i = 0; i < planes.Length; i++)
            {
                result[i * 4] = planes[i].normal.x;
                result[i * 4 + 1] = planes[i].normal.y;
                result[i * 4 + 2] = planes[i].normal.z;
                result[i * 4 + 3] = planes[i].distance;
            }
            return result;
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable) return "Parallax not available\n";
            if (camera == null) return "Camera is null\n";

            var info = $"Parallax Integration for {camera.name}:\n";
            info += $"- Culling mask includes layer {ParallaxLayer}: {(camera.cullingMask & (1 << ParallaxLayer)) != 0}\n";

            try
            {
                var instance = _smInstanceField.GetValue(null);
                var activeRenderers = instance != null ? _smActiveRenderersField.GetValue(instance) as IList : null;
                info += $"- Active scatter renderers: {activeRenderers?.Count ?? 0}\n";
            }
            catch (Exception ex)
            {
                info += $"- Error: {ex.Message}\n";
            }

            return info;
        }

        public static bool HasActiveScatters()
        {
            if (!IsAvailable) return false;

            try
            {
                var instance = _smInstanceField.GetValue(null);
                if (instance == null) return false;
                var activeRenderers = _smActiveRenderersField.GetValue(instance) as IList;
                return activeRenderers != null && activeRenderers.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}