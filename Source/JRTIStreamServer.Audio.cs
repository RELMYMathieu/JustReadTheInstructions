using System.Net;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static readonly byte[] AudioStreamHeader = CameraAudioMixer.WavStreamHeader();

        private void ServeCameraAudio(HttpListenerContext ctx, int cameraId, CameraStreamState state)
        {
            var audio = CameraAudio.Instance;
            if (audio == null)
            {
                ServeError(ctx, 503, "Camera audio is not running");
                return;
            }

            ctx.Response.ContentType = "audio/wav";
            ctx.Response.SendChunked = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");

            var client = new AudioClient();
            audio.Subscribe(cameraId, client);

            try
            {
                var output = ctx.Response.OutputStream;
                output.Write(AudioStreamHeader, 0, AudioStreamHeader.Length);
                AudioBlock block;
                while ((block = client.Take(StreamIdleTimeoutMs)) != null && IsOpenCamera(cameraId, state))
                {
                    output.Write(block.Pcm, 0, block.Pcm.Length);
                    output.Flush();
                }
            }
            catch { }
            finally
            {
                client.Dispose();
                try { ctx.Response.Close(); } catch { }
            }
        }

        private bool IsOpenCamera(int cameraId, CameraStreamState state)
            => _states.TryGetValue(cameraId, out var current) && current == state;
    }
}
