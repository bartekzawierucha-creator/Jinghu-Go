using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>
    /// The current stage's Qi vessel: the meditating hero on the left, a glowing vessel that fills as the session
    /// runs (projected from the server's rate, ticking every minute), the percentage and how much meditation is
    /// still needed for 100%. Display only: the server credits the real amount when the session ends.
    /// </summary>
    public sealed class QiChargeView : View
    {
        private readonly float _d;
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _art = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Anim _meditate;
        private readonly Anim _idle;
        private readonly Anim _gather;
        private readonly List<(float X, float Y, float V, long Born)> _motes = new List<(float, float, float, long)>();
        private readonly Random _rnd = new Random();
        private float _shown = -1;
        private long _lastForecast;

        public QiChargeView(Context ctx) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _text.SetTypeface(Ui.Display);
            _text.SetShadowLayer(4, 0, 1, Color.Black);
            _meditate = new Anim(Ui.Art(ctx, Resource.Drawable.anim_hero_meditate), 320, 6, true);
            _idle = new Anim(Ui.Art(ctx, Resource.Drawable.anim_hero_idle), 320, 6, true);
            _gather = new Anim(Ui.Art(ctx, Resource.Drawable.fx_qi_gather), 256, 7, true);
        }

        public CultivationDto State { get; set; }
        public MeditationForecast Forecast { get; private set; }

        /// <summary>Raised about once a second while meditating, so the screen can refresh its texts.</summary>
        public event Action<MeditationForecast> Ticked;

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Width == 0 || State == null) return;
            var now = SystemClock.UptimeMillis();
            if (Forecast == null || now - _lastForecast > 1000)
            {
                Forecast = MeditationForecast.For(State, DateTimeOffset.UtcNow);
                _lastForecast = now;
                Ticked?.Invoke(Forecast);
            }

            var f = Forecast;
            var meditating = State.IsMeditating;
            float w = Width, h = Height;

            // the cultivator
            var side = Math.Min(h * 0.74f, 120 * _d);
            var heroRect = new RectF(0, 0, side, side);
            if (meditating)
            {
                var g = side * 1.05f;
                _art.Alpha = 200;
                canvas.DrawBitmap(_gather.Strip, _gather.Frame(now), new RectF(side / 2 - g / 2, side * 0.62f - g / 2, side / 2 + g / 2, side * 0.62f + g / 2), _art);
                _art.Alpha = 255;
            }

            var a = meditating ? _meditate : _idle;
            canvas.DrawBitmap(a.Strip, a.Frame(now), new RectF(-side * 0.12f, 0, side * 1.12f, side * 1.0f), _art);

            // the vessel
            var left = side + 8 * _d;
            var right = w - 4 * _d;
            var top = side * 0.45f;
            var bottom = side * 0.45f + 22 * _d;
            var r = (bottom - top) / 2;
            var vessel = new RectF(left, top, right, bottom);
            _p.SetShader(null);
            _p.SetStyle(Paint.Style.Fill);
            _p.Color = Color.Argb(235, 6, 9, 12);
            canvas.DrawRoundRect(vessel, r, r, _p);

            var target = (float)f.Progress;
            _shown = _shown < 0 ? target : _shown + (target - _shown) * 0.06f;
            var fillW = (right - left - 4 * _d) * Math.Clamp(_shown, 0, 1);
            if (fillW > 1)
            {
                var fill = new RectF(left + 2 * _d, top + 2 * _d, left + 2 * _d + fillW, bottom - 2 * _d);
                _p.SetShader(new LinearGradient(0, fill.Top, 0, fill.Bottom,
                    new int[] { Color.Rgb(170, 225, 255).ToArgb(), Ui.QiBlue.ToArgb(), Color.Rgb(30, 70, 140).ToArgb() }, new float[] { 0, 0.45f, 1 }, Shader.TileMode.Clamp));
                canvas.DrawRoundRect(fill, r, r, _p);
                // flowing light bands (energy moving into the vessel)
                var save = canvas.Save();
                var clip = new Path();
                clip.AddRoundRect(fill, r, r, Path.Direction.Cw);
                canvas.ClipPath(clip);
                var speed = meditating ? 0.09f : 0.025f;
                var band = 46 * _d;
                var offset = now * speed * _d % band;
                _p.SetShader(null);
                _p.Color = Color.Argb(meditating ? 70 : 35, 255, 255, 255);
                for (var x = fill.Left - band + offset; x < fill.Right; x += band)
                {
                    var path = new Path();
                    path.MoveTo(x, fill.Bottom);
                    path.LineTo(x + 14 * _d, fill.Top);
                    path.LineTo(x + 24 * _d, fill.Top);
                    path.LineTo(x + 10 * _d, fill.Bottom);
                    path.Close();
                    canvas.DrawPath(path, _p);
                }

                canvas.RestoreToCount(save);

                // the charging edge
                if (meditating && _shown < 0.999f)
                {
                    var pulse = (float)(0.5 + 0.5 * Math.Sin(now / 160.0));
                    _p.SetShader(new RadialGradient(fill.Right, (fill.Top + fill.Bottom) / 2, (14 + 6 * pulse) * _d,
                        new int[] { Color.Argb(220, 220, 245, 255).ToArgb(), Color.Argb(0, 120, 190, 255).ToArgb() }, null, Shader.TileMode.Clamp));
                    canvas.DrawCircle(fill.Right, (fill.Top + fill.Bottom) / 2, (14 + 6 * pulse) * _d, _p);
                    _p.SetShader(null);
                    if (_rnd.NextDouble() < 0.35) _motes.Add((left + (float)_rnd.NextDouble() * fillW, fill.Top, 20 + (float)_rnd.NextDouble() * 30, now));
                }
            }

            // rising Qi motes
            _p.SetShader(null);
            _motes.RemoveAll(m => now - m.Born > 1400);
            foreach (var m in _motes)
            {
                var k = (now - m.Born) / 1400f;
                _p.Color = Color.Argb((int)(200 * (1 - k)), 170, 225, 255);
                canvas.DrawCircle(m.X + (float)Math.Sin((now - m.Born) / 200.0) * 3 * _d, m.Y - k * m.V * _d, 1.8f * _d, _p);
            }

            // rim
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 1.4f * _d;
            _p.Color = Ui.Gold;
            canvas.DrawRoundRect(vessel, r, r, _p);
            _p.SetStyle(Paint.Style.Fill);
            // quarter marks
            _p.Color = Color.Argb(120, 212, 175, 96);
            for (var q = 1; q < 4; q++)
            {
                var x = left + (right - left) * q / 4f;
                canvas.DrawRect(x - 0.6f * _d, bottom + 2 * _d, x + 0.6f * _d, bottom + 6 * _d, _p);
            }

            // texts: percentage above, numbers inside, time to 100% below
            _text.TextAlign = Paint.Align.Left;
            _text.TextSize = 22 * _d;
            _text.Color = _shown >= 0.999f ? Ui.Gold : Ui.Ink;
            canvas.DrawText((int)Math.Floor(Math.Clamp(_shown, 0, 1) * 100) + "%", left, top - 8 * _d, _text);
            _text.TextAlign = Paint.Align.Right;
            _text.TextSize = 12 * _d;
            _text.Color = Ui.Muted;
            canvas.DrawText(meditating ? "MEDYTACJA · " + f.SessionMinutes + " min" : "QI ETAPU", right, top - 9 * _d, _text);
            _text.TextAlign = Paint.Align.Center;
            _text.TextSize = 11 * _d;
            _text.Color = Ui.Ink;
            canvas.DrawText((State.Qi + f.PendingQi) + " / " + State.QiRequired + " Qi", (left + right) / 2, (top + bottom) / 2 - (_text.Descent() + _text.Ascent()) / 2, _text);
            _text.TextAlign = Paint.Align.Left;
            _text.TextSize = 13 * _d;
            string line;
            if (State.QiRequired <= 0) { line = "Szczyt królestwa"; _text.Color = Ui.Gold; }
            else if (f.Progress >= 1) { line = meditating ? "Pełne! Zakończ medytację i spróbuj przełomu" : "Gotowe do przełomu"; _text.Color = Ui.Gold; }
            else if (f.MinutesToFull < 0) { line = "Medytacja nic teraz nie daje"; _text.Color = Ui.Cinnabar; }
            else
            {
                line = "Do 100%: " + MeditationForecast.FormatMinutes(f.MinutesToFull) + " medytacji";
                _text.Color = Ui.Ink;
            }

            canvas.DrawText(line, 4 * _d, side + 22 * _d, _text);
            if (f.LimitedByDailyCap || f.SessionFull)
            {
                _text.TextSize = 11 * _d;
                _text.Color = Ui.Cinnabar;
                canvas.DrawText(f.SessionFull ? "Sesja osiągnęła limit — zakończ ją, by zebrać Qi" : "Dzienny limit medytacji nie wystarczy — reszta jutro", 4 * _d, side + 40 * _d, _text);
            }

            if (meditating || Math.Abs(_shown - target) > 0.001f || _motes.Count > 0) PostInvalidateDelayed(50);
            else PostInvalidateDelayed(500);
        }
    }
}

