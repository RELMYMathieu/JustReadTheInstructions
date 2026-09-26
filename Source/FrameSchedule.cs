namespace JustReadTheInstructions
{
    internal sealed class FrameSchedule
    {
        private float _next;

        public float Overdue(float now) => now - _next;

        public void Advance(float now, float period, bool rephase)
        {
            _next = !rephase && now - _next < period ? _next + period : now + period;
        }
    }
}
