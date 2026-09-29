using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        internal sealed class LatestFrameSlot : IDisposable
        {
            private byte[] _frame;
            private readonly ManualResetEventSlim _signal;
            private volatile bool _disposed;

            public LatestFrameSlot() : this(new ManualResetEventSlim(false)) { }

            public LatestFrameSlot(ManualResetEventSlim sharedSignal) => _signal = sharedSignal;

            public bool IsDisposed => _disposed;

            public void Push(byte[] jpeg)
            {
                if (_disposed) return;
                Interlocked.Exchange(ref _frame, jpeg);
                _signal.Set();
            }

            public byte[] Take(int timeoutMs)
            {
                if (_disposed) return null;
                if (!_signal.Wait(timeoutMs)) return null;
                _signal.Reset();
                return TakeReady();
            }

            public byte[] TakeReady() => Interlocked.Exchange(ref _frame, null);

            public void Dispose()
            {
                _disposed = true;
                _signal.Set();
            }
        }

        internal readonly struct CapturedFrame
        {
            public readonly byte[] Raw;
            public readonly int Width;
            public readonly int Height;
            public readonly bool Rgba;
            public readonly long CapturedAt;
            public readonly long Sequence;

            public CapturedFrame(byte[] raw, int width, int height, bool rgba, long capturedAt, long sequence)
            {
                Raw = raw;
                Width = width;
                Height = height;
                Rgba = rgba;
                CapturedAt = capturedAt;
                Sequence = sequence;
            }

            public GraphicsFormat Format => Rgba ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R8G8B8_UNorm;
        }

        internal sealed class CameraStreamState : IDisposable
        {
            public readonly int FrameWidth;
            public readonly int FrameHeight;

            public CameraStreamState(int frameWidth, int frameHeight)
            {
                FrameWidth = frameWidth;
                FrameHeight = frameHeight;
            }

            public byte[] LatestJpeg;
            public readonly object JpegLock = new object();

            public volatile string DisplayName;
            public float Fov;
            public float FovMin;
            public float FovMax;
            private volatile bool _hasFov;
            public bool HasFov => _hasFov;

            private float _pendingFov;
            private volatile bool _hasPendingFov;

            public void PublishInfo(string name, float fov, float min, float max)
            {
                DisplayName = name;
                Fov = fov;
                FovMin = min;
                FovMax = max;
                _hasFov = max > min;
            }

            public void SetPendingFov(float fov)
            {
                _pendingFov = fov;
                _hasPendingFov = true;
            }

            public bool TryTakePendingFov(out float fov)
            {
                if (!_hasPendingFov) { fov = 0f; return false; }
                fov = _pendingFov;
                _hasPendingFov = false;
                return true;
            }

            public float Brightness;
            public float Contrast = 1f;
            public float Gamma = 1f;

            private byte[] _lut;
            private float _lutBrightness;
            private float _lutContrast = -1f;
            private float _lutGamma;

            public bool HasAdjustment => Brightness != 0f || Contrast != 1f || Gamma != 1f;

            public byte[] GetLut()
            {
                if (_lut != null && _lutBrightness == Brightness && _lutContrast == Contrast && _lutGamma == Gamma)
                    return _lut;
                _lut = CameraImageAdjust.BuildLut(Brightness, Contrast, Gamma);
                _lutBrightness = Brightness;
                _lutContrast = Contrast;
                _lutGamma = Gamma;
                return _lut;
            }

            private volatile bool _snapshotPending;

            public readonly ConcurrentDictionary<Guid, LatestFrameSlot> MjpegClients
                = new ConcurrentDictionary<Guid, LatestFrameSlot>();

            public readonly ConcurrentDictionary<Guid, LatestFrameSlot> PreviewClients
                = new ConcurrentDictionary<Guid, LatestFrameSlot>();

            public int MjpegClientCount => MjpegClients.Count;

            public bool NeedsJpeg
                => MjpegClients.Count > 0 || PreviewClients.Count > 0 || _snapshotPending;

            public bool HasActiveClients => NeedsJpeg || Recorder != null;

            private Mp4Recorder _recorder;
            public readonly object RecordingLock = new object();
            public DateTime RecordingStartedUtc;

            public Mp4Recorder Recorder => Volatile.Read(ref _recorder);

            public void SetRecorder(Mp4Recorder recorder)
            {
                RecordingStartedUtc = DateTime.UtcNow;
                Volatile.Write(ref _recorder, recorder);
            }

            public Mp4Recorder TakeRecorder() => Interlocked.Exchange(ref _recorder, null);

            public void MarkSnapshotInterest() => _snapshotPending = true;

            private const int MaxCapturesInFlight = 2;
            private int _capturesInFlight;
            private long _nextCaptureSequence;
            private long _lastPublishedSequence = -1;
            private readonly FrameSchedule _captureSchedule = new FrameSchedule();
            private readonly Stack<byte[]> _freeFrameBuffers = new Stack<byte[]>();
            private Texture2D _readbackTexture;

            public float CaptureOverdue(float now)
                => Volatile.Read(ref _capturesInFlight) >= MaxCapturesInFlight
                    ? float.NegativeInfinity
                    : _captureSchedule.Overdue(now);

            public long BeginCapture(float now, float period, bool rephase)
            {
                Interlocked.Increment(ref _capturesInFlight);
                _captureSchedule.Advance(now, period, rephase);
                return _nextCaptureSequence++;
            }

            public void EndCapture(byte[] frameBuffer)
            {
                if (frameBuffer != null)
                    lock (_freeFrameBuffers) _freeFrameBuffers.Push(frameBuffer);
                Interlocked.Decrement(ref _capturesInFlight);
            }

            public byte[] RentFrameBuffer(int size)
            {
                lock (_freeFrameBuffers)
                {
                    while (_freeFrameBuffers.Count > 0)
                    {
                        var buffer = _freeFrameBuffers.Pop();
                        if (buffer.Length == size) return buffer;
                    }
                }
                return new byte[size];
            }

            public Texture2D GetReadbackTexture(int width, int height, TextureFormat format)
            {
                if (_readbackTexture == null || _readbackTexture.width != width || _readbackTexture.height != height
                    || _readbackTexture.format != format)
                {
                    DestroyReadbackTexture();
                    _readbackTexture = new Texture2D(width, height, format, false);
                }
                return _readbackTexture;
            }

            private void DestroyReadbackTexture()
            {
                if (_readbackTexture != null)
                    UnityEngine.Object.Destroy(_readbackTexture);
                _readbackTexture = null;
            }

            public void PushFrame(byte[] jpeg, long sequence)
            {
                _snapshotPending = false;
                lock (JpegLock)
                {
                    if (sequence < _lastPublishedSequence) return;
                    _lastPublishedSequence = sequence;
                    LatestJpeg = jpeg;
                    foreach (var kv in MjpegClients)
                        kv.Value.Push(jpeg);
                    foreach (var kv in PreviewClients)
                        kv.Value.Push(jpeg);
                }
            }

            public void Dispose()
            {
                foreach (var kv in MjpegClients)
                    kv.Value.Dispose();
                foreach (var kv in PreviewClients)
                    kv.Value.Dispose();
                StopRecordingOnClose();
                DestroyReadbackTexture();
            }

            private void StopRecordingOnClose()
            {
                var recorder = TakeRecorder();
                if (recorder == null) return;
                recorder.Stop();
                Debug.Log($"[JRTI-Stream]: In-game recording saved (camera closed): {recorder.FilePath}");
            }
        }

        internal sealed class RecordingSession : IDisposable
        {
            public string SessionId { get; }
            public string DisplayPath { get; }
            public long BytesWritten { get; private set; }
            public DateTime LastActivityUtc { get; private set; }

            private readonly FileStream _stream;
            private readonly object _writeLock = new object();
            private bool _disposed;

            private RecordingSession(string sessionId, string path, FileStream stream)
            {
                SessionId = sessionId;
                DisplayPath = path;
                _stream = stream;
                LastActivityUtc = DateTime.UtcNow;
            }

            public static RecordingSession Create(string sessionId, string path)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var finalPath = ResolveUniquePath(path);
                var stream = new FileStream(finalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536);
                return new RecordingSession(sessionId, finalPath, stream);
            }

            internal static string ResolveUniquePath(string requested)
            {
                if (!File.Exists(requested)) return requested;

                var dir = Path.GetDirectoryName(requested);
                var baseName = Path.GetFileNameWithoutExtension(requested);
                var ext = Path.GetExtension(requested);

                for (int i = 1; i < 10000; i++)
                {
                    var candidate = Path.Combine(dir, $"{baseName}_{i}{ext}");
                    if (!File.Exists(candidate)) return candidate;
                }

                return Path.Combine(dir, $"{baseName}_{Guid.NewGuid():N}{ext}");
            }

            public void AppendFromStream(Stream input)
            {
                var buffer = new byte[16 * 1024];
                lock (_writeLock)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(RecordingSession));
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        _stream.Write(buffer, 0, read);
                        BytesWritten += read;
                        Interlocked.Add(ref _recordedBytesTotal, read);
                    }
                    _stream.Flush();
                    LastActivityUtc = DateTime.UtcNow;
                }
            }

            public void Touch() => LastActivityUtc = DateTime.UtcNow;

            public void Dispose()
            {
                lock (_writeLock)
                {
                    if (_disposed) return;
                    _disposed = true;
                    try { _stream.Flush(); } catch { }
                    try { _stream.Dispose(); } catch { }
                }
            }

            public void DisposeAndDelete()
            {
                Dispose();
                try { if (File.Exists(DisplayPath)) File.Delete(DisplayPath); } catch { }
            }
        }
    }
}