namespace JianghuGo.Android
{
    /// <summary>A one-shot full-screen effect drawn over the UI (dims the screen slightly, plays the strip once).</summary>
    public sealed class FxOverlayView : global::Android.Views.View
    {
        private readonly Anim _anim;
        private readonly long _start = global::Android.OS.SystemClock.UptimeMillis();
        private readonly Paint _p = new Paint(PaintFlags.FilterBitmap);

        public FxOverlayView(Context ctx, Anim anim) : base(ctx)
        {
            _anim = anim;
            Clickable = false;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var t = global::Android.OS.SystemClock.UptimeMillis() - _start;
            var fade = Math.Clamp(1f - (t - _anim.DurationMs) / 700f, 0f, 1f);
            canvas.DrawColor(Color.Argb((int)(110 * fade), 0, 0, 0));
            var size = Math.Min(Width, Height) * 1.0f;
            _p.Alpha = (int)(255 * fade);
            canvas.DrawBitmap(_anim.Strip, _anim.Frame(Math.Min(t, _anim.DurationMs - 1)), new RectF(Width / 2f - size / 2, Height * 0.42f - size / 2, Width / 2f + size / 2, Height * 0.42f + size / 2), _p);
            if (fade > 0) PostInvalidateOnAnimation();
        }
    }
}
