using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class CameraImageAdjust
    {
        internal static byte[] BuildLut(float brightness, float contrast, float gamma)
        {
            var lut = new byte[256];
            float gammaInv = 1f / Mathf.Max(gamma, 0.01f);
            for (int i = 0; i < 256; i++)
            {
                float v = i / 255f;
                v += brightness;
                v = (v - 0.5f) * contrast + 0.5f;
                v = Mathf.Pow(Mathf.Max(v, 0f), gammaInv);
                lut[i] = (byte)(int)(Mathf.Clamp01(v) * 255f + 0.5f);
            }
            return lut;
        }

        internal static void Apply(byte[] pixels, byte[] lut)
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = lut[pixels[i]];
        }
    }
}
