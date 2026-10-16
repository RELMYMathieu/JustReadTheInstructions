using System.Collections.Generic;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class PanMount
    {
        public string YawJoint;
        public string PitchJoint;
        public string BaseJoint;
        public float PitchMin;
        public float PitchMax;
        public float PitchPivotHeight;
        public Vector3? LensInYawJoint;

        public bool SharedJoint => PitchJoint == YawJoint;
    }

    internal static class PanMounts
    {
        private static readonly Dictionary<string, PanMount> ByPartName = new Dictionary<string, PanMount>
        {
            ["DC.TurretCam"] = new PanMount
            {
                YawJoint = "TopJoint",
                PitchMin = -45f,
                PitchMax = 90f,
                LensInYawJoint = new Vector3(0.047f, 0.046f, -0.200f),
            },
            ["hc.launchcam"] = new PanMount
            {
                YawJoint = "hc_launchcam",
                PitchJoint = "hc_launchcam",
                BaseJoint = "hc_launchbase",
                PitchMin = -60f,
                PitchMax = 90f,
                PitchPivotHeight = 0.3f,
            },
        };

        public static PanMount For(string partName)
            => partName != null && ByPartName.TryGetValue(partName, out var mount) ? mount : null;
    }
}
