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
            _text.SetShadowLayer(4, 0, 1, Color.Argb(170, 250, 247, 240));
            _meditate = HeroArt.Load(ctx, HeroArt.CurrentPath, "meditate", 3, true);
            _idle = HeroArt.Load(ctx, HeroArt.CurrentPath, "idle", 6, true);
            _gather = HeroArt.Overlay(ctx, "ovl_meditate", 200, 8, true) ?? new Anim(Ui.Art(ctx, Resource.Drawable.fx_qi_gather), 256, 7, true);
            _aura = HeroArt.Overlay(ctx, "ovl_aura_" + HeroArt.Normalize(HeroArt.CurrentPath), 200, 9, true);
            var fig = ctx.Resources.GetIdentifier("ink_meditate_" + HeroArt.Normalize(HeroArt.CurrentPath), "drawable", ctx.PackageName);
            _figure = fig != 0 ? Ui.Art(ctx, fig) : null;
            _ring = Ui.Art(ctx, Resource.Drawable.ink_ui_ring_progress);
            _seal = Ui.Art(ctx, Resource.Drawable.ink_ui_seal_red);
        }

        private readonly Anim _aura;
        private readonly Bitmap _figure;
        private readonly Bitmap _ring;
        private readonly Bitmap _seal;

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
            var target = (float)f.Progress;
            _shown = _shown < 0 ? target : _shown + (target - _shown) * 0.06f;
            var shown = Math.Clamp(_shown, 0, 1);

            // the enso: a faint full brush circle, inked in clockwise as the stage fills
            var R = Math.Min(w * 0.40f, (h - 64 * _d) / 2.15f);
            var cx = w / 2;
            var cy = 4 * _d + R * 1.06f;
            var ring = new RectF(cx - R, cy - R, cx + R, cy + R);
            if (meditating)
            {
                var pulse = (float)(0.5 + 0.5 * Math.Sin(now / 700.0));
                _p.SetShader(new RadialGradient(cx, cy + R * 0.2f, R * (0.9f + 0.08f * pulse),
                    new int[] { Color.Argb(70, 60, 64, 62).ToArgb(), Color.Argb(0, 60, 64, 62).ToArgb() }, null, Shader.TileMode.Clamp));
                canvas.DrawCircle(cx, cy + R * 0.2f, R, _p);
                _p.SetShader(null);
            }

            if (_ring != null)
            {
                _art.Alpha = 42;
                canvas.DrawBitmap(_ring, null, ring, _art);
                if (shown >= 0.999f)
                {
                    _art.Alpha = 255;
                    canvas.DrawBitmap(_ring, null, ring, _art);
                }
                else if (shown > 0.004f)
                {
                    var save = canvas.Save();
                    var wedge = new Path();
                    wedge.MoveTo(cx, cy);
                    wedge.ArcTo(new RectF(cx - R * 1.2f, cy - R * 1.2f, cx + R * 1.2f, cy + R * 1.2f), -90, 360 * shown);
                    wedge.Close();
                    canvas.ClipPath(wedge);
                    _art.Alpha = 255;
                    canvas.DrawBitmap(_ring, null, ring, _art);
                    canvas.RestoreToCount(save);
                }

                _art.Alpha = 255;
            }

            // the cultivator sits inside the circle, breathing slowly
            var breath = 1f + 0.012f * (float)Math.Sin(now / (meditating ? 520.0 : 900.0));
            if (_figure != null)
            {
                var fh = R * 1.42f * breath;
                var fw = fh * _figure.Width / _figure.Height;
                var bottom = cy + R * 0.72f;
                canvas.DrawBitmap(_figure, null, new RectF(cx - fw / 2, bottom - fh, cx + fw / 2, bottom), _art);
            }
            else
            {
                var a = meditating ? _meditate : _idle;
                var side = R * 1.4f;
                canvas.DrawBitmap(a.Strip, a.Frame(now), new RectF(cx - side / 2, cy + R * 0.72f - side, cx + side / 2, cy + R * 0.72f), _art);
            }

            // ink motes rising while the hero gathers Qi
            if (meditating && _rnd.NextDouble() < 0.25)
            {
                var ang = _rnd.NextDouble() * Math.PI * 2;
                _motes.Add((cx + (float)Math.Cos(ang) * R * 0.92f, cy + (float)Math.Sin(ang) * R * 0.92f, 18 + (float)_rnd.NextDouble() * 26, now));
            }

            _p.SetShader(null);
            _motes.RemoveAll(m => now - m.Born > 1600);
            foreach (var m in _motes)
            {
                var k = (now - m.Born) / 1600f;
                _p.Color = Color.Argb((int)(150 * (1 - k)), 40, 42, 40);
                // drift toward the centre: Qi flows into the cultivator
                var mx = m.X + (cx - m.X) * k * 0.6f;
                var my = m.Y + (cy - m.Y) * k * 0.6f - k * m.V * _d * 0.3f;
                canvas.DrawCircle(mx, my, (2.2f - k) * _d, _p);
            }

            // red seal with the percentage, where the brush stroke currently ends
            var endAng = (-90 + 360 * shown) * Math.PI / 180;
            var sx = cx + (float)Math.Cos(endAng) * R * 0.98f;
            var sy = cy + (float)Math.Sin(endAng) * R * 0.98f;
            var ss = 34 * _d;
            if (_seal != null)
            {
                _art.Alpha = 235;
                canvas.DrawBitmap(_seal, null, new RectF(sx - ss / 2, sy - ss / 2, sx + ss / 2, sy + ss / 2), _art);
                _art.Alpha = 255;
            }

            _text.TextAlign = Paint.Align.Center;
            _text.TextSize = 12 * _d;
            _text.Color = Ui.Cinnabar;
            canvas.DrawText((int)Math.Floor(shown * 100) + "%", sx, sy - (_text.Descent() + _text.Ascent()) / 2, _text);

            // texts under the circle: Qi numbers, then time to full
            var below = cy + R + 20 * _d;
            _text.TextSize = 15 * _d;
            _text.Color = Ui.Ink;
            canvas.DrawText((State.Qi + f.PendingQi) + " / " + State.QiRequired + " Qi" + (meditating ? "  ·  medytacja " + f.SessionMinutes + " min" : string.Empty), cx, below, _text);
            _text.TextSize = 12 * _d;
            string line;
            if (State.QiRequired <= 0) { line = "Szczyt królestwa"; _text.Color = Ui.Cinnabar; }
            else if (f.Progress >= 1) { line = meditating ? "Pełne! Zakończ medytację i spróbuj przełomu" : "Gotowe do przełomu"; _text.Color = Ui.Cinnabar; }
            else if (f.MinutesToFull < 0) { line = "Medytacja nic teraz nie daje"; _text.Color = Ui.Cinnabar; }
            else
            {
                line = "Do 100%: " + MeditationForecast.FormatMinutes(f.MinutesToFull) + " medytacji";
                _text.Color = Ui.Muted;
            }

            canvas.DrawText(line, cx, below + 18 * _d, _text);
            if (f.LimitedByDailyCap || f.SessionFull)
            {
                _text.TextSize = 11 * _d;
                _text.Color = Ui.Cinnabar;
                canvas.DrawText(f.SessionFull ? "Sesja osiągnęła limit — zakończ ją, by zebrać Qi" : "Dzienny limit medytacji nie wystarczy — reszta jutro", cx, below + 34 * _d, _text);
            }

            if (meditating || Math.Abs(_shown - target) > 0.001f || _motes.Count > 0) PostInvalidateDelayed(40);
            else PostInvalidateDelayed(120); // the hero keeps breathing
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
