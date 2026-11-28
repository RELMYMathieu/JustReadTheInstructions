using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class DistantObjectSkyDimming
    {
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool? _isAvailable;
        private static PropertyInfo _darkenSkyInstance;
        private static PropertyInfo _engineInstance;
        private static PropertyInfo _settingsInstance;
        private static FieldInfo _skyboxSettings;
        private static FieldInfo _maxBrightness;
        private static FieldInfo _referenceBodySize;
        private static FieldInfo _minimumBodySize;
        private static FieldInfo _minimumRelativeAngle;
        private static MethodInfo _sunBrightness;
        private static MethodInfo _planetsBrightness;
        private static MethodInfo _coronaBrightness;

        private static bool IsAvailable
        {
            get
            {
                if (!_isAvailable.HasValue) _isAvailable = Load();
                return _isAvailable.Value;
            }
        }

        public static Color MaxGalaxyColorFor(GalaxyCubeControl cube, Camera camera)
        {
            if (MapView.MapIsEnabled || !IsAvailable || !IsDimming())
                return cube.maxGalaxyColor;

            try
            {
                return SkyColorFor(camera);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-DOE]: Sky dimming failed, feeds now follow the main camera: {(ex.InnerException ?? ex).Message}");
                _isAvailable = false;
                return cube.maxGalaxyColor;
            }
        }

        private static bool IsDimming()
        {
            return _darkenSkyInstance.GetValue(null) is Behaviour darkenSky && darkenSky.isActiveAndEnabled;
        }

        private static Color SkyColorFor(Camera camera)
        {
            var engine = _engineInstance.GetValue(null);
            var skybox = _skyboxSettings.GetValue(_settingsInstance.GetValue(null));
            var minimumBodySize = _minimumBodySize.GetValue(skybox);

            double brightness = (double)_sunBrightness.Invoke(engine, new[] { minimumBodySize, camera });
            brightness = Math.Min(brightness, (double)_planetsBrightness.Invoke(engine, new[]
            {
                minimumBodySize, _minimumRelativeAngle.GetValue(skybox), _referenceBodySize.GetValue(skybox), camera,
            }));
            if (SunInView(camera))
                brightness = Math.Max(brightness, (double)_coronaBrightness.Invoke(engine, new[] { minimumBodySize, camera }));

            float max = (float)(double)_maxBrightness.GetValue(skybox);
            return new Color(max, max, max) * (float)brightness;
        }

        private static bool SunInView(Camera camera)
        {
            var sun = Planetarium.fetch?.Sun;
            if (sun == null)
                return false;

            Vector3 viewport = camera.WorldToViewportPoint(sun.position);
            return viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        }

        private static bool Load()
        {
            var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name == "DistantObject")?.assembly;
            if (assembly == null)
                return false;

            try
            {
                _darkenSkyInstance = assembly.GetType("DistantObject.DarkenSky")?.GetProperty("Instance", AnyStatic);
                _engineInstance = assembly.GetType("DistantObject.Contract.SolarSystemEngine")?.GetProperty("Instance", AnyStatic);
                _settingsInstance = assembly.GetType("DistantObject.Settings")?.GetProperty("Instance", AnyStatic);
                _skyboxSettings = _settingsInstance?.PropertyType.GetField("SkyboxBrightness");

                var skyboxType = _skyboxSettings?.FieldType;
                _maxBrightness = skyboxType?.GetField("maxBrightness");
                _referenceBodySize = skyboxType?.GetField("referenceBodySize");
                _minimumBodySize = skyboxType?.GetField("minimumSignificantBodySize");
                _minimumRelativeAngle = skyboxType?.GetField("minimumTargetRelativeAngle");

                var engineType = _engineInstance?.PropertyType;
                _sunBrightness = engineType?.GetMethod("CalculateSunBrightness");
                _planetsBrightness = engineType?.GetMethod("CalculatePlanetsBrightness");
                _coronaBrightness = engineType?.GetMethod("CalculateSunCoronaBrightness");

                bool bound = _darkenSkyInstance != null && _maxBrightness != null && _referenceBodySize != null
                    && _minimumBodySize != null && _minimumRelativeAngle != null
                    && _sunBrightness != null && _planetsBrightness != null && _coronaBrightness != null;

                Debug.Log(bound
                    ? "[JRTI-DOE]: Detected, feeds will dim their own sky"
                    : "[JRTI-DOE]: Detected but this version is not supported, feeds will instead follow the main camera's sky dimming");
                return bound;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-DOE]: Error binding sky dimming: {ex.Message}");
                return false;
            }
        }
    }
}
