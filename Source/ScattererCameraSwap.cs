using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public class ScattererCameraSwap : MonoBehaviour
    {
        private Camera _camera;

        private static bool _initialized;
        private static object _scattererInstance;
        private static FieldInfo _nearCameraField;
        private static Camera _mainCamera;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            EnsureInitialized();
        }

        private static bool EnsureInitialized()
        {
            if (_initialized && _mainCamera != null && _mainCamera.isActiveAndEnabled && IsAlive(_scattererInstance))
                return true;

            var mainCamera = CameraLookup.FindActive("Camera 00");
            return mainCamera != null && Initialize(mainCamera);
        }

        private static bool Initialize(Camera mainCamera)
        {
            try
            {
                var assembly = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a.name == "Scatterer")?.assembly;

                if (assembly == null)
                    return false;

                var scattererType = assembly.GetType("Scatterer.Scatterer");
                if (scattererType == null)
                {
                    Debug.LogWarning("[JRTI-CameraSwap]: Scatterer.Scatterer type not found");
                    return false;
                }

                var instanceProp = scattererType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);

                _scattererInstance = instanceProp?.GetValue(null);

                if (_scattererInstance == null)
                {
                    Debug.LogWarning("[JRTI-CameraSwap]: Scatterer.Instance is null");
                    return false;
                }

                _nearCameraField = scattererType.GetField("nearCamera",
                    BindingFlags.Public | BindingFlags.Instance);

                if (_nearCameraField == null)
                {
                    Debug.LogWarning("[JRTI-CameraSwap]: nearCamera field not found");
                    return false;
                }

                if (mainCamera == null)
                    return false;

                _mainCamera = mainCamera;
                _initialized = true;
                Debug.Log("[JRTI-CameraSwap]: Ready - will swap Scatterer.Instance.nearCamera in OnPreCull");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-CameraSwap]: Init failed: {ex.Message}");
                _initialized = false;
                return false;
            }
        }

        private static bool IsAlive(object value)
        {
            if (value == null)
                return false;

            var unityObject = value as UnityEngine.Object;
            return unityObject == null
                ? ReferenceEquals(unityObject, null)
                : unityObject != null;
        }

        void OnPreCull()
        {
            if (!EnsureInitialized() || _nearCameraField == null)
                return;

            _nearCameraField.SetValue(_scattererInstance, _camera);
        }

        void OnPostRender()
        {
            if (!_initialized || !IsAlive(_scattererInstance) || _nearCameraField == null)
                return;

            if (_mainCamera != null)
                _nearCameraField.SetValue(_scattererInstance, _mainCamera);
        }
    }
}
