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
    /// The meridian constellation: nine acupoints on the meditating cultivator's silhouette, joined by qi channels
    /// that glow brighter the more points are opened. Tap an acupoint to open it (raises <see cref="NodeTapped"/>).
    /// </summary>
    public sealed class MeridianView : View
    {
        private static readonly (string Stat, float X, float Y)[] Nodes =
        {
            ("spirit", 0.50f, 0.10f), ("insight", 0.50f, 0.24f), ("willpower", 0.50f, 0.40f), ("strength", 0.20f, 0.44f), ("agility", 0.80f, 0.44f),
            ("focus", 0.50f, 0.56f), ("constitution", 0.50f, 0.72f), ("elementalAffinity", 0.24f, 0.86f), ("fate", 0.76f, 0.86f),
        };

        private static readonly (int A, int B)[] Channels = { (0, 1), (1, 2), (2, 3), (2, 4), (2, 5), (5, 6), (6, 7), (6, 8), (3, 7), (4, 8) };

        private readonly float _d;
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _art = new Paint(PaintFlags.FilterBitmap);
        private readonly Bitmap _silhouette;
        private readonly Dictionary<string, Bitmap> _icons = new Dictionary<string, Bitmap>();
        private readonly Func<string, string> _name;

        public MeridianView(Context ctx, Func<string, string> statName) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _name = statName;
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(4, 0, 1, Color.Black);
            _silhouette = Ui.Art(ctx, HeroArt.Id(ctx, HeroArt.CurrentPath, "meditate"));
            foreach (var (stat, _, _) in Nodes)
            {
                var id = ctx.Resources.GetIdentifier("icon_" + stat, "drawable", ctx.PackageName);
                if (id != 0) _icons[stat] = Ui.Art(ctx, id);
            }
        }

        public MeridiansDto State { get; set; }

        public event Action<string> NodeTapped;

        private (float X, float Y) Pos(int i) => (Nodes[i].X * Width, Nodes[i].Y * Height);

        private int Points(string stat) => State != null && State.Allocated.TryGetValue(stat, out var p) ? p : 0;

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Width == 0) return;
            var now = SystemClock.UptimeMillis();
            var pulse = (float)(0.5 + 0.5 * Math.Sin(now / 400.0));

            // the meditating cultivator, faint, behind the constellation (first frame of the strip)
            var cell = new Rect(0, 0, 320, _silhouette.Height);
            var h = Height * 0.98f;
            var w = h * 320f / _silhouette.Height;
            _art.Alpha = 70;
            canvas.DrawBitmap(_silhouette, cell, new RectF(Width / 2f - w / 2, Height - h, Width / 2f + w / 2, Height), _art);
            _art.Alpha = 255;

            // channels
            foreach (var (a, b) in Channels)
            {
                var (x1, y1) = Pos(a);
                var (x2, y2) = Pos(b);
                var lit = Math.Min(Points(Nodes[a].Stat), Points(Nodes[b].Stat));
                _p.SetStyle(Paint.Style.Stroke);
                _p.StrokeWidth = (2 + Math.Min(lit, 6)) * _d;
                _p.Color = lit > 0 ? Color.Argb((int)(140 + 80 * pulse), 120, 200, 255) : Color.Argb(70, 212, 175, 96);
                canvas.DrawLine(x1, y1, x2, y2, _p);
            }

            // acupoints
            var free = State != null && State.Free > 0;
            var r = Math.Min(Width, Height) * 0.075f;
            for (var i = 0; i < Nodes.Length; i++)
            {
                var (x, y) = Pos(i);
                var stat = Nodes[i].Stat;
                var pts = Points(stat);
                _p.SetStyle(Paint.Style.Fill);
                var glowR = r * (1.6f + (free ? 0.25f * pulse : 0));
                _p.SetShader(new RadialGradient(x, y, glowR, new int[] { Color.Argb(pts > 0 ? 170 : 90, 110, 190, 255).ToArgb(), Color.Argb(0, 110, 190, 255).ToArgb() }, null, Shader.TileMode.Clamp));
                canvas.DrawCircle(x, y, glowR, _p);
                _p.SetShader(null);
                _p.Color = Color.Argb(240, 10, 13, 16);
                canvas.DrawCircle(x, y, r, _p);
                if (_icons.TryGetValue(stat, out var icon))
                    canvas.DrawBitmap(icon, null, new RectF(x - r * 0.7f, y - r * 0.7f, x + r * 0.7f, y + r * 0.7f), _art);
                _p.SetStyle(Paint.Style.Stroke);
                _p.StrokeWidth = 2 * _d;
                _p.Color = pts > 0 ? Ui.QiBlue : free ? Ui.Gold : Ui.GoldDim;
                canvas.DrawCircle(x, y, r, _p);
                _text.TextSize = 11 * _d;
                _text.Color = Ui.Ink;
                if (Math.Abs(Nodes[i].X - 0.5f) < 0.01f)
                {
                    // spine points sit close together: label them on the side
                    _text.TextAlign = i % 2 == 0 ? Paint.Align.Left : Paint.Align.Right;
                    canvas.DrawText(_name(stat), i % 2 == 0 ? x + r + 8 * _d : x - r - 8 * _d, y + 4 * _d, _text);
                    _text.TextAlign = Paint.Align.Center;
                }
                else
                {
                    canvas.DrawText(_name(stat), x, y + r + 13 * _d, _text);
                }
                if (pts > 0)
                {
                    _text.TextSize = 12 * _d;
                    _text.Color = Ui.QiBlue;
                    canvas.DrawText("+" + pts, x + r * 1.1f, y - r * 0.8f, _text);
                }
            }

            PostInvalidateDelayed(50);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down) return true;
            if (e.Action != MotionEventActions.Up) return base.OnTouchEvent(e);
            var r = Math.Min(Width, Height) * 0.075f * 1.6f;
            for (var i = 0; i < Nodes.Length; i++)
            {
                var (x, y) = Pos(i);
                if ((x - e.GetX()) * (x - e.GetX()) + (y - e.GetY()) * (y - e.GetY()) <= r * r)
                {
                    NodeTapped?.Invoke(Nodes[i].Stat);
                    return true;
                }
            }

            return true;
        }
    }
}
