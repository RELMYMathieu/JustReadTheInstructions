using HullcamVDS;
using System;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using System.Reflection;

namespace JustReadTheInstructions
{
    public class HullCameraRenderer
    {
        private readonly MuMechModuleHullCamera _hullCamera;
        private readonly Camera[] _cameras = new Camera[3];
        private readonly Dictionary<Light, CommandBuffer> _strippedRaymarchedBuffers = new Dictionary<Light, CommandBuffer>();
        private int _frameCount;

        private static Light _cachedScaledSunLight;

        public RenderTexture TargetTexture { get; private set; }
        public bool IsActive { get; private set; }
        public int InstanceId { get; }

        private const int NearCameraIndex = 0;
        private const int ScaledCameraIndex = 1;
        private const int GalaxyCameraIndex = 2;

        private bool _deferredApplied;
        private bool _tufxApplied;
        private bool _eveApplied;
        private bool _parallaxApplied;
        private bool _fireflyApplied;
        private bool _scattererApplied;

        private DockingCameraOverlay _dockingOverlay;

        public HullCameraRenderer(MuMechModuleHullCamera hullCamera)
        {
            _hullCamera = hullCamera ?? throw new ArgumentNullException(nameof(hullCamera));
            InstanceId = GetStableId(hullCamera);

            if (hullCamera.cameraFoVMax > 0f)
                hullCamera.cameraFoV = hullCamera.cameraFoVMax;

            InitializeRenderTexture();
            SetupCameras();

            _dockingOverlay = new DockingCameraOverlay(_hullCamera.vessel, TargetTexture, InstanceId);

            IsActive = true;
        }

        public static int GetStableId(MuMechModuleHullCamera hullCamera)
        {
            var config = hullCamera.part.FindModuleImplementing<JRTICameraConfigModule>();
            return JRTICameraRuntime.ResolveId(hullCamera.part.persistentId, config?.jrtiId ?? 0);
            var config = hullCamera.part.FindModuleImplementing<JRTICameraConfigModule>();
            return JRTICameraRuntime.ResolveId(hullCamera.part.persistentId, config?.jrtiId ?? 0);
        }

        private void InitializeRenderTexture()
        {
            TargetTexture = new RenderTexture(
                JRTISettings.RenderWidth,
                JRTISettings.RenderHeight,
                24,
                RenderTextureFormat.ARGB32
            )
            {
                antiAliasing = (ScattererIntegration.IsAvailable || JRTISettings.AntiAliasing == 0) ? 1 : JRTISettings.AntiAliasing
            };

            TargetTexture.Create();
        }

        private void SetupCameras()
        {
            SetupNearCamera();
            SetupScaledCamera();
            SetupGalaxyCamera();

            JRTIStreamServer.Instance?.RegisterCamera(InstanceId);

            Debug.Log($"[JRTI]: Cameras created for '{GetDisplayName()}'");
        }

