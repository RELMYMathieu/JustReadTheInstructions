using System;

namespace JustReadTheInstructions
{
    internal enum CameraMic
    {
        Game,
        External,
        Onboard,
    }

    internal readonly struct SoundSettings
    {
        public static readonly SoundSettings Default = new SoundSettings(CameraMic.Game, 0f, false);

        public readonly CameraMic Mic;
        public readonly float GainDb;
        public readonly bool AutoGain;

        public SoundSettings(CameraMic mic, float gainDb, bool autoGain)
        {
            Mic = mic;
            GainDb = gainDb;
            AutoGain = autoGain;
        }
    }

    internal static class CameraMics
    {
        public static string Id(CameraMic mic) => mic.ToString().ToLowerInvariant();

        public static bool TryParse(string id, out CameraMic mic)
            => Enum.TryParse(id, ignoreCase: true, out mic) && Enum.IsDefined(typeof(CameraMic), mic);
    }

    internal readonly struct VoicePath
    {
        public const float OpenCutoff = 20000f;
        public static readonly VoicePath Silent = new VoicePath(0f, 0f, OpenCutoff, 0f);

        public readonly float Left;
        public readonly float Right;
        public readonly float Cutoff;
        public readonly float DelaySeconds;
        public readonly float EchoDelaySeconds;
        public readonly float EchoMix;
        public readonly float Distortion;

        public VoicePath(float left, float right, float cutoff, float delaySeconds)
            : this(left, right, cutoff, delaySeconds, 0f, 0f, 0f) { }

        private VoicePath(float left, float right, float cutoff, float delaySeconds, float echoDelaySeconds, float echoMix, float distortion)
        {
            Left = left;
            Right = right;
            Cutoff = cutoff;
            DelaySeconds = delaySeconds;
            EchoDelaySeconds = echoDelaySeconds;
            EchoMix = echoMix;
            Distortion = distortion;
        }

        public bool IsSilent => Left == 0f && Right == 0f;

        public VoicePath WithAirCharacter(float echoDelaySeconds, float echoMix, float distortion)
            => new VoicePath(Left, Right, Cutoff, DelaySeconds, echoDelaySeconds, echoMix, distortion);

        public VoicePath Muffled(float gain, float maxCutoff)
            => new VoicePath(Left * gain, Right * gain, Math.Min(Cutoff, maxCutoff), DelaySeconds, EchoDelaySeconds, EchoMix, Distortion);
    }

    internal struct PathInputs
    {
        public float Volume;
        public float SpatialBlend;
        public float GameRolloff;
        public float Distance;
        public float ThrustKn;
        public float Pan;
        public float AirFactor;
        public float SpeedOfSound;
        public bool SameVessel;
        public bool Interior;
        public bool Muted;
        public bool AheadOfShock;
    }

    internal static class SoundPaths
    {
        private const float SeaLevelDensity = 1.225f;
        private const float DefaultSpeedOfSound = 343f;
        private const float ReferenceDistance = 15f;
        private const float ReferenceThrustKn = 60f;
        private const float AbsorptionDistance = 250f;
        private const float ThinAirCutoff = 250f;
        private const float ExteriorHullGain = 0.3f;
        private const float ExteriorHullCutoff = 900f;
        private const float OnboardHullGain = 0.8f;
        private const float OnboardHullCutoff = 2500f;
        private const float OnboardAirGain = 0.5f;
        private const float OnboardAirCutoff = 1200f;
        private const float AheadOfShockGain = 0.05f;
        private const float AheadOfShockCutoff = 250f;
        private const float ReflectionHeight = 5f;
        private const float MaxEchoDelay = 0.02f;
        private const float MaxEchoMix = 0.5f;
        private const float FullEchoDistance = 1000f;
        private const float MaxDistortion = 0.35f;
        private const float FullDistortionDistance = 3000f;

        public static float Reach(float thrustKn)
            => thrustKn > ReferenceThrustKn ? (float)Math.Sqrt(thrustKn / ReferenceThrustKn) : 1f;

        public static float AirFactor(double density) => Clamp01((float)(density / SeaLevelDensity));

        public static bool InsideMachCone(double cosineBehind, double mach)
            => mach > 1.0 && cosineBehind > Math.Sqrt(1.0 - 1.0 / (mach * mach));

