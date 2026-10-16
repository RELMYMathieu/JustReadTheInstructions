using System.Collections.Generic;
using System.Linq;
using HullcamVDS;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class CameraPan
    {
        private const float SlewDegPerSecond = 90f;
        private const float RateDegPerSecond = 25f;

        private static readonly Dictionary<Transform, JointPose> Poses = new Dictionary<Transform, JointPose>();

        private readonly PanMount _mount;
        private readonly Transform _yaw;
        private readonly Transform _pitch;
        private readonly Transform _base;
        private readonly JointPose _pose;
        private Transform _lens;
        private Quaternion _lensRotation = Quaternion.identity;
        private float _targetYaw;
        private float _targetPitch;

        public float Yaw => _pose.Yaw;
        public float Pitch => _pose.Pitch;
        public float PitchMin => _mount.PitchMin;
        public float PitchMax => _mount.PitchMax;

        private CameraPan(PanMount mount, Transform yaw, Transform pitch, Transform baseJoint)
        {
            _mount = mount;
            _yaw = yaw;
            _pitch = pitch;
            _base = baseJoint;
            _pose = PoseOf(yaw, pitch, baseJoint);
            _targetYaw = _pose.Yaw;
            _targetPitch = _pose.Pitch;
        }

        public static CameraPan TryCreate(MuMechModuleHullCamera hullCamera)
        {
            string partName = hullCamera.part.partInfo?.name;
            var mount = PanMounts.For(partName);
            if (mount == null) return null;

            var yaw = hullCamera.part.FindModelTransform(mount.YawJoint);
            if (yaw == null)
            {
                Debug.LogWarning($"[JRTI-Pan]: '{partName}' has no '{mount.YawJoint}' joint, pan disabled");
                return null;
            }

            var pitch = string.IsNullOrEmpty(mount.PitchJoint) ? null : hullCamera.part.FindModelTransform(mount.PitchJoint);
            var baseJoint = string.IsNullOrEmpty(mount.BaseJoint) ? null : hullCamera.part.FindModelTransform(mount.BaseJoint);
            return new CameraPan(mount, yaw, pitch, baseJoint);
        }

        public void AttachLens(Transform lens, Transform part, Vector3 position, Vector3 forward, Vector3 up)
        {
            Steer(0f, 0f);

            lens.parent = _yaw;
            lens.localPosition = _mount.LensInYawJoint ?? _yaw.InverseTransformPoint(part.TransformPoint(position));
            _lensRotation = Quaternion.LookRotation(
                _yaw.InverseTransformDirection(part.TransformDirection(forward)),
                _yaw.InverseTransformDirection(part.TransformDirection(up)));
            lens.localRotation = _lensRotation;
            _lens = lens;

            Steer(_pose.Yaw, _pose.Pitch);
        }

        public void PointAt(float? yaw, float? pitch)
        {
            if (yaw.HasValue)
                _targetYaw = _pose.Yaw + Mathf.DeltaAngle(_pose.Yaw, yaw.Value);
            if (pitch.HasValue)
                _targetPitch = Mathf.Clamp(pitch.Value, PitchMin, PitchMax);
        }

        public void AimAt(Vector3 worldPoint)
        {
            if (_lens == null) return;

            var basis = _yaw.parent != null ? _yaw.parent : _yaw;
            Vector3 toTarget = Quaternion.Inverse(_pose.YawRest)
                * basis.InverseTransformDirection(worldPoint - _lens.position).normalized;
            Vector3 lensForward = _lensRotation * Vector3.forward;

            float yaw = Mathf.Atan2(toTarget.x, toTarget.z) - Mathf.Atan2(lensForward.x, lensForward.z);
            float pitch = Mathf.Asin(Mathf.Clamp(toTarget.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(lensForward.y, -1f, 1f));
            PointAt(yaw * Mathf.Rad2Deg, pitch * Mathf.Rad2Deg);
        }

        public void Tick(float deltaTime, float yawRate, float pitchRate)
        {
            _targetYaw += yawRate * RateDegPerSecond * deltaTime;
            _targetPitch = Mathf.Clamp(_targetPitch + pitchRate * RateDegPerSecond * deltaTime, PitchMin, PitchMax);

            float step = SlewDegPerSecond * deltaTime;
            float yaw = Mathf.MoveTowards(_pose.Yaw, _targetYaw, step);
            float pitch = Mathf.MoveTowards(_pose.Pitch, _targetPitch, step);
            if (yaw != _pose.Yaw || pitch != _pose.Pitch)
                Steer(yaw, pitch);
        }

        private void Steer(float yaw, float pitch)
        {
            _pose.Yaw = yaw;
            _pose.Pitch = pitch;

            if (_pitch != null && _mount.SharedJoint)
            {
                var rotation = _pose.YawRest * Quaternion.Euler(-pitch, yaw, 0f);
                if (_mount.PitchPivotHeight != 0f)
                {
                    var pivot = _pose.YawRestPosition + new Vector3(0f, _mount.PitchPivotHeight, 0f);
                    _yaw.localPosition = pivot + rotation * Quaternion.Inverse(_pose.YawRest) * (_pose.YawRestPosition - pivot);
                }
                _yaw.localRotation = rotation;
            }
            else
            {
                _yaw.localRotation = _pose.YawRest * Quaternion.Euler(0f, yaw, 0f);
                if (_pitch != null)
                    _pitch.localRotation = _pose.PitchRest * Quaternion.Euler(-pitch, 0f, 0f);
                else if (_lens != null)
                    _lens.localRotation = _lensRotation * Quaternion.Euler(-pitch, 0f, 0f);
            }

            if (_base != null)
                _base.localRotation = _pose.BaseRest * Quaternion.Euler(0f, yaw, 0f);
        }

        private static JointPose PoseOf(Transform yaw, Transform pitch, Transform baseJoint)
        {
            foreach (var gone in Poses.Keys.Where(t => t == null).ToList())
                Poses.Remove(gone);

            if (!Poses.TryGetValue(yaw, out var pose))
            {
                pose = new JointPose
                {
                    YawRest = yaw.localRotation,
                    YawRestPosition = yaw.localPosition,
                    PitchRest = pitch != null ? pitch.localRotation : Quaternion.identity,
                    BaseRest = baseJoint != null ? baseJoint.localRotation : Quaternion.identity,
                };
                Poses[yaw] = pose;
            }
            return pose;
        }

        public static Vector3? TrackTarget(CameraTrack track, Vessel own)
        {
            switch (track)
            {
                case CameraTrack.ActiveVessel:
                    return PositionOf(FlightGlobals.ActiveVessel, own);
                case CameraTrack.Target:
                    var target = FlightGlobals.fetch?.VesselTarget;
                    if (target is Vessel vessel) return PositionOf(vessel, own);
                    var transform = target?.GetTransform();
                    return transform != null ? transform.position : (Vector3?)null;
                default:
                    return null;
            }
        }

        private static Vector3? PositionOf(Vessel vessel, Vessel own)
        {
            if (vessel == null || vessel == own) return null;
            return vessel.loaded ? vessel.CoM : (Vector3)vessel.GetWorldPos3D();
        }

        private sealed class JointPose
        {
            public Quaternion YawRest;
            public Vector3 YawRestPosition;
            public Quaternion PitchRest;
            public Quaternion BaseRest;
            public float Yaw;
            public float Pitch;
        }
    }

    internal enum CameraTrack
    {
        Off,
        ActiveVessel,
        Target,
    }

    internal static class CameraTracks
    {
        public static string Id(CameraTrack track)
        {
            switch (track)
            {
                case CameraTrack.ActiveVessel: return "vessel";
                case CameraTrack.Target: return "target";
                default: return "off";
            }
        }

        public static bool TryParse(string id, out CameraTrack track)
        {
            switch (id)
            {
                case "off": track = CameraTrack.Off; return true;
                case "vessel": track = CameraTrack.ActiveVessel; return true;
                case "target": track = CameraTrack.Target; return true;
                default: track = CameraTrack.Off; return false;
            }
        }
    }
}
