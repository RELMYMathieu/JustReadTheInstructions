using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    public static class DeferredIntegration
    {
        private const int LocalCameraQueue = 15;

        private static bool? _isAvailable;
        private static Type _forwardRenderingCompatibilityType;
        private static Type _pqsFadeType;
        private static MethodInfo _initMethod;
        private static FieldInfo _dummyObjectField;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    var deferredAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "Deferred")?.assembly;

                    if (deferredAssembly == null)
                    {
                        Debug.Log("[JRTI-Deferred]: Deferred mod not found - using forward rendering");
                        _isAvailable = false;
                        return false;
                    }

                    _forwardRenderingCompatibilityType = deferredAssembly.GetType("Deferred.ForwardRenderingCompatibility");
                    _pqsFadeType = deferredAssembly.GetType("Deferred.DeferredPQSFade");

                    if (_forwardRenderingCompatibilityType == null)
                    {
                        Debug.LogWarning("[JRTI-Deferred]: ForwardRenderingCompatibility type not found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _initMethod = _forwardRenderingCompatibilityType.GetMethod("Init",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_initMethod == null)
                    {
                        Debug.LogWarning("[JRTI-Deferred]: Init method not found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _dummyObjectField = _forwardRenderingCompatibilityType.GetField("go",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    _isAvailable = true;
                    Debug.Log("[JRTI-Deferred]: Integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-Deferred]: Error checking availability: {ex.Message}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        public static void ApplyToCamera(Camera camera, int renderQueue = LocalCameraQueue)
        {
            if (!IsAvailable || camera == null)
                return;

            try
            {
                camera.renderingPath = RenderingPath.DeferredShading;

                if (renderQueue == LocalCameraQueue)
                    AddPQSFade(camera);

                var existingComponent = camera.gameObject.GetComponent(_forwardRenderingCompatibilityType);
                if (existingComponent != null)
                {
                    Debug.Log($"[JRTI-Deferred]: ForwardRenderingCompatibility already exists on {camera.name}");
                    return;
                }

                var component = camera.gameObject.AddComponent(_forwardRenderingCompatibilityType);

                if (component != null && _initMethod != null)
                {
                    _initMethod.Invoke(component, new object[] { renderQueue });
                    DetachDummyObject(component, camera);
                    Debug.Log($"[JRTI-Deferred]: Applied deferred rendering to {camera.name} (queue: {renderQueue})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Deferred]: Failed to apply to {camera.name}: {ex.Message}");

                try
                {
                    camera.renderingPath = RenderingPath.Forward;
                    var component = camera.gameObject.GetComponent(_forwardRenderingCompatibilityType);
                    if (component != null)
                        UnityEngine.Object.Destroy(component);
                }
                catch { }
            }
        }

        private static void AddPQSFade(Camera camera)
        {
            if (_pqsFadeType == null || camera.gameObject.GetComponent(_pqsFadeType) != null)
                return;

            camera.gameObject.AddComponent(_pqsFadeType);
            Debug.Log($"[JRTI-Deferred]: Added PQS fade to {camera.name}");
        }

        // TEMP: until Firefly merges https://github.com/M1rageDev/Firefly/pull/58
        private static void DetachDummyObject(Component component, Camera camera)
        {
            if (!(_dummyObjectField?.GetValue(component) is GameObject dummy))
                return;

            dummy.transform.SetParent(null, true);
            dummy.AddComponent<CameraFollower>().Follow(camera);
        }

        public static void RemoveFromCamera(Camera camera)
        {
            if (camera == null)
                return;

            try
            {
                camera.renderingPath = RenderingPath.Forward;

                if (_pqsFadeType != null)
                {
                    var fade = camera.gameObject.GetComponent(_pqsFadeType);
                    if (fade != null)
                        UnityEngine.Object.Destroy(fade);
                }

                if (_forwardRenderingCompatibilityType != null)
                {
                    var component = camera.gameObject.GetComponent(_forwardRenderingCompatibilityType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                        Debug.Log($"[JRTI-Deferred]: Removed from {camera.name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Deferred]: Error removing from {camera.name}: {ex.Message}");
            }
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return "Deferred not available";

            var info = $"Deferred Integration for {camera.name}:\n";
            info += $"- Rendering path: {camera.renderingPath}\n";

            if (_forwardRenderingCompatibilityType != null)
            {
                var component = camera.GetComponent(_forwardRenderingCompatibilityType);
                info += component != null
                    ? "- ForwardRenderingCompatibility: Present\n"
                    : "- ForwardRenderingCompatibility: Missing\n";
            }

            if (_pqsFadeType != null)
                info += $"- DeferredPQSFade: {(camera.GetComponent(_pqsFadeType) != null ? "Present" : "Missing")}\n";

            return info;
        }
    }

    internal sealed class CameraFollower : MonoBehaviour
    {
        private Camera _camera;

        public void Follow(Camera camera)
        {
            _camera = camera;
            Camera.onPreCull += MoveToCamera;
        }

        private void MoveToCamera(Camera camera)
        {
            if (camera == _camera)
                transform.position = camera.transform.position;
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= MoveToCamera;
        }
    }
}
