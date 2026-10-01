using System;

namespace JustReadTheInstructions
{
    internal sealed class Biquad
    {
        public const float BypassHz = 19000f;
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
            double w0 = 2.0 * Math.PI * Math.Min(cutoff, CameraAudioMixer.SampleRate * 0.45) / CameraAudioMixer.SampleRate;
            double cos = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * Butterworth);
            double a0 = 1.0 + alpha;

            _b0 = (float)((1.0 - cos) / 2.0 / a0);
            _b1 = (float)((1.0 - cos) / a0);
            _b2 = _b0;
            _a1 = (float)(-2.0 * cos / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }
    }

    internal sealed class Limiter
    {
        public const float Ceiling = 0.89f;
        private static readonly float ReleasePerFrame = (float)(1.0 - Math.Exp(-1.0 / (0.2 * CameraAudioMixer.SampleRate)));

        private float _gain = 1f;

        public void Process(float[] stereo, float fromGain, float toGain)
        {
            float gainStep = (toGain - fromGain) * 2f / stereo.Length;
            for (int i = 0; i < stereo.Length; i += 2)
            {
                float inputGain = fromGain + gainStep * (i / 2);
                float left = stereo[i] * inputGain;
                float right = stereo[i + 1] * inputGain;
                float peak = Math.Max(Math.Abs(left), Math.Abs(right));
                float wanted = peak > Ceiling ? Ceiling / peak : 1f;
                _gain = wanted < _gain ? wanted : _gain + (wanted - _gain) * ReleasePerFrame;
                stereo[i] = left * _gain;
                stereo[i + 1] = right * _gain;
            }
        }
    }

    internal sealed class AutoGain
    {
        private const float MaxDb = 24f;
        private const float TargetRms = 0.1f;
        private const float GateRms = 0.0005f;
        private const float RiseDbPerSecond = 4f;
        private const float FallDbPerSecond = 20f;
        private const float LevelSeconds = 0.5f;
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
                float wanted = Math.Max(0f, Math.Min(MaxDb, 20f * (float)Math.Log10(TargetRms / rms)));
                _db = wanted > _db
                    ? Math.Min(wanted, _db + RiseDbPerSecond * BlockSeconds)
                    : Math.Max(wanted, _db - FallDbPerSecond * BlockSeconds);
            }
            return (float)Math.Pow(10.0, _db / 20.0);
        }
    }
}
