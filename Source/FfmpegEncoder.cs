using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace JustReadTheInstructions
{
    internal sealed class FfmpegEncoder : IVideoEncoder
    {
        private const int FinishTimeoutMs = 60_000;
        private const int ErrorTailChars = 2000;

        private readonly Process _process;
        private readonly Stream _input;
        private readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private readonly StringBuilder _errors = new StringBuilder();
        private readonly int _frameBytes;

        public string Description { get; }

        public FfmpegEncoder(string executable, string arguments, string description, int frameBytes)
        {
            Description = description;
            _frameBytes = frameBytes;
            _process = Ffmpeg.Start(executable, arguments, redirectInput: true);
            _process.ErrorDataReceived += (sender, e) => RememberError(e.Data);
            _process.BeginErrorReadLine();
            _input = _process.StandardInput.BaseStream;
        }

        public object CopyFrame(byte[] bottomUpRgba)
        {
            if (!_pool.TryTake(out var frame)) frame = new byte[_frameBytes];
            Buffer.BlockCopy(bottomUpRgba, 0, frame, 0, _frameBytes);
            return frame;
        }

        public void Encode(object frame, long frameIndex)
        {
            try { _input.Write((byte[])frame, 0, _frameBytes); }
            catch (IOException) { throw new InvalidOperationException($"ffmpeg stopped: {ErrorTail()}"); }
        }

        public void ReleaseFrame(object frame) => _pool.Add((byte[])frame);

        public void Finish()
        {
            _input.Close();
            if (!_process.WaitForExit(FinishTimeoutMs))
                throw new TimeoutException("ffmpeg did not finish writing the recording in time");
            _process.WaitForExit();
            if (_process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg exited with code {_process.ExitCode}: {ErrorTail()}");
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill();
            }
            catch (InvalidOperationException) { }
            _process.Dispose();
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
