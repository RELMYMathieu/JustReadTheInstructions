using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    public static class VGFXIntegration
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly int VolFarId = Shader.PropertyToID("_GEVolFar");
        private static readonly int DebrisBufferId = Shader.PropertyToID("_GEDebris");

        private static bool? _isAvailable;
        private static PropertyInfo _addonInstance;
        private static PropertyInfo _addonRenderer;
        private static Type _volumetricRendererType;
        private static FieldInfo _coreField;
        private static FieldInfo _rendererCameraField;
        private static Func<object, int> _getDivisor;
        private static FieldInfo _gcReadField;
        private static FieldInfo _gcWriteField;
        private static FieldInfo _volumeField;
        private static FieldInfo _debrisField;
        private static FieldInfo _debrisBufferField;
        private static Func<object, int> _getLowHeight;
        private static Action<object, int> _setLowHeight;
        private static Func<object, float> _getFovTan;
        private static Action<object, float> _setFovTan;
        private static MethodInfo _recordMethod;
        private static MethodInfo _updateCameraMethod;
        private static FieldInfo _paramsField;
        private static FieldInfo _pointsField;
        private static FieldInfo _gridOField;
        private static FieldInfo _gridUField;
        private static FieldInfo _markPointsField;
        private static PropertyInfo _markCountProperty;

        private static int _resolvedFrame = -1;
        private static VGFXCore _core;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                _isAvailable = Resolve();
                return _isAvailable.Value;
            }
        }

        internal static VGFXCore Current
        {
            get
            {
                if (!IsAvailable)
                    return null;

                if (_resolvedFrame != Time.frameCount)
                {
                    _resolvedFrame = Time.frameCount;
                    _core = ResolveCore();
                }
                return _core;
            }
        }

        private static bool Resolve()
        {
            try
            {
                var assembly = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a.name.Equals("GroundBlastFx", StringComparison.OrdinalIgnoreCase))?.assembly;

                if (assembly == null)
                {
                    Debug.Log("[JRTI-VGFX]: Volumetric Ground FX not found");
                    return false;
                }

                var addonType = assembly.GetType("GroundBlastFx.Core.GroundBlastFxAddon");
                _volumetricRendererType = assembly.GetType("GroundBlastFx.Rendering.VolumetricRenderer");
                var coreType = assembly.GetType("GroundBlastFx.Rendering.RenderCore");

                _addonInstance = addonType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _addonRenderer = addonType?.GetProperty("Renderer", BindingFlags.Public | BindingFlags.Instance);
                _coreField = _volumetricRendererType?.GetField("_core", Instance);
                var divisorField = _volumetricRendererType?.GetField("_divisor", Instance);
                _rendererCameraField = _volumetricRendererType?.GetField("_camera", Instance);

                _gcReadField = coreType?.GetField("_gcRead", Instance);
                _gcWriteField = coreType?.GetField("_gcWrite", Instance);
                _volumeField = coreType?.GetField("_volume", Instance);
                _debrisField = coreType?.GetField("_debris", Instance);
                _debrisBufferField = coreType?.GetField("_debrisBuffer", Instance);
                var lowHeightField = coreType?.GetField("_lowHeight", Instance);
                var fovTanField = coreType?.GetField("_fovTan", Instance);
                _recordMethod = coreType?.GetMethod("Record", new[] { typeof(CommandBuffer), typeof(int), typeof(int), typeof(int), typeof(int) });
                _updateCameraMethod = coreType?.GetMethod("UpdateCamera", new[] { typeof(Camera) });
                _paramsField = coreType?.GetField("_params", Instance);
                _pointsField = coreType?.GetField("_points", Instance);
                _gridOField = coreType?.GetField("_gridO", Instance);
                _gridUField = coreType?.GetField("_gridU", Instance);
                _markPointsField = coreType?.GetField("_markPoints", Instance);
                _markCountProperty = coreType?.GetProperty("MarkCount", BindingFlags.Public | BindingFlags.Instance);

                if (_addonInstance == null || _addonRenderer == null || _coreField == null || divisorField == null || _rendererCameraField == null
                    || _gcReadField == null || _gcWriteField == null || _volumeField == null || _debrisField == null
                    || _debrisBufferField == null || lowHeightField == null || fovTanField == null
                    || _recordMethod == null || _updateCameraMethod == null
                    || _paramsField == null || _pointsField == null || _gridOField == null || _gridUField == null
                    || _markPointsField == null || _markCountProperty == null)
                {
                    Debug.LogWarning("[JRTI-VGFX]: Volumetric Ground FX members not found - incompatible version?");
                    return false;
                }

                _getDivisor = Getter<int>(divisorField);
                _getLowHeight = Getter<int>(lowHeightField);
                _setLowHeight = Setter<int>(lowHeightField);
                _getFovTan = Getter<float>(fovTanField);
                _setFovTan = Setter<float>(fovTanField);

                Debug.Log("[JRTI-VGFX]: Integration enabled");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-VGFX]: Error checking availability: {ex.Message}");
                return false;
            }
        }

        private static VGFXCore ResolveCore()
        {
            try
            {
                var addon = _addonInstance.GetValue(null);
                var renderer = addon != null ? _addonRenderer.GetValue(addon) : null;
                if (renderer == null || !_volumetricRendererType.IsInstanceOfType(renderer))
                    return null;

                var core = _coreField.GetValue(renderer);
                if (core == null)
                    return null;

                return _core != null && ReferenceEquals(_core.Target, core)
                    ? _core
                    : new VGFXCore(core, renderer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-VGFX]: Failed to read renderer: {ex.Message}");
                _isAvailable = false;
                return null;
            }
        }

        private static Func<object, T> Getter<T>(FieldInfo field)
        {
            var method = new DynamicMethod("JRTI_get_" + field.Name, typeof(T), new[] { typeof(object) }, field.DeclaringType, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
        }

        private static Action<object, T> Setter<T>(FieldInfo field)
        {
            var method = new DynamicMethod("JRTI_set_" + field.Name, null, new[] { typeof(object), typeof(T) }, field.DeclaringType, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            return (Action<object, T>)method.CreateDelegate(typeof(Action<object, T>));
        }

        internal sealed class FeedMaterials
        {
            public Material Volume;
            public Material Debris;

            public void Destroy()
            {
                if (Volume != null) UnityEngine.Object.Destroy(Volume);
                if (Debris != null) UnityEngine.Object.Destroy(Debris);
                Volume = null;
                Debris = null;
            }

            public void CopyFrom(Material volume, Material debris)
            {
                Volume = CopyInto(Volume, volume);
                Debris = CopyInto(Debris, debris);
            }

            private static Material CopyInto(Material copy, Material source)
            {
                if (source == null)
                    return copy;

                if (copy == null || copy.shader != source.shader)
                {
                    if (copy != null) UnityEngine.Object.Destroy(copy);
                    copy = new Material(source) { name = source.name + " (JRTI feed)", hideFlags = HideFlags.DontSave };
                }
                else
                {
                    copy.CopyPropertiesFromMaterial(source);
                }
                return copy;
            }
        }

        internal sealed class VGFXCore
        {
            private const float ActiveThreshold = 0.002f;
            private const float VacuumMinRadius = 50f;
            private const int GroundSurfaces = 1;
            private const int WaterSurfaces = 2;

            public readonly object Target;
            private readonly object _renderer;

            private readonly Action<CommandBuffer, int, int, int, int> _record;
            private readonly Action<Camera> _updateCamera;
            private readonly Vector4[] _params;
            private readonly Vector4[] _points;
            private readonly Vector4[] _gridO;
            private readonly Vector4[] _gridU;
            private readonly Vector4[] _markPoints;
            private readonly Func<int> _markCount;

            public VGFXCore(object core, object renderer)
            {
                Target = core;
                _renderer = renderer;
                _record = (Action<CommandBuffer, int, int, int, int>)Delegate.CreateDelegate(
                    typeof(Action<CommandBuffer, int, int, int, int>), core, _recordMethod);
                _updateCamera = (Action<Camera>)Delegate.CreateDelegate(typeof(Action<Camera>), core, _updateCameraMethod);
                _markCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), core, _markCountProperty.GetGetMethod());
                _params = (Vector4[])_paramsField.GetValue(core);
                _points = (Vector4[])_pointsField.GetValue(core);
                _gridO = (Vector4[])_gridOField.GetValue(core);
                _gridU = (Vector4[])_gridUField.GetValue(core);
                _markPoints = (Vector4[])_markPointsField.GetValue(core);
            }

            public bool IsUsable
                => _volumeField.GetValue(Target) is Material volume && volume != null
                && _rendererCameraField.GetValue(_renderer) is Camera main && main != null && main.isActiveAndEnabled;

            public int MainDivisor => _getDivisor(_renderer);

            public int SlotCount => _params.Length;

            public int MarkCount => Math.Min(_markCount(), _markPoints.Length);

            public void RecordFeed(Camera camera, FeedMaterials materials, float volFar, int width, int height, int divisor,
                CommandBuffer ground, CommandBuffer water)
            {
                var volume = (Material)_volumeField.GetValue(Target);
                var debris = (Material)_debrisField.GetValue(Target);
                var gcRead = _gcReadField.GetValue(Target);
                var gcWrite = _gcWriteField.GetValue(Target);
                int lowHeight = _getLowHeight(Target);
                float fovTan = _getFovTan(Target);

                materials.CopyFrom(volume, debris);
                if (materials.Debris != null && _debrisBufferField.GetValue(Target) is ComputeBuffer debrisBuffer)
                    materials.Debris.SetBuffer(DebrisBufferId, debrisBuffer);

                try
                {
                    _volumeField.SetValue(Target, materials.Volume);
                    _debrisField.SetValue(Target, debris != null ? materials.Debris : null);
                    _gcReadField.SetValue(Target, null);
                    _gcWriteField.SetValue(Target, null);
                    _updateCamera(camera);
                    materials.Volume.SetFloat(VolFarId, volFar);
                    _record(ground, width, height, divisor, GroundSurfaces);
                    _record(water, width, height, divisor, WaterSurfaces);
                }
                finally
                {
                    _volumeField.SetValue(Target, volume);
                    _debrisField.SetValue(Target, debris);
                    _gcReadField.SetValue(Target, gcRead);
                    _gcWriteField.SetValue(Target, gcWrite);
                    _setLowHeight(Target, lowHeight);
                    _setFovTan(Target, fovTan);
                }
            }

            public bool TryGetCloud(int slot, out Vector3 center, out float radius)
            {
                center = default;
                radius = 0f;
                if (_params[slot].x < ActiveThreshold)
                    return false;

                if (_params[slot].w > 0.5f)
                {
                    center = _points[slot];
                    radius = Mathf.Max(_points[slot].w, VacuumMinRadius) * 1.2f;
                    return true;
                }

                Vector3 up = _gridU[slot];
                center = (Vector3)_gridO[slot] + up * (0.5f * _gridU[slot].w);
                radius = Mathf.Sqrt(2f * _gridO[slot].w * _gridO[slot].w + _gridU[slot].w * _gridU[slot].w);
                return true;
            }

            public void GetMark(int index, out Vector3 center, out float radius)
            {
                center = _markPoints[index];
                radius = _markPoints[index].w;
            }
        }
    }
}
