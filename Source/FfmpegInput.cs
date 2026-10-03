using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace JustReadTheInstructions
{
    internal sealed class FfmpegInput : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly BlockingCollection<byte[]> _blocks;
        private readonly Action<byte[]> _sent;
        private readonly Thread _thread;
        private volatile TcpClient _client;

        public string Url { get; }

        public FfmpegInput(string name, int maxQueuedBlocks, Action<byte[]> sent)
        {
            _blocks = maxQueuedBlocks > 0 ? new BlockingCollection<byte[]>(maxQueuedBlocks) : new BlockingCollection<byte[]>();
            _sent = sent;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            Url = $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
        }

        public void Add(byte[] block) => _blocks.Add(block);

        public void Complete() => _blocks.CompleteAdding();

        public bool WaitUntilSent(int milliseconds) => _thread.Join(milliseconds);

        public void Dispose()
        {
            _blocks.CompleteAdding();
            _listener.Stop();
            _client?.Close();
        }

        private void Run()
        {
            try
            {
                using (var client = _listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    _client = client;
                    foreach (var block in _blocks.GetConsumingEnumerable())
                    {
                        stream.Write(block, 0, block.Length);
                        _sent?.Invoke(block);
                    }
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
