using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace JustReadTheInstructions
{
    internal sealed class FfmpegEncoder : IVideoEncoder, IAudioEncoder
    {
        private const int FinishTimeoutMs = 60_000;

        private readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private readonly Dictionary<byte[], int> _pendingWrites = new Dictionary<byte[], int>();
        private readonly HashSet<byte[]> _releasedWhilePending = new HashSet<byte[]>();
        private readonly int _frameBytes;
        private readonly FfmpegInput _video;
        private readonly FfmpegInput _audio;
        private readonly FfmpegProcess _process;

        public string Description { get; }

        public IAudioEncoder Audio => this;

        public FfmpegEncoder(string executable, Func<string, string, string> arguments, string description, int frameBytes, int maxQueuedFrames)
        {
            Description = description;
            _frameBytes = frameBytes;
            _video = new FfmpegInput("JRTI-FfmpegVideo", maxQueuedFrames, FinishWrite);
            _audio = new FfmpegInput("JRTI-FfmpegAudio", 0, null);
            try
            {
                _process = FfmpegProcess.Start(executable, arguments(_video.Url, _audio.Url), StopInputs);
            }
            catch
            {
                StopInputs();
                throw;
            }
        }

        public object CopyFrame(byte[] bottomUpRgba)
        {
            if (!_pool.TryTake(out var frame)) frame = new byte[_frameBytes];
            Buffer.BlockCopy(bottomUpRgba, 0, frame, 0, _frameBytes);
            return frame;
        }

        public void Encode(object frame, long frameIndex)
        {
            var buffer = (byte[])frame;
            lock (_pendingWrites)
            {
                _pendingWrites.TryGetValue(buffer, out int pending);
                _pendingWrites[buffer] = pending + 1;
            }
            try { _video.Add(buffer); }
            catch (InvalidOperationException) { throw new InvalidOperationException($"ffmpeg stopped: {_process.ErrorTail}"); }
        }

        public void ReleaseFrame(object frame)
        {
            var buffer = (byte[])frame;
            lock (_pendingWrites)
            {
                if (_pendingWrites.ContainsKey(buffer)) _releasedWhilePending.Add(buffer);
                else _pool.Add(buffer);
            }
        }

        public void EncodeAudio(byte[] pcm, int offset, int frames, long firstFrame)
        {
            var copy = new byte[frames * CameraAudioMixer.BytesPerFrame];
            Buffer.BlockCopy(pcm, offset, copy, 0, copy.Length);
            try { _audio.Add(copy); }
            catch (InvalidOperationException) { }
        }

        public void Finish()
        {
            _audio.Complete();
            _video.Complete();
            if (!_video.WaitUntilSent(FinishTimeoutMs))
                throw new TimeoutException("ffmpeg did not take the last frames in time");
            if (!_process.WaitForExit(FinishTimeoutMs))
                throw new TimeoutException("ffmpeg did not finish writing the recording in time");
            if (_process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg exited with code {_process.ExitCode}: {_process.ErrorTail}");
        }

        public void Dispose()
        {
            StopInputs();
            _process?.Dispose();
        }

        private void StopInputs()
        {
            _video.Dispose();
            _audio.Dispose();
        }

        private void FinishWrite(byte[] frame)
        {
            lock (_pendingWrites)
            {
                int pending = _pendingWrites[frame] - 1;
                if (pending > 0)
                {
                    _pendingWrites[frame] = pending;
                    return;
                }
                _pendingWrites.Remove(frame);
                if (_releasedWhilePending.Remove(frame)) _pool.Add(frame);
            }
        }
    }
}