        private void SetupNearCamera()
        {
            var camObj = new GameObject("JRTI_Near_" + InstanceId);
            var camera = camObj.AddComponent<Camera>();

            var mainCam = Camera.allCameras.FirstOrDefault(c => c.name == "Camera 00");
            if (mainCam != null)
                camera.CopyFrom(mainCam);

            camera.useOcclusionCulling = false;
            camera.name = "JRTI_Near";

            camera.transform.parent = !string.IsNullOrEmpty(_hullCamera.cameraTransformName)
                ? _hullCamera.part.FindModelTransform(_hullCamera.cameraTransformName)
                : _hullCamera.part.transform;

            camera.transform.localRotation = Quaternion.LookRotation(
                _hullCamera.cameraForward,
                _hullCamera.cameraUp
            );
            camera.transform.localPosition = _hullCamera.cameraPosition;

            //camera.nearClipPlane = 0.07f; // TODO: Find proper near clip plane value
            // Basically, this current value causes an issue where the shading on the cameras is a bit
            // "off" looking. Still looking into it.
            camera.fieldOfView = _hullCamera.cameraFoV;
            camera.targetTexture = TargetTexture;
            camera.allowHDR = JRTISettings.UseHDR;
            camera.allowMSAA = !ScattererIntegration.IsAvailable;

            if (JRTISettings.EnableDeferred)
                DeferredIntegration.ApplyToCamera(camera, 15);

            if (JRTISettings.EnableTUFX)
                TUFXIntegration.ApplyToCamera(camera);

            if (JRTISettings.EnableEVE)
                EVEIntegration.ApplyToCamera(camera, mainCam, includeLocalEffects: true);

            if (JRTISettings.EnableParallax)
                ParallaxIntegration.ApplyToCamera(camera);

            if (JRTISettings.EnableFirefly)
                FireflyIntegration.ApplyToCamera(camera);

            if (JRTISettings.EnableScatterer)
                ScattererIntegration.ApplyToCamera(camera);

            _deferredApplied = JRTISettings.EnableDeferred;
            _tufxApplied = JRTISettings.EnableTUFX;
            _eveApplied = JRTISettings.EnableEVE;
            _parallaxApplied = JRTISettings.EnableParallax;
            _fireflyApplied = JRTISettings.EnableFirefly;
            _scattererApplied = JRTISettings.EnableScatterer;

            camObj.AddComponent<CanvasFix>();

            if (ScattererIntegration.IsAvailable)
                camObj.AddComponent<ScattererCameraSwap>();

            _cameras[NearCameraIndex] = camera;
            camera.enabled = false;
        }

        private void SetupScaledCamera()
        {
            var camObj = new GameObject("JRTI_Scaled_" + InstanceId);
            var camera = camObj.AddComponent<Camera>();

            var mainScaledCam = FindCameraByName("Camera ScaledSpace");
            if (mainScaledCam != null)
            {
                camera.CopyFrom(mainScaledCam);
                camera.transform.parent = mainScaledCam.transform;
                camera.depth = mainScaledCam.depth - 0.5f;
            }

            camera.useOcclusionCulling = false;
            camera.name = "JRTI_Scaled";

            camera.transform.localRotation = Quaternion.identity;
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localScale = Vector3.one;

            camera.fieldOfView = _hullCamera.cameraFoV;
            camera.targetTexture = TargetTexture;
            camera.allowHDR = JRTISettings.UseHDR;
            camera.allowMSAA = !ScattererIntegration.IsAvailable;

            if (JRTISettings.EnableDeferred)
                DeferredIntegration.ApplyToCamera(camera, 10);

            if (JRTISettings.EnableTUFX)
                TUFXIntegration.ApplyToCamera(camera);

            if (JRTISettings.EnableEVE)
                EVEIntegration.ApplyToCamera(camera, mainScaledCam, includeLocalEffects: false);

            if (JRTISettings.EnableScatterer)
                ScattererIntegration.ApplyToScaledCamera(camera);

            var synchronizer = camObj.AddComponent<CameraSynchronizer>();
            synchronizer.SourceCamera = _cameras[NearCameraIndex];

            camObj.AddComponent<CanvasFix>();

            if (ScattererIntegration.IsAvailable)
                camObj.AddComponent<ScattererScaledCameraSwap>();

            _cameras[ScaledCameraIndex] = camera;
            camera.enabled = false;
        }

