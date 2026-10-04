using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace JustReadTheInstructions
{
    internal sealed class Mp4Recorder : IAudioSink, IDisposable
    {
        private const int MaxQueuedFrames = 8;
        private const int MaxQueuedAudioBlocks = 50;
        private const double MaxRepeatedGapSeconds = 2;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

        private struct QueuedFrame
        {
            public object Frame;
            public byte[] Audio;
            public long Ticks;
        }

        public string FilePath { get; }
        public int FramesWritten => _framesWritten;
        public int FramesDropped => _framesDropped;
        public string Error => _error;
        public bool IsPaused => Interlocked.Read(ref _pausedAt) != 0;
        public bool IsOpen => _stopping == 0 && _error == null;
        public string EncoderDescription => _encoder?.Description ?? "";

        private readonly int _fps;
        private readonly int _frameBytes;
        private readonly Func<IVideoEncoder> _createEncoder;
        private readonly Action<string> _log;
        private readonly BlockingCollection<QueuedFrame> _queue = new BlockingCollection<QueuedFrame>();
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim(false);
        private readonly Thread _thread;
        private volatile IVideoEncoder _encoder;
        private volatile bool _recordsAudio;
        private int _queuedFrames;
        private int _queuedAudioBlocks;
        private volatile string _error;
        private volatile bool _discard;
        private int _stopping;
        private int _framesWritten;
        private int _framesDropped;
        private long _pausedTicks;
        private long _pausedAt;

        public Mp4Recorder(string filePath, int width, int height, int fps, Func<IVideoEncoder> createEncoder, Action<string> log)
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

            if (Volatile.Read(ref _queuedFrames) >= MaxQueuedFrames)
            {
                Interlocked.Increment(ref _framesDropped);
                return;
            }

            object frame = null;
            try
            {
                frame = encoder.CopyFrame(bottomUpRgba);
                Interlocked.Increment(ref _queuedFrames);
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

        public void Push(AudioBlock block)
        {
            if (!_recordsAudio || !IsOpen || IsPaused) return;
            if (Volatile.Read(ref _queuedAudioBlocks) >= MaxQueuedAudioBlocks) return;

            Interlocked.Increment(ref _queuedAudioBlocks);
            try { _queue.Add(new QueuedFrame { Audio = block.Pcm, Ticks = block.Ticks - Interlocked.Read(ref _pausedTicks) }); }
            catch (InvalidOperationException) { }
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
            IVideoEncoder encoder = null;
            object previous = null;
            object current = null;
            try
            {
                encoder = _createEncoder();
                _encoder = encoder;
                var audio = encoder.Audio != null ? new RecordingAudioTrack(encoder.Audio) : null;
                _recordsAudio = audio != null;
                _started.Set();

                var clock = new ConstantRateClock(_fps, MaxRepeatedGapSeconds);
                var audioBeforeFirstFrame = new List<QueuedFrame>();
                long lastFrame = -1;
                foreach (var queued in _queue.GetConsumingEnumerable())
                {
                    if (queued.Audio != null)
                    {
                        Interlocked.Decrement(ref _queuedAudioBlocks);
                        if (lastFrame >= 0)
                        {
                            WriteAudio(audio, clock, queued);
                            continue;
                        }
                        if (audioBeforeFirstFrame.Count == MaxQueuedAudioBlocks) audioBeforeFirstFrame.RemoveAt(0);
                        audioBeforeFirstFrame.Add(queued);
                        continue;
                    }

                    Interlocked.Decrement(ref _queuedFrames);
                    current = queued.Frame;
                    if (clock.TryPlace(queued.Ticks, out long repeats, out long frameIndex))
                    {
                        if (lastFrame < 0 && audio != null) StartAudio(audio, clock, audioBeforeFirstFrame, frameIndex);
                        for (long repeat = repeats; repeat > 0; repeat--)
                            EncodeFrame(encoder, audio, clock, previous, frameIndex - repeat);
                        EncodeFrame(encoder, audio, clock, current, frameIndex);
                        Interlocked.Increment(ref _framesWritten);
                        lastFrame = frameIndex;
                    }
                    else
                    {
                        Interlocked.Increment(ref _framesDropped);
                    }

                    if (previous != null) encoder.ReleaseFrame(previous);
                    previous = current;
                    current = null;
                }

                if (lastFrame >= 0) audio?.FillUntil(clock.FrameEndSeconds(lastFrame));
                encoder.Finish();
                return true;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _started.Set();
                Stop();
                while (_queue.TryTake(out var queued))
                    if (queued.Frame != null) encoder?.ReleaseFrame(queued.Frame);
                return false;
            }
            finally
            {
                if (current != null) encoder?.ReleaseFrame(current);
                if (previous != null) encoder?.ReleaseFrame(previous);
                encoder?.Dispose();
            }
        }

        private static void EncodeFrame(IVideoEncoder encoder, RecordingAudioTrack audio, ConstantRateClock clock, object frame, long frameIndex)
        {
            audio?.KeepUpWith(clock.FrameEndSeconds(frameIndex));
            encoder.Encode(frame, frameIndex);
        }

        private static void StartAudio(RecordingAudioTrack audio, ConstantRateClock clock, List<QueuedFrame> early, long firstFrame)
        {
            foreach (var queued in early) WriteAudio(audio, clock, queued);
            early.Clear();
            audio.FillUntil(clock.FrameEndSeconds(firstFrame));
        }

        private static void WriteAudio(RecordingAudioTrack audio, ConstantRateClock clock, QueuedFrame queued)
        {
            if (clock.TryGetSeconds(queued.Ticks, out double seconds)) audio.Write(queued.Audio, seconds);
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
