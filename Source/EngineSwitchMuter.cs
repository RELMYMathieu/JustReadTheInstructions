using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class EngineSwitchMuter
    {
        private const float SwitchSeconds = 0.5f;

        private static readonly ModuleEnginesFX[] NoEngines = new ModuleEnginesFX[0];
        private static readonly FieldInfo EffectSources = typeof(AudioFX).GetField("sources", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Dictionary<Part, ModuleEnginesFX[]> _engines = new Dictionary<Part, ModuleEnginesFX[]>();
        private readonly Dictionary<ModuleEnginesFX, bool> _wasIgnited = new Dictionary<ModuleEnginesFX, bool>();
        private readonly HashSet<AudioSource> _muted = new HashSet<AudioSource>();
        private readonly HashSet<string> _switchEffects = new HashSet<string>();
        private readonly Dictionary<Part, float> _switchingUntil = new Dictionary<Part, float>();
        private readonly List<Part> _goneParts = new List<Part>();

        public bool Mutes(AudioSource source) => _muted.Contains(source);

        public void Update()
        {
            float now = Time.unscaledTime;
            _muted.RemoveWhere(source => source == null || !source.isPlaying);
            foreach (var vessel in FlightGlobals.VesselsLoaded)
                foreach (var part in vessel.parts)
                    if (SwitchedMode(EnginesOf(part))) _switchingUntil[part] = now + SwitchSeconds;

            _goneParts.Clear();
            foreach (var kv in _switchingUntil)
            {
                if (kv.Key == null || now > kv.Value) _goneParts.Add(kv.Key);
                else MuteSwitchEffects(kv.Key);
            }
            foreach (var part in _goneParts)
                _switchingUntil.Remove(part);
        }

        public void Forget()
        {
            _goneParts.Clear();
            foreach (var part in _engines.Keys)
                if (part == null) _goneParts.Add(part);
            foreach (var part in _goneParts)
            {
                foreach (var engine in _engines[part]) _wasIgnited.Remove(engine);
                _engines.Remove(part);
            }
        }

        private ModuleEnginesFX[] EnginesOf(Part part)
        {
            if (_engines.TryGetValue(part, out var engines)) return engines;
            var found = part.FindModulesImplementing<ModuleEnginesFX>();
            _engines[part] = engines = found.Count > 1 ? found.ToArray() : NoEngines;
            return engines;
        }

        private bool SwitchedMode(ModuleEnginesFX[] engines)
        {
            bool started = false, stopped = false;
            foreach (var engine in engines)
            {
                bool ignited = engine.EngineIgnited;
                if (_wasIgnited.TryGetValue(engine, out bool was) && was != ignited)
                {
                    started |= ignited;
                    stopped |= !ignited;
                }
                _wasIgnited[engine] = ignited;
            }
            return started && stopped;
        }

        private void MuteSwitchEffects(Part part)
        {
            _switchEffects.Clear();
            foreach (var engine in EnginesOf(part))
            {
                _switchEffects.Add(engine.engageEffectName);
                _switchEffects.Add(engine.disengageEffectName);
                _switchEffects.Add(engine.flameoutEffectName);
            }

            foreach (var effect in part.GetComponentsInChildren<AudioFX>(true))
                if (!effect.loop && _switchEffects.Contains(effect.effectName) && EffectSources?.GetValue(effect) is List<AudioSource> sources)
                    _muted.UnionWith(sources);
        }
    }
}
