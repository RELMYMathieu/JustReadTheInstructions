using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace JustReadTheInstructions
{
    internal sealed class Mp4Recorder : IDisposable
    {
        private const int MaxQueuedFrames = 8;
        private const double MaxRepeatedGapSeconds = 2;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

        private struct QueuedFrame
        {
            public object Frame;
            public long Ticks;
        }

        public string FilePath { get; }
        public int FramesWritten => _framesWritten;
        public int FramesDropped => _framesDropped;
        public string Error => _error;
        public bool IsPaused => Interlocked.Read(ref _pausedAt) != 0;
        public string EncoderDescription => _encoder?.Description ?? "";

        private readonly int _fps;
        private readonly int _frameBytes;
        private readonly Func<IH264Encoder> _createEncoder;
        private readonly Action<string> _log;
        private readonly BlockingCollection<QueuedFrame> _queue = new BlockingCollection<QueuedFrame>();
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim(false);
        private readonly Thread _thread;
        private volatile IH264Encoder _encoder;
        private volatile string _error;
        private volatile bool _discard;
        private int _stopping;
        private int _framesWritten;
        private int _framesDropped;
        private long _pausedTicks;
        private long _pausedAt;

        public Mp4Recorder(string filePath, int width, int height, int fps, Func<IH264Encoder> createEncoder, Action<string> log)
        {
            FilePath = filePath;
            _fps = fps;
            _frameBytes = width * height * 4;
            _createEncoder = createEncoder;
            _log = log;

            _thread = new Thread(Run) { IsBackground = true, Name = "JRTI-Mp4Recorder" };
            _thread.Start();

            if (!_started.Wait(StartTimeout))
                _error = "The video encoder did not start in time";
            if (_error != null)
            {
                Stop();
                throw new InvalidOperationException(_error);
            }
        }

        public void Write(byte[] bottomUpRgba, long timestamp)
        {
            var encoder = _encoder;
            if (encoder == null || _stopping != 0 || _error != null || IsPaused || bottomUpRgba.Length != _frameBytes) return;

            if (_queue.Count >= MaxQueuedFrames)
            {
                Interlocked.Increment(ref _framesDropped);
                return;
            }

            object frame = null;
            try
            {
                frame = encoder.CopyFrame(bottomUpRgba);
                _queue.Add(new QueuedFrame { Frame = frame, Ticks = timestamp - Interlocked.Read(ref _pausedTicks) });
            }
            catch (InvalidOperationException) when (_stopping != 0)
            {
                if (frame != null) encoder.ReleaseFrame(frame);
            }
            catch (Exception ex)
            {
                if (frame != null) encoder.ReleaseFrame(frame);
                _error = ex.Message;
                Stop();
            }
        }

        public void Pause() => Interlocked.CompareExchange(ref _pausedAt, Stopwatch.GetTimestamp(), 0);

        public void Resume()
        {
            long pausedAt = Interlocked.Exchange(ref _pausedAt, 0);
            if (pausedAt != 0) Interlocked.Add(ref _pausedTicks, Stopwatch.GetTimestamp() - pausedAt);
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0)
                _queue.CompleteAdding();
        }

        public void Discard()
        {
            _discard = true;
            Stop();
        }

        public bool WaitUntilFinished(TimeSpan timeout) => _thread.Join(timeout);

        public void Dispose() => Stop();

        private void Run()
        {
            bool finished = WriteRecording();
            if (_discard || _framesWritten == 0) DeleteFile();
            else if (finished) MakeSeekable();
        }

        private bool WriteRecording()
        {
            IH264Encoder encoder = null;
            object previous = null;
            object current = null;
            try
            {
                encoder = _createEncoder();
                _encoder = encoder;
                _started.Set();

                var clock = new ConstantRateClock(_fps, MaxRepeatedGapSeconds);
                foreach (var queued in _queue.GetConsumingEnumerable())
                {
                    current = queued.Frame;
                    if (clock.TryPlace(queued.Ticks, out long repeats, out long frameIndex))
                    {
                        for (long repeat = repeats; repeat > 0; repeat--)
                            encoder.Encode(previous, frameIndex - repeat);
                        encoder.Encode(current, frameIndex);
                        Interlocked.Increment(ref _framesWritten);
                    }
                    else
                    {
                        Interlocked.Increment(ref _framesDropped);
                    }

                    if (previous != null) encoder.ReleaseFrame(previous);
                    previous = current;
                    current = null;
                }

                encoder.Finish();
                return true;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _started.Set();
                Stop();
                while (_queue.TryTake(out var queued))
                    encoder?.ReleaseFrame(queued.Frame);
                return false;
            }
            finally
            {
                if (current != null) encoder?.ReleaseFrame(current);
                if (previous != null) encoder?.ReleaseFrame(previous);
                encoder?.Dispose();
            }
        }

        private void MakeSeekable()
        {
            try { Mp4Remuxer.MakeProgressive(FilePath); }
            catch (Exception ex) { _log($"Kept the fragmented MP4, which plays but may scrub poorly: {ex.Message}"); }
        }

        private void DeleteFile()
        {
            try { File.Delete(FilePath); }
            catch (Exception ex) { _log($"Could not delete discarded recording: {ex.Message}"); }
        }
    }
}
