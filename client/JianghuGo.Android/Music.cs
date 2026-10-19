using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Media;

namespace JianghuGo.Android
{
    /// <summary>
    /// Quiet background music per era (2.0), synthesized on the device like the sound effects: a plucked pentatonic
    /// melody over a drone, each era in its own mode and tempo. Generated once per era into the cache and looped.
    /// Off when sound is muted or music is switched off in the settings.
    /// </summary>
    public static class Music
    {
        private const string PrefKey = "jianghu.music";
        private static ISharedPreferences _prefs;
        private static Context _ctx;
        private static MediaPlayer _player;
        private static int _era = -1;
        private static bool _paused;

        public static bool Enabled
        {
            get => _prefs?.GetBoolean(PrefKey, true) ?? false;
            set
            {
                using (var e = _prefs.Edit())
                {
                    e.PutBoolean(PrefKey, value);
                    e.Apply();
                }

                if (value && !Sfx.Muted) Restart();
                else Stop();
            }
        }

        public static void Init(Context ctx, ISharedPreferences prefs)
        {
            _ctx = ctx.ApplicationContext;
            _prefs = prefs;
        }

        /// <summary>Plays the era's theme (no-op when it is already playing).</summary>
        public static void PlayEra(int era)
        {
            era = Math.Clamp(era, 0, 5);
            if (era == _era && _player != null) return;
            _era = era;
            Restart();
        }

        public static void Pause()
        {
            _paused = true;
            try { _player?.Pause(); } catch (Exception) { }
        }

        public static void Resume()
        {
            _paused = false;
            if (!Enabled || Sfx.Muted) return;
            try { _player?.Start(); } catch (Exception) { }
        }

        public static void Stop()
        {
            try
            {
                _player?.Stop();
                _player?.Release();
            }
            catch (Exception)
            {
                // audio is optional
            }

            _player = null;
        }

        private static async void Restart()
        {
            Stop();
            if (_ctx == null || _era < 0 || !Enabled || Sfx.Muted) return;
            var era = _era;
            try
            {
                var dir = Path.Combine(_ctx.CacheDir.AbsolutePath, "music-v1");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "era" + era + ".wav");
                if (!File.Exists(file)) await Task.Run(() => Sfx.WriteWav(file, Compose(era)));
                if (era != _era || !Enabled || Sfx.Muted) return;
                var p = new MediaPlayer();
                p.SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game).SetContentType(AudioContentType.Music).Build());
                p.SetDataSource(file);
                p.Looping = true;
                p.SetVolume(0.18f, 0.18f);
                p.Prepare();
                _player = p;
                if (!_paused) p.Start();
            }
            catch (Exception)
            {
                Stop();
            }
        }

        // Pentatonic modes (semitones from the root) and tempo per era: mortal calm → heavenly bright → dao slow and deep.
        private static readonly int[][] Modes =
        {
            new[] { 0, 2, 4, 7, 9 }, new[] { 0, 2, 5, 7, 10 }, new[] { 0, 3, 5, 7, 10 },
            new[] { 0, 2, 5, 7, 9 }, new[] { 0, 3, 5, 8, 10 }, new[] { 0, 2, 4, 7, 9 },
        };

        private static readonly double[] Roots = { 220.0, 196.0, 174.61, 246.94, 164.81, 130.81 };
        private static readonly double[] Beats = { 0.62, 0.55, 0.5, 0.46, 0.7, 0.9 };

        private static short[] Compose(int era)
        {
            var rnd = new Random(1000 + era);
            var mode = Modes[era];
            var root = Roots[era];
            var beat = Beats[era];
            const double Length = 32.0;
            var steps = (int)(Length / beat);
            var notes = new double[steps];
            var degree = 0;
            for (var i = 0; i < steps; i++)
            {
                degree = Math.Clamp(degree + rnd.Next(-2, 3), -2, 7);
                notes[i] = rnd.NextDouble() < 0.18 ? 0 : root * Math.Pow(2, (mode[((degree % 5) + 5) % 5] + (12 * (int)Math.Floor(degree / 5.0))) / 12.0);
            }

            return Sfx.Render(Length, t =>
            {
                var i = Math.Min((int)(t / beat), steps - 1);
                var local = t - (i * beat);
                var f = notes[i];
                // plucked string: fast attack, long decay, a little second harmonic
                var pluck = f == 0 ? 0 : (Math.Sin(2 * Math.PI * f * t) + (0.35 * Math.Sin(4 * Math.PI * f * t))) * Math.Exp(-local * 3.2) * 0.32;
                // drone on the root and fifth, breathing slowly; fades at the loop seam
                var breath = 0.75 + (0.25 * Math.Sin(2 * Math.PI * t / 8.0));
                var drone = ((Math.Sin(Math.PI * root * t) * 0.12) + (Math.Sin(Math.PI * root * 1.5 * t) * 0.07)) * breath;
                var seam = Math.Min(1, Math.Min(t, Length - t) / 1.5);
                return (pluck + drone) * seam;
            });
        }
    }
}
