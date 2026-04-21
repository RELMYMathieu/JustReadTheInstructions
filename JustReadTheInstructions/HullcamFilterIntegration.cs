using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HullcamVDS;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class HullcamFilterIntegration
    {
        private static bool? _isAvailable;
        private static Assembly _hullcamAssembly;

        private static FieldInfo _mtField;
        private static MethodInfo _setCameraModeMethod;
        private static Type _eCameraModeType;
        private static object _normalModeValue;

        private static bool _cachePrepopulated;

        private class CachedFilter
        {
            public Type ComponentType;
            public (FieldInfo Field, object Value)[] Fields;
        }

        private static readonly Dictionary<int, CachedFilter> _cache = new Dictionary<int, CachedFilter>();

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

                    Debug.Log(_isAvailable.Value
                        ? "[JRTI-HullcamFilter]: Integration enabled"
                        : "[JRTI-HullcamFilter]: HullcamVDS not found");

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

        public static void SyncToCamera(Camera targetCamera, MuMechModuleHullCamera hullCamera)
        {
            if (!IsAvailable || targetCamera == null || hullCamera == null)
                return;

            int mode = (int)hullCamera.cameraMode;

            if (mode == 0)
            {
                RemoveFromCamera(targetCamera);
                return;
            }

            if (!_cachePrepopulated)
                TryPrepopulateCache();

            if (MuMechModuleHullCamera.sCurrentCamera == hullCamera)
                TryUpdateCacheFromMain(mode);

            if (!_cache.TryGetValue(mode, out var cached))
            {
                RemoveFromCamera(targetCamera);
                return;
            }

            ApplyCached(targetCamera, cached);
        }

        public static void RemoveFromCamera(Camera targetCamera)
        {
            if (!IsAvailable || targetCamera == null)
                return;

            var comp = FindHullcamComponent(targetCamera);
            if (comp != null)
                UnityEngine.Object.Destroy(comp);
        }

        private static void TryPrepopulateCache()
        {
            if (!EnsureReflectionReady())
            {
                _cachePrepopulated = true;
                return;
            }

            var cameras = MuMechModuleHullCamera.sCameras;
            if (cameras == null || cameras.Count == 0)
                return;

            _cachePrepopulated = true;

            var modes = cameras
                .Select(c => (int)c.cameraMode)
                .Where(m => m != 0)
                .Distinct()
                .ToList();

            if (modes.Count == 0)
                return;

            var probe = cameras.FirstOrDefault();
            if (probe == null)
                return;

            var mt = _mtField.GetValue(probe);
            if (mt == null)
                return;

            foreach (int mode in modes)
            {
                if (_cache.ContainsKey(mode))
                    continue;

                try
                {
                    _setCameraModeMethod.Invoke(mt, new[] { Enum.ToObject(_eCameraModeType, mode) });
                    TryUpdateCacheFromMain(mode);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[JRTI-HullcamFilter]: Failed to probe mode {mode}: {ex.Message}");
                }
            }

            try { _setCameraModeMethod.Invoke(mt, new[] { _normalModeValue }); }
            catch { }

            Debug.Log($"[JRTI-HullcamFilter]: Pre-populated cache for modes: [{string.Join(", ", _cache.Keys)}]");
        }

        private static bool EnsureReflectionReady()
        {
            if (_mtField != null)
                return true;

            _mtField = typeof(MuMechModuleHullCamera).GetField(
                "mt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_mtField == null)
                return false;

            _setCameraModeMethod = _mtField.FieldType.GetMethod(
                "SetCameraMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _eCameraModeType = _hullcamAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == "eCameraMode");

            if (_setCameraModeMethod == null || _eCameraModeType == null)
                return false;

            _normalModeValue = Enum.ToObject(_eCameraModeType, 0);
            return true;
        }

        private static void TryUpdateCacheFromMain(int mode)
        {
            var sourceComp = FindHullcamComponent(Camera.main);
            if (sourceComp == null)
                return;

            var type = sourceComp.GetType();
            _cache[mode] = new CachedFilter
            {
                ComponentType = type,
                Fields = type
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => (f, f.GetValue(sourceComp)))
                    .ToArray()
            };
        }

        private static void ApplyCached(Camera targetCamera, CachedFilter cached)
        {
            var existing = FindHullcamComponent(targetCamera);

            if (existing == null || existing.GetType() != cached.ComponentType)
            {
                if (existing != null)
                    UnityEngine.Object.Destroy(existing);
                existing = targetCamera.gameObject.AddComponent(cached.ComponentType) as MonoBehaviour;
            }

            foreach (var (field, value) in cached.Fields)
            {
                try { field.SetValue(existing, value); }
                catch { }
            }
        }

        private static MonoBehaviour FindHullcamComponent(Camera camera)
        {
            if (camera == null)
                return null;

            try
            {
                return camera.GetComponents<MonoBehaviour>()
                    .FirstOrDefault(c => c != null && c.GetType().Assembly == _hullcamAssembly);
            }
            catch
            {
                return null;
            }
        }

        public static string GetDiagnosticInfo()
        {
            if (!IsAvailable)
                return "HullcamFilter: unavailable\n";

            var comp = FindHullcamComponent(Camera.main);
            var cached = _cache.Count > 0
                ? $"cached modes: [{string.Join(", ", _cache.Keys)}]"
                : "no cached modes";
            return comp != null
                ? $"HullcamFilter: active ({comp.GetType().Name}), {cached}\n"
                : $"HullcamFilter: idle, {cached}\n";
        }
    }
}