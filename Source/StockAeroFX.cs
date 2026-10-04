using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class StockAeroFX
    {
        private const string RenderTypeTag = "RenderType";

        private static FXDepthCamera _stockDepth;
        private static bool _logged;

        private readonly Camera _depth;
        private readonly Camera _fx;

        public StockAeroFX(int instanceId)
        {
            _depth = CreateCamera("JRTI_AeroFXDepth_" + instanceId);
            _fx = CreateCamera("JRTI_AeroFX_" + instanceId);
        }

        public static bool IsAvailable => !FireflyIntegration.IsAvailable;

        public void Render(Camera near, RenderTexture target)
        {
            var stockFx = FXCamera.Instance;
            if (near == null || stockFx == null || stockFx.ReplacementShaders.Length == 0) return;

            var stockFxCamera = stockFx.GetComponent<Camera>();
            if (stockFxCamera == null || !stockFxCamera.enabled) return;

            if (_stockDepth == null) _stockDepth = Object.FindObjectOfType<FXDepthCamera>();
            if (_stockDepth == null || _stockDepth.depthCamera == null) return;

            LogStockSetupOnce(stockFxCamera, _stockDepth.depthCamera);
            float far = FarClip(near, stockFxCamera);

            Mirror(_depth, _stockDepth.depthCamera, near, target, far, CameraClearFlags.Depth);
            _depth.SetReplacementShader(_stockDepth.ReplacementShader, RenderTypeTag);
            _depth.Render();

            Mirror(_fx, stockFxCamera, near, target, far, CameraClearFlags.Nothing);
            _fx.SetReplacementShader(stockFx.ReplacementShaders[Mathf.Clamp(stockFx.shaderLOD, 0, stockFx.ReplacementShaders.Length - 1)], RenderTypeTag);
            _fx.Render();
        }

        public void Dispose()
        {
            if (_depth != null) Object.Destroy(_depth.gameObject);
            if (_fx != null) Object.Destroy(_fx.gameObject);
        }

        private static Camera CreateCamera(string name)
        {
            var camera = new GameObject(name).AddComponent<Camera>();
            camera.enabled = false;
            return camera;
        }

        private static void Mirror(Camera camera, Camera stock, Camera near, RenderTexture target, float far, CameraClearFlags clearFlags)
        {
            camera.CopyFrom(stock);
            camera.transform.SetPositionAndRotation(near.transform.position, near.transform.rotation);
            camera.clearFlags = clearFlags;
            camera.useOcclusionCulling = false;
            camera.targetTexture = target;
            camera.fieldOfView = near.fieldOfView;
            camera.aspect = near.aspect;
            camera.nearClipPlane = near.nearClipPlane;
            camera.farClipPlane = far;
            camera.renderingPath = RenderingPath.Forward;
            camera.allowHDR = false;
            camera.allowMSAA = false;
        }

        private static float FarClip(Camera near, Camera stockFxCamera)
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return stockFxCamera.farClipPlane;
            float reach = Vector3.Distance(near.transform.position, vessel.transform.position) + vessel.vesselSize.magnitude;
            return Mathf.Max(stockFxCamera.farClipPlane, reach);
        }

        private static void LogStockSetupOnce(Camera fx, Camera depth)
        {
            if (_logged) return;
            _logged = true;
            Debug.Log($"[JRTI-AeroFX]: Mirroring stock aero effects. FX camera: clear {fx.clearFlags}, mask {fx.cullingMask}, path {fx.renderingPath}, hdr {fx.allowHDR}, depth texture {fx.depthTextureMode}, own target {fx.targetTexture != null}. "
                      + $"Depth camera: clear {depth.clearFlags}, mask {depth.cullingMask}, path {depth.renderingPath}, hdr {depth.allowHDR}, own target {depth.targetTexture != null}");
        }
    }
}
