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

        public void Process(float[] stereo, float inputGain)
        {
            for (int i = 0; i < stereo.Length; i += 2)
            {
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
}