        private void SetupGalaxyCamera()
        {
            var camObj = new GameObject("JRTI_Galaxy_" + InstanceId);
            var camera = camObj.AddComponent<Camera>();

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            var mainGalaxyCam = FindCameraByName("GalaxyCamera");
            if (mainGalaxyCam != null)
            {
                camera.CopyFrom(mainGalaxyCam);
                camera.transform.parent = mainGalaxyCam.transform;
            }

            camera.useOcclusionCulling = false;
            camera.name = "JRTI_Galaxy";

            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            camera.transform.localScale = Vector3.one;

            camera.fieldOfView = _hullCamera.cameraFoV;
            camera.targetTexture = TargetTexture;
            camera.allowHDR = JRTISettings.UseHDR;
            camera.allowMSAA = !ScattererIntegration.IsAvailable;

            if (JRTISettings.EnableDeferred)
                DeferredIntegration.ApplyToCamera(camera, 10);

            if (JRTISettings.EnableTUFX)
                TUFXIntegration.ApplyToCamera(camera);

            var synchronizer = camObj.AddComponent<CameraSynchronizer>();
            synchronizer.SourceCamera = _cameras[NearCameraIndex];

            camObj.AddComponent<CanvasFix>();

            _cameras[GalaxyCameraIndex] = camera;
            camera.enabled = false;
        }

