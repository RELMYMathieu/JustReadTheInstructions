using System.Collections.Generic;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class VesselLengths
    {
        private readonly struct Measured
        {
            public readonly int Parts;
            public readonly float Length;

            public Measured(int parts, float length)
            {
                Parts = parts;
                Length = length;
            }
        }

        private readonly Dictionary<uint, Measured> _measured = new Dictionary<uint, Measured>();

        public float Of(Vessel vessel)
        {
            int parts = vessel.Parts.Count;
            if (_measured.TryGetValue(vessel.persistentId, out var known) && known.Parts == parts) return known.Length;

            float length = Measure(vessel);
            _measured[vessel.persistentId] = new Measured(parts, length);
            Debug.Log($"[JRTI-Audio]: '{vessel.vesselName}' measures {length:F1} m long for its sonic booms ({SoundPaths.ShockCount(length)} shocks)");
            return length;
        }

        private float Measure(Vessel vessel)
        {
            if (vessel.rootPart == null) return vessel.vesselSize.magnitude;

            var frame = vessel.rootPart.transform;
            var bounds = new Bounds();
            bool any = false;
            foreach (var part in vessel.Parts)
            {
                foreach (var collider in part.GetPartColliders())
                {
                    if (!collider.enabled || collider.isTrigger || !TryLocalBox(collider, out var box)) continue;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = frame.InverseTransformPoint(collider.transform.TransformPoint(box.center + Vector3.Scale(box.extents, Corner(corner))));
                        if (any) bounds.Encapsulate(point);
                        else bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }

            if (!any) return vessel.vesselSize.magnitude;
            var size = bounds.size;
            return Mathf.Max(size.x, size.y, size.z);
        }

        private static bool TryLocalBox(Collider collider, out Bounds box)
        {
            switch (collider)
            {
                case MeshCollider mesh when mesh.sharedMesh != null:
                    box = mesh.sharedMesh.bounds;
                    return true;
                case BoxCollider cube:
                    box = new Bounds(cube.center, cube.size);
                    return true;
                case SphereCollider sphere:
                    box = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
                    return true;
                case CapsuleCollider capsule:
                    box = new Bounds(capsule.center, Vector3.one * Mathf.Max(capsule.height, capsule.radius * 2f));
                    return true;
                default:
                    box = default;
                    return false;
            }
        }

        private static Vector3 Corner(int corner)
            => new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
    }
}
