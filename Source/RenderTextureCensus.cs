using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    internal static class RenderTextureCensus
    {
        private const int MaxRows = 40;
        private const double BytesPerMb = 1024.0 * 1024.0;

        private static readonly Regex InstanceNumber = new Regex(@"(?<![\dx])\d+(?![\dx])");

        public static long EstimateBytes(RenderTexture texture)
        {
            if (texture == null || !texture.IsCreated()) return 0;

            long pixels = (long)texture.width * texture.height * Layers(texture);
            int samples = Mathf.Max(1, texture.antiAliasing);
            long color = pixels * BytesPerPixel(texture.format);
            long bytes = (color + pixels * DepthBytes(texture.depth)) * samples;
            if (samples > 1 && !texture.bindTextureMS) bytes += color;
            return texture.useMipMap ? bytes * 4 / 3 : bytes;
        }

        public static void Log()
        {
            var textures = Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t => t.IsCreated()).ToList();
            var groups = textures
                .GroupBy(t => $"{GroupName(t.name)}  {t.width}x{t.height} {t.format} x{Mathf.Max(1, t.antiAliasing)}")
                .Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(t => EstimateBytes(t)) })
                .OrderByDescending(g => g.Bytes)
                .ToList();

            long total = groups.Sum(g => g.Bytes);
            long jrti = textures.Where(t => t.name.StartsWith(HullCameraRenderer.TargetTexturePrefix)).Sum(t => EstimateBytes(t));

            var sb = new StringBuilder();
            sb.AppendLine($"[JRTI-VRAM]: {textures.Count} render textures, about {total / BytesPerMb:0} MB, JRTI camera targets {jrti / BytesPerMb:0} MB");
            foreach (var g in groups.Take(MaxRows))
                sb.AppendLine($"[JRTI-VRAM]: {g.Bytes / BytesPerMb,8:0.0} MB  {g.Count,3}x  {g.Key}");
            if (groups.Count > MaxRows)
                sb.AppendLine($"[JRTI-VRAM]: ... {groups.Count - MaxRows} smaller groups");
            Debug.Log(sb.ToString());
        }

        private static string GroupName(string name)
            => string.IsNullOrEmpty(name) ? "(unnamed)" : InstanceNumber.Replace(name, "#");

        private static int Layers(RenderTexture texture)
        {
            switch (texture.dimension)
            {
                case TextureDimension.Cube: return 6;
                case TextureDimension.Tex2DArray:
                case TextureDimension.Tex3D:
                case TextureDimension.CubeArray: return Mathf.Max(1, texture.volumeDepth);
                default: return 1;
            }
        }

        private static int DepthBytes(int bits) => bits >= 24 ? 4 : bits >= 16 ? 2 : 0;

        private static int BytesPerPixel(RenderTextureFormat format)
        {
            switch (format)
            {
                case RenderTextureFormat.Depth:
                case RenderTextureFormat.Shadowmap:
                    return 0;
                case RenderTextureFormat.R8:
                    return 1;
                case RenderTextureFormat.RHalf:
                case RenderTextureFormat.R16:
                case RenderTextureFormat.RG16:
                case RenderTextureFormat.RGB565:
                case RenderTextureFormat.ARGB4444:
                case RenderTextureFormat.ARGB1555:
                    return 2;
                case RenderTextureFormat.ARGBHalf:
                case RenderTextureFormat.DefaultHDR:
                case RenderTextureFormat.ARGB64:
                case RenderTextureFormat.RGBAUShort:
                case RenderTextureFormat.RGFloat:
                case RenderTextureFormat.RGInt:
                case RenderTextureFormat.BGRA10101010_XR:
                    return 8;
                case RenderTextureFormat.ARGBFloat:
                case RenderTextureFormat.ARGBInt:
                    return 16;
                default:
                    return 4;
            }
        }
    }
}
