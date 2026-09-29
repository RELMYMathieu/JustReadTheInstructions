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

        public BoomHit(int cameraId, Vessel source, Vector3 sourcePosition, float distance)
        {
            CameraId = cameraId;
            Source = source;
            SourcePosition = sourcePosition;
            Distance = distance;
        }
    }

    internal sealed class SonicBoomDetector
    {
        private const float MaxDistance = 30000f;
        private const float MinDistance = 1f;
        private const double MinSpeed = 1.0;

        private readonly Dictionary<long, bool> _inside = new Dictionary<long, bool>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();

        public bool IsAheadOfShock(Vessel vessel, int cameraId)
            => _inside.TryGetValue(Key(vessel, cameraId), out bool inside) && !inside;

        public void Update(List<BoomListener> listeners, List<BoomHit> hits)
        {
            _seen.Clear();
            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (vessel == null || vessel.mach <= 1.0 || vessel.staticPressurekPa <= 0.0 || vessel.srf_velocity.magnitude < MinSpeed) continue;

                Vector3 behind = -(Vector3)vessel.srf_velocity.normalized;
                var position = (Vector3)vessel.CoMD;
                foreach (var listener in listeners)
                {
                    if (listener.Vessel == vessel) continue;
                    var offset = listener.Position - position;
                    float distance = offset.magnitude;
                    if (distance < MinDistance || distance > MaxDistance) continue;

                    long key = Key(vessel, listener.CameraId);
                    _seen.Add(key);
                    bool inside = SoundPaths.InsideMachCone(Vector3.Dot(offset / distance, behind), vessel.mach);
                    if (inside && _inside.TryGetValue(key, out bool wasInside) && !wasInside)
                        hits.Add(new BoomHit(listener.CameraId, vessel, position, distance));
                    _inside[key] = inside;
                }
            }

            _stale.Clear();
            foreach (long key in _inside.Keys)
                if (!_seen.Contains(key)) _stale.Add(key);
            foreach (long key in _stale)
                _inside.Remove(key);
        }

        private static long Key(Vessel vessel, int cameraId) => (long)vessel.persistentId << 32 | (uint)cameraId;
    }
}
