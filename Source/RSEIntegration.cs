using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;

namespace JustReadTheInstructions
{
    internal static class RSEIntegration
    {
        public const string SonicBoomGroup = "SONICBOOM";
        public const string ReentryHeatGroup = "REENTRYHEAT";
        internal const double FullMachEffectsPressureKPa = 0.4041;

        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const string InteriorChannel = "Interior";
        private const string EnginesModule = "RSE_Engines";
        private const string ShipEffectsParentPrefix = "ShipEffects_";
        private const string SourceTag = "RSE_";
        private const float UnityDefaultMaxDistance = 500f;
        private const float RseDefaultCombMix = 0.25f;
        private const float RseDefaultDistortion = 0.5f;
        private const int NormalMuffler = 0;
        private const int AirSimMuffler = 2;
        private const int MaxCachedLayerGroups = 64;

        private static readonly Dictionary<object, Dictionary<string, List<RseLayer>>> LayerCache = new Dictionary<object, Dictionary<string, List<RseLayer>>>();
        private static readonly List<RseLayer> NoLayers = new List<RseLayer>();

        private static bool? _isAvailable;
        private static Type _moduleType;
        private static Type _effectType;
        private static Type _filterType;
        private static FieldInfo _moduleSources;
        private static FieldInfo _moduleDoppler;
        private static FieldInfo _effectSource;
        private static FieldInfo _effectDoppler;
        private static PropertyInfo _interiorMixer;
        private static AudioMixerGroup _interiorGroup;
        private static Type _shipEffectsType;
        private static FieldInfo _soundLayerGroups;
        private static FieldInfo _configSoundLayerGroups;
        private static Type _partAudioManagerType;
        private static FieldInfo _managerSource;
        private static FieldInfo _managerMinDistance;
        private static FieldInfo _moduleMachPass;
        private static FieldInfo _moduleAirSimFilters;
        private static FieldInfo _moduleUsesAirSim;
        private static FieldInfo _effectLoop;
        private static FieldInfo _effectAirSimFilter;
        private static FieldInfo _effectMachPass;
        private static FieldInfo _shipSources;
        private static FieldInfo _shipAirSimFilters;
        private static FieldInfo _shipMachPass;
        private static FieldInfo _mufflerQuality;
        private static FieldInfo _audioEffectsEnabled;
        private static FieldInfo _machEffectsAmount;
        private static FieldInfo _exteriorVolume;
        private static FieldInfo _autoLimiter;
        private static FieldInfo _customLimiter;
        private static FieldInfo _limiterThreshold;
        private static FieldInfo _limiterGain;
        private static FieldInfo _limiterAttack;
        private static FieldInfo _limiterRelease;
        private static FieldInfo _defaultCombMix;
        private static FieldInfo _defaultDistortion;
        private static bool _machVolumeReadable;
        private static bool _airSimReadable;
        private static FieldInfo _layerName;
        private static FieldInfo _layerClips;
        private static FieldInfo _layerChannel;
        private static FieldInfo _layerVolume;
        private static FieldInfo _layerVolumeCurve;
        private static FieldInfo _layerPitch;
        private static FieldInfo _layerPitchCurve;
        private static FieldInfo _layerRolloffMode;
        private static FieldInfo _layerMaxDistance;
        private static FieldInfo _layerRolloffCurve;
        private static FieldInfo _layerMassToVolume;
        private static FieldInfo _layerMassToPitch;
        private static bool _layersReadable;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;
                _isAvailable = Load();
                return _isAvailable.Value;
            }
        }

        internal static float EffectiveMach(Vessel vessel)
            => (float)vessel.mach * Mathf.Clamp01((float)(vessel.staticPressurekPa / FullMachEffectsPressureKPa));

        internal static float MachVolume(float machPass) => Mathf.Log10(Mathf.Lerp(0.1f, 10f, machPass)) * 0.5f;

        public static RsePlayerEffects PlayerEffectsOf(AudioSource source, Part part)
        {
            if (part == null || !IsAvailable) return null;

            foreach (PartModule module in part.Modules)
            {
                if (!_moduleType.IsInstanceOfType(module) || !(_moduleSources.GetValue(module) is IDictionary sources)) continue;
                string layer = LayerOf(sources, source);
                if (layer == null) continue;
                return new RsePlayerEffects(
                    () => (float)_moduleDoppler.GetValue(module),
                    () => ModuleScalesVolume(module, layer),
                    () => (float)_moduleMachPass.GetValue(module),
                    FilterProfile(module, _moduleAirSimFilters, layer),
                    module.GetType().Name == EnginesModule);
            }

            foreach (var effect in part.GetComponentsInChildren(_effectType, true))
            {
                if (!ReferenceEquals(_effectSource.GetValue(effect), source)) continue;
                bool loop = (bool)_effectLoop.GetValue(effect);
                return new RsePlayerEffects(
                    () => loop ? (float)_effectDoppler.GetValue(effect) : 1f,
                    () => EffectScalesVolume(effect),
                    () => (float)_effectMachPass.GetValue(effect),
                    ProfileFrom(name => _effectType.GetField(name)?.GetValue(effect)),
                    false);
            }

            var shipEffects = part.vessel != null && _shipEffectsType != null ? part.vessel.GetComponent(_shipEffectsType) : null;
            if (shipEffects != null && _shipSources?.GetValue(shipEffects) is IDictionary shipSources)
            {
                string layer = LayerOf(shipSources, source);
                if (layer != null)
                    return new RsePlayerEffects(
                        () => 1f,
                        () => ShipScalesVolume(shipEffects, layer),
                        () => (float)_shipMachPass.GetValue(shipEffects),
                        FilterProfile(shipEffects, _shipAirSimFilters, layer),
                        false);
            }
            return null;
        }

        public static bool IsReplacedByJrti(AudioSource source)
        {
            var parent = source.transform.parent;
            if (parent == null || !parent.name.StartsWith(ShipEffectsParentPrefix, StringComparison.Ordinal)) return false;

            var vessel = source.GetComponentInParent<Vessel>();
            if (vessel == null) return false;

            string layerName = source.name.StartsWith(SourceTag, StringComparison.Ordinal) ? source.name.Substring(SourceTag.Length) : source.name;
            return ShipLayers(vessel, SonicBoomGroup).Any(layer => layer.Name == layerName)
                   || ShipLayers(vessel, ReentryHeatGroup).Any(layer => layer.Name == layerName);
        }

        public static List<RseLayer> ShipLayers(Vessel vessel, string group)
        {
            var groups = IsAvailable && _layersReadable ? SoundLayerGroupsOf(vessel) as IDictionary : null;
            if (groups == null) return NoLayers;

            if (!LayerCache.TryGetValue(groups, out var byGroup))
            {
                if (LayerCache.Count >= MaxCachedLayerGroups) LayerCache.Clear();
                LayerCache[groups] = byGroup = ReadLayerGroups(groups);
            }
            return byGroup.TryGetValue(group, out var layers) ? layers : NoLayers;
        }

        public static MasteringSettings Mastering()
        {
            if (!IsAvailable || _autoLimiter == null) return MasteringSettings.RseDefault;
            if (_customLimiter != null && (bool)_customLimiter.GetValue(null))
                return new MasteringSettings((float)_limiterThreshold.GetValue(null), (float)_limiterGain.GetValue(null),
                    (float)_limiterAttack.GetValue(null), (float)_limiterRelease.GetValue(null));
            return MasteringSettings.FromAutoLimiter((float)_autoLimiter.GetValue(null));
        }

        public static float ExteriorVolume() => IsAvailable && _exteriorVolume != null ? (float)_exteriorVolume.GetValue(null) : 1f;

        public static Func<float> PlayerNeutralMinDistanceOf(AudioSource source, Part part)
        {
            if (part == null || !IsAvailable || _partAudioManagerType == null) return null;
            var manager = part.GetComponent(_partAudioManagerType);
            if (manager == null || !ReferenceEquals(_managerSource.GetValue(manager), source)) return null;
            return () => (float)_managerMinDistance.GetValue(manager);
        }

        public static bool IsInterior(AudioMixerGroup group)
        {
            if (group == null || !IsAvailable) return false;
            if (ReferenceEquals(_interiorGroup, null)) _interiorGroup = _interiorMixer.GetValue(null) as AudioMixerGroup;
            return ReferenceEquals(group, _interiorGroup);
        }

        private static bool ModuleScalesVolume(object module, string layer)
            => MachScalesVolume(requiresAudioEffects: true)
               && !(IsAirSim() && Has(_moduleAirSimFilters.GetValue(module), layer) && (bool)_moduleUsesAirSim.GetValue(module));

        private static bool EffectScalesVolume(object effect)
            => MachScalesVolume(requiresAudioEffects: false)
               && !(IsAirSim() && _effectAirSimFilter.GetValue(effect) is UnityEngine.Object filter && filter != null);

        private static bool ShipScalesVolume(object shipEffects, string layer)
            => MachScalesVolume(requiresAudioEffects: false)
               && !(IsAirSim() && Has(_shipAirSimFilters.GetValue(shipEffects), layer));

        private static bool MachScalesVolume(bool requiresAudioEffects)
            => _machVolumeReadable
               && (!requiresAudioEffects || (bool)_audioEffectsEnabled.GetValue(null))
               && Convert.ToInt32(_mufflerQuality.GetValue(null)) > NormalMuffler
               && (float)_machEffectsAmount.GetValue(null) > 0f;

        private static bool IsAirSim() => Convert.ToInt32(_mufflerQuality.GetValue(null)) == AirSimMuffler;

        private static bool Has(object dictionary, string key) => dictionary is IDictionary entries && entries.Contains(key);

        private static AirSimProfile FilterProfile(object owner, FieldInfo filtersField, string layer)
        {
            if (!_airSimReadable || filtersField == null) return AirSimProfile.Full;
            if (!(filtersField.GetValue(owner) is IDictionary filters) || !filters.Contains(layer)) return new AirSimProfile(0f, 0f, 0f);
            var filter = filters[layer];
            return ProfileFrom(name => _filterType.GetProperty(name)?.GetValue(filter));
        }

        private static AirSimProfile ProfileFrom(Func<string, object> read)
        {
            if (!_airSimReadable) return AirSimProfile.Full;
            try
            {
                bool comb = read("EnableCombFilter") is bool c && c;
                bool distortion = read("EnableDistortionFilter") is bool d && d;
                float combMix = read("MaxCombMix") is float m ? m : RseDefaultCombMix;
                float maxDistortion = read("MaxDistortion") is float x ? x : RseDefaultDistortion;
                float highpass = read("AngleHighpass") is float h ? h : 0f;
                return new AirSimProfile(
                    comb ? combMix / DefaultOf(_defaultCombMix, RseDefaultCombMix) : 0f,
                    distortion ? maxDistortion / DefaultOf(_defaultDistortion, RseDefaultDistortion) : 0f,
                    highpass);
            }
            catch (Exception)
            {
                return AirSimProfile.Full;
            }
        }

        private static float DefaultOf(FieldInfo field, float fallback)
            => field?.GetValue(null) is float value && value > 0f ? value : fallback;

        private static string LayerOf(IDictionary sources, AudioSource source)
        {
            foreach (DictionaryEntry entry in sources)
                if (ReferenceEquals(entry.Value, source)) return entry.Key as string;
            return null;
        }

        private static object SoundLayerGroupsOf(Vessel vessel)
        {
            if (_configSoundLayerGroups != null) return _configSoundLayerGroups.GetValue(null);
            var shipEffects = vessel.GetComponent(_shipEffectsType);
            return shipEffects != null ? _soundLayerGroups.GetValue(shipEffects) : null;
        }

        private static Dictionary<string, List<RseLayer>> ReadLayerGroups(IDictionary groups)
        {
            var byGroup = new Dictionary<string, List<RseLayer>>();
            foreach (DictionaryEntry group in groups)
            {
                if (!(group.Value is IEnumerable members)) continue;
                var layers = new List<RseLayer>();
                foreach (var member in members)
                {
                    var layer = ToLayer(member);
                    if (layer != null) layers.Add(layer);
                }
                byGroup[group.Key.ToString()] = layers;
            }
            return byGroup;
        }

        private static RseLayer ToLayer(object layer)
        {
            if (!(_layerClips.GetValue(layer) is AudioClip[] clips) || clips.Length == 0) return null;
            if (_layerChannel.GetValue(layer)?.ToString() == InteriorChannel) return null;
            return new RseLayer(
                _layerName.GetValue(layer) as string ?? "",
                clips,
                CurveOf(layer, _layerVolumeCurve, _layerVolume),
                CurveOf(layer, _layerPitchCurve, _layerPitch),
                CurveOf(layer, null, _layerMassToVolume),
                CurveOf(layer, null, _layerMassToPitch),
                RolloffOf(layer));
        }

        private static Func<float, float> CurveOf(object layer, FieldInfo floatCurve, FieldInfo fxCurve)
        {
            if (floatCurve?.GetValue(layer) is FloatCurve curve) return control => curve.Evaluate(control);
            if (fxCurve?.GetValue(layer) is FXCurve fx) return control => fx.Value(control);
            return control => 1f;
        }

        private static RseRolloff RolloffOf(object layer)
        {
            var mode = (AudioRolloffMode)_layerRolloffMode.GetValue(layer);
            float max = mode == AudioRolloffMode.Logarithmic ? UnityDefaultMaxDistance : Mathf.Max((float)_layerMaxDistance.GetValue(layer), 1f);
            var curve = mode == AudioRolloffMode.Custom ? _layerRolloffCurve.GetValue(layer) as FloatCurve : null;
            return new RseRolloff(mode, max, curve);
        }

        private static bool Load()
        {
            try
            {
                var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name == "RocketSoundEnhancement")?.assembly;
                if (assembly == null) return false;

                _moduleType = assembly.GetType("RocketSoundEnhancement.PartModules.RSE_Module");
                _effectType = assembly.GetType("RocketSoundEnhancement.EffectBehaviours.RSE_AudioEffects");
                var mainType = assembly.GetType("RocketSoundEnhancement.RocketSoundEnhancement");
                _moduleSources = _moduleType?.GetField("Sources", InstanceFields);
                _moduleDoppler = _moduleType?.GetField("doppler", InstanceFields);
                _effectSource = _effectType?.GetField("audioSource", InstanceFields);
                _effectDoppler = _effectType?.GetField("doppler", InstanceFields);
                _interiorMixer = mainType?.GetProperty("InteriorMixer", BindingFlags.Public | BindingFlags.Static);

                if (_moduleSources == null || _moduleDoppler == null || _effectSource == null || _effectDoppler == null || _interiorMixer == null)
                {
                    Debug.LogWarning("[JRTI-Audio]: Rocket Sound Enhancement found but not recognised (new version?) - its sounds keep the player camera's Doppler on JRTI cameras");
                    return false;
                }

                LoadLayers(assembly);
                LoadPartAudioManager(assembly);
                LoadMachVolume(assembly);
                LoadSettings(assembly);
                Debug.Log("[JRTI-Audio]: Rocket Sound Enhancement support enabled");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Audio]: Rocket Sound Enhancement support failed to load: {ex.Message}");
                return false;
            }
        }

        private static void LoadLayers(Assembly assembly)
        {
            _shipEffectsType = assembly.GetType("RocketSoundEnhancement.ShipEffects");
            var layerType = assembly.GetType("RocketSoundEnhancement.SoundLayer");
            _soundLayerGroups = _shipEffectsType?.GetField("SoundLayerGroups", InstanceFields);
            _configSoundLayerGroups = assembly.GetType("RocketSoundEnhancement.ShipEffectsConfig")?.GetField("SoundLayerGroups", StaticFields);
            _layerName = layerType?.GetField("name", InstanceFields);
            _layerClips = layerType?.GetField("audioClips", InstanceFields);
            _layerChannel = layerType?.GetField("channel", InstanceFields);
            _layerVolume = layerType?.GetField("volume", InstanceFields);
            _layerVolumeCurve = layerType?.GetField("volumeFC", InstanceFields);
            _layerPitch = layerType?.GetField("pitch", InstanceFields);
            _layerPitchCurve = layerType?.GetField("pitchFC", InstanceFields);
            _layerRolloffMode = layerType?.GetField("rolloffMode", InstanceFields);
            _layerMaxDistance = layerType?.GetField("maxDistance", InstanceFields);
            _layerRolloffCurve = layerType?.GetField("rollOffCurve", InstanceFields);
            _layerMassToVolume = layerType?.GetField("massToVolume", InstanceFields);
            _layerMassToPitch = layerType?.GetField("massToPitch", InstanceFields);

            _layersReadable = (_configSoundLayerGroups != null || _soundLayerGroups != null) && _layerName != null && _layerClips != null && _layerChannel != null
                              && _layerVolume != null && _layerVolumeCurve != null && _layerRolloffMode != null && _layerMaxDistance != null && _layerRolloffCurve != null;
            if (!_layersReadable)
                Debug.LogWarning("[JRTI-Audio]: Rocket Sound Enhancement ship sound layers not recognised (new version?) - cameras hear no sonic booms and RSE's own re-entry sound");
        }

        private static void LoadMachVolume(Assembly assembly)
        {
            var settingsType = assembly.GetType("RocketSoundEnhancement.Settings");
            _mufflerQuality = settingsType?.GetField("MufflerQuality", StaticFields);
            _audioEffectsEnabled = settingsType?.GetField("EnableAudioEffects", StaticFields);
            _machEffectsAmount = settingsType?.GetField("MachEffectsAmount", StaticFields);
            _moduleMachPass = _moduleType.GetField("machPass", InstanceFields);
            _moduleAirSimFilters = _moduleType.GetField("AirSimFilters", InstanceFields);
            _moduleUsesAirSim = _moduleType.GetField("UseAirSimulation", InstanceFields);
            _effectLoop = _effectType.GetField("loop", InstanceFields);
            _effectAirSimFilter = _effectType.GetField("airSimFilter", InstanceFields);
            _effectMachPass = _effectType.GetField("machPass", InstanceFields);
            _shipSources = _shipEffectsType?.GetField("Sources", InstanceFields);
            _shipAirSimFilters = _shipEffectsType?.GetField("AirSimFilters", InstanceFields);
            _shipMachPass = _shipEffectsType?.GetField("MachPass", InstanceFields);

            _machVolumeReadable = _mufflerQuality != null && _audioEffectsEnabled != null && _machEffectsAmount != null
                                  && _moduleMachPass != null && _moduleAirSimFilters != null && _moduleUsesAirSim != null
                                  && _effectLoop != null && _effectAirSimFilter != null && _effectMachPass != null
                                  && _shipSources != null && _shipAirSimFilters != null && _shipMachPass != null;
            if (!_machVolumeReadable)
                Debug.LogWarning("[JRTI-Audio]: Rocket Sound Enhancement Mach settings not recognised (new version?) - some RSE sounds keep the player camera's Mach muffling on JRTI cameras");
        }

        private static void LoadSettings(Assembly assembly)
        {
            var settingsType = assembly.GetType("RocketSoundEnhancement.Settings");
            _filterType = assembly.GetType("RocketSoundEnhancement.AudioFilters.AirSimulationFilter");
            _exteriorVolume = settingsType?.GetField("ExteriorVolume", StaticFields);
            _autoLimiter = settingsType?.GetField("AutoLimiter", StaticFields);
            _customLimiter = settingsType?.GetField("EnableCustomLimiter", StaticFields);
            _limiterThreshold = settingsType?.GetField("LimiterThreshold", StaticFields);
            _limiterGain = settingsType?.GetField("LimiterGain", StaticFields);
            _limiterAttack = settingsType?.GetField("LimiterAttack", StaticFields);
            _limiterRelease = settingsType?.GetField("LimiterRelease", StaticFields);
            _defaultCombMix = settingsType?.GetField("AirSimMaxCombMix", StaticFields);
            _defaultDistortion = settingsType?.GetField("AirSimMaxDistortion", StaticFields);
            if (_customLimiter == null || _limiterThreshold == null || _limiterGain == null || _limiterAttack == null || _limiterRelease == null)
                _customLimiter = null;

            _airSimReadable = _filterType != null && _filterType.GetProperty("EnableCombFilter") != null && _filterType.GetProperty("MaxDistortion") != null;
            if (!_airSimReadable)
                Debug.LogWarning("[JRTI-Audio]: Rocket Sound Enhancement AirSim settings not recognised (new version?) - every RSE sound gets JRTI's full air character on cameras");
        }

        private static void LoadPartAudioManager(Assembly assembly)
        {
            _partAudioManagerType = assembly.GetType("RocketSoundEnhancement.RSE_PartAudioManager");
            _managerSource = _partAudioManagerType?.GetField("source", InstanceFields);
            _managerMinDistance = _partAudioManagerType?.GetField("managedMinDistance", InstanceFields);
            if (_managerSource == null || _managerMinDistance == null) _partAudioManagerType = null;
        }
    }

    internal sealed class RseLayer : IEmitterShape
    {
        public readonly string Name;
        public readonly AudioClip[] Clips;
        private readonly Func<float, float> _volume;
        private readonly Func<float, float> _pitch;
        private readonly Func<float, float> _massToVolume;
        private readonly Func<float, float> _massToPitch;
        private readonly RseRolloff _rolloff;

        public RseLayer(string name, AudioClip[] clips, Func<float, float> volume, Func<float, float> pitch,
            Func<float, float> massToVolume, Func<float, float> massToPitch, RseRolloff rolloff)
        {
            Name = name;
            Clips = clips;
            _volume = volume;
            _pitch = pitch;
            _massToVolume = massToVolume;
            _massToPitch = massToPitch;
            _rolloff = rolloff;
        }

        public float Volume(float control, float mass) => _volume(control) * _massToVolume(mass);

        public float Pitch(float control, float mass) => _pitch(control) * _massToPitch(mass);

        public float Rolloff(float distance, bool inMetres) => _rolloff.At(distance, inMetres);

        public float GameRolloff(float distance) => _rolloff.At(distance, false);

        public float Width(float distance) => 0f;
    }

    internal sealed class RseRolloff
    {
        private readonly AudioRolloffMode _mode;
        private readonly float _maxDistance;
        private readonly FloatCurve _curve;
        private readonly bool _keyedInMetres;
        private readonly float _peakDistance;
        private readonly float _peakValue;

        public RseRolloff(AudioRolloffMode mode, float maxDistance, FloatCurve curve)
        {
            _mode = mode;
            _maxDistance = maxDistance;
            _curve = curve;
            var keys = curve?.Curve.keys;
            if (keys == null || keys.Length == 0) return;

            _keyedInMetres = keys[keys.Length - 1].time > 1f;
            var peak = keys.OrderByDescending(key => key.value).First();
            _peakDistance = peak.time;
            _peakValue = peak.value;
        }

        public float At(float distance, bool inMetres)
        {
            if (_mode == AudioRolloffMode.Custom && _curve != null)
            {
                if (!inMetres || !_keyedInMetres) return _curve.Evaluate(Mathf.Clamp01(distance / _maxDistance));
                return _peakDistance > 0f && distance >= _peakDistance ? _peakValue : _curve.Evaluate(distance);
            }
            if (_mode == AudioRolloffMode.Linear) return Mathf.Clamp01(1f - distance / _maxDistance);
            return 1f / Mathf.Clamp(distance, 1f, _maxDistance);
        }
    }

    internal sealed class RsePlayerEffects
    {
        private const float MinMachVolume = 0.01f;
        private static readonly float NeutralMachVolume = RSEIntegration.MachVolume(1f);

        public readonly AirSimProfile AirSim;
        public readonly bool LoudnessScalesWithThrust;
        private readonly Func<float> _doppler;
        private readonly Func<bool> _machScalesVolume;
        private readonly Func<float> _machPass;

        public RsePlayerEffects(Func<float> doppler, Func<bool> machScalesVolume, Func<float> machPass, AirSimProfile airSim, bool loudnessScalesWithThrust)
        {
            _doppler = doppler;
            _machScalesVolume = machScalesVolume;
            _machPass = machPass;
            AirSim = airSim;
            LoudnessScalesWithThrust = loudnessScalesWithThrust;
        }

        public float Doppler => _doppler();

        public float VolumeCorrection
        {
            get
            {
                if (!_machScalesVolume()) return 1f;
                float applied = RSEIntegration.MachVolume(_machPass());
                return applied > MinMachVolume ? NeutralMachVolume / applied : 1f;
            }
        }
    }
}
