using Android.OS;

namespace JianghuGo.Android
{
    /// <summary>
    /// The battle screens' clock: real time scaled by the chosen speed (×1/×2/×3), so every animation, step and
    /// pause of a fight speeds up together. Changing the speed rebases the clock so nothing jumps.
    /// </summary>
    public static class BattleClock
    {
        private static float _speed = 1f;
        private static long _baseVirtual;
        private static long _baseReal = SystemClock.UptimeMillis();

        public static float Speed
        {
            get => _speed;
            set
            {
                var real = SystemClock.UptimeMillis();
                _baseVirtual = Now;
                _baseReal = real;
                _speed = value < 1f ? 1f : value > 3f ? 3f : value;
            }
        }

        public static long Now => _baseVirtual + (long)((SystemClock.UptimeMillis() - _baseReal) * _speed);

        /// <summary>A virtual duration in real milliseconds (for Task.Delay between turns).</summary>
        public static int Real(long virtualMs) => (int)(virtualMs / _speed);
    }
}
