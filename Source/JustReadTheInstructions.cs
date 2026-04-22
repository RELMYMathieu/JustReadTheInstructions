using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class JustReadTheInstructions : MonoBehaviour
    {
        private static readonly string ModVersion =
    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        private bool _initialized;

        void Start()
        {
            if (!_initialized)
            {
                Initialize();
                _initialized = true;
            }
        }

        private void Initialize()
        {
            Debug.Log($"[JRTI]: Just Read The Instructions v{ModVersion} initializing...");

            if (!JRTISettings.IsLoaded)
            {
                Debug.LogWarning("[JRTI]: Settings not loaded, using defaults");
            }

            if (!VerifyDependencies())
            {
                Debug.LogError("[JRTI]: Missing required dependencies!");
                return;
            }

            Debug.Log($"[JRTI]: Initialization complete");
            Debug.Log($"[JRTI]: Render resolution: {JRTISettings.RenderWidth}x{JRTISettings.RenderHeight}");
        }

        private bool VerifyDependencies()
        {
            try
            {
                var type = typeof(HullcamVDS.MuMechModuleHullCamera);
                if (type != null)
                {
                    Debug.Log("[JRTI]: HullcamVDS detected");
                    return true;
                }
            }
            catch
            {
                Debug.LogError("[JRTI]: HullcamVDS not found! This mod requires HullcamVDS Continued");
                return false;
            }

            return false;
        }
    }
}
