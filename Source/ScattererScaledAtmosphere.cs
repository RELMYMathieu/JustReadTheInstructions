using System;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class ScattererScaledAtmosphere
    {
        private const string ReflectionProbeScaledCameraName = "ScattererReflectionProbeScaledSpaceCamera";
        private const float ReflectionProbeSearchInterval = 1f;

        private static bool _resolved;
        private static Type _skyNodeType;
        private static FieldInfo _materialField;
        private static FieldInfo _containerField;
        private static FieldInfo _parentScaledTransformField;
        private static FieldInfo _rtField;
        private static FieldInfo _inScaledSpaceField;
        private static MethodInfo _updateScreenBoundsMethod;
        private static MethodInfo _enableScreenCopyMethod;
        private static readonly object[] ScreenCopyArgs = new object[1];

        private static GameObject _reflectionProbeScaledCamera;
        private static float _nextReflectionProbeSearch;

        private static readonly int CameraToWorldId = Shader.PropertyToID("CameraToWorld");
        private static readonly int CurrentVpId = Shader.PropertyToID("currentVP");
        private static readonly int ScreenBoundsId = Shader.PropertyToID("_scaledAtmosphereScreenBounds");
        private static readonly object[] BoundsArgs = new object[5];

        public static void Bind(Camera viewer)
        {
            var mainScaledCamera = ScattererScaledCameraSwap.MainScaledCamera;
            if (viewer == null || mainScaledCamera == null || !Resolve())
                return;

            try
            {
                foreach (var skyNode in mainScaledCamera.GetComponents(_skyNodeType))
                    BindSkyNode(skyNode, viewer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-ScaledSwap]: Scaled atmosphere bind failed, disabled: {ex.Message}");
                _skyNodeType = null;
            }
        }

        public static void RestoreMain() => Bind(ScattererScaledCameraSwap.MainScaledCamera);

        public static void EnableScreenCopy(Camera camera)
        {
            if (camera == null || !Resolve() || _enableScreenCopyMethod == null)
                return;

            try
            {
                ScreenCopyArgs[0] = camera;
                _enableScreenCopyMethod.Invoke(null, ScreenCopyArgs);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-ScaledSwap]: Screen copy failed, disabled: {ex.Message}");
                _enableScreenCopyMethod = null;
            }
        }

        public static void AttachToReflectionProbe()
        {
            if (_reflectionProbeScaledCamera != null || Time.unscaledTime < _nextReflectionProbeSearch)
                return;

            _nextReflectionProbeSearch = Time.unscaledTime + ReflectionProbeSearchInterval;
            _reflectionProbeScaledCamera = GameObject.Find(ReflectionProbeScaledCameraName);
            if (_reflectionProbeScaledCamera == null || _reflectionProbeScaledCamera.GetComponent<ScattererAtmosphereBinding>() != null)
                return;

            _reflectionProbeScaledCamera.AddComponent<ScattererAtmosphereBinding>();
            Debug.Log("[JRTI-ScaledSwap]: Scaled atmosphere follows the reflection probe camera");
        }

        public static string GetDiagnosticInfo()
        {
            var mainScaledCamera = ScattererScaledCameraSwap.MainScaledCamera;
            if (mainScaledCamera == null || !Resolve())
                return "Scatterer scaled atmosphere: not bound\n";

            var info = $"Scatterer scaled atmosphere (reflection probe camera {(_reflectionProbeScaledCamera != null ? "bound" : "not found")}):\n";
            foreach (var skyNode in mainScaledCamera.GetComponents(_skyNodeType))
            {
                var material = _materialField.GetValue(skyNode) as Material;
                var container = _containerField.GetValue(skyNode);
                info += $"- {skyNode.name}: inScaledSpace={_inScaledSpaceField?.GetValue(skyNode)}, " +
                        $"container={(container != null ? "yes" : "no")}, " +
                        $"bounds={(material != null ? material.GetVector(ScreenBoundsId).ToString("F3") : "no material")}\n";

                var quad = FindQuad(skyNode);
                info += quad == null
                    ? "  quad: not found\n"
                    : $"  quad: enabled={quad.enabled}, active={quad.gameObject.activeInHierarchy}, layer={quad.gameObject.layer}, " +
                      $"isVisible={quad.isVisible}, worldBounds={quad.bounds.center:F1}/{quad.bounds.extents:F1}, parentRot={quad.transform.parent.rotation.eulerAngles:F2}\n";
            }
            return info;
        }

        public static string DescribeQuadVisibility(Camera camera)
        {
            var mainScaledCamera = ScattererScaledCameraSwap.MainScaledCamera;
            if (camera == null || mainScaledCamera == null || !Resolve())
                return "";

            var info = "";
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            foreach (var skyNode in mainScaledCamera.GetComponents(_skyNodeType))
            {
                var quad = FindQuad(skyNode);
                if (quad == null)
                    continue;

                int layer = quad.gameObject.layer;
                info += $"- sees {skyNode.name} quad: inMask={(camera.cullingMask & (1 << layer)) != 0}, " +
                        $"layerCull={camera.layerCullDistances[layer]}, frustum={GeometryUtility.TestPlanesAABB(planes, quad.bounds)}\n";
            }
            return info;
        }

        private static Renderer FindQuad(Component skyNode)
        {
            var parent = _parentScaledTransformField.GetValue(skyNode) as Transform;
            return parent?.Find("Scatterer scaled depth-buffer atmo")?.GetComponent<Renderer>();
        }

        private static void BindSkyNode(Component skyNode, Camera camera)
        {
            var material = _materialField.GetValue(skyNode) as Material;
            var container = _containerField.GetValue(skyNode);
            var parent = _parentScaledTransformField.GetValue(skyNode) as Transform;
            if (material == null || container == null || parent == null)
                return;

            var projection = camera.nonJitteredProjectionMatrix;
            var view = camera.worldToCameraMatrix;

            var cameraToWorld = view.inverse;
            cameraToWorld.SetColumn(2, -cameraToWorld.GetColumn(2));
            material.SetMatrix(CameraToWorldId, cameraToWorld);
            material.SetMatrix(CurrentVpId, GL.GetGPUProjectionMatrix(projection, false) * view);

            BoundsArgs[0] = camera;
            BoundsArgs[1] = projection;
            BoundsArgs[2] = view;
            BoundsArgs[3] = parent.position;
            BoundsArgs[4] = (float)_rtField.GetValue(skyNode) / ScaledSpace.ScaleFactor;
            _updateScreenBoundsMethod.Invoke(container, BoundsArgs);
        }

        private static bool Resolve()
        {
            if (_resolved)
                return _skyNodeType != null;

            var scatterer = ScattererScaledCameraSwap.ScattererAssembly;
            if (scatterer == null)
                return false;

            _resolved = true;
            var skyNodeType = scatterer.GetType("Scatterer.SkyNode");
            if (skyNodeType == null)
                return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            _materialField = skyNodeType.GetField("scaledDepthBufferScatteringMaterial", flags);
            _containerField = skyNodeType.GetField("scaledDepthBufferScatteringContainer", flags);
            _parentScaledTransformField = skyNodeType.GetField("parentScaledTransform", flags);
            _rtField = skyNodeType.GetField("Rt", flags);
            _inScaledSpaceField = skyNodeType.GetField("inScaledSpace", flags);
            _updateScreenBoundsMethod = _containerField?.FieldType.GetMethod("UpdateScreenBounds", flags);
            _enableScreenCopyMethod = scatterer.GetType("Scatterer.ScreenCopyCommandBuffer")
                ?.GetMethod("EnableScreenCopyForFrame", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Camera) }, null);

            if (_materialField == null || _parentScaledTransformField == null || _rtField == null || _updateScreenBoundsMethod == null)
            {
                Debug.Log("[JRTI-ScaledSwap]: Scatterer has no scaled depth-buffer atmosphere - nothing to bind");
                return false;
            }

            _skyNodeType = skyNodeType;
            Debug.Log("[JRTI-ScaledSwap]: Scaled atmosphere follows feed cameras");
            return true;
        }
    }
}
