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
            if (!_initialized)
                Initialize();
        }

        private static void Initialize()
        {
            _initialized = true;

            try
            {
                var assembly = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a.name == "Scatterer")?.assembly;

                if (assembly == null)
                    return;

                var scattererType = assembly.GetType("Scatterer.Scatterer");
                if (scattererType == null)
                {
                    Debug.LogWarning("[JRTI-ScaledSwap]: Scatterer.Scatterer type not found");
                    return;
                }

                var instanceProp = scattererType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);

                _scattererInstance = instanceProp?.GetValue(null);

                if (_scattererInstance == null)
                {
                    Debug.LogWarning("[JRTI-ScaledSwap]: Scatterer.Instance is null");
                    return;
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
                    Debug.LogWarning("[JRTI-ScaledSwap]: No scaled-space camera field found — swap disabled");
                    return;
                }

                _mainScaledCamera = Camera.allCameras.FirstOrDefault(c => c.name == "Camera ScaledSpace");
                Debug.Log("[JRTI-ScaledSwap]: Ready");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-ScaledSwap]: Init failed: {ex.Message}");
            }
        }

        void OnPreCull()
        {
            if (_scattererInstance == null || _scaledCameraField == null)
                return;

            _scaledCameraField.SetValue(_scattererInstance, _camera);
        }

        void OnPostRender()
        {
            if (_scattererInstance == null || _scaledCameraField == null)
                return;

            if (_mainScaledCamera != null)
                _scaledCameraField.SetValue(_scattererInstance, _mainScaledCamera);
        }
    }
}