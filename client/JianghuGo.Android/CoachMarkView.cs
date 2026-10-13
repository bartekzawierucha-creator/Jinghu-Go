using System;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace JianghuGo.Android
{
    /// <summary>
    /// Tutorial overlay: dims the screen except a glowing cut-out around the real control being explained, with a
    /// speech card from the mentor above or below it. Tap anywhere to continue.
    /// </summary>
    public sealed class CoachMarkView : FrameLayout
    {
        private readonly View _target;
        private readonly LinearLayout _card;
        private readonly Paint _dim = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _ring = new Paint(PaintFlags.AntiAlias);
        private readonly float _d;
        private RectF _hole;

        public event System.Action Done;

        public CoachMarkView(Context ctx, View target, string title, string text, string button) : base(ctx)
        {
            _target = target;
            _d = ctx.Resources.DisplayMetrics.Density;
            SetWillNotDraw(false);
            Clickable = true;
            _dim.Color = Color.Argb(185, 0, 0, 0);
            _ring.SetStyle(Paint.Style.Stroke);
            _ring.StrokeWidth = 2.5f * _d;
            _ring.Color = Ui.Gold;

            _card = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
            _card.Background = new FrameDrawable(_d, Color.Argb(250, 18, 20, 24), Ui.Gold, Ui.Gold);
            _card.SetPadding(D(12), D(10), D(12), D(10));
            var portrait = new ImageView(ctx);
            portrait.SetImageBitmap(Ui.Art(ctx, Resource.Drawable.portrait));
            portrait.SetScaleType(ImageView.ScaleType.CenterCrop);
            _card.AddView(portrait, new LinearLayout.LayoutParams(D(54), D(60)) { RightMargin = D(10) });
            var col = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
            col.AddView(Make(title, 16, Ui.Gold, true));
            col.AddView(Make(text, 13, Ui.Ink, false));
            var go = Make(button, 13, Ui.Cinnabar, true);
            go.Gravity = GravityFlags.End;
            col.AddView(go, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = D(6) });
            _card.AddView(col, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            AddView(_card, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = D(16), RightMargin = D(16) });
            Click += (_, __) =>
            {
                Sfx.Play(Sfx.Click, 0.5f);
                Done?.Invoke();
            };
            Alpha = 0f;
            Animate().Alpha(1f).SetDuration(220).Start();
        }

        private int D(float dp) => (int)(dp * _d + 0.5f);

        private TextView Make(string text, float sp, Color color, bool bold)
        {
            var t = new TextView(Context) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
            t.SetTextColor(color);
            t.SetTypeface(bold ? Ui.Display : Ui.Body, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            return t;
        }

        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        {
            base.OnLayout(changed, left, top, right, bottom);
            _hole = null;
            if (_target != null && _target.IsShown && _target.Width > 0)
            {
                var me = new int[2];
                var it = new int[2];
                GetLocationInWindow(me);
                _target.GetLocationInWindow(it);
                var pad = 6 * _d;
                _hole = new RectF(it[0] - me[0] - pad, it[1] - me[1] - pad, it[0] - me[0] + _target.Width + pad, it[1] - me[1] + _target.Height + pad);
            }

            // card below the cut-out when it is in the upper half, otherwise above it (or centred)
            var h = bottom - top;
            var lp = (LayoutParams)_card.LayoutParameters;
            var cardH = _card.Height > 0 ? _card.Height : D(120);
            int y;
            if (_hole == null) y = (h - cardH) / 2;
            else if (_hole.CenterY() < h / 2f) y = (int)Math.Min(h - cardH - D(8), _hole.Bottom + D(14));
            else y = (int)Math.Max(D(8), _hole.Top - cardH - D(14));
            if (lp.TopMargin != y)
            {
                lp.TopMargin = y;
                _card.LayoutParameters = lp;
            }
        }

        protected override void DispatchDraw(Canvas canvas)
        {
            var path = new Path();
            path.SetFillType(Path.FillType.EvenOdd);
            path.AddRect(0, 0, Width, Height, Path.Direction.Cw);
            if (_hole != null) path.AddRoundRect(_hole, 14 * _d, 14 * _d, Path.Direction.Cw);
            canvas.DrawPath(path, _dim);
            if (_hole != null)
            {
                var pulse = (float)(0.5 + 0.5 * Math.Sin(global::Android.OS.SystemClock.UptimeMillis() / 220.0));
                _ring.Alpha = (int)(150 + 100 * pulse);
                canvas.DrawRoundRect(_hole, 14 * _d, 14 * _d, _ring);
                PostInvalidateDelayed(40);
            }

            base.DispatchDraw(canvas);
        }
    }
}
