using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public class ScattererScaledCameraSwap : MonoBehaviour
    {
        private Camera _camera;

        private static bool _initialized;
        private static object _scattererInstance;
        private static FieldInfo _scaledCameraField;
        private static Camera _mainScaledCamera;

        private static readonly string[] _candidateFieldNames =
        {
            "scaledSpaceCamera",
            "farCamera",
            "scaledCamera",
            "mainScaledCamera",
        };

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
            if (_initialized && _mainScaledCamera != null && _mainScaledCamera.isActiveAndEnabled && IsAlive(_scattererInstance))
                return true;

            var scaledCamera = CameraLookup.FindActive("Camera ScaledSpace");
            return scaledCamera != null && Initialize(scaledCamera);
        }

        private static bool Initialize(Camera scaledCamera)
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
                    Debug.LogWarning("[JRTI-ScaledSwap]: Scatterer.Scatterer type not found");
                    return false;
                }

                var instanceProp = scattererType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);

                _scattererInstance = instanceProp?.GetValue(null);

                if (_scattererInstance == null)
                {
                    Debug.LogWarning("[JRTI-ScaledSwap]: Scatterer.Instance is null");
                    return false;
                }

                foreach (var name in _candidateFieldNames)
                {
                    var field = scattererType.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (field != null && typeof(Camera).IsAssignableFrom(field.FieldType))
                    {
                        _scaledCameraField = field;
                        Debug.Log($"[JRTI-ScaledSwap]: Using field '{name}'");
                        break;
                    }
                }

                if (_scaledCameraField == null)
                {
                    Debug.LogWarning("[JRTI-ScaledSwap]: No scaled-space camera field found - swap disabled");
                    return false;
                }

                if (scaledCamera == null)
                    return false;

                _mainScaledCamera = scaledCamera;
                _initialized = true;
                Debug.Log("[JRTI-ScaledSwap]: Ready");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-ScaledSwap]: Init failed: {ex.Message}");
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
            if (!EnsureInitialized() || _scaledCameraField == null)
                return;

            _scaledCameraField.SetValue(_scattererInstance, _camera);
        }

        void OnPostRender()
        {
            if (!_initialized || !IsAlive(_scattererInstance) || _scaledCameraField == null)
                return;

            if (_mainScaledCamera != null)
                _scaledCameraField.SetValue(_scattererInstance, _mainScaledCamera);
        }
    }
}
