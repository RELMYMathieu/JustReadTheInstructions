using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    internal sealed class VGFXFeed
    {
        private const CameraEvent GroundEvent = CameraEvent.BeforeForwardAlpha;
        private const CameraEvent WaterEvent = CameraEvent.AfterForwardAlpha;
        private const int MinDivisor = 2;
        private const float MinCloudPixels = 2f;

        private static VGFXFeed _rendering;
        private static bool _hooked;
        private static bool _loggedFailure;

        private readonly int _instanceId;
        private readonly CommandBuffer _ground = new CommandBuffer { name = "JRTI VGFX ground" };
        private readonly CommandBuffer _water = new CommandBuffer { name = "JRTI VGFX water" };
        private readonly CommandBuffer _timeGroundFx = new CommandBuffer { name = "JRTI VGFX timing start" };
        private readonly CommandBuffer _timeNear = new CommandBuffer { name = "JRTI VGFX timing end" };
        private readonly VGFXIntegration.FeedMaterials _materials = new VGFXIntegration.FeedMaterials();
        private readonly Plane[] _planes = new Plane[6];

        private Camera _camera;
        private DepthTextureMode _savedDepthMode;
        private float _volFar;
        private bool _timed;
        private string _skipReason = "not rendered yet";
        private int _lowWidth;
        private int _lowHeight;

        public string State => _skipReason ?? $"rendered, low-res pass {_lowWidth}x{_lowHeight}";

        public VGFXFeed(int instanceId)
        {
            _instanceId = instanceId;
            if (_hooked) return;
            Camera.onPreRender += AttachToRendering;
            _hooked = true;
        }

        public bool Begin(Camera camera)
        {
            var core = VGFXIntegration.Current;
            if (core == null || camera == null || !core.IsUsable)
            {
                _skipReason = "renderer inactive";
                return false;
            }

            if (!CloudInView(core, camera))
            {
                _skipReason = "nothing in view";
                return false;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int divisor = Mathf.Max(core.MainDivisor, MinDivisor);

            try
            {
                core.RecordFeed(camera, _materials, _volFar, width, height, divisor, _ground, _water);
            }
            catch (Exception ex)
            {
                _skipReason = "failed: " + ex.Message;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogError($"[JRTI-VGFX]: Feed render failed: {ex}");
                }
                return false;
            }

            _timeGroundFx.Clear();
            _timeNear.Clear();
            GpuPerf.Mark(_timeGroundFx, _instanceId, GpuSection.GroundFx);
            GpuPerf.Mark(_timeNear, _instanceId, GpuSection.Near);
            _timed = _timeGroundFx.sizeInBytes > 0;

            _savedDepthMode = camera.depthTextureMode;
            camera.depthTextureMode |= DepthTextureMode.Depth;

            _camera = camera;
            _rendering = this;
            _skipReason = null;
            _lowWidth = width / divisor;
            _lowHeight = height / divisor;
            return true;
        }

        public void End()
        {
            if (_rendering == this)
                _rendering = null;

            if (_camera == null)
                return;

            Detach(_camera, GroundEvent, _ground);
            Detach(_camera, WaterEvent, _water);
            _camera.depthTextureMode = _savedDepthMode;
            _camera = null;
        }

        public void Dispose()
        {
            End();
            _ground.Release();
            _water.Release();
            _timeGroundFx.Release();
            _timeNear.Release();
            _materials.Destroy();
        }

        private static void AttachToRendering(Camera camera)
        {
            var feed = _rendering;
            if (feed == null || camera != feed._camera) return;

            feed.Attach(camera, GroundEvent, feed._ground);
            feed.Attach(camera, WaterEvent, feed._water);
        }

        private void Attach(Camera camera, CameraEvent evt, CommandBuffer buffer)
        {
            if (buffer.sizeInBytes == 0) return;

            Detach(camera, evt, buffer);
            if (_timed) camera.AddCommandBuffer(evt, _timeGroundFx);
            camera.AddCommandBuffer(evt, buffer);
            if (_timed) camera.AddCommandBuffer(evt, _timeNear);
        }

        private void Detach(Camera camera, CameraEvent evt, CommandBuffer buffer)
        {
            camera.RemoveCommandBuffer(evt, buffer);
            camera.RemoveCommandBuffer(evt, _timeGroundFx);
            camera.RemoveCommandBuffer(evt, _timeNear);
        }

        private bool CloudInView(VGFXIntegration.VGFXCore core, Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            Vector3 origin = camera.transform.position;
            float minAngularSize = MinCloudPixels * 2f * Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad)
                / Mathf.Max(1, camera.pixelHeight);

            bool visible = false;
            _volFar = 1f;

            for (int slot = 0; slot < core.SlotCount; slot++)
            {
                if (!core.TryGetCloud(slot, out var center, out float radius)) continue;
                _volFar = Mathf.Max(_volFar, Vector3.Distance(origin, center) + radius);
                visible |= InView(origin, center, radius, minAngularSize);
            }

            for (int i = 0; i < core.MarkCount && !visible; i++)
            {
                core.GetMark(i, out var center, out float radius);
                visible = InView(origin, center, radius, minAngularSize);
            }

            return visible;
        }

        private bool InView(Vector3 origin, Vector3 center, float radius, float minAngularSize)
        {
            float distance = Vector3.Distance(origin, center);
            if (distance > radius && 2f * radius < distance * minAngularSize)
                return false;

            return GeometryUtility.TestPlanesAABB(_planes, new Bounds(center, Vector3.one * (2f * radius)));
        }
    }
}
