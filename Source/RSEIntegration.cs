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
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const string SonicBoomGroup = "SONICBOOM";
        private const string InteriorChannel = "Interior";
        private const float UnityDefaultMaxDistance = 500f;
        private const int NormalMuffler = 0;
        private const int AirSimMuffler = 2;

        private static bool? _isAvailable;
        private static Type _moduleType;
        private static Type _effectType;
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
        private static bool _machVolumeReadable;
        private static FieldInfo _layerClips;
        private static FieldInfo _layerChannel;
        private static FieldInfo _layerVolume;
        private static FieldInfo _layerVolumeCurve;
        private static FieldInfo _layerRolloffMode;
        private static FieldInfo _layerMaxDistance;
        private static FieldInfo _layerRolloffCurve;
        private static bool _boomsReadable;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;
                _isAvailable = Load();
                return _isAvailable.Value;
            }
        }

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
                    () => (float)_moduleMachPass.GetValue(module));
            }

            foreach (var effect in part.GetComponentsInChildren(_effectType, true))
            {
                if (!ReferenceEquals(_effectSource.GetValue(effect), source)) continue;
                bool loop = (bool)_effectLoop.GetValue(effect);
                return new RsePlayerEffects(
                    () => loop ? (float)_effectDoppler.GetValue(effect) : 1f,
                    () => EffectScalesVolume(effect),
                    () => (float)_effectMachPass.GetValue(effect));
            }

            var shipEffects = part.vessel != null && _shipEffectsType != null ? part.vessel.GetComponent(_shipEffectsType) : null;
            if (shipEffects != null && _shipSources?.GetValue(shipEffects) is IDictionary shipSources)
            {
                string layer = LayerOf(shipSources, source);
                if (layer != null)
                    return new RsePlayerEffects(
                        () => 1f,
                        () => ShipScalesVolume(shipEffects, layer),
                        () => (float)_shipMachPass.GetValue(shipEffects));
            }
            return null;
        }

        internal static float MachVolume(float machPass) => Mathf.Log10(Mathf.Lerp(0.1f, 10f, machPass)) * 0.5f;

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

        private static string LayerOf(IDictionary sources, AudioSource source)
        {
            foreach (DictionaryEntry entry in sources)
                if (ReferenceEquals(entry.Value, source)) return entry.Key as string;
            return null;
        }

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

        public static bool FollowsPlayerCamera(AudioSource source)
        {
            var parent = source.transform.parent;
            return parent != null
                   && parent.name.StartsWith("ShipEffects_", StringComparison.Ordinal)
                   && source.name.IndexOf("SonicBoom", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<BoomLayer> SonicBoomLayers(Vessel vessel)
        {
            var layers = new List<BoomLayer>();
            if (!IsAvailable || !_boomsReadable) return layers;

            if (!(SoundLayerGroupsOf(vessel) is IDictionary groups)) return layers;

            foreach (DictionaryEntry group in groups)
            {
                if (group.Key.ToString() != SonicBoomGroup || !(group.Value is IEnumerable members)) continue;
                foreach (var member in members)
                {
                    var layer = ToBoomLayer(member);
                    if (layer != null) layers.Add(layer);
                }
            }
            return layers;
        }

        private static object SoundLayerGroupsOf(Vessel vessel)
        {
            if (_configSoundLayerGroups != null) return _configSoundLayerGroups.GetValue(null);
            var shipEffects = vessel.GetComponent(_shipEffectsType);
            return shipEffects != null ? _soundLayerGroups.GetValue(shipEffects) : null;
        }

        private static BoomLayer ToBoomLayer(object layer)
        {
            if (!(_layerClips.GetValue(layer) is AudioClip[] clips) || clips.Length == 0) return null;
            if (_layerChannel.GetValue(layer)?.ToString() == InteriorChannel) return null;
            return new BoomLayer(clips, VolumeOf(layer), RolloffOf(layer));
        }

        private static Func<float, float> VolumeOf(object layer)
        {
            if (_layerVolumeCurve.GetValue(layer) is FloatCurve curve) return control => curve.Evaluate(control);
            if (_layerVolume.GetValue(layer) is FXCurve fx) return control => fx.Value(control);
            return control => 1f;
        }

        private static Func<float, float> RolloffOf(object layer)
        {
            var mode = (AudioRolloffMode)_layerRolloffMode.GetValue(layer);
            float max = mode == AudioRolloffMode.Logarithmic ? UnityDefaultMaxDistance : Mathf.Max((float)_layerMaxDistance.GetValue(layer), 1f);
            if (mode == AudioRolloffMode.Custom && _layerRolloffCurve.GetValue(layer) is FloatCurve curve)
                return distance => curve.Evaluate(Mathf.Clamp01(distance / max));
            if (mode == AudioRolloffMode.Linear)
                return distance => Mathf.Clamp01(1f - distance / max);
            return distance => 1f / Mathf.Clamp(distance, 1f, max);
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

                LoadSonicBooms(assembly);
                LoadPartAudioManager(assembly);
                LoadMachVolume(assembly);
                Debug.Log("[JRTI-Audio]: Rocket Sound Enhancement support enabled");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Audio]: Rocket Sound Enhancement support failed to load: {ex.Message}");
                return false;
            }
        }

        private static void LoadSonicBooms(Assembly assembly)
        {
            _shipEffectsType = assembly.GetType("RocketSoundEnhancement.ShipEffects");
            var layerType = assembly.GetType("RocketSoundEnhancement.SoundLayer");
            _soundLayerGroups = _shipEffectsType?.GetField("SoundLayerGroups", InstanceFields);
            _configSoundLayerGroups = assembly.GetType("RocketSoundEnhancement.ShipEffectsConfig")?.GetField("SoundLayerGroups", StaticFields);
            _layerClips = layerType?.GetField("audioClips", InstanceFields);
            _layerChannel = layerType?.GetField("channel", InstanceFields);
            _layerVolume = layerType?.GetField("volume", InstanceFields);
            _layerVolumeCurve = layerType?.GetField("volumeFC", InstanceFields);
            _layerRolloffMode = layerType?.GetField("rolloffMode", InstanceFields);
            _layerMaxDistance = layerType?.GetField("maxDistance", InstanceFields);
            _layerRolloffCurve = layerType?.GetField("rollOffCurve", InstanceFields);

            _boomsReadable = (_configSoundLayerGroups != null || _soundLayerGroups != null) && _layerClips != null && _layerChannel != null && _layerVolume != null
                             && _layerVolumeCurve != null && _layerRolloffMode != null && _layerMaxDistance != null && _layerRolloffCurve != null;
            if (!_boomsReadable)
                Debug.LogWarning("[JRTI-Audio]: Rocket Sound Enhancement sonic boom layers not recognised (new version?) - cameras hear no sonic booms");
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

        private static void LoadPartAudioManager(Assembly assembly)
        {
            _partAudioManagerType = assembly.GetType("RocketSoundEnhancement.RSE_PartAudioManager");
            _managerSource = _partAudioManagerType?.GetField("source", InstanceFields);
            _managerMinDistance = _partAudioManagerType?.GetField("managedMinDistance", InstanceFields);
            if (_managerSource == null || _managerMinDistance == null) _partAudioManagerType = null;
        }
    }

    internal sealed class BoomLayer
    {
        private const float MaxControlMach = 4f;

        public readonly AudioClip[] Clips;
        private readonly Func<float, float> _volume;
        private readonly Func<float, float> _rolloff;

        public BoomLayer(AudioClip[] clips, Func<float, float> volume, Func<float, float> rolloff)
        {
            Clips = clips;
            _volume = volume;
            _rolloff = rolloff;
        }

        public float Gain(double mach, float distance) => _volume(Mathf.Min((float)mach, MaxControlMach)) * _rolloff(distance);
    }

    internal sealed class RsePlayerEffects
    {
        private const float MinMachVolume = 0.01f;
        private static readonly float NeutralMachVolume = RSEIntegration.MachVolume(1f);

        private readonly Func<float> _doppler;
        private readonly Func<bool> _machScalesVolume;
        private readonly Func<float> _machPass;

        public RsePlayerEffects(Func<float> doppler, Func<bool> machScalesVolume, Func<float> machPass)
        {
            _doppler = doppler;
            _machScalesVolume = machScalesVolume;
            _machPass = machPass;
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
