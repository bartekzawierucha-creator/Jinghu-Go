using System;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.Views;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;

namespace JianghuGo.Android
{
    /// <summary>
    /// Ink-on-paper map of the player's surroundings: the player in the centre, nearby nodes placed by their real
    /// offset (metres), range rings and the direction to the nearest Flame site. Tapping a node raises
    /// <see cref="NodeTapped"/>; whether it can be harvested is decided by the server.
    /// </summary>
    public sealed class WorldMapView : View
    {
        private readonly Paint _fill = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _stroke = new Paint(PaintFlags.AntiAlias) { StrokeWidth = 3f };
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly float _density;

        public WorldMapView(Context context) : base(context)
        {
            _density = context.Resources.DisplayMetrics.Density;
            _stroke.SetStyle(Paint.Style.Stroke);
            _text.TextSize = 12 * _density;
            _text.Color = Ui.Ink;
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

        private (float X, float Y) Project(double lat, double lon)
        {
            var cx = Width / 2f;
            var cy = Height / 2f;
            var scale = Math.Min(Width, Height) / 2f * 0.9f / (float)RadiusM;
            var dx = (lon - Player.Lon) * Math.Cos(Player.Lat * Math.PI / 180) * 111320;
            var dy = (lat - Player.Lat) * 110540;
            return (cx + (float)dx * scale, cy - (float)dy * scale);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            canvas.DrawColor(Ui.PaperDark);
            var cx = Width / 2f;
            var cy = Height / 2f;
            var half = Math.Min(Width, Height) / 2f * 0.9f;

            if (Player == null)
            {
                _text.TextAlign = Paint.Align.Center;
                canvas.DrawText("Czekam na sygnał GPS…", cx, cy, _text);
                return;
            }

            // range rings: interaction radius and the scale ring
            var scale = half / (float)RadiusM;
            _stroke.Color = Color.Argb(70, 31, 26, 23);
            canvas.DrawCircle(cx, cy, half, _stroke);
            canvas.DrawCircle(cx, cy, half / 2, _stroke);
            _stroke.Color = Color.Argb(160, 179, 41, 31);
            canvas.DrawCircle(cx, cy, InteractionRadiusM * scale, _stroke);
            _text.TextAlign = Paint.Align.Left;
            _text.Color = Color.Argb(140, 31, 26, 23);
            canvas.DrawText((int)RadiusM + " m", cx + 4 * _density, cy - half + 14 * _density, _text);

            if (Nearby != null)
            {
                foreach (var n in Nearby.Nodes)
                {
                    var (x, y) = Project(n.Lat, n.Lon);
                    _fill.Color = n.Kind switch
                    {
                        "qi_spring" => Color.Rgb(46, 104, 150),
                        "spirit_herb" => Color.Rgb(66, 125, 64),
                        "spirit_beast" => Ui.Cinnabar,
                        _ => Ui.Ink,
                    };
                    if (n.Harvested) _fill.Alpha = 70;
                    canvas.DrawCircle(x, y, 9 * _density, _fill);
                    if (n.InRange && !n.Harvested)
                    {
                        _stroke.Color = Ui.Ink;
                        canvas.DrawCircle(x, y, 14 * _density, _stroke);
                    }

                    _text.Color = n.Harvested ? Color.Argb(110, 31, 26, 23) : Ui.Ink;
                    _text.TextAlign = Paint.Align.Center;
                    canvas.DrawText(n.NamePl, x, y - 14 * _density, _text);
                }

                var site = Nearby.NearestFlameSite;
                if (site != null)
                {
                    // inside the map: at its place; farther away: on the rim, pointing the way
                    float ax, ay;
                    if (site.DistanceM <= RadiusM)
                    {
                        (ax, ay) = Project(site.Lat, site.Lon);
                    }
                    else
                    {
                        var angle = Math.Atan2((site.Lat - Player.Lat) * 110540, (site.Lon - Player.Lon) * Math.Cos(Player.Lat * Math.PI / 180) * 111320);
                        ax = cx + (float)Math.Cos(angle) * half;
                        ay = cy - (float)Math.Sin(angle) * half;
                    }

                    _fill.Color = Color.Rgb(214, 120, 30);
                    _fill.Alpha = 255;
                    canvas.DrawCircle(ax, ay, 7 * _density, _fill);
                    _text.Color = Color.Rgb(150, 70, 10);
                    _text.TextAlign = ax < cx ? Paint.Align.Left : Paint.Align.Right;
                    canvas.DrawText("🔥 " + site.NamePl, ax, ay + (ay < cy ? 22 : -12) * _density, _text);
                }
            }

            // the player
            _fill.Color = Ui.Ink;
            _fill.Alpha = 255;
            canvas.DrawCircle(cx, cy, 7 * _density, _fill);
            _fill.Color = Ui.Paper;
            canvas.DrawCircle(cx, cy, 3 * _density, _fill);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down) return true;
            if (e.Action != MotionEventActions.Up || Player == null || Nearby == null) return base.OnTouchEvent(e);

            NodeDto best = null;
            var bestD = 36 * _density;
            foreach (var n in Nearby.Nodes)
            {
                var (x, y) = Project(n.Lat, n.Lon);
                var d = (float)Math.Sqrt((x - e.GetX()) * (x - e.GetX()) + (y - e.GetY()) * (y - e.GetY()));
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }

            if (best != null) NodeTapped?.Invoke(best);
            return true;
        }
    }
}
