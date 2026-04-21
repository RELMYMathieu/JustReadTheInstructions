using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    public static class FireflyIntegration
    {
        private static bool? _isAvailable;
        private static Assembly _fireflyAssembly;
        private static Type _cameraManagerType;
        private static PropertyInfo _instanceProperty;
        private static FieldInfo _cameraBuffersField;

        private static readonly Dictionary<Camera, List<(CameraEvent evt, CommandBuffer buf)>> _appliedBuffers
            = new Dictionary<Camera, List<(CameraEvent, CommandBuffer)>>();

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    Debug.Log("[JRTI-Firefly]: Searching for Firefly assembly...");

                    _fireflyAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name.Equals("Firefly", StringComparison.OrdinalIgnoreCase))?.assembly;

                    if (_fireflyAssembly == null)
                    {
                        Debug.Log("[JRTI-Firefly]: Firefly not found - re-entry effects disabled");
                        _isAvailable = false;
                        return false;
                    }

                    Debug.Log($"[JRTI-Firefly]: Found assembly: {_fireflyAssembly.GetName().Name}");

                    _cameraManagerType = _fireflyAssembly.GetType("Firefly.CameraManager");

                    if (_cameraManagerType == null)
                    {
                        Debug.LogWarning("[JRTI-Firefly]: CameraManager type not found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _instanceProperty = _cameraManagerType.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static);

                    _cameraBuffersField = _cameraManagerType.GetField("cameraBuffers",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (_instanceProperty == null || _cameraBuffersField == null)
                    {
                        Debug.LogWarning("[JRTI-Firefly]: CameraManager members not found - incompatible version?");
                        _isAvailable = false;
                        return false;
                    }

                    _isAvailable = true;
                    Debug.Log("[JRTI-Firefly]: Integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JRTI-Firefly]: Error checking availability: {ex.Message}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        public static bool ShouldHaveEffects(Vessel vessel)
        {
            if (!IsAvailable || vessel == null)
                return false;

            if (!vessel.mainBody.atmosphere)
                return false;

            if (!vessel.loaded || vessel.packed)
                return false;

            if (vessel.altitude > vessel.mainBody.atmosphereDepth)
                return false;

            return true;
        }

        public static bool HasActiveEffects()
        {
            if (!IsAvailable)
                return false;

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null) return false;

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null) return false;

                var buffersList = cameraBuffers as System.Collections.IList;
                return buffersList != null && buffersList.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void ApplyToCamera(Camera camera)
        {
            if (!IsAvailable || camera == null)
                return;

            RemoveFromCamera(camera);

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null) return;

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null) return;

                var buffersList = cameraBuffers as System.Collections.IList;
                if (buffersList == null || buffersList.Count == 0) return;

                var added = new List<(CameraEvent, CommandBuffer)>();

                foreach (var item in buffersList)
                {
                    var itemType = item.GetType();
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");

                    if (keyProperty == null || valueProperty == null) continue;

                    var cameraEvent = (CameraEvent)keyProperty.GetValue(item);
                    var commandBuffer = (CommandBuffer)valueProperty.GetValue(item);

                    if (commandBuffer == null) continue;

                    var existingBuffers = camera.GetCommandBuffers(cameraEvent);
                    if (existingBuffers.Contains(commandBuffer)) continue;

                    camera.AddCommandBuffer(cameraEvent, commandBuffer);
                    added.Add((cameraEvent, commandBuffer));
                }

                if (added.Count > 0)
                {
                    _appliedBuffers[camera] = added;
                    Debug.Log($"[JRTI-Firefly]: Added {added.Count} command buffers to {camera.name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Firefly]: Failed to apply effects to {camera.name}: {ex.Message}");
            }
        }

        public static void RemoveFromCamera(Camera camera)
        {
            if (camera == null)
                return;

            if (!_appliedBuffers.TryGetValue(camera, out var buffers))
                return;

            int removed = 0;

            try
            {
                foreach (var (evt, buf) in buffers)
                {
                    if (buf == null) continue;

                    var existing = camera.GetCommandBuffers(evt);
                    if (existing.Contains(buf))
                    {
                        camera.RemoveCommandBuffer(evt, buf);
                        removed++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Firefly]: Error removing effects from {camera.name}: {ex.Message}");
            }
            finally
            {
                _appliedBuffers.Remove(camera);
            }

            if (removed > 0)
                Debug.Log($"[JRTI-Firefly]: Removed {removed} command buffers from {camera.name}");
        }

        public static void UpdateForCamera(Camera camera, Vessel vessel)
        {
            if (!IsAvailable || camera == null)
                return;

            bool shouldHaveEffects = ShouldHaveEffects(vessel) && HasActiveEffects();
            bool hasBuffers = _appliedBuffers.ContainsKey(camera);

            if (shouldHaveEffects && !hasBuffers)
                ApplyToCamera(camera);
            else if (!shouldHaveEffects && hasBuffers)
                RemoveFromCamera(camera);
        }

        public static void CleanupCamera(Camera camera)
        {
            if (camera != null)
                _appliedBuffers.Remove(camera);
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsAvailable)
                return "Firefly not available";

            if (camera == null)
                return "Camera is null";

            var info = $"Firefly Integration for {camera.name}:\n";
            bool tracked = _appliedBuffers.TryGetValue(camera, out var buffers);
            info += $"- Tracked as having buffers: {tracked}\n";

            if (tracked)
                info += $"- Tracked buffer count: {buffers.Count}\n";

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null)
                {
                    info += "- CameraManager instance: Not found\n";
                    return info;
                }

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null)
                {
                    info += "- Command buffers list: Not found\n";
                    return info;
                }

                var buffersList = cameraBuffers as System.Collections.IList;
                if (buffersList == null)
                {
                    info += "- Could not access buffers list\n";
                    return info;
                }

                info += $"- Total Firefly command buffers available: {buffersList.Count}\n";

                int buffersOnCamera = 0;
                foreach (var item in buffersList)
                {
                    var itemType = item.GetType();
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");

                    if (keyProperty == null || valueProperty == null) continue;

                    var cameraEvent = (CameraEvent)keyProperty.GetValue(item);
                    var commandBuffer = (CommandBuffer)valueProperty.GetValue(item);

                    if (commandBuffer == null) continue;

                    var existingBuffers = camera.GetCommandBuffers(cameraEvent);
                    if (existingBuffers.Contains(commandBuffer))
                    {
                        info += $"  * {commandBuffer.name} at {cameraEvent}\n";
                        buffersOnCamera++;
                    }
                }

                if (buffersOnCamera == 0)
                    info += "- No Firefly command buffers found on this camera\n";
                else
                    info += $"- Total buffers on camera: {buffersOnCamera}\n";
            }
            catch (Exception ex)
            {
                info += $"- Error getting diagnostic info: {ex.Message}\n";
            }

            return info;
        }
    }
}