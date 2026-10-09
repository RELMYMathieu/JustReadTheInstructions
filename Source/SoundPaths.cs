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
        public static readonly SoundSettings Default = new SoundSettings(CameraMic.Game, 0f, false, false);

        public readonly CameraMic Mic;
        public readonly float GainDb;
        public readonly bool AutoGain;
        public readonly bool Mastering;

        public SoundSettings(CameraMic mic, float gainDb, bool autoGain, bool mastering)
        {
            Mic = mic;
            GainDb = gainDb;
            AutoGain = autoGain;
            Mastering = mastering;
        }
    }

    internal static class CameraMics
    {
        public static string Id(CameraMic mic) => mic.ToString().ToLowerInvariant();

        public static bool TryParse(string id, out CameraMic mic)
            => Enum.TryParse(id, ignoreCase: true, out mic) && Enum.IsDefined(typeof(CameraMic), mic);
    }

    internal readonly struct AirSimProfile
    {
        public static readonly AirSimProfile Full = new AirSimProfile(1f, 1f, 0f);

        public readonly float Echo;
        public readonly float Grit;
        public readonly float AngleHighpassHz;

        public AirSimProfile(float echo, float grit, float angleHighpassHz)
        {
            Echo = echo;
            Grit = grit;
            AngleHighpassHz = angleHighpassHz;
        }
    }

    internal readonly struct VoicePath
    {
        public const float OpenCutoff = 20000f;
        public static readonly VoicePath Silent = new VoicePath(0f, 0f, OpenCutoff, 0f);

        public readonly float Left;
        public readonly float Right;
        public readonly float Cutoff;
        public readonly float Highpass;
        public readonly float DelaySeconds;
        public readonly float EchoDelaySeconds;
        public readonly float EchoMix;
        public readonly float Distortion;

        public VoicePath(float left, float right, float cutoff, float delaySeconds)
            : this(left, right, cutoff, 0f, delaySeconds, 0f, 0f, 0f) { }

        private VoicePath(float left, float right, float cutoff, float highpass, float delaySeconds, float echoDelaySeconds, float echoMix, float distortion)
        {
            Left = left;
            Right = right;
            Cutoff = cutoff;
            Highpass = highpass;
            DelaySeconds = delaySeconds;
            EchoDelaySeconds = echoDelaySeconds;
            EchoMix = echoMix;
            Distortion = distortion;
        }

        public bool IsSilent => Left == 0f && Right == 0f;

        public VoicePath WithAirCharacter(float echoDelaySeconds, float echoMix, float distortion, float highpass)
            => new VoicePath(Left, Right, Cutoff, highpass, DelaySeconds, echoDelaySeconds, echoMix, distortion);

        public VoicePath Muffled(float gain, float maxCutoff)
            => new VoicePath(Left * gain, Right * gain, Math.Min(Cutoff, maxCutoff), Highpass, DelaySeconds, EchoDelaySeconds, EchoMix, Distortion);
    }

    internal struct PathInputs
    {
        public float Volume;
        public float SpatialBlend;
        public float GameRolloff;
        public float Distance;
        public float ThrustKn;
        public bool LoudnessScalesWithThrust;
        public float Pan;
        public float AirFactor;
        public float SpeedOfSound;
        public bool SameVessel;
        public bool Interior;
        public bool Muted;
        public AirSimProfile AirSim;
        public float Behind;
        public float AheadOfCone;
        public float Mach;
        public bool AheadOfShock;
    }

    internal static class SoundPaths
    {
        private const float SeaLevelDensity = 1.225f;
        private const float DefaultSpeedOfSound = 343f;
        private const float ReferenceDistance = 15f;
        private const float ReferenceThrustKn = 60f;
        private const float PhysicalReachExponent = 0.5f;
        private const float RseLoudnessThrustExponent = 0.32f;
        private const float AbsorptionDistance = 250f;
        private const float ThinAirCutoff = 250f;
        private const float ExteriorHullGain = 0.3f;
        private const float ExteriorHullCutoff = 900f;
        private const float OnboardHullGain = 0.8f;
        private const float OnboardHullCutoff = 2500f;
        private const float OnboardAirGain = 0.5f;
        private const float OnboardAirCutoff = 1200f;
        private const float ExternalBoomGain = 3.16f;
        private const float OnboardBoomGain = 0.7f;
        private const float OnboardBoomCutoff = 3000f;
        private const float ShockMuffledGain = 0.05f;
        private const float ShockMuffledCutoff = 250f;
        private const float ReflectionHeight = 5f;
        private const float MaxEchoDelay = 0.02f;
        private const float MaxEchoMix = 0.5f;
        private const float FullEchoDistance = 1000f;
        private const float MaxDistortion = 0.35f;
        private const float FullDistortionDistance = 3000f;
        private const float NearDistortionPerMach = 0.5f;
        private const float ReferenceBoomLength = 20f;
        private const float ReferenceBoomDistance = 1000f;
        private const double BoomLengthExponent = 0.75;
        private const double BoomDistanceExponent = 0.25;
        private const double BoomStretchSoftening = 0.15;
        private const float MinBoomStretch = 0.87f;
        private const float MaxBoomStretch = 1.15f;
        private const float MinShockSpacing = 0.05f;
        private const float MaxShockSpacing = 0.6f;
        private const float TripleShockLength = 25f;
        private const float LeadGapPerSpacing = 1.6f;
        private const float PairGapPerSpacing = 0.4f;
        private const float MinPairGap = 0.12f;
        private const float TrailingShockGain = 0.8f;

        public static float Reach(float thrustKn, bool loudnessScalesWithThrust)
        {
            if (thrustKn <= ReferenceThrustKn) return 1f;
            float exponent = loudnessScalesWithThrust ? PhysicalReachExponent - RseLoudnessThrustExponent : PhysicalReachExponent;
            return (float)Math.Pow(thrustKn / ReferenceThrustKn, exponent);
        }

        public static float AirFactor(double density) => Clamp01((float)(density / SeaLevelDensity));

        public static float ViewAngleDegrees(float cosineAhead) => (1f + cosineAhead) * 90f;

        public static float MachAngleDegrees(float mach) => mach > 1f ? (float)(Math.Asin(1.0 / mach) * 180.0 / Math.PI) : 90f;

        public static float AheadOfCone(float viewAngle, float mach)
        {
            float cone = MachAngleDegrees(mach);
            return Clamp01((viewAngle - cone) / cone);
        }

        public static float SpeedOfSoundBetween(float atSource, float atListener)
        {
            if (atSource <= 1f) return atListener > 1f ? atListener : DefaultSpeedOfSound;
            return atListener > 1f ? (atSource + atListener) * 0.5f : atSource;
        }

        public static float BoomStretch(float lengthMetres, float distance)
        {
            double physical = Math.Pow(Math.Max(lengthMetres, 1f) / ReferenceBoomLength, BoomLengthExponent)
                              * Math.Pow(Math.Max(distance, 1f) / ReferenceBoomDistance, BoomDistanceExponent);
            return Math.Max(MinBoomStretch, Math.Min(MaxBoomStretch, (float)Math.Pow(physical, BoomStretchSoftening)));
        }

        public static float ShockSpacing(float lengthMetres, float mach, float distance, float speedOfSound)
        {
            float length = Math.Max(lengthMetres, 1f);
            float c = speedOfSound > 1f ? speedOfSound : DefaultSpeedOfSound;
            double farField = Math.Pow(Math.Max(1.0, distance / length), BoomDistanceExponent);
            return Math.Max(MinShockSpacing, Math.Min(MaxShockSpacing, (float)(length / (Math.Max(mach, 1f) * c) * farField)));
        }

        public static int ShockCount(float lengthMetres) => lengthMetres >= TripleShockLength ? 3 : 2;

        public static float ShockDelay(int shock, float spacing)
        {
            if (shock == 0) return 0f;
            float lead = spacing * LeadGapPerSpacing;
            return shock == 1 ? lead : lead + Math.Max(MinPairGap, spacing * PairGapPerSpacing);
        }

        public static float ShockGain(int shock) => shock < 2 ? 1f : TrailingShockGain;

        public static VoicePath Boom(CameraMic mic, float gain, float pan, float airFactor)
        {
            switch (mic)
            {
                case CameraMic.External: return Panned(pan, gain * ExternalBoomGain * (float)Math.Sqrt(airFactor), VoicePath.OpenCutoff, 0f);
                case CameraMic.Onboard: return Panned(pan, gain * OnboardBoomGain, OnboardBoomCutoff, 0f);
                default: return Panned(pan, gain, VoicePath.OpenCutoff, 0f);
            }
        }

        public static VoicePath For(CameraMic mic, PathInputs input)
        {
            var path = ForMic(mic, input);
            return input.AheadOfShock && mic == CameraMic.Game ? path.Muffled(ShockMuffledGain, ShockMuffledCutoff) : path;
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
            if (!s.SameVessel) return AirCharacter(Panned(s.Pan, airGain, airCutoff, delay), s, air);

            float hullGain = s.Volume * ExteriorHullGain * (1f - air);
            return AirCharacter(Panned(s.Pan, airGain + hullGain, Lerp(ExteriorHullCutoff, airCutoff, air), delay * air), s, air);
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
                Math.Min(AirCutoff(s.Distance, air), OnboardAirCutoff), Delay(s.Distance, s.SpeedOfSound)), s, air);
        }

        private static VoicePath AirCharacter(VoicePath path, PathInputs s, float air)
        {
            float echoDelay = Math.Min(MaxEchoDelay, 2f * ReflectionHeight * ReflectionHeight / Math.Max(s.Distance, 1f) / DefaultSpeedOfSound);
            float echoMix = MaxEchoMix * s.AirSim.Echo * FarAmount(s.Distance, FullEchoDistance) * air;
            float grit = MaxDistortion * s.AirSim.Grit * s.Behind
                         * Lerp(NearDistortionPerMach * Clamp01(s.Mach), 1f, FarAmount(s.Distance, FullDistortionDistance)) * air;
            return path.WithAirCharacter(echoDelay, echoMix, grit, s.AirSim.AngleHighpassHz * s.AheadOfCone);
        }

        private static float FarAmount(float distance, float fullDistance)
            => distance <= ReferenceDistance ? 0f : Clamp01((float)(Math.Log(distance / ReferenceDistance) / Math.Log(fullDistance / ReferenceDistance)));

        private static float ThroughAir(PathInputs s, float air)
            => s.Volume * Lerp(1f, DistanceGain(s), s.SpatialBlend) * air;

        private static float DistanceGain(PathInputs s)
        {
            float fullLevelDistance = ReferenceDistance * Reach(s.ThrustKn, s.LoudnessScalesWithThrust);
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