        public static VoicePath Boom(CameraMic mic, float gain, float pan, float airFactor)
        {
            switch (mic)
            {
                case CameraMic.External: return Panned(pan, gain * (float)Math.Sqrt(airFactor), VoicePath.OpenCutoff, 0f);
                case CameraMic.Onboard: return Panned(pan, gain * OnboardAirGain, OnboardAirCutoff, 0f);
                default: return Panned(pan, gain, VoicePath.OpenCutoff, 0f);
            }
        }

        public static VoicePath For(CameraMic mic, PathInputs input)
        {
            var path = ForMic(mic, input);
            return input.AheadOfShock ? path.Muffled(AheadOfShockGain, AheadOfShockCutoff) : path;
        }

        private static VoicePath ForMic(CameraMic mic, PathInputs input)
        {
            switch (mic)
            {
                case CameraMic.Game: return Game(input);
                case CameraMic.External: return External(input);
                default: return Onboard(input);
            }
        }

        private static VoicePath Game(PathInputs s)
            => s.Muted || (s.Interior && !s.SameVessel) ? VoicePath.Silent : Panned(s.Pan, s.Volume * Lerp(1f, s.GameRolloff, s.SpatialBlend), VoicePath.OpenCutoff, 0f);

        private static VoicePath External(PathInputs s)
        {
            if (s.Interior) return VoicePath.Silent;

            float air = (float)Math.Sqrt(s.AirFactor);
            float airGain = ThroughAir(s, air);
            float airCutoff = AirCutoff(s.Distance, air);
            float delay = Delay(s.Distance, s.SpeedOfSound);
            if (!s.SameVessel) return AirCharacter(Panned(s.Pan, airGain, airCutoff, delay), s.Distance, air);

            float hullGain = s.Volume * ExteriorHullGain * (1f - air);
            return AirCharacter(Panned(s.Pan, airGain + hullGain, Lerp(ExteriorHullCutoff, airCutoff, air), delay * air), s.Distance, air);
        }

        private static VoicePath Onboard(PathInputs s)
        {
            if (s.SameVessel)
            {
                float gain = s.Volume * (s.Interior ? 1f : OnboardHullGain) * Lerp(1f, DistanceGain(s), s.SpatialBlend);
                return Panned(s.Pan, gain, s.Interior ? VoicePath.OpenCutoff : OnboardHullCutoff, 0f);
            }
            if (s.Interior) return VoicePath.Silent;

            float air = (float)Math.Sqrt(s.AirFactor);
            return AirCharacter(Panned(s.Pan, ThroughAir(s, air) * OnboardAirGain,
                Math.Min(AirCutoff(s.Distance, air), OnboardAirCutoff), Delay(s.Distance, s.SpeedOfSound)), s.Distance, air);
        }

        private static VoicePath AirCharacter(VoicePath path, float distance, float air)
            => path.WithAirCharacter(
                Math.Min(MaxEchoDelay, 2f * ReflectionHeight * ReflectionHeight / Math.Max(distance, 1f) / DefaultSpeedOfSound),
                MaxEchoMix * FarAmount(distance, FullEchoDistance) * air,
                MaxDistortion * FarAmount(distance, FullDistortionDistance) * air);

        private static float FarAmount(float distance, float fullDistance)
            => distance <= ReferenceDistance ? 0f : Clamp01((float)(Math.Log(distance / ReferenceDistance) / Math.Log(fullDistance / ReferenceDistance)));

        private static float ThroughAir(PathInputs s, float air)
            => s.Volume * Lerp(1f, DistanceGain(s), s.SpatialBlend) * air;

        private static float DistanceGain(PathInputs s)
        {
            float fullLevelDistance = ReferenceDistance * Reach(s.ThrustKn);
            return s.Distance <= fullLevelDistance ? 1f : fullLevelDistance / s.Distance;
        }

        private static float AirCutoff(float distance, float air)
            => Math.Min(VoicePath.OpenCutoff / (1f + distance / AbsorptionDistance), Lerp(ThinAirCutoff, VoicePath.OpenCutoff, air));

        private static float Delay(float distance, float speedOfSound)
            => distance / (speedOfSound > 1f ? speedOfSound : DefaultSpeedOfSound);

        private static VoicePath Panned(float pan, float gain, float cutoff, float delaySeconds)
        {
            if (gain <= 0f) return VoicePath.Silent;
            double angle = (pan + 1.0) * Math.PI / 4.0;
            return new VoicePath(gain * (float)Math.Cos(angle), gain * (float)Math.Sin(angle), cutoff, delaySeconds);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
