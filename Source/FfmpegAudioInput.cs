using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace JustReadTheInstructions
{
    internal sealed class FfmpegAudioInput : IAudioEncoder, IDisposable
    {
        private readonly TcpListener _listener;
        private readonly BlockingCollection<byte[]> _blocks = new BlockingCollection<byte[]>();
        private readonly Thread _thread;

        public string Url { get; }

        public FfmpegAudioInput()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            Url = $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _thread = new Thread(Run) { IsBackground = true, Name = "JRTI-FfmpegAudio" };
            _thread.Start();
        }

        public void EncodeAudio(byte[] pcm, int offset, int frames, long firstFrame)
        {
            var copy = new byte[frames * CameraAudioMixer.BytesPerFrame];
            Buffer.BlockCopy(pcm, offset, copy, 0, copy.Length);
            try { _blocks.Add(copy); }
            catch (InvalidOperationException) { }
        }

        public void Complete() => _blocks.CompleteAdding();

        public void Dispose()
        {
            _blocks.CompleteAdding();
            _listener.Stop();
        }

        private void Run()
        {
            try
            {
                using (var client = _listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    foreach (var block in _blocks.GetConsumingEnumerable())
                        stream.Write(block, 0, block.Length);
                }
            }
            catch (Exception) { }
            finally
            {
                _blocks.CompleteAdding();
                _listener.Stop();
            }
        }
    }
}
