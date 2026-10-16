using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal sealed class CameraAimControl
    {
        private static readonly long RateHoldTicks = TimeSpan.FromMilliseconds(600).Ticks;

        private readonly object _lock = new object();
        private bool _canPan;
        private float _yaw, _pitch, _pitchMin, _pitchMax;
        private CameraTrack _track;
        private bool _hasTarget;
        private float? _targetYaw, _targetPitch;
        private float _yawRate, _pitchRate, _zoomRate;
        private long _panRateUntil, _zoomRateUntil;

        public bool CanPan { get { lock (_lock) return _canPan; } }

        public CameraTrack Track { get { lock (_lock) return _track; } }

        public void SetTrack(CameraTrack track)
        {
            lock (_lock) _track = track;
        }

        public void SetTarget(float? yaw, float? pitch)
        {
            lock (_lock)
            {
                _targetYaw = yaw ?? (_hasTarget ? _targetYaw : null);
                _targetPitch = pitch ?? (_hasTarget ? _targetPitch : null);
                _hasTarget = true;
                _track = CameraTrack.Off;
            }
        }

        public void SetPanRate(float? yawRate, float? pitchRate)
        {
            lock (_lock)
            {
                _yawRate = Mathf.Clamp(yawRate ?? _yawRate, -1f, 1f);
                _pitchRate = Mathf.Clamp(pitchRate ?? _pitchRate, -1f, 1f);
                _panRateUntil = DateTime.UtcNow.Ticks + RateHoldTicks;
                if (_yawRate != 0f || _pitchRate != 0f)
                    _track = CameraTrack.Off;
            }
        }

        public void SetZoomRate(float zoomRate)
        {
            lock (_lock)
            {
                _zoomRate = Mathf.Clamp(zoomRate, -1f, 1f);
                _zoomRateUntil = DateTime.UtcNow.Ticks + RateHoldTicks;
            }
        }

        public bool TryTakeTarget(out float? yaw, out float? pitch)
        {
            lock (_lock)
            {
                yaw = _targetYaw;
                pitch = _targetPitch;
                bool had = _hasTarget;
                _hasTarget = false;
                _targetYaw = _targetPitch = null;
                return had;
            }
        }

        public void GetRates(out float yawRate, out float pitchRate, out float zoomRate)
        {
            lock (_lock)
            {
                long now = DateTime.UtcNow.Ticks;
                bool panning = now < _panRateUntil;
                yawRate = panning ? _yawRate : 0f;
                pitchRate = panning ? _pitchRate : 0f;
                zoomRate = now < _zoomRateUntil ? _zoomRate : 0f;
            }
        }

        public void Publish(CameraPan pan)
        {
            lock (_lock)
            {
                _canPan = pan != null;
                if (pan == null) return;
                _yaw = Mathf.DeltaAngle(0f, pan.Yaw);
                _pitch = pan.Pitch;
                _pitchMin = pan.PitchMin;
                _pitchMax = pan.PitchMax;
            }
        }

        public void AppendJson(StringBuilder sb)
        {
            var ic = CultureInfo.InvariantCulture;
            lock (_lock)
            {
                if (!_canPan) return;
                sb.Append($",\"pan\":{{\"yaw\":{_yaw.ToString("F1", ic)},\"pitch\":{_pitch.ToString("F1", ic)},")
                  .Append($"\"pitchMin\":{_pitchMin.ToString("F0", ic)},\"pitchMax\":{_pitchMax.ToString("F0", ic)},")
                  .Append($"\"track\":\"{CameraTracks.Id(_track)}\"}}");
            }
        }
    }
}
