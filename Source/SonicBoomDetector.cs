using System.Collections.Generic;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal readonly struct BoomListener
    {
        public readonly int CameraId;
        public readonly Vector3 Position;
        public readonly Vessel Vessel;

        public BoomListener(int cameraId, Vector3 position, Vessel vessel)
        {
            CameraId = cameraId;
            Position = position;
            Vessel = vessel;
        }
    }

    internal readonly struct BoomHit
    {
        public readonly int CameraId;
        public readonly Vessel Source;
        public readonly Vector3 SourcePosition;
        public readonly float Distance;
        public readonly float SpeedOfSound;

        public BoomHit(int cameraId, Vessel source, Vector3 sourcePosition, float distance, float speedOfSound)
        {
            CameraId = cameraId;
            Source = source;
            SourcePosition = sourcePosition;
            Distance = distance;
            SpeedOfSound = speedOfSound;
        }

        public float TravelSeconds => Distance / SpeedOfSound;
    }

    internal sealed class SonicBoomDetector
    {
        private const float MaxDistance = 30000f;
        private const float MinDistance = 1f;
        private const float ApproachBand = 0.02f;

        private struct Shock
        {
            public bool Outruns;
            public float SilentFrom;
            public float SilentUntil;
        }

        private readonly Dictionary<long, Shock> _shocks = new Dictionary<long, Shock>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();

        public bool Silences(Vessel source, int cameraId, float now)
            => _shocks.TryGetValue(Key(source, cameraId), out var shock) && now >= shock.SilentFrom && now < shock.SilentUntil;

        public void Update(List<BoomListener> listeners, List<BoomHit> hits, float now)
        {
            _seen.Clear();
            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (!InAir(vessel) || vessel.speedOfSound <= 1.0) continue;

                var velocity = (Vector3)vessel.srf_velocity;
                var position = (Vector3)vessel.CoMD;
                float soundSpeed = (float)vessel.speedOfSound;
                foreach (var listener in listeners)
                {
                    if (listener.Vessel == vessel) continue;
                    var offset = listener.Position - position;
                    float distance = offset.magnitude;
                    if (distance < MinDistance || distance > MaxDistance) continue;

                    long key = Key(vessel, listener.CameraId);
                    _seen.Add(key);
                    float approach = Vector3.Dot(velocity, offset / distance);
                    bool known = _shocks.TryGetValue(key, out var shock);
                    bool outruns = shock.Outruns ? approach > soundSpeed * (1f - ApproachBand) : approach > soundSpeed * (1f + ApproachBand);
                    if (!known)
                    {
                        _shocks[key] = new Shock { Outruns = outruns, SilentFrom = outruns ? now : float.MaxValue, SilentUntil = float.MaxValue };
                        continue;
                    }
                    if (outruns == shock.Outruns) continue;

                    var hit = new BoomHit(listener.CameraId, vessel, position, distance, SpeedOfSoundBetween(vessel, listener.Vessel));
                    float arrival = now + hit.TravelSeconds;
                    shock.Outruns = outruns;
                    if (outruns)
                    {
                        shock.SilentFrom = arrival;
                        shock.SilentUntil = float.MaxValue;
                    }
                    else shock.SilentUntil = arrival;
                    _shocks[key] = shock;
                    hits.Add(hit);
                }
            }

            _stale.Clear();
            foreach (long key in _shocks.Keys)
                if (!_seen.Contains(key)) _stale.Add(key);
            foreach (long key in _stale)
                _shocks.Remove(key);
        }

        private static float SpeedOfSoundBetween(Vessel source, Vessel listener)
        {
            float atSource = (float)source.speedOfSound;
            if (atSource <= 1f) return SoundPaths.DefaultSpeedOfSound;
            float atListener = listener != null ? (float)listener.speedOfSound : 0f;
            return atListener > 1f ? (atSource + atListener) * 0.5f : atSource;
        }

        private static bool InAir(Vessel vessel)
            => vessel != null && vessel.staticPressurekPa >= RSEIntegration.FullMachEffectsPressureKPa;

        private static long Key(Vessel vessel, int cameraId) => (long)vessel.persistentId << 32 | (uint)cameraId;
    }
}
