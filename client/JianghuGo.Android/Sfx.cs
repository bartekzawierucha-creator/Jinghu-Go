using System;
using System.Collections.Generic;
using System.IO;
using Android.Content;
using Android.Media;

namespace JianghuGo.Android
{
    /// <summary>
    /// Small synthesized sound effects (no audio assets needed): short WAVs are generated once into the cache folder
    /// and played through a SoundPool. Muting is remembered in preferences.
    /// </summary>
    public static class Sfx
    {
        public const string Hit = "hit";
        public const string Hurt = "hurt";
        public const string Victory = "victory";
        public const string Defeat = "defeat";
        public const string Chime = "chime";
        public const string Breakthrough = "breakthrough";
        public const string Coin = "coin";
        public const string Click = "click";

        internal const int Rate = 22050;
        private static SoundPool _pool;
        private static readonly Dictionary<string, int> Ids = new Dictionary<string, int>();
        private static ISharedPreferences _prefs;

        public static bool Muted
        {
            get => _prefs?.GetBoolean("jianghu.muted", false) ?? true;
            set
            {
                using (var e = _prefs.Edit())
                {
                    e.PutBoolean("jianghu.muted", value);
                    e.Apply();
                }
            }
        }

        public static void Init(Context ctx, ISharedPreferences prefs)
        {
            _prefs = prefs;
            if (_pool != null) return;
            try
            {
                var attrs = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game).SetContentType(AudioContentType.Sonification).Build();
                _pool = new SoundPool.Builder().SetMaxStreams(6).SetAudioAttributes(attrs).Build();
                var dir = Path.Combine(ctx.CacheDir.AbsolutePath, "sfx-v1");
                Directory.CreateDirectory(dir);
                foreach (var (name, samples) in Synthesize())
                {
                    var file = Path.Combine(dir, name + ".wav");
                    if (!File.Exists(file)) WriteWav(file, samples);
                    Ids[name] = _pool.Load(file, 1);
                }
            }
            catch (Exception)
            {
                _pool = null; // sound is optional: a device without audio still plays the game
            }
        }

        public static void Play(string name, float volume = 0.8f)
        {
            if (_pool == null || Muted || !Ids.TryGetValue(name, out var id)) return;
            _pool.Play(id, volume, volume, 1, 0, 1f);
        }

        private static IEnumerable<(string, short[])> Synthesize()
        {
            var rnd = new Random(7);
            yield return (Hit, Render(0.16, t => Noise(rnd) * Env(t, 0.002, 0.14) * 0.7 + Sine(160 - 300 * t, t) * Env(t, 0.002, 0.12) * 0.6));
            yield return (Hurt, Render(0.22, t => Square(110 - 120 * t, t) * Env(t, 0.004, 0.2) * 0.35 + Noise(rnd) * Env(t, 0.002, 0.08) * 0.4));
            yield return (Victory, Render(0.9, t => Notes(t, new[] { 523.25, 659.25, 783.99, 1046.5 }, 0.16) * 0.45));
            yield return (Defeat, Render(0.9, t => Notes(t, new[] { 392.0, 349.23, 311.13, 261.63 }, 0.2) * 0.4));
            yield return (Chime, Render(0.6, t => (Sine(1318.5, t) + 0.5 * Sine(1975.5, t)) * Env(t, 0.003, 0.55) * 0.35));
            yield return (Breakthrough, Render(1.6, t => (Sine(220 + 440 * t, t) * 0.4 + Sine(660 + 660 * t, t) * 0.25 + Noise(rnd) * 0.08 * (1 - t / 1.6)) * Env(t, 0.25, 1.3)));
            yield return (Coin, Render(0.3, t => Sine(t < 0.07 ? 987.8 : 1318.5, t) * Env(t, 0.002, 0.28) * 0.4));
            yield return (Click, Render(0.05, t => Sine(1800, t) * Env(t, 0.001, 0.04) * 0.25));
        }

        private static double Notes(double t, double[] freqs, double step)
        {
            var i = Math.Min((int)(t / step), freqs.Length - 1);
            var local = t - i * step;
            var tail = i == freqs.Length - 1 ? 0.9 - i * step : step;
            return (Sine(freqs[i], t) + 0.3 * Sine(freqs[i] * 2, t)) * Env(local, 0.005, tail);
        }

        private static double Sine(double f, double t) => Math.Sin(2 * Math.PI * f * t);
        private static double Square(double f, double t) => Math.Sign(Math.Sin(2 * Math.PI * f * t));
        private static double Noise(Random r) => r.NextDouble() * 2 - 1;

        /// <summary>Linear attack, exponential-ish release over <paramref name="length"/> seconds.</summary>
        private static double Env(double t, double attack, double length)
        {
            if (t < 0 || t > length) return 0;
            if (t < attack) return t / attack;
            var k = (t - attack) / Math.Max(1e-6, length - attack);
            return Math.Pow(1 - k, 2);
        }

        internal static short[] Render(double seconds, Func<double, double> f)
        {
            var n = (int)(seconds * Rate);
            var data = new short[n];
            for (var i = 0; i < n; i++) data[i] = (short)(Math.Clamp(f(i / (double)Rate), -1, 1) * 30000);
            return data;
        }

        internal static void WriteWav(string path, short[] samples)
        {
            using var w = new BinaryWriter(File.Create(path));
            var bytes = samples.Length * 2;
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + bytes);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(bytes);
            foreach (var s in samples) w.Write(s);
        }
    }
}
