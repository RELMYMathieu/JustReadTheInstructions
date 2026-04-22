using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        internal sealed class LatestFrameSlot : IDisposable
        {
            private byte[] _frame;
            private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);
            private volatile bool _disposed;

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
                return Interlocked.Exchange(ref _frame, null);
            }

            public void Dispose()
            {
                _disposed = true;
                _signal.Set();
            }
        }

        internal sealed class CameraStreamState : IDisposable
        {
            public byte[] LatestJpeg;
            public readonly object JpegLock = new object();

            private volatile bool _snapshotPending;

            public readonly ConcurrentDictionary<Guid, LatestFrameSlot> MjpegClients
                = new ConcurrentDictionary<Guid, LatestFrameSlot>();

            public readonly ConcurrentDictionary<Guid, LatestFrameSlot> PreviewClients
                = new ConcurrentDictionary<Guid, LatestFrameSlot>();

            public int MjpegClientCount => MjpegClients.Count;

            public bool HasActiveClients
                => MjpegClients.Count > 0 || PreviewClients.Count > 0 || _snapshotPending;

            public void MarkSnapshotInterest() => _snapshotPending = true;

            public void PushFrame(byte[] jpeg)
            {
                _snapshotPending = false;
                lock (JpegLock)
                    LatestJpeg = jpeg;
                foreach (var kv in MjpegClients)
                    kv.Value.Push(jpeg);
                foreach (var kv in PreviewClients)
                    kv.Value.Push(jpeg);
            }

            public void Dispose()
            {
                foreach (var kv in MjpegClients)
                    kv.Value.Dispose();
                foreach (var kv in PreviewClients)
                    kv.Value.Dispose();
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

            private static string ResolveUniquePath(string requested)
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
                    }
                    _stream.Flush();
                    LastActivityUtc = DateTime.UtcNow;
                }
            }

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