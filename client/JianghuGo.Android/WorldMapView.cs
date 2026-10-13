using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;

namespace JianghuGo.Android
{
    /// <summary>
    /// The player's surroundings as a night map of the Jianghu: the player in the centre, nearby places drawn as
    /// illustrated medallions at their real offset (metres), a glowing reach ring, and the direction to the nearest
    /// Flame site. Tapping a place raises <see cref="NodeTapped"/>; whether it can be used is decided by the server.
    /// </summary>
    public sealed class WorldMapView : View
    {
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _art = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly float _d;
        private readonly Dictionary<string, Bitmap> _medallions = new Dictionary<string, Bitmap>();
        private readonly Bitmap _lotus, _grove, _wolf, _brazier;
        private Bitmap _terrain;

        public WorldMapView(Context context) : base(context)
        {
            _d = context.Resources.DisplayMetrics.Density;
            _text.TextSize = 11 * _d;
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(4, 0, 1, Color.Black);
            _lotus = Ui.Art(context, Resource.Drawable.place_lotus);
            _grove = Ui.Art(context, Resource.Drawable.place_grove);
            _wolf = Ui.Art(context, Resource.Drawable.sprite_wolf);
            _brazier = Ui.Art(context, Resource.Drawable.place_brazier);
        }

        public LocationFix Player { get; set; }
        public NearbyDto Nearby { get; set; }
        public int InteractionRadiusM { get; set; } = 40;

        public event Action<NodeDto> NodeTapped;

        private double RadiusM
        {
            get
            {
                var far = Nearby?.Nodes.Count > 0 ? Nearby.Nodes.Max(n => n.DistanceM) : 0;
                return Math.Clamp(far * 1.15, 150, 650);
            }
        }

        private float Half => Math.Min(Width, Height) / 2f * 0.9f;

        private (float X, float Y) Project(double lat, double lon)
        {
            var scale = Half / (float)RadiusM;
            var dx = (lon - Player.Lon) * Math.Cos(Player.Lat * Math.PI / 180) * 111320;
            var dy = (lat - Player.Lat) * 110540;
            return (Width / 2f + (float)dx * scale, Height / 2f - (float)dy * scale);
        }

        private Bitmap Medallion(string key, Bitmap art, Color ring, bool flip = false)
        {
            if (_medallions.TryGetValue(key, out var m)) return m;
            var size = (int)(46 * _d);
            m = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888);
            var c = new Canvas(m);
            var p = new Paint(PaintFlags.AntiAlias);
            p.Color = Color.Rgb(10, 14, 18);
            c.DrawCircle(size / 2f, size / 2f, size / 2f - 2, p);
            // art clipped to the circle, cropped to its centre
            var save = c.Save();
            var clip = new Path();
            clip.AddCircle(size / 2f, size / 2f, size / 2f - 3 * _d, Path.Direction.Cw);
            c.ClipPath(clip);
            var s = Math.Min(art.Width, art.Height);
            var src = new Rect((art.Width - s) / 2, (art.Height - s) / 2, (art.Width + s) / 2, (art.Height + s) / 2);
            if (flip)
            {
                c.Scale(-1, 1, size / 2f, size / 2f);
            }

            c.DrawBitmap(art, src, new RectF(0, 0, size, size), new Paint(PaintFlags.FilterBitmap));
            c.RestoreToCount(save);
            p.SetStyle(Paint.Style.Stroke);
            p.StrokeWidth = 2.4f * _d;
            p.Color = ring;
            c.DrawCircle(size / 2f, size / 2f, size / 2f - 2 * _d, p);
            p.StrokeWidth = 1f * _d;
            p.Color = Color.Argb(200, 0, 0, 0);
            c.DrawCircle(size / 2f, size / 2f, size / 2f - 3.6f * _d, p);
            _medallions[key] = m;
            return m;
        }

        private (Bitmap Art, Color Glow) Look(string kind) => kind switch
        {
            "qi_spring" => (Medallion("qi", _lotus, Ui.QiBlue), Ui.QiBlue),
            "spirit_herb" => (Medallion("herb", _grove, Ui.Jade), Ui.Jade),
            "spirit_beast" => (Medallion("beast", _wolf, Ui.Cinnabar, flip: true), Ui.Cinnabar),
            _ => (Medallion("other", _lotus, Ui.Gold), Ui.Gold),
        };

