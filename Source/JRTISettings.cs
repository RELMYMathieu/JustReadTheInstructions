using System;
using System.Globalization;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class JRTISettings : MonoBehaviour
    {
        private const string ConfigUrl = "GameData/JustReadTheInstructions/settings.cfg";

        public static int RenderWidth { get; internal set; } = 1280;
        public static int RenderHeight { get; internal set; } = 720;
        public static int AntiAliasing { get; internal set; } = 2;
        public static bool UseHDR { get; internal set; } = true;
        public static bool RenderEveryOtherFrame { get; internal set; } = true;

        public static float MaxWindowScale { get; internal set; } = 3f;
        public static float MinWindowScale { get; internal set; } = 0.5f;

        public static int MaxPreviewSize { get; internal set; } = 360;
        public static uint MaxOpenCameras { get; internal set; } = 8u;

        public static bool IsLoaded { get; private set; }

        public static int StreamPort { get; internal set; } = 8080;
        public static int StreamJpegQuality { get; internal set; } = 90;
        public static int StreamMaxFps { get; internal set; } = 30;

        public static bool EnableDeferred { get; internal set; } = true;
        public static bool EnableTUFX { get; internal set; } = true;
        public static bool EnableEVE { get; internal set; } = true;
        public static bool EnableParallax { get; internal set; } = false;
        public static bool EnableFirefly { get; internal set; } = true;
        public static bool EnableScatterer { get; internal set; } = true;
        public static bool EnableHullcamFilter { get; internal set; } = true;

        private static readonly int[] ValidAntiAliasingValues = { 0, 1, 2, 4, 8 };

        internal static int SanitizeAntiAliasing(int value)
        {
            int best = ValidAntiAliasingValues[0];
            int bestDist = int.MaxValue;
            foreach (int v in ValidAntiAliasingValues)
            {
                int dist = Math.Abs(v - value);
                if (dist < bestDist) { bestDist = dist; best = v; }
            }
            return best;
        }

        private static void Sanitize()
        {
            RenderWidth = Mathf.Clamp(RenderWidth, 128, 7680);
            RenderHeight = Mathf.Clamp(RenderHeight, 128, 4320);
            AntiAliasing = SanitizeAntiAliasing(AntiAliasing);
            MaxPreviewSize = Mathf.Clamp(MaxPreviewSize, 100, 2000);
            MaxWindowScale = Mathf.Clamp(MaxWindowScale, 1f, 10f);
            MinWindowScale = Mathf.Clamp(MinWindowScale, 0.1f, 1f);
            MinWindowScale = Mathf.Min(MinWindowScale, MaxWindowScale);
            StreamPort = Mathf.Clamp(StreamPort, 1024, 65535);
            StreamJpegQuality = Mathf.Clamp(StreamJpegQuality, 1, 100);
            StreamMaxFps = Mathf.Clamp(StreamMaxFps, 1, 120);
        }

        void Awake()
        {
            DontDestroyOnLoad(this);
            LoadConfig();
            IsLoaded = true;
        }

        private static void LoadConfig()
        {
            try
            {
                Debug.Log("[JRTI]: Loading configuration...");

                ConfigNode fileNode = ConfigNode.Load(ConfigUrl);
                if (fileNode == null || !fileNode.HasNode("Settings"))
                {
                    Debug.Log("[JRTI]: No config found, using defaults");
                    return;
                }

                ConfigNode settings = fileNode.GetNode("Settings");

                RenderWidth = ParseInt(settings, "RenderWidth", RenderWidth);
                RenderHeight = ParseInt(settings, "RenderHeight", RenderHeight);
                AntiAliasing = ParseInt(settings, "AntiAliasing", AntiAliasing);
                UseHDR = ParseBool(settings, "UseHDR", UseHDR);
                RenderEveryOtherFrame = ParseBool(settings, "RenderEveryOtherFrame", RenderEveryOtherFrame);
                MaxOpenCameras = ParseUInt(settings, "MaxOpenCameras", MaxOpenCameras, 1, 64);
                MaxWindowScale = ParseFloat(settings, "MaxWindowScale", MaxWindowScale);
                MinWindowScale = ParseFloat(settings, "MinWindowScale", MinWindowScale);
                MaxPreviewSize = ParseInt(settings, "MaxPreviewSize", MaxPreviewSize);
                StreamPort = ParseInt(settings, "StreamPort", StreamPort);
                StreamJpegQuality = ParseInt(settings, "StreamJpegQuality", StreamJpegQuality);
                StreamMaxFps = ParseInt(settings, "StreamMaxFps", StreamMaxFps);

                EnableDeferred = ParseBool(settings, "EnableDeferred", EnableDeferred);
                EnableTUFX = ParseBool(settings, "EnableTUFX", EnableTUFX);
                EnableEVE = ParseBool(settings, "EnableEVE", EnableEVE);
                EnableParallax = ParseBool(settings, "EnableParallax", EnableParallax);
                EnableFirefly = ParseBool(settings, "EnableFirefly", EnableFirefly);
                EnableScatterer = ParseBool(settings, "EnableScatterer", EnableScatterer);
                EnableHullcamFilter = ParseBool(settings, "EnableHullcamFilter", EnableHullcamFilter);

                Sanitize();

                Debug.Log($"[JRTI]: Config loaded - {RenderWidth}x{RenderHeight}");
                Debug.Log($"[JRTI]: Stream config - port:{StreamPort}, quality:{StreamJpegQuality}, maxFps:{StreamMaxFps}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI]: Failed to load config: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                Sanitize();

                var root = new ConfigNode();
                var settings = root.AddNode("Settings");

                settings.AddValue("RenderWidth", RenderWidth);
                settings.AddValue("RenderHeight", RenderHeight);
                settings.AddValue("AntiAliasing", AntiAliasing);
                settings.AddValue("UseHDR", UseHDR);
                settings.AddValue("RenderEveryOtherFrame", RenderEveryOtherFrame);
                settings.AddValue("MaxWindowScale", MaxWindowScale.ToString(CultureInfo.InvariantCulture));
                settings.AddValue("MinWindowScale", MinWindowScale.ToString(CultureInfo.InvariantCulture));
                settings.AddValue("MaxPreviewSize", MaxPreviewSize);
                settings.AddValue("MaxOpenCameras", MaxOpenCameras);
                settings.AddValue("StreamPort", StreamPort);
                settings.AddValue("StreamJpegQuality", StreamJpegQuality);
                settings.AddValue("StreamMaxFps", StreamMaxFps);

                settings.AddValue("EnableDeferred", EnableDeferred);
                settings.AddValue("EnableTUFX", EnableTUFX);
                settings.AddValue("EnableEVE", EnableEVE);
                settings.AddValue("EnableParallax", EnableParallax);
                settings.AddValue("EnableFirefly", EnableFirefly);
                settings.AddValue("EnableScatterer", EnableScatterer);
                settings.AddValue("EnableHullcamFilter", EnableHullcamFilter);

                root.Save(KSPUtil.ApplicationRootPath + ConfigUrl);
                Debug.Log("[JRTI]: Settings saved");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI]: Failed to save config: {ex.Message}");
            }
        }

        private static int ParseInt(ConfigNode node, string key, int defaultValue)
        {
            if (!node.HasValue(key) || !int.TryParse(node.GetValue(key), out int result))
                return defaultValue;
            return result;
        }

        private static float ParseFloat(ConfigNode node, string key, float defaultValue)
        {
            if (!node.HasValue(key) || !float.TryParse(node.GetValue(key),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return defaultValue;
            return result;
        }

        private static bool ParseBool(ConfigNode node, string key, bool defaultValue)
        {
            return node.HasValue(key) && bool.TryParse(node.GetValue(key), out bool result)
                ? result
                : defaultValue;
        }

        private static uint ParseUInt(ConfigNode node, string key, uint defaultValue, uint min = uint.MinValue, uint max = uint.MaxValue)
        {
            if (!node.HasValue(key) || !uint.TryParse(node.GetValue(key), out uint result))
                return defaultValue;

            if (result < min) return min;
            if (result > max) return max;
            return result;
        }
    }
}