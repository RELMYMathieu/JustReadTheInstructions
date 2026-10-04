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
        public readonly bool OwnVessel;

        public BoomHit(int cameraId, Vessel source, Vector3 sourcePosition, float distance, bool ownVessel)
        {
            CameraId = cameraId;
            Source = source;
            SourcePosition = sourcePosition;
            Distance = distance;
            OwnVessel = ownVessel;
        }
    }

    internal sealed class SonicBoomDetector
    {
        private const float MaxDistance = 30000f;
        private const float MinDistance = 1f;
        private const float ApproachBand = 0.02f;
        private const double SupersonicFromMach = 1.02;
        private const double SubsonicBelowMach = 0.98;

        private readonly Dictionary<long, bool> _outrunsSound = new Dictionary<long, bool>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
        private readonly Dictionary<uint, bool> _supersonic = new Dictionary<uint, bool>();
        private readonly HashSet<uint> _inAir = new HashSet<uint>();
        private readonly HashSet<uint> _crossedMachOne = new HashSet<uint>();
        private readonly List<uint> _leftAir = new List<uint>();

        public void Update(List<BoomListener> listeners, List<BoomHit> hits)
        {
            TrackMachOneCrossings();
            foreach (var listener in listeners)
                if (listener.Vessel != null && _crossedMachOne.Contains(listener.Vessel.persistentId))
                    hits.Add(new BoomHit(listener.CameraId, listener.Vessel, listener.Position, 0f, ownVessel: true));

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
                    bool known = _outrunsSound.TryGetValue(key, out bool was);
                    bool now = was ? approach > soundSpeed * (1f - ApproachBand) : approach > soundSpeed * (1f + ApproachBand);
                    if (known && now != was)
                        hits.Add(new BoomHit(listener.CameraId, vessel, position, distance, ownVessel: false));
                    _outrunsSound[key] = now;
                }
            }

            _stale.Clear();
            foreach (long key in _outrunsSound.Keys)
                if (!_seen.Contains(key)) _stale.Add(key);
            foreach (long key in _stale)
                _outrunsSound.Remove(key);
        }

        private void TrackMachOneCrossings()
        {
            _crossedMachOne.Clear();
            _inAir.Clear();
            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (!InAir(vessel)) continue;

                uint id = vessel.persistentId;
                _inAir.Add(id);
                bool known = _supersonic.TryGetValue(id, out bool was);
                bool now = was ? vessel.mach > SubsonicBelowMach : vessel.mach >= SupersonicFromMach;
                if (known && now != was) _crossedMachOne.Add(id);
                _supersonic[id] = now;
            }

            _leftAir.Clear();
            foreach (uint id in _supersonic.Keys)
                if (!_inAir.Contains(id)) _leftAir.Add(id);
            foreach (uint id in _leftAir)
                _supersonic.Remove(id);
        }

        private static bool InAir(Vessel vessel)
            => vessel != null && vessel.staticPressurekPa >= RSEIntegration.FullMachEffectsPressureKPa;

        private static long Key(Vessel vessel, int cameraId) => (long)vessel.persistentId << 32 | (uint)cameraId;
    }
}
