using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace JustReadTheInstructions
{
    internal sealed class FfmpegEncoder : IVideoEncoder
    {
        private const int FinishTimeoutMs = 60_000;
        private const int ErrorTailChars = 2000;

        private readonly Process _process;
        private readonly Stream _input;
        private readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private readonly Dictionary<byte[], int> _pendingWrites = new Dictionary<byte[], int>();
        private readonly HashSet<byte[]> _releasedWhilePending = new HashSet<byte[]>();
        private readonly BlockingCollection<byte[]> _writes;
        private readonly Thread _writer;
        private readonly StringBuilder _errors = new StringBuilder();
        private readonly int _frameBytes;
        private readonly FfmpegAudioInput _audio;

        public string Description { get; }

        public IAudioEncoder Audio => _audio;

        public FfmpegEncoder(string executable, string arguments, string description, int frameBytes, int maxQueuedFrames, FfmpegAudioInput audio)
        {
            Description = description;
            _frameBytes = frameBytes;
            _audio = audio;
            _writes = new BlockingCollection<byte[]>(maxQueuedFrames);
            _process = Ffmpeg.Start(executable, arguments, redirectInput: true);
            _process.ErrorDataReceived += (sender, e) => RememberError(e.Data);
            _process.BeginErrorReadLine();
            _input = _process.StandardInput.BaseStream;
            _writer = new Thread(WriteFrames) { IsBackground = true, Name = "JRTI-FfmpegVideo" };
            _writer.Start();
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
            try { _writes.Add(buffer); }
            catch (InvalidOperationException) { throw new InvalidOperationException($"ffmpeg stopped: {ErrorTail()}"); }
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

        public void Finish()
        {
            _audio.Complete();
            _writes.CompleteAdding();
            if (!_writer.Join(FinishTimeoutMs))
                throw new TimeoutException("ffmpeg did not take the last frames in time");
            _input.Close();
            if (!_process.WaitForExit(FinishTimeoutMs))
                throw new TimeoutException("ffmpeg did not finish writing the recording in time");
            _process.WaitForExit();
            if (_process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg exited with code {_process.ExitCode}: {ErrorTail()}");
        }

        public void Dispose()
        {
            _writes.CompleteAdding();
            try
            {
                if (!_process.HasExited) _process.Kill();
            }
            catch (InvalidOperationException) { }
            _process.Dispose();
            _audio.Dispose();
        }

        private void WriteFrames()
        {
            try
            {
                foreach (var frame in _writes.GetConsumingEnumerable())
                {
                    _input.Write(frame, 0, _frameBytes);
                    FinishWrite(frame);
                }
            }
            catch (Exception) { _writes.CompleteAdding(); }
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

        private void RememberError(string line)
        {
            if (line == null) return;
            lock (_errors)
            {
                _errors.AppendLine(line);
                if (_errors.Length > ErrorTailChars) _errors.Remove(0, _errors.Length - ErrorTailChars);
            }
        }

        private string ErrorTail()
        {
            lock (_errors) return _errors.ToString().Trim();
        }
    }
}
