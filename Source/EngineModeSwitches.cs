using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class EngineModeSwitches
    {
        private const float SwitchSeconds = 0.5f;
        private const float BridgeSeconds = 1.5f;
        private const float BridgeFadeSeconds = 0.3f;

        private static readonly FieldInfo EffectSources = typeof(AudioFX).GetField("sources", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class MultiModePart
        {
            public ModuleEnginesFX[] Engines;
            public AudioFX[] Loops;
        }

        private sealed class Bridge
        {
            public float HeldVolume;
            public AudioFX[] Replacements;
            public float Until;
        }

        private readonly Dictionary<Part, MultiModePart> _parts = new Dictionary<Part, MultiModePart>();
        private readonly Dictionary<ModuleEnginesFX, bool> _wasIgnited = new Dictionary<ModuleEnginesFX, bool>();
        private readonly Dictionary<AudioSource, float> _lastVolume = new Dictionary<AudioSource, float>();
        private readonly Dictionary<AudioSource, Bridge> _bridges = new Dictionary<AudioSource, Bridge>();
        private readonly HashSet<AudioSource> _muted = new HashSet<AudioSource>();
        private readonly HashSet<string> _switchEffects = new HashSet<string>();
        private readonly Dictionary<Part, float> _switchingUntil = new Dictionary<Part, float>();
        private readonly List<ModuleEnginesFX> _started = new List<ModuleEnginesFX>();
        private readonly List<ModuleEnginesFX> _stopped = new List<ModuleEnginesFX>();
        private readonly List<Part> _goneParts = new List<Part>();
        private readonly List<AudioSource> _endedBridges = new List<AudioSource>();

        public bool Mutes(AudioSource source) => _muted.Contains(source);

        public bool Bridges(AudioSource source, out float gain)
        {
            gain = 0f;
            if (!_bridges.TryGetValue(source, out var bridge)) return false;

            float replaced = 0f;
            foreach (var effect in bridge.Replacements)
                foreach (var replacement in SourcesOf(effect))
                    if (replacement != null && replacement.isPlaying) replaced += replacement.volume;

            float fade = Mathf.Clamp01((bridge.Until - Time.unscaledTime) / BridgeFadeSeconds);
            gain = Mathf.Clamp01(1f - replaced / bridge.HeldVolume) * fade;
            return true;
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            _muted.RemoveWhere(source => source == null || !source.isPlaying);
            EndBridges(now);

            foreach (var vessel in FlightGlobals.VesselsLoaded)
                foreach (var part in vessel.parts)
                {
                    var multiMode = MultiModeOf(part);
                    if (multiMode == null) continue;
                    if (SwitchedMode(multiMode))
                    {
                        _switchingUntil[part] = now + SwitchSeconds;
                        BridgeRunningSounds(multiMode, now);
                    }
                    RememberVolumes(multiMode);
                }

            _goneParts.Clear();
            foreach (var kv in _switchingUntil)
            {
                if (kv.Key == null || now > kv.Value) _goneParts.Add(kv.Key);
                else MuteSwitchEffects(kv.Key, _parts[kv.Key]);
            }
            foreach (var part in _goneParts)
                _switchingUntil.Remove(part);
        }

        public void Forget()
        {
            _goneParts.Clear();
            foreach (var part in _parts.Keys)
                if (part == null) _goneParts.Add(part);
            foreach (var part in _goneParts)
            {
                if (_parts[part] != null)
                    foreach (var engine in _parts[part].Engines) _wasIgnited.Remove(engine);
                _parts.Remove(part);
            }

            _endedBridges.Clear();
            foreach (var source in _lastVolume.Keys)
                if (source == null) _endedBridges.Add(source);
            foreach (var source in _endedBridges)
                _lastVolume.Remove(source);
        }

        private MultiModePart MultiModeOf(Part part)
        {
            if (_parts.TryGetValue(part, out var multiMode)) return multiMode;
            var engines = part.FindModulesImplementing<ModuleEnginesFX>();
            _parts[part] = multiMode = engines.Count > 1
                ? new MultiModePart { Engines = engines.ToArray(), Loops = part.GetComponentsInChildren<AudioFX>(true).Where(effect => effect.loop).ToArray() }
                : null;
            return multiMode;
        }

        private bool SwitchedMode(MultiModePart multiMode)
        {
            _started.Clear();
            _stopped.Clear();
            foreach (var engine in multiMode.Engines)
            {
                bool ignited = engine.EngineIgnited;
                if (_wasIgnited.TryGetValue(engine, out bool was) && was != ignited)
                    (ignited ? _started : _stopped).Add(engine);
                _wasIgnited[engine] = ignited;
            }
            return _started.Count > 0 && _stopped.Count > 0;
        }

        private void BridgeRunningSounds(MultiModePart multiMode, float now)
        {
            var startedEffects = new HashSet<string>(_started.SelectMany(RunningEffectsOf));
            var stoppedEffects = new HashSet<string>(_stopped.SelectMany(RunningEffectsOf).Where(name => !startedEffects.Contains(name)));
            var bridge = new Bridge
            {
                Replacements = multiMode.Loops.Where(effect => startedEffects.Contains(effect.effectName)).ToArray(),
                Until = now + BridgeSeconds,
            };

            var held = new List<AudioSource>();
            foreach (var effect in multiMode.Loops)
                if (stoppedEffects.Contains(effect.effectName))
                    foreach (var source in SourcesOf(effect))
                        if (source != null && _lastVolume.TryGetValue(source, out float volume) && volume > 0f)
                        {
                            bridge.HeldVolume += volume;
                            held.Add(source);
                        }

            if (bridge.HeldVolume <= 0f) return;
            foreach (var source in held)
                _bridges[source] = bridge;
        }

        private void RememberVolumes(MultiModePart multiMode)
        {
            foreach (var effect in multiMode.Loops)
                foreach (var source in SourcesOf(effect))
                    if (source != null && source.isPlaying && source.volume > 0f && !_bridges.ContainsKey(source))
                        _lastVolume[source] = source.volume;
        }

        private void EndBridges(float now)
        {
            _endedBridges.Clear();
            foreach (var kv in _bridges)
                if (kv.Key == null || now > kv.Value.Until) _endedBridges.Add(kv.Key);
            foreach (var source in _endedBridges)
                _bridges.Remove(source);
        }

        private void MuteSwitchEffects(Part part, MultiModePart multiMode)
        {
            _switchEffects.Clear();
            foreach (var engine in multiMode.Engines)
            {
                _switchEffects.Add(engine.engageEffectName);
                _switchEffects.Add(engine.disengageEffectName);
                _switchEffects.Add(engine.flameoutEffectName);
            }

            foreach (var effect in part.GetComponentsInChildren<AudioFX>(true))
                if (!effect.loop && _switchEffects.Contains(effect.effectName))
                    _muted.UnionWith(SourcesOf(effect));
        }

        private static IEnumerable<string> RunningEffectsOf(ModuleEnginesFX engine)
            => new[] { engine.runningEffectName, engine.powerEffectName }.Where(name => !string.IsNullOrEmpty(name));

        private static List<AudioSource> SourcesOf(AudioFX effect)
            => EffectSources?.GetValue(effect) as List<AudioSource> ?? new List<AudioSource>();
    }
}
