using System;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class AeroHeat
    {
        private const double HeatFluxStart = 8000000.0;
        private const double HeatFluxRange = 5.0 * HeatFluxStart;

        public static float ReentryControl(Vessel vessel)
        {
            double density = vessel.atmDensity;
            if (density <= 0.0 || vessel.packed) return 0f;

            double fadeStart = PhysicsGlobals.AeroFXDensityFadeStart;
            double faded = density < fadeStart ? density * density * Math.Ceiling(1.0 / fadeStart) : density;
            double factor = Math.Pow(faded, PhysicsGlobals.AeroFXDensityExponent1) * PhysicsGlobals.AeroFXDensityScalar1
                            + Math.Pow(faded, PhysicsGlobals.AeroFXDensityExponent2) * PhysicsGlobals.AeroFXDensityScalar2;
            double speed = vessel.srfSpeed;
            if (factor * speed <= 0.0) return 0f;

            float state = Mathf.Clamp01(Mathf.InverseLerp((float)PhysicsGlobals.AeroFXStartThermalFX, (float)PhysicsGlobals.AeroFXFullThermalFX, (float)vessel.mach));
            double heatFlux = 0.5 * factor * Math.Pow(speed, PhysicsGlobals.AeroFXVelocityExponent);
            float scalar = (float)Math.Min(1.0, (heatFlux - HeatFluxStart) / HeatFluxRange);
            if (state < 1f && density < PhysicsGlobals.AeroFXMachFXFadeStart)
                scalar *= Mathf.Lerp(MachFade(density), 1f, state);
            return Mathf.Max(0f, scalar * state);
        }

        private static float MachFade(double density)
        {
            double fadeEnd = PhysicsGlobals.AeroFXMachFXFadeEnd;
            if (density <= fadeEnd) return 0f;
            return (float)((fadeEnd - density) / (fadeEnd - PhysicsGlobals.AeroFXMachFXFadeStart));
        }
    }
}
