using System;

namespace JustReadTheInstructions
{
    internal sealed class Biquad
    {
        public const float BypassHz = 19000f;
        public const float HighpassOffHz = 10f;
        private const float Butterworth = 0.7071f;
        private const float Denormal = 1e-15f;

        private float _b0, _b1, _b2, _a1, _a2;
        private float _x1, _x2, _y1, _y2;

        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            if (y > -Denormal && y < Denormal) y = 0f;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        public float Pass(float x)
        {
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = x;
            return x;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0f;

        public void SetLowpass(float cutoff)
        {
            Prepare(cutoff, out double cos, out double alpha, out double a0);
            _b0 = (float)((1.0 - cos) / 2.0 / a0);
            _b1 = (float)((1.0 - cos) / a0);
            _b2 = _b0;
            SetFeedback(cos, alpha, a0);
        }

        public void SetHighpass(float cutoff)
        {
            Prepare(cutoff, out double cos, out double alpha, out double a0);
            _b0 = (float)((1.0 + cos) / 2.0 / a0);
            _b1 = (float)(-(1.0 + cos) / a0);
            _b2 = _b0;
            SetFeedback(cos, alpha, a0);
        }

        private static void Prepare(float cutoff, out double cos, out double alpha, out double a0)
        {
            double w0 = 2.0 * Math.PI * Math.Min(cutoff, CameraAudioMixer.SampleRate * 0.45) / CameraAudioMixer.SampleRate;
            cos = Math.Cos(w0);
            alpha = Math.Sin(w0) / (2.0 * Butterworth);
            a0 = 1.0 + alpha;
        }

        private void SetFeedback(double cos, double alpha, double a0)
        {
            _a1 = (float)(-2.0 * cos / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }
    }

    internal static class GainRamp
    {
        public static void Apply(float[] stereo, float fromGain, float toGain)
        {
            float step = (toGain - fromGain) * 2f / stereo.Length;
            for (int i = 0; i < stereo.Length; i += 2)
            {
                float gain = fromGain + step * (i / 2);
                stereo[i] *= gain;
                stereo[i + 1] *= gain;
            }
        }
    }

    internal sealed class Limiter
    {
        public const float Ceiling = 0.89f;
        private static readonly float ReleasePerFrame = (float)(1.0 - Math.Exp(-1.0 / (0.2 * CameraAudioMixer.SampleRate)));

        private float _gain = 1f;

        public void Process(float[] stereo)
        {
            for (int i = 0; i < stereo.Length; i += 2)
            {
                float peak = Math.Max(Math.Abs(stereo[i]), Math.Abs(stereo[i + 1]));
                float wanted = peak > Ceiling ? Ceiling / peak : 1f;
                _gain = wanted < _gain ? wanted : _gain + (wanted - _gain) * ReleasePerFrame;
                stereo[i] *= _gain;
                stereo[i + 1] *= _gain;
            }
        }
    }

    internal readonly struct MasteringSettings
    {
        public static readonly MasteringSettings RseDefault = FromAutoLimiter(0.5f);

        public readonly float ThresholdDb;
        public readonly float MakeupDb;
        public readonly float AttackMs;
        public readonly float ReleaseMs;

        public MasteringSettings(float thresholdDb, float makeupDb, float attackMs, float releaseMs)
        {
            ThresholdDb = thresholdDb;
            MakeupDb = makeupDb;
            AttackMs = attackMs;
            ReleaseMs = releaseMs;
        }

        public static MasteringSettings FromAutoLimiter(float amount)
            => new MasteringSettings(Lerp(0f, -16f, amount), Lerp(0f, 16f, amount), Lerp(10f, 200f, amount), Lerp(20f, 1000f, amount));

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    internal sealed class Compressor
    {
        private float _gain = 1f;

        public void Process(float[] stereo, MasteringSettings settings)
        {
            float threshold = DbToGain(settings.ThresholdDb);
            float makeup = DbToGain(settings.MakeupDb);
            float attack = PerFrame(settings.AttackMs);
            float release = PerFrame(settings.ReleaseMs);
            for (int i = 0; i < stereo.Length; i += 2)
            {
                float peak = Math.Max(Math.Abs(stereo[i]), Math.Abs(stereo[i + 1]));
                float wanted = peak > threshold ? threshold / peak : 1f;
                _gain += (wanted - _gain) * (wanted < _gain ? attack : release);
                stereo[i] *= _gain * makeup;
                stereo[i + 1] *= _gain * makeup;
            }
        }

        private static float DbToGain(float db) => (float)Math.Pow(10.0, db / 20.0);

        private static float PerFrame(float milliseconds)
            => (float)(1.0 - Math.Exp(-1.0 / (Math.Max(milliseconds, 0.1f) * 0.001 * CameraAudioMixer.SampleRate)));
    }

    internal sealed class AutoGain
    {
        private const float MaxBoostDb = 24f;
        private const float MaxCutDb = 18f;
        private const float TargetRms = 0.1f;
        private const float GateRms = 0.0005f;
        private const float RiseDbPerSecond = 4f;
        private const float FallDbPerSecond = 6f;
        private const float LevelSeconds = 1f;
        private const float BlockSeconds = CameraAudioMixer.BlockFrames / (float)CameraAudioMixer.SampleRate;

        private float _meanSquare;
        private float _db;

        public float Next(float[] stereo, float inputGain, bool enabled)
        {
            if (!enabled)
            {
                _meanSquare = 0f;
                _db = 0f;
                return 1f;
            }

            float sum = 0f;
            foreach (float sample in stereo) sum += sample * sample;
            _meanSquare += (sum / stereo.Length * inputGain * inputGain - _meanSquare) * (BlockSeconds / LevelSeconds);

            float rms = (float)Math.Sqrt(_meanSquare);
            if (rms > GateRms)
            {
                float wanted = Math.Max(-MaxCutDb, Math.Min(MaxBoostDb, 20f * (float)Math.Log10(TargetRms / rms)));
                _db = wanted > _db
                    ? Math.Min(wanted, _db + RiseDbPerSecond * BlockSeconds)
                    : Math.Max(wanted, _db - FallDbPerSecond * BlockSeconds);
            }
            return (float)Math.Pow(10.0, _db / 20.0);
        }
    }
}
