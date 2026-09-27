using System;

namespace JustReadTheInstructions
{
    internal sealed class RecordingAudioTrack
    {
        private const int SampleRate = CameraAudioMixer.SampleRate;
        private const int BytesPerFrame = CameraAudioMixer.BytesPerFrame;
        private const long ToleranceFrames = SampleRate / 1000;
        private const long MaxLagFrames = SampleRate / 4;
        private const int SilenceChunkFrames = SampleRate / 10;

        private static readonly byte[] Silence = new byte[SilenceChunkFrames * BytesPerFrame];

        private readonly IAudioEncoder _encoder;
        private long _framesWritten;

        public RecordingAudioTrack(IAudioEncoder encoder) => _encoder = encoder;

        public void Write(byte[] pcm, double seconds)
        {
            long target = (long)Math.Round(seconds * SampleRate);
            long frames = pcm.Length / BytesPerFrame;
            long skip = 0;

            if (target > _framesWritten + ToleranceFrames) WriteSilence(target - _framesWritten);
            else if (target < _framesWritten - ToleranceFrames) skip = _framesWritten - target;
            if (skip >= frames) return;

            _encoder.EncodeAudio(pcm, (int)(skip * BytesPerFrame), (int)(frames - skip), _framesWritten);
            _framesWritten += frames - skip;
        }

        public void KeepUpWith(double videoSeconds) => FillSilenceUntil(FrameAt(videoSeconds) - MaxLagFrames);

        public void FillUntil(double videoSeconds) => FillSilenceUntil(FrameAt(videoSeconds));

        private static long FrameAt(double seconds) => (long)Math.Round(seconds * SampleRate);

        private void FillSilenceUntil(long frame)
        {
            if (frame > _framesWritten) WriteSilence(frame - _framesWritten);
        }

        private void WriteSilence(long frames)
        {
            while (frames > 0)
            {
                int chunk = (int)Math.Min(frames, SilenceChunkFrames);
                _encoder.EncodeAudio(Silence, 0, chunk, _framesWritten);
                _framesWritten += chunk;
                frames -= chunk;
            }
        }
    }
}
