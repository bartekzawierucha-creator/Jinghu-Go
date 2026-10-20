using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.Animations;

namespace JianghuGo.Android
{
    /// <summary>Small touches of motion that make the UI feel alive (2.1 look): presses, pops and drifting Qi motes.</summary>
    public static class Juice
    {
        /// <summary>A pressed control sinks slightly and springs back.</summary>
        public static void Press(View v)
        {
            v.Touch += (_, e) =>
            {
                switch (e.Event?.Action)
                {
                    case MotionEventActions.Down:
                        v.Animate().ScaleX(0.95f).ScaleY(0.95f).SetDuration(70).Start();
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        v.Animate().ScaleX(1f).ScaleY(1f).SetDuration(220).SetInterpolator(new OvershootInterpolator(3f)).Start();
                        break;
                }

                e.Handled = false; // the click still fires
            };
        }

        /// <summary>A quick "pop" for rewards and new values.</summary>
        public static void Pop(View v)
        {
            v.ScaleX = 0.7f;
            v.ScaleY = 0.7f;
            v.Animate().ScaleX(1f).ScaleY(1f).SetDuration(320).SetInterpolator(new OvershootInterpolator(2.5f)).Start();
        }
    }

    /// <summary>
    /// Ambient Qi: a few dozen soft golden and jade motes drifting upward over the backdrop. Cheap (no allocations per
    /// frame, ~30 fps) and stops drawing when the activity is paused.
    /// </summary>
    public sealed class QiMotesView : View
    {
        private sealed class Mote
        {
            public float X, Y, R, Vy, Vx, Phase, Alpha;
            public bool Jade;
        }

        private readonly List<Mote> _motes = new List<Mote>();
        private readonly Random _rnd = new Random();
        private readonly Paint _paint = new Paint(PaintFlags.AntiAlias);
        private readonly float _d;
        private long _last;

        public bool Running { get; set; } = true;

        public QiMotesView(Context ctx) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            SetLayerType(LayerType.Hardware, null);
        }

        private Mote NewMote(bool anywhere)
        {
            var w = Math.Max(1, Width);
            var h = Math.Max(1, Height);
            return new Mote
            {
                X = (float)_rnd.NextDouble() * w,
                Y = anywhere ? (float)_rnd.NextDouble() * h : h + (10 * _d),
                R = (0.8f + ((float)_rnd.NextDouble() * 2.2f)) * _d,
                Vy = -(6 + ((float)_rnd.NextDouble() * 18)) * _d,
                Vx = ((float)_rnd.NextDouble() - 0.5f) * 6 * _d,
                Phase = (float)(_rnd.NextDouble() * Math.PI * 2),
                Alpha = 0.25f + ((float)_rnd.NextDouble() * 0.5f),
                Jade = _rnd.NextDouble() < 0.3,
            };
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Width == 0 || Height == 0) return;
            if (_motes.Count == 0)
                for (var i = 0; i < 36; i++) _motes.Add(NewMote(true));
            var now = Java.Lang.JavaSystem.NanoTime();
            var dt = _last == 0 ? 0f : Math.Min(0.1f, (now - _last) / 1e9f);
            _last = now;
            for (var i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                m.Phase += dt * 1.6f;
                m.Y += m.Vy * dt;
                m.X += (m.Vx + (float)Math.Sin(m.Phase) * 4 * _d) * dt;
                if (m.Y < -10 * _d) _motes[i] = m = NewMote(false);
                var twinkle = 0.6f + (0.4f * (float)Math.Sin(m.Phase * 2.3f));
                var a = (int)(255 * m.Alpha * twinkle);
                _paint.Color = m.Jade ? Color.Argb(a / 3, 60, 70, 66) : Color.Argb(a / 3, 70, 66, 60);
                canvas.DrawCircle(m.X, m.Y, m.R * 2.4f, _paint); // halo
                _paint.Color = m.Jade ? Color.Argb(a * 2 / 3, 40, 46, 44) : Color.Argb(a * 2 / 3, 48, 44, 40);
                canvas.DrawCircle(m.X, m.Y, m.R, _paint);
            }

            if (Running) PostInvalidateDelayed(33);
            else _last = 0;
        }
    }
}