        private Camera FindCameraByName(string cameraName)
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam.name == cameraName)
                    return cam;
            }

            Debug.LogWarning($"[JRTI]: Camera '{cameraName}' not found");
            return null;
        }

        public void Update(bool hasInGameViewer = false)
        {
            if (!IsActive || _hullCamera == null) return;

            _frameCount++;

            bool hasViewers = hasInGameViewer || (JRTIStreamServer.Instance?.HasActiveClients(InstanceId) ?? false);
            bool shouldRender = hasViewers && (_frameCount % (JRTISettings.RenderEveryOtherFrame ? 2 : 1)) == 0;

            if (!shouldRender) return;

            if (!TargetTexture.IsCreated()) TargetTexture.Create();

            if (_parallaxApplied) RenderParallaxScatters();

            StripRaymarchedLightBuffers();

            for (int i = _cameras.Length - 1; i >= 0; i--)
            {
                var camera = _cameras[i];
                if (camera == null) continue;
                camera.targetTexture = TargetTexture;
                camera.GetComponent<CameraSynchronizer>()?.ManualSync();
                camera.Render();
            }

            RestoreRaymarchedLightBuffers();

            if (_fireflyApplied) UpdateFireflyEffects();

            if (JRTISettings.EnableHullcamFilter && HullcamFilterIntegration.IsAvailable)
                HullcamFilterIntegration.SyncToCamera(_cameras[NearCameraIndex], _hullCamera);

            if (JRTISettings.EnableDockingOverlay && GetCameraMode() == CameraFilter.eCameraMode.DockingCam)
                _dockingOverlay.Render(TargetTexture);

            JRTIStreamServer.Instance?.TryCaptureFrame(InstanceId, TargetTexture);
        }

        private void RenderParallaxScatters()
        {
            if (!JRTISettings.EnableParallax || !ParallaxIntegration.IsAvailable) return;
            var nearCamera = _cameras[NearCameraIndex];
            if (nearCamera != null)
                ParallaxIntegration.RenderToCamera(nearCamera);
        }

        private void UpdateFireflyEffects()
        {
            if (!FireflyIntegration.IsAvailable) return;
            var nearCamera = _cameras[NearCameraIndex];
            if (nearCamera != null)
                FireflyIntegration.UpdateForCamera(nearCamera, GetVessel());
        }

        private static Light GetScaledSunLight()
        {
            if (_cachedScaledSunLight != null) return _cachedScaledSunLight;
            var field = typeof(Sun).GetField("scaledSunLight",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _cachedScaledSunLight = field?.GetValue(Sun.Instance) as Light;
            return _cachedScaledSunLight;
        }

        private void StripRaymarchedLightBuffers()
        {
            _strippedRaymarchedBuffers.Clear();
            var sunLight = GetScaledSunLight();
            if (sunLight == null) return;
            foreach (var buf in sunLight.GetCommandBuffers(LightEvent.AfterScreenspaceMask))
            {
                if (buf.name != "Composite Shadows") continue;
                _strippedRaymarchedBuffers[sunLight] = buf;
                sunLight.RemoveCommandBuffer(LightEvent.AfterScreenspaceMask, buf);
            }
        }

        private void RestoreRaymarchedLightBuffers()
        {
            foreach (var kvp in _strippedRaymarchedBuffers)
                kvp.Key.AddCommandBuffer(LightEvent.AfterScreenspaceMask, kvp.Value);
            _strippedRaymarchedBuffers.Clear();
        }

        public string GetDisplayName()
        {
            if (_hullCamera?.vessel == null)
                return "Unknown Camera";

            return $"{_hullCamera.vessel.GetDisplayName()}.{_hullCamera.cameraName}";
        }

        public Vessel GetVessel()
        {
            return _hullCamera?.vessel;
        }

        public bool IsValid()
        {
            return _hullCamera != null && _hullCamera.vessel != null && _hullCamera.vessel.loaded;
        }

        public float GetFOV() => _hullCamera?.cameraFoV ?? 0f;
        public float GetMinFOV() => _hullCamera?.cameraFoVMin ?? 0f;
        public float GetMaxFOV() => _hullCamera?.cameraFoVMax ?? 0f;

        public void SetFieldOfView(float fov)
        {
            if (_hullCamera != null)
                _hullCamera.cameraFoV = fov;

            foreach (var camera in _cameras)
            {
                if (camera != null)
                    camera.fieldOfView = fov;
            }
        }

        public void UpdateVisualEffects()
        {
            UpdateIntegration(
                JRTISettings.EnableDeferred, ref _deferredApplied,
                cam => DeferredIntegration.ApplyToCamera(cam, cam.name.Contains("Near") ? 15 : 10),
                cam => DeferredIntegration.RemoveFromCamera(cam),
                "Deferred"
            );

            UpdateIntegration(
                JRTISettings.EnableTUFX, ref _tufxApplied,
                cam => TUFXIntegration.ApplyToCamera(cam),
                cam => TUFXIntegration.RemoveFromCamera(cam),
                "TUFX"
            );

            if (JRTISettings.EnableEVE && !_eveApplied)
            {
                var mainCam = Camera.allCameras.FirstOrDefault(c => c.name == "Camera 00");
                var scaledCam = FindCameraByName("Camera ScaledSpace");

                if (_cameras[NearCameraIndex] != null)
                    EVEIntegration.ApplyToCamera(_cameras[NearCameraIndex], mainCam, includeLocalEffects: true);
                if (_cameras[ScaledCameraIndex] != null)
                    EVEIntegration.ApplyToCamera(_cameras[ScaledCameraIndex], scaledCam, includeLocalEffects: false);

                _eveApplied = true;
                Debug.Log($"[JRTI]: Applied EVE to {GetDisplayName()}");
            }
            else if (!JRTISettings.EnableEVE && _eveApplied)
            {
                foreach (var camera in _cameras)
                {
                    if (camera != null)
                        EVEIntegration.RemoveFromCamera(camera);
                }
                _eveApplied = false;
                Debug.Log($"[JRTI]: Removed EVE from {GetDisplayName()}");
            }

            UpdateIntegration(
                JRTISettings.EnableParallax, ref _parallaxApplied,
                cam => ParallaxIntegration.ApplyToCamera(cam),
                cam => ParallaxIntegration.RemoveFromCamera(cam),
                "Parallax"
            );

            if (JRTISettings.EnableFirefly && !_fireflyApplied)
            {
                var nearCamera = _cameras[NearCameraIndex];
                if (nearCamera != null)
                    FireflyIntegration.ApplyToCamera(nearCamera);
                _fireflyApplied = true;
                Debug.Log($"[JRTI]: Applied Firefly to {GetDisplayName()}");
            }
            else if (!JRTISettings.EnableFirefly && _fireflyApplied)
            {
                var nearCamera = _cameras[NearCameraIndex];
                if (nearCamera != null)
                {
                    FireflyIntegration.RemoveFromCamera(nearCamera);
                    FireflyIntegration.CleanupCamera(nearCamera);
                }
                _fireflyApplied = false;
                Debug.Log($"[JRTI]: Removed Firefly from {GetDisplayName()}");
            }

            if (JRTISettings.EnableScatterer && !_scattererApplied)
            {
                if (_cameras[NearCameraIndex] != null)
                    ScattererIntegration.ApplyToCamera(_cameras[NearCameraIndex]);
                if (_cameras[ScaledCameraIndex] != null)
                    ScattererIntegration.ApplyToScaledCamera(_cameras[ScaledCameraIndex]);
                _scattererApplied = true;
                Debug.Log($"[JRTI]: Applied Scatterer to {GetDisplayName()}");
            }
            else if (!JRTISettings.EnableScatterer && _scattererApplied)
            {
                foreach (var camera in _cameras)
                {
                    if (camera != null)
                        ScattererIntegration.RemoveFromCamera(camera);
                }
                _scattererApplied = false;
                Debug.Log($"[JRTI]: Removed Scatterer from {GetDisplayName()}");
            }
        }

        private void UpdateIntegration(
            bool shouldEnable,
            ref bool isApplied,
            Action<Camera> apply,
            Action<Camera> remove,
            string name)
        {
            if (shouldEnable && !isApplied)
            {
                foreach (var camera in _cameras)
                {
                    if (camera != null)
                        apply(camera);
                }
                isApplied = true;
                Debug.Log($"[JRTI]: Applied {name} to {GetDisplayName()}");
            }
            else if (!shouldEnable && isApplied)
            {
                foreach (var camera in _cameras)
                {
                    if (camera != null)
                        remove(camera);
                }
                isApplied = false;
                Debug.Log($"[JRTI]: Removed {name} from {GetDisplayName()}");
            }
        }

        public void Dispose()
        {
            IsActive = false;

            JRTIStreamServer.Instance?.UnregisterCamera(InstanceId);

            foreach (var camera in _cameras)
            {
                if (camera != null)
                {
                    DeferredIntegration.RemoveFromCamera(camera);
                    TUFXIntegration.RemoveFromCamera(camera);
                    EVEIntegration.RemoveFromCamera(camera);
                    ParallaxIntegration.RemoveFromCamera(camera);
                    FireflyIntegration.RemoveFromCamera(camera);
                    FireflyIntegration.CleanupCamera(camera);
                    ScattererIntegration.RemoveFromCamera(camera);
                    HullcamFilterIntegration.RemoveFromCamera(camera);

                    if (camera.gameObject != null)
                        UnityEngine.Object.Destroy(camera.gameObject);
                }
            }

            TargetTexture?.Release();
            TargetTexture = null;

            _dockingOverlay.Dispose();

            Debug.Log($"[JRTI]: Disposed camera '{GetDisplayName()}'");
        }

        public string GetDiagnosticInfo()
        {
            var info = $"=== {GetDisplayName()} ===\n";
            info += $"Instance ID: {InstanceId}\n";
            info += $"Active: {IsActive}\n";
            info += $"Valid: {IsValid()}\n\n";

            foreach (var camera in _cameras)
            {
                if (camera != null)
                {
                    info += $"--- {camera.name} ---\n";
                    info += $"Enabled: {camera.enabled}\n";
                    info += $"FOV: {camera.fieldOfView}\n";
                    info += $"allowMSAA: {camera.allowMSAA}\n";
                    info += DeferredIntegration.GetDiagnosticInfo(camera);
                    info += TUFXIntegration.GetDiagnosticInfo(camera);
                    info += EVEIntegration.GetDiagnosticInfo(camera);
                    info += ParallaxIntegration.GetDiagnosticInfo(camera);
                    info += FireflyIntegration.GetDiagnosticInfo(camera);
                    info += ScattererIntegration.GetDiagnosticInfo(camera);
                    info += "\n";
                }
            }

            info += HullcamFilterIntegration.GetDiagnosticInfo();
            return info;
        }
    }
}
