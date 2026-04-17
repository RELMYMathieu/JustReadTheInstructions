using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class HullcamFilterIntegration
    {
        private static bool? _isAvailable;
        private static Assembly _hullcamAssembly;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    _hullcamAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a =>
                            a.name.Equals("HullcamVDS", StringComparison.OrdinalIgnoreCase) ||
                            a.name.Equals("HullcamVDSContinued", StringComparison.OrdinalIgnoreCase))
                        ?.assembly;

                    _isAvailable = _hullcamAssembly != null;

                    if (_isAvailable.Value)
                        Debug.Log("[JRTI-HullcamFilter]: Integration enabled");
                    else
                        Debug.Log("[JRTI-HullcamFilter]: HullcamVDS not found");

                    return _isAvailable.Value;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-HullcamFilter]: Error checking availability: {ex.Message}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        public static Material GetFilterMaterial()
        {
            if (!IsAvailable)
                return null;

            var mainCam = Camera.main;
            if (mainCam == null)
                return null;

            try
            {
                foreach (var comp in mainCam.GetComponents<MonoBehaviour>())
                {
                    if (comp == null || comp.GetType().Assembly != _hullcamAssembly)
                        continue;

                    var matField = comp.GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(f => typeof(Material).IsAssignableFrom(f.FieldType));

                    if (matField == null)
                        continue;

                    var mat = matField.GetValue(comp) as Material;
                    if (mat != null)
                        return mat;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-HullcamFilter]: Error getting filter material: {ex.Message}");
            }

            return null;
        }

        public static string GetDiagnosticInfo()
        {
            if (!IsAvailable)
                return "HullcamFilter: unavailable\n";

            var mat = GetFilterMaterial();
            return mat != null
                ? $"HullcamFilter: active ({mat.name})\n"
                : "HullcamFilter: no active filter\n";
        }
    }
}
