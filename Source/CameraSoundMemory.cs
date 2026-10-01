using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class CameraSoundMemory
    {
        private const int MaxCameras = 256;
        private const string CameraNode = "CAMERA";

        private static readonly string FilePath =
            KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/PluginData/camera-sound.cfg";

        private static Dictionary<string, Entry> _cameras;

        public static string KeyOf(uint partPersistentId, int cameraIndex) => $"{partPersistentId}:{cameraIndex}";

        public static bool TryRecall(string key, out SoundSettings sound)
        {
            sound = SoundSettings.Default;
            if (!Cameras().TryGetValue(key, out var entry)) return false;
            entry.UsedTicks = DateTime.UtcNow.Ticks;
            sound = entry.Sound;
            return true;
        }

        public static void Remember(string key, SoundSettings sound)
        {
            var cameras = Cameras();
            var known = cameras.TryGetValue(key, out var entry) ? entry.Sound : SoundSettings.Default;
            if (Same(sound, known)) return;

            cameras[key] = new Entry { Sound = sound, UsedTicks = DateTime.UtcNow.Ticks };
            foreach (var stale in cameras.OrderByDescending(kv => kv.Value.UsedTicks).Skip(MaxCameras).Select(kv => kv.Key).ToList())
                cameras.Remove(stale);
            Save(cameras);
        }

        private static bool Same(SoundSettings a, SoundSettings b)
            => a.Mic == b.Mic && a.GainDb == b.GainDb && a.AutoGain == b.AutoGain && a.Mastering == b.Mastering;

        private static Dictionary<string, Entry> Cameras()
        {
            if (_cameras == null) _cameras = Load();
            return _cameras;
        }

        private static Dictionary<string, Entry> Load()
        {
            var cameras = new Dictionary<string, Entry>();
            if (!File.Exists(FilePath)) return cameras;

            try
            {
                foreach (var node in ConfigNode.Load(FilePath).GetNodes(CameraNode))
                {
                    string key = node.GetValue("key");
                    if (string.IsNullOrEmpty(key) || !CameraMics.TryParse(node.GetValue("mic"), out var mic)) continue;
                    float.TryParse(node.GetValue("gainDb"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gainDb);
                    bool.TryParse(node.GetValue("autoGain"), out bool autoGain);
                    bool.TryParse(node.GetValue("mastering"), out bool mastering);
                    long.TryParse(node.GetValue("used"), out long used);
                    cameras[key] = new Entry { Sound = new SoundSettings(mic, Mathf.Clamp(gainDb, -24f, 24f), autoGain, mastering), UsedTicks = used };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Audio]: Could not read remembered camera sound settings: {ex.Message}");
            }
            return cameras;
        }

        private static void Save(Dictionary<string, Entry> cameras)
        {
            try
            {
                var root = new ConfigNode();
                foreach (var kv in cameras)
                {
                    var node = root.AddNode(CameraNode);
                    node.AddValue("key", kv.Key);
                    node.AddValue("mic", CameraMics.Id(kv.Value.Sound.Mic));
                    node.AddValue("gainDb", kv.Value.Sound.GainDb.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    node.AddValue("autoGain", kv.Value.Sound.AutoGain);
                    node.AddValue("mastering", kv.Value.Sound.Mastering);
                    node.AddValue("used", kv.Value.UsedTicks);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                root.Save(FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Audio]: Could not save camera sound settings: {ex.Message}");
            }
        }

        private sealed class Entry
        {
            public SoundSettings Sound;
            public long UsedTicks;
        }
    }
}
