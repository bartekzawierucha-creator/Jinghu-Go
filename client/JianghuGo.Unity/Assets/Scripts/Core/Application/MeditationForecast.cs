using System;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// Display-only projection of a meditation session from the server's state and rate: how full the current stage is
    /// including the Qi a running session will credit, and how much meditation is still needed to reach 100%.
    /// The server computes the real amount when the session is finished.
    /// </summary>
    public sealed class MeditationForecast
    {
        private MeditationForecast()
        {
        }

        /// <summary>Qi credited so far by the running session (0 when not meditating).</summary>
        public long PendingQi { get; private set; }

        /// <summary>Minutes counted so far in the running session (capped at the session maximum).</summary>
        public int SessionMinutes { get; private set; }

        /// <summary>0..1 fill of the current stage including pending Qi.</summary>
        public double Progress { get; private set; }

        /// <summary>Meditation minutes still needed for 100% at the current rate (0 when full; -1 when impossible).</summary>
        public int MinutesToFull { get; private set; }

        /// <summary>True when today's meditation cap stops the stage from filling today.</summary>
        public bool LimitedByDailyCap { get; private set; }

        /// <summary>True when the running session reached its maximum length and should be finished.</summary>
        public bool SessionFull { get; private set; }

        public static MeditationForecast For(CultivationDto s, DateTimeOffset now)
        {
            var f = new MeditationForecast();
            if (s == null) return f;
            var rate = Math.Max(0, s.MeditationQiPerMinute);
            // cultivation-3 has no daily cap (cap 0): the vessel is the only limit
            var dailyLeft = s.DailyMeditationQiCap > 0 ? Math.Max(0, s.DailyMeditationQiCap - s.DailyMeditationQi)
                : s.Vessel > 0 ? Math.Max(0, s.Vessel - s.Qi) : long.MaxValue;
            if (s.IsMeditating && s.MeditationStartedAt.HasValue)
            {
                var elapsed = (int)Math.Floor(Math.Max(0, (now - s.MeditationStartedAt.Value).TotalMinutes));
                var max = s.MeditationMaxMinutes > 0 ? s.MeditationMaxMinutes : int.MaxValue;
                f.SessionMinutes = Math.Min(elapsed, max);
                f.SessionFull = elapsed >= max;
                // the flow up to ReceivedAt is already in s.Qi (cultivation-3 settles on every read)
                var from = s.ReceivedAt.HasValue && s.ReceivedAt.Value > s.MeditationStartedAt.Value ? s.ReceivedAt.Value : s.MeditationStartedAt.Value;
                var fresh = (int)Math.Floor(Math.Max(0, (now - from).TotalMinutes));
                f.PendingQi = f.SessionMinutes < s.MeditationMinMinutes ? 0 : Math.Min((long)Math.Floor(Math.Min(fresh, f.SessionMinutes) * rate), dailyLeft);
            }

            if (s.QiRequired <= 0)
            {
                f.Progress = 1;
                return f;
            }

            var have = s.Qi + f.PendingQi;
            f.Progress = Math.Min(1.0, have / (double)s.QiRequired);
            var missing = s.QiRequired - have;
            if (missing <= 0) return f;
            f.LimitedByDailyCap = s.DailyMeditationQiCap > 0 && missing > dailyLeft - f.PendingQi;
            f.MinutesToFull = rate <= 0 ? -1 : (int)Math.Ceiling(missing / rate);
            return f;
        }

        /// <summary>"1 h 05 min", "12 min", "< 1 min".</summary>
        public static string FormatMinutes(int minutes)
        {
            if (minutes < 1) return "< 1 min";
            if (minutes < 60) return minutes + " min";
            return minutes / 60 + " h " + (minutes % 60).ToString("00") + " min";
        }
    }
}
