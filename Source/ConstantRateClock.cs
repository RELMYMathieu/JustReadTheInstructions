using System;
using System.Diagnostics;

namespace JustReadTheInstructions
{
    internal sealed class ConstantRateClock
    {
        private const double EarlyTolerance = 0.6;

        private readonly double _framesPerTick;
        private readonly long _maxRepeats;
        private bool _started;
        private long _firstTicks;
        private long _lastFrame = -1;
        private long _skippedFrames;

        public ConstantRateClock(int fps, double maxGapSeconds)
        {
            _framesPerTick = fps / (double)Stopwatch.Frequency;
            _maxRepeats = (long)(fps * maxGapSeconds);
        }

        public bool TryPlace(long ticks, out long repeats, out long frame)
        {
            if (!_started)
            {
                _started = true;
                _firstTicks = ticks;
            }

            double position = (ticks - _firstTicks) * _framesPerTick - _skippedFrames;
            long target = (long)Math.Round(position);
            repeats = 0;
            frame = _lastFrame;
            if (target <= _lastFrame)
            {
                if (position < _lastFrame + 1 - EarlyTolerance) return false;
                target = _lastFrame + 1;
            }

            long gap = target - _lastFrame - 1;
            if (gap > _maxRepeats)
            {
                _skippedFrames += gap - _maxRepeats;
                gap = _maxRepeats;
            }

            repeats = _lastFrame < 0 ? 0 : gap;
            frame = _lastFrame + repeats + 1;
            _lastFrame = frame;
            return true;
        }
    }
}
