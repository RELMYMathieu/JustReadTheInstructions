using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class JRTICameraRuntime : MonoBehaviour
    {
        public static JRTICameraRuntime Instance { get; private set; }

        private static readonly string ConfigUrl = "GameData/JustReadTheInstructions/camera_configs.cfg";
        private static readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        private static readonly Dictionary<uint, int> _ids = new Dictionary<uint, int>();

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(this);
            Load();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static int ResolveId(uint persistentId)
        {
            if (_ids.TryGetValue(persistentId, out int id) && id > 0) return id;
            int next = NextAvailableId(persistentId);
            _ids[persistentId] = next;
            Save();
            return next;
        }

        public static string ResolveName(uint persistentId, string fallback)
            => _names.TryGetValue(persistentId, out string name) && !string.IsNullOrEmpty(name) ? name : fallback;

        public static int GetStoredId(uint persistentId)
            => _ids.TryGetValue(persistentId, out int id) ? id : 0;

        public static string GetStoredName(uint persistentId)
            => _names.TryGetValue(persistentId, out string name) ? name : "";

        public static void SetEntry(uint persistentId, string name, int id)
        {
            if (!string.IsNullOrEmpty(name))
                _names[persistentId] = name;
            else
                _names.Remove(persistentId);

            if (id > 0)
                _ids[persistentId] = id;

            Save();
        }

        public static bool IsIdTaken(uint excludePersistentId, int candidateId)
            => _ids.Any(kvp => kvp.Key != excludePersistentId && kvp.Value == candidateId);

        public static HashSet<int> GetAllAssignedIds(uint excludePersistentId)
        {
            var ids = new HashSet<int>();
            foreach (var kvp in _ids)
                if (kvp.Key != excludePersistentId && kvp.Value > 0)
                    ids.Add(kvp.Value);
            return ids;
        }

        private static int NextAvailableId(uint excludePersistentId)
        {
            var taken = GetAllAssignedIds(excludePersistentId);
            int next = 1;
            while (taken.Contains(next)) next++;
            return next;
        }

        public static void Save()
        {
            try
            {
                var root = new ConfigNode();
                var configsNode = root.AddNode("CameraConfigs");

                var allKeys = new HashSet<uint>(_names.Keys);
                allKeys.UnionWith(_ids.Keys);

                foreach (var key in allKeys)
                {
                    var node = configsNode.AddNode("Camera");
                    node.AddValue("persistentId", key.ToString());
                    if (_names.TryGetValue(key, out string name))
                        node.AddValue("name", name);
                    if (_ids.TryGetValue(key, out int id) && id > 0)
                        node.AddValue("id", id.ToString());
                }

                root.Save(KSPUtil.ApplicationRootPath + ConfigUrl);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI]: Failed to save camera configs: {ex.Message}");
            }
        }

        private static void Load()
        {
            try
            {
                var root = ConfigNode.Load(ConfigUrl);
                if (root == null || !root.HasNode("CameraConfigs")) return;

                var configsNode = root.GetNode("CameraConfigs");
                foreach (ConfigNode node in configsNode.GetNodes("Camera"))
                {
                    if (!uint.TryParse(node.GetValue("persistentId"), out uint key)) continue;
                    string name = node.GetValue("name");
                    if (!string.IsNullOrEmpty(name)) _names[key] = name;
                    if (int.TryParse(node.GetValue("id"), out int id) && id > 0)
                        _ids[key] = id;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI]: Failed to load camera configs: {ex.Message}");
            }
        }
    }
}