        /// <summary>Night parchment with faint survey lines, drawn once per size.</summary>
        private Bitmap Terrain()
        {
            if (_terrain != null && _terrain.Width == Width && _terrain.Height == Height) return _terrain;
            _terrain = Bitmap.CreateBitmap(Width, Height, Bitmap.Config.Argb8888);
            var c = new Canvas(_terrain);
            var p = new Paint(PaintFlags.AntiAlias);
            p.SetShader(new RadialGradient(Width / 2f, Height / 2f, Math.Max(Width, Height) * 0.75f,
                new int[] { Color.Rgb(28, 40, 44).ToArgb(), Color.Rgb(14, 20, 24).ToArgb(), Color.Rgb(6, 8, 10).ToArgb() }, new float[] { 0f, 0.55f, 1f }, Shader.TileMode.Clamp));
            c.DrawRect(0, 0, Width, Height, p);
            p.SetShader(null);
            var rnd = new Random(7);
            p.Color = Color.Argb(26, 120, 170, 160);
            p.StrokeWidth = 1;
            var step = 22 * _d;
            for (var x = 0f; x < Width; x += step) c.DrawLine(x, 0, x, Height, p);
            for (var y = 0f; y < Height; y += step) c.DrawLine(0, y, Width, y, p);
            // drifting mist blotches
            for (var i = 0; i < 26; i++)
            {
                p.SetShader(new RadialGradient((float)rnd.NextDouble() * Width, (float)rnd.NextDouble() * Height, (30 + (float)rnd.NextDouble() * 60) * _d,
                    Color.Argb(28, 110, 160, 150).ToArgb(), Color.Transparent.ToArgb(), Shader.TileMode.Clamp));
                c.DrawRect(0, 0, Width, Height, p);
            }

            p.SetShader(null);
            // gold compass rose in the corner
            p.Color = Color.Argb(150, 212, 175, 96);
            p.SetStyle(Paint.Style.Stroke);
            p.StrokeWidth = 1.2f * _d;
            float cx = Width - 30 * _d, cy = 30 * _d, r = 16 * _d;
            c.DrawCircle(cx, cy, r, p);
            c.DrawLine(cx, cy - r - 4 * _d, cx, cy + r + 4 * _d, p);
            c.DrawLine(cx - r - 4 * _d, cy, cx + r + 4 * _d, cy, p);
            var t = new Paint(PaintFlags.AntiAlias) { Color = Ui.Gold, TextSize = 10 * _d, TextAlign = Paint.Align.Center };
            t.SetTypeface(Ui.Display);
            c.DrawText("N", cx, cy - r - 6 * _d, t);
            // vignette frame
            p.SetStyle(Paint.Style.Stroke);
            p.StrokeWidth = 2 * _d;
            p.Color = Ui.GoldDim;
            c.DrawRoundRect(new RectF(_d, _d, Width - _d, Height - _d), 10 * _d, 10 * _d, p);
            return _terrain;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Width == 0 || Height == 0) return;
            canvas.DrawBitmap(Terrain(), 0, 0, null);
            float cx = Width / 2f, cy = Height / 2f, half = Half;
            var t = SystemClock.UptimeMillis() / 1000.0;
            var pulse = (float)(0.5 + 0.5 * Math.Sin(t * 3.2));

            if (Player == null)
            {
                _text.Color = Ui.Gold;
                _text.TextSize = 14 * _d;
                canvas.DrawText("Szukam sygnału GPS…", cx, cy, _text);
                _text.TextSize = 11 * _d;
                PostInvalidateDelayed(50);
                return;
            }

            var scale = half / (float)RadiusM;
            // scale rings
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 1f * _d;
            _p.Color = Color.Argb(70, 212, 175, 96);
            _p.SetPathEffect(new DashPathEffect(new[] { 4 * _d, 6 * _d }, 0));
            canvas.DrawCircle(cx, cy, half, _p);
            canvas.DrawCircle(cx, cy, half / 2, _p);
            _p.SetPathEffect(null);
            _text.Color = Color.Argb(160, 212, 175, 96);
            _text.TextAlign = Paint.Align.Left;
            canvas.DrawText((int)RadiusM + " m", cx + 4 * _d, cy - half - 4 * _d, _text);
            _text.TextAlign = Paint.Align.Center;

            // reach: what is inside this glowing ring can be gathered or fought
            var reach = InteractionRadiusM * scale;
            _p.SetStyle(Paint.Style.Fill);
            _p.SetShader(new RadialGradient(cx, cy, Math.Max(reach, 1), new int[] { Color.Argb(10, 214, 72, 56).ToArgb(), Color.Argb(70, 214, 72, 56).ToArgb() }, null, Shader.TileMode.Clamp));
            canvas.DrawCircle(cx, cy, reach, _p);
            _p.SetShader(null);
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 1.6f * _d;
            _p.Color = Color.Argb(200, 214, 72, 56);
            canvas.DrawCircle(cx, cy, reach, _p);

