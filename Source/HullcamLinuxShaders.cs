using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HullcamVDS;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HullcamLinuxShaders : MonoBehaviour
    {
        private const string LogTag = "[JRTI-HullcamShaders]";

        private void Awake()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer || !HullcamFilterIntegration.IsAvailable)
                return;

            try
            {
                Install();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag}: Failed, HullcamVDS will load its own shaders: {ex.Message}");
            }
        }

        private static void Install()
        {
            var bundleLoaded = typeof(CameraFilter).GetField("BundleLoaded", BindingFlags.NonPublic | BindingFlags.Static);
            var loadedShaders = typeof(CameraFilter).GetField("LoadedShaders", BindingFlags.NonPublic | BindingFlags.Static);
            if (bundleLoaded == null || loadedShaders == null)
            {
                Debug.LogWarning($"{LogTag}: HullcamVDS changed shape, keeping its own shaders");
                return;
            }

            if ((bool)bundleLoaded.GetValue(null))
            {
                Debug.Log($"{LogTag}: HullcamVDS shaders already loaded, nothing to do");
                return;
            }

            string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "JustReadTheInstructions", "HullcamShaders", "shaders.linux");
            if (!File.Exists(path))
            {
                Debug.Log($"{LogTag}: No replacement bundle at {path}, keeping HullcamVDS's own");
                return;
            }

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Debug.LogWarning($"{LogTag}: Could not open {path}, keeping HullcamVDS's own");
                return;
            }

            var shaders = (Dictionary<string, Shader>)loadedShaders.GetValue(null) ?? new Dictionary<string, Shader>();
            int count = 0;
            foreach (var shader in bundle.LoadAllAssets<Shader>())
            {
                if (shader == null) continue;
                shaders[shader.name] = shader;
                count++;
            }
            bundle.Unload(false);

            if (count == 0)
            {
                Debug.LogWarning($"{LogTag}: Replacement bundle has no shaders, keeping HullcamVDS's own");
                return;
            }

            loadedShaders.SetValue(null, shaders);
            bundleLoaded.SetValue(null, true);
            Debug.Log($"{LogTag}: Rebuilt Linux shaders installed ({count}: {string.Join(", ", shaders.Keys)}), supported={string.Join(", ", SupportFlags(shaders.Values))}");
        }

        private static IEnumerable<string> SupportFlags(IEnumerable<Shader> shaders)
        {
            foreach (var shader in shaders)
                yield return $"{shader.name}:{shader.isSupported}";
        }
    }
}
