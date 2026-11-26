using System;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class ScattererScaledAtmosphere
    {
        private static bool _resolved;
        private static Type _skyNodeType;
        private static FieldInfo _materialField;
        private static FieldInfo _containerField;
        private static FieldInfo _parentScaledTransformField;
        private static FieldInfo _rtField;
        private static MethodInfo _updateScreenBoundsMethod;

        private static readonly int CameraToWorldId = Shader.PropertyToID("CameraToWorld");
        private static readonly int CurrentVpId = Shader.PropertyToID("currentVP");
        private static readonly object[] BoundsArgs = new object[5];

        public static void Bind(Camera feedCamera, Camera mainScaledCamera, Assembly scatterer)
        {
            if (feedCamera == null || mainScaledCamera == null || !Resolve(scatterer))
                return;

            try
            {
                foreach (var skyNode in mainScaledCamera.GetComponents(_skyNodeType))
                    BindSkyNode(skyNode, feedCamera);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-ScaledSwap]: Scaled atmosphere bind failed, disabled: {ex.Message}");
                _skyNodeType = null;
            }
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

            var cameraToWorld = camera.cameraToWorldMatrix;
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

        private static bool Resolve(Assembly scatterer)
        {
            if (_resolved)
                return _skyNodeType != null;

            _resolved = true;
            var skyNodeType = scatterer?.GetType("Scatterer.SkyNode");
            if (skyNodeType == null)
                return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            _materialField = skyNodeType.GetField("scaledDepthBufferScatteringMaterial", flags);
            _containerField = skyNodeType.GetField("scaledDepthBufferScatteringContainer", flags);
            _parentScaledTransformField = skyNodeType.GetField("parentScaledTransform", flags);
            _rtField = skyNodeType.GetField("Rt", flags);
            _updateScreenBoundsMethod = _containerField?.FieldType.GetMethod("UpdateScreenBounds", flags);

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