            if (Nearby != null)
            {
                var site = Nearby.NearestFlameSite;
                if (site != null)
                {
                    float ax, ay;
                    var inside = site.DistanceM <= RadiusM;
                    if (inside) (ax, ay) = Project(site.Lat, site.Lon);
                    else
                    {
                        var angle = Math.Atan2((site.Lat - Player.Lat) * 110540, (site.Lon - Player.Lon) * Math.Cos(Player.Lat * Math.PI / 180) * 111320);
                        ax = cx + (float)Math.Cos(angle) * half;
                        ay = cy - (float)Math.Sin(angle) * half;
                    }

                    Glow(canvas, ax, ay, 30 * _d, Color.Rgb(176, 96, 232), 0.6f + 0.4f * pulse);
                    var m = Medallion("flame", _brazier, Color.Rgb(176, 96, 232));
                    var sz = inside ? 1f : 0.7f;
                    canvas.DrawBitmap(m, null, new RectF(ax - m.Width * sz / 2, ay - m.Height * sz / 2, ax + m.Width * sz / 2, ay + m.Height * sz / 2), _art);
                    _text.Color = Color.Rgb(214, 170, 250);
                    canvas.DrawText(site.NamePl, ax, ay + (ay < cy ? 34 : -28) * _d, _text);
                }

                foreach (var n in Nearby.Nodes.OrderByDescending(n => n.DistanceM))
                {
                    var (x, y) = Project(n.Lat, n.Lon);
                    var (art, glow) = Look(n.Kind);
                    var active = n.InRange && !n.Harvested;
                    if (!n.Harvested) Glow(canvas, x, y, (active ? 34 : 26) * _d, glow, active ? 0.55f + 0.45f * pulse : 0.45f);
                    _art.Alpha = n.Harvested ? 90 : 255;
                    var s = active ? 1f + 0.06f * pulse : 0.86f;
                    canvas.DrawBitmap(art, null, new RectF(x - art.Width * s / 2, y - art.Height * s / 2, x + art.Width * s / 2, y + art.Height * s / 2), _art);
                    _art.Alpha = 255;
                    _text.Color = n.Harvested ? Color.Argb(120, 232, 220, 192) : active ? Ui.Gold : Ui.Ink;
                    canvas.DrawText(n.NamePl + (n.Harvested ? " ✓" : string.Empty), x, y - art.Height * s / 2 - 4 * _d, _text);
                }
            }

            // the player: a gold seal with a breathing ring
            Glow(canvas, cx, cy, (18 + 10 * pulse) * _d, Ui.Gold, 0.7f);
            _p.SetStyle(Paint.Style.Fill);
            _p.Color = Color.Rgb(16, 18, 20);
            canvas.DrawCircle(cx, cy, 9 * _d, _p);
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 2.4f * _d;
            _p.Color = Ui.Gold;
            canvas.DrawCircle(cx, cy, 9 * _d, _p);
            _p.SetStyle(Paint.Style.Fill);
            canvas.DrawCircle(cx, cy, 3.5f * _d, _p);
            PostInvalidateDelayed(40); // ~25 fps while the map is on screen
        }

        private void Glow(Canvas canvas, float x, float y, float radius, Color color, float strength)
        {
            var a = (int)(150 * Math.Clamp(strength, 0, 1));
            _p.SetStyle(Paint.Style.Fill);
            _p.SetShader(new RadialGradient(x, y, Math.Max(radius, 1), new int[] { Color.Argb(a, color.R, color.G, color.B).ToArgb(), Color.Argb(0, color.R, color.G, color.B).ToArgb() }, null, Shader.TileMode.Clamp));
            canvas.DrawCircle(x, y, radius, _p);
            _p.SetShader(null);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down) return true;
            if (e.Action != MotionEventActions.Up || Player == null || Nearby == null) return base.OnTouchEvent(e);

            NodeDto best = null;
            var bestD = 36 * _d;
            foreach (var n in Nearby.Nodes)
            {
                var (x, y) = Project(n.Lat, n.Lon);
                var dist = (float)Math.Sqrt((x - e.GetX()) * (x - e.GetX()) + (y - e.GetY()) * (y - e.GetY()));
                if (dist < bestD)
                {
                    bestD = dist;
                    best = n;
                }
            }

            if (best != null) NodeTapped?.Invoke(best);
            return true;
        }
    }
}
