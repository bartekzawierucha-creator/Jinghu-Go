using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace JianghuGo.Android
{
    /// <summary>
    /// Visual language of the game, built from code: night-ink panels framed in gold, cinnabar seals for actions,
    /// pixel-art illustrations from the concept sheets. Every screen is composed from these pieces.
    /// </summary>
    public sealed class Ui
    {
        // palette
        public static readonly Color Night = Color.Rgb(11, 14, 17);
        public static readonly Color Paper = Color.Rgb(11, 14, 17);          // screen background
        public static readonly Color PaperDark = Color.Rgb(18, 24, 28);      // inset background
        public static readonly Color PanelFill = Color.Argb(224, 20, 26, 31);
        public static readonly Color Ink = Color.Rgb(232, 220, 192);         // main text (parchment on night)
        public static readonly Color Muted = Color.Rgb(150, 142, 124);
        public static readonly Color Gold = Color.Rgb(212, 175, 96);
        public static readonly Color GoldDim = Color.Rgb(122, 100, 56);
        public static readonly Color Cinnabar = Color.Rgb(214, 72, 56);
        public static readonly Color CinnabarDeep = Color.Rgb(120, 22, 18);
        public static readonly Color Jade = Color.Rgb(86, 170, 104);
        public static readonly Color QiBlue = Color.Rgb(74, 150, 222);
        public static readonly Color Violet = Color.Rgb(160, 96, 220);

        private static readonly Dictionary<int, Bitmap> Bitmaps = new Dictionary<int, Bitmap>();
        private readonly Context _ctx;
        private readonly float _density;

        public Ui(Context ctx, LinearLayout content)
        {
            _ctx = ctx;
            _density = ctx.Resources.DisplayMetrics.Density;
            Content = content;
        }

        public LinearLayout Content { get; }
        public Context Context => _ctx;

        public int Dp(float dp) => (int)(dp * _density + 0.5f);

        public static Typeface Display => Typeface.Create(Typeface.Serif, TypefaceStyle.Bold);
        public static Typeface Body => Typeface.Create(Typeface.Serif, TypefaceStyle.Normal);

        /// <summary>Decoded art, cached for the process (the pixel art is small).</summary>
        public static Bitmap Art(Context ctx, int resId)
        {
            lock (Bitmaps)
            {
                if (!Bitmaps.TryGetValue(resId, out var bmp))
                {
                    bmp = BitmapFactory.DecodeResource(ctx.Resources, resId, new BitmapFactory.Options { InScaled = false });
                    Bitmaps[resId] = bmp;
                }

                return bmp;
            }
        }

        public static Rarity RarityOf(string rarity) => rarity switch
        {
            "legendary" => new Rarity("legendarna", Gold),
            "epic" => new Rarity("epicka", Violet),
            "rare" => new Rarity("rzadka", QiBlue),
            _ => new Rarity("zwykła", Muted),
        };

        public readonly struct Rarity
        {
            public Rarity(string name, Color color)
            {
                Name = name;
                Color = color;
            }

            public string Name { get; }
            public Color Color { get; }
        }

        private LinearLayout.LayoutParams Row(int height = ViewGroup.LayoutParams.WrapContent, int top = 6)
        {
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, height);
            lp.TopMargin = Dp(top);
            return lp;
        }

        public TextView Label(string text, float sp = 15, Color? color = null, bool bold = false, ViewGroup parent = null)
        {
            var t = new TextView(_ctx) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
            t.SetTextColor(color ?? Ink);
            t.SetTypeface(bold ? Display : Body, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            t.SetLineSpacing(0, 1.12f);
            t.SetShadowLayer(3, 0, 1, Color.Argb(200, 0, 0, 0));
            (parent ?? Content).AddView(t, Row());
            return t;
        }

        /// <summary>Section title: gold capitals between thin ornamental rules.</summary>
        public TextView Heading(string text, ViewGroup parent = null)
        {
            var row = new LinearLayout(_ctx) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            row.AddView(Rule(), new LinearLayout.LayoutParams(0, Dp(1), 1f));
            var t = new TextView(_ctx) { Text = text.ToUpperInvariant() };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13);
            t.SetTextColor(Gold);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.LetterSpacing = 0.18f;
            t.SetPadding(Dp(10), 0, Dp(10), 0);
            row.AddView(t);
            row.AddView(Rule(), new LinearLayout.LayoutParams(0, Dp(1), 1f));
            (parent ?? Content).AddView(row, Row(top: 18));
            return t;
        }

        private View Rule()
        {
            var v = new View(_ctx);
            v.Background = new GradientDrawable(GradientDrawable.Orientation.LeftRight, new int[] { Color.Transparent.ToArgb(), GoldDim.ToArgb(), Color.Transparent.ToArgb() });
            return v;
        }

        public EditText Field(string hint, bool password = false, bool number = false, string value = null, ViewGroup parent = null)
        {
            var e = new EditText(_ctx) { Hint = hint, Text = value ?? string.Empty };
            e.SetSingleLine(true);
            e.SetTextColor(Ink);
            e.SetHintTextColor(Muted);
            e.SetTypeface(Body, TypefaceStyle.Normal);
            e.InputType = password ? InputTypes.ClassText | InputTypes.TextVariationPassword
                : number ? InputTypes.ClassNumber
                : InputTypes.ClassText;
            var bg = new GradientDrawable();
            bg.SetColor(Color.Argb(200, 8, 11, 13));
            bg.SetStroke(Dp(1), GoldDim);
            bg.SetCornerRadius(Dp(6));
            e.Background = bg;
            e.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
            (parent ?? Content).AddView(e, Row(top: 8));
            return e;
        }

        /// <summary>Primary action: a cinnabar seal with a gold rim. Secondary: dark lacquer with a dim gold rim.</summary>
        public Button Action(string caption, System.Action onClick, bool secondary = false, ViewGroup parent = null)
        {
            var b = new Button(_ctx) { Text = caption };
            b.SetAllCaps(false);
            b.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 16);
            b.SetTypeface(Display, TypefaceStyle.Bold);
            b.SetTextColor(secondary ? Gold : Ink);
            b.SetShadowLayer(2, 0, 1, Color.Argb(220, 0, 0, 0));
            b.Background = ButtonBackground(secondary);
            b.StateListAnimator = null;
            b.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
            b.Click += (_, __) => onClick();
            (parent ?? Content).AddView(b, Row(top: 8));
            return b;
        }

        public Drawable ButtonBackground(bool secondary)
        {
            GradientDrawable Make(bool pressed)
            {
                var g = secondary
                    ? new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Argb(235, 32, 38, 44).ToArgb(), Color.Argb(235, 16, 20, 24).ToArgb() })
                    : new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Rgb(188, 52, 38).ToArgb(), CinnabarDeep.ToArgb() });
                g.SetCornerRadius(Dp(8));
                g.SetStroke(Dp(pressed ? 2 : 1), pressed ? Gold : secondary ? GoldDim : Gold);
                if (pressed) g.SetAlpha(200);
                return g;
            }

            var states = new StateListDrawable();
            states.AddState(new[] { global::Android.Resource.Attribute.StatePressed }, Make(true));
            states.AddState(new int[0], Make(false));
            return states;
        }

        /// <summary>A framed panel (night lacquer, gold rim, corner ornaments) holding a vertical column.</summary>
        public LinearLayout Panel(ViewGroup parent = null, int padDp = 14)
        {
            var p = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            p.Background = new FrameDrawable(_density, PanelFill, GoldDim, Gold);
            p.SetPadding(Dp(padDp), Dp(padDp - 4), Dp(padDp), Dp(padDp));
            (parent ?? Content).AddView(p, Row(top: 10));
            return p;
        }

        /// <summary>A horizontal row whose children share the width equally.</summary>
        public LinearLayout Columns(ViewGroup parent = null)
        {
            var row = new LinearLayout(_ctx) { Orientation = Orientation.Horizontal };
            (parent ?? Content).AddView(row, Row(top: 4));
            return row;
        }

        public static void AddCell(LinearLayout row, View child, int marginPx)
        {
            var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
            if (row.ChildCount > 0) lp.LeftMargin = marginPx;
            row.AddView(child, lp);
        }

        public MeterView Meter(string caption, Color color, ViewGroup parent = null, int heightDp = 22)
        {
            var m = new MeterView(_ctx, color) { Text = caption };
            (parent ?? Content).AddView(m, Row(Dp(heightDp), 6));
            return m;
        }

        public static void SetMeter(MeterView m, string caption, double value, double max)
        {
            m.Text = caption;
            m.SetValue(value, max);
        }

        public LinearLayout Column(ViewGroup parent = null)
        {
            var col = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            (parent ?? Content).AddView(col, Row());
            return col;
        }

        public void Divider(ViewGroup parent = null)
        {
            var v = Rule();
            (parent ?? Content).AddView(v, Row(Dp(1), 14));
        }

        public void Add(View view, int heightDp, ViewGroup parent = null)
        {
            (parent ?? Content).AddView(view, Row(Dp(heightDp), 8));
        }

        /// <summary>
        /// A picture card: illustration (cropped to fill) under a dark gradient, title and subtitle at the bottom.
        /// The whole card is the button.
        /// </summary>
        public FrameLayout Tile(string title, string subtitle, int artRes, System.Action onClick, ViewGroup parent = null, int heightDp = 120, bool pixelArt = false, Color? accent = null)
        {
            var tile = new FrameLayout(_ctx) { Clickable = true, Focusable = true, ClipToOutline = true };
            tile.Background = new FrameDrawable(_density, Color.Argb(255, 14, 18, 22), accent ?? GoldDim, accent ?? Gold);
            tile.SetPadding(Dp(2), Dp(2), Dp(2), Dp(2));
            if (artRes != 0)
            {
                var img = new ImageView(_ctx);
                var drawable = new BitmapDrawable(_ctx.Resources, Art(_ctx, artRes));
                drawable.SetFilterBitmap(!pixelArt);
                img.SetImageDrawable(drawable);
                img.SetScaleType(pixelArt ? ImageView.ScaleType.FitCenter : ImageView.ScaleType.CenterCrop);
                tile.AddView(img, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }

            var shade = new View(_ctx);
            shade.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Argb(0, 0, 0, 0).ToArgb(), Color.Argb(60, 0, 0, 0).ToArgb(), Color.Argb(235, 6, 8, 10).ToArgb() });
            tile.AddView(shade, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var text = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            text.SetPadding(Dp(12), Dp(8), Dp(12), Dp(10));
            var t = new TextView(_ctx) { Text = title };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 17);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(Ink);
            t.SetShadowLayer(4, 0, 1, Color.Black);
            text.AddView(t);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = new TextView(_ctx) { Text = subtitle };
                s.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
                s.SetTypeface(Body, TypefaceStyle.Normal);
                s.SetTextColor(Gold);
                s.SetShadowLayer(3, 0, 1, Color.Black);
                text.AddView(s);
            }

            tile.AddView(text, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));
            tile.Click += (_, __) => onClick();
            tile.Touch += (_, e) =>
            {
                if (e.Event.Action == MotionEventActions.Down) tile.Animate().ScaleX(0.97f).ScaleY(0.97f).SetDuration(80).Start();
                else if (e.Event.Action == MotionEventActions.Up || e.Event.Action == MotionEventActions.Cancel) tile.Animate().ScaleX(1f).ScaleY(1f).SetDuration(120).Start();
                e.Handled = false;
            };
            if (parent is LinearLayout row && row.Orientation == Orientation.Horizontal) AddCell(row, tile, Dp(10));
            else (parent ?? Content).AddView(tile, Row(Dp(heightDp), 10));
            if (parent is LinearLayout r2 && r2.Orientation == Orientation.Horizontal) r2.LayoutParameters.Height = Dp(heightDp);
            return tile;
        }

        /// <summary>A small rounded tag (stat values, costs, rarities).</summary>
        public TextView Chip(string text, Color color, ViewGroup parent)
        {
            var t = new TextView(_ctx) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(color);
            var bg = new GradientDrawable();
            bg.SetColor(Color.Argb(200, 10, 13, 16));
            bg.SetStroke(Dp(1), Color.Argb(160, color.R, color.G, color.B));
            bg.SetCornerRadius(Dp(12));
            t.Background = bg;
            t.SetPadding(Dp(9), Dp(3), Dp(9), Dp(3));
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = Dp(6), TopMargin = Dp(6) };
            parent.AddView(t, lp);
            return t;
        }

        /// <summary>Wrapping row for chips.</summary>
        public FlowLayout Flow(ViewGroup parent = null)
        {
            var f = new FlowLayout(_ctx);
            (parent ?? Content).AddView(f, Row(top: 2));
            return f;
        }
    }

    /// <summary>Rounded lacquer panel with a thin rim and bright corner ornaments.</summary>
    public sealed class FrameDrawable : Drawable
    {
        private readonly Paint _fill = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _rim = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _corner = new Paint(PaintFlags.AntiAlias);
        private readonly float _d;

        public FrameDrawable(float density, Color fill, Color rim, Color corner)
        {
            _d = density;
            _fill.Color = fill;
            _rim.Color = rim;
            _rim.SetStyle(Paint.Style.Stroke);
            _rim.StrokeWidth = 1.2f * density;
            _corner.Color = corner;
            _corner.SetStyle(Paint.Style.Stroke);
            _corner.StrokeWidth = 2.2f * density;
            _corner.StrokeCap = Paint.Cap.Square;
        }

        public override void Draw(Canvas canvas)
        {
            var b = Bounds;
            var r = new RectF(b.Left + _d, b.Top + _d, b.Right - _d, b.Bottom - _d);
            var radius = 8 * _d;
            canvas.DrawRoundRect(r, radius, radius, _fill);
            canvas.DrawRoundRect(r, radius, radius, _rim);
            var len = 14 * _d;
            var inset = 3 * _d;
            float l = r.Left + inset, t = r.Top + inset, rr = r.Right - inset, bb = r.Bottom - inset;
            canvas.DrawLine(l, t + len, l, t + radius * 0.4f, _corner);
            canvas.DrawLine(l + radius * 0.4f, t, l + len, t, _corner);
            canvas.DrawLine(rr, t + len, rr, t + radius * 0.4f, _corner);
            canvas.DrawLine(rr - radius * 0.4f, t, rr - len, t, _corner);
            canvas.DrawLine(l, bb - len, l, bb - radius * 0.4f, _corner);
            canvas.DrawLine(l + radius * 0.4f, bb, l + len, bb, _corner);
            canvas.DrawLine(rr, bb - len, rr, bb - radius * 0.4f, _corner);
            canvas.DrawLine(rr - radius * 0.4f, bb, rr - len, bb, _corner);
        }

        public override void SetAlpha(int alpha) => _fill.Alpha = alpha;
        public override void SetColorFilter(ColorFilter colorFilter) => _fill.SetColorFilter(colorFilter);
#pragma warning disable CS0672, CS0618
        public override int Opacity => (int)Format.Translucent;
#pragma warning restore CS0672, CS0618

        public override void GetOutline(Outline outline)
        {
            outline.SetRoundRect(Bounds, 8 * _d);
        }
    }

    /// <summary>HP / Qi / progress bar: dark trough, glossy gradient fill, gold rim, caption inside.</summary>
    public sealed class MeterView : View
    {
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly float _d;
        private readonly Color _color;
        private float _shown;
        private float _target;
        private float _ghost;

        public MeterView(Context ctx, Color color) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _color = color;
            _text.TextSize = 12 * _d;
            _text.SetTypeface(Ui.Display);
            _text.Color = Ui.Ink;
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(3, 0, 1, Color.Black);
        }

        public string Text { get; set; }

        public void SetValue(double value, double max)
        {
            var f = max <= 0 ? 0f : (float)Math.Clamp(value / max, 0, 1);
            if (f < _target) _ghost = Math.Max(_ghost, _shown); // keep a fading "damage taken" trail
            _target = f;
            PostInvalidateOnAnimation();
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var w = Width;
            var h = Height;
            var r = h / 2f;
            var rect = new RectF(0, 0, w, h);
            _p.SetShader(null);
            _p.SetStyle(Paint.Style.Fill);
            _p.Color = Color.Argb(230, 6, 8, 10);
            canvas.DrawRoundRect(rect, r, r, _p);

            // ease towards the target value
            _shown += (_target - _shown) * 0.18f;
            if (Math.Abs(_target - _shown) < 0.002f) _shown = _target;
            if (_ghost > _shown) _ghost = Math.Max(_shown, _ghost - 0.01f);

            var inset = 2 * _d;
            if (_ghost > _shown)
            {
                _p.Color = Color.Argb(170, 240, 220, 170);
                canvas.DrawRoundRect(new RectF(inset, inset, inset + (w - 2 * inset) * _ghost, h - inset), r, r, _p);
            }

            if (_shown > 0.001f)
            {
                var fw = (w - 2 * inset) * _shown;
                var light = Color.Argb(255, Math.Min(255, _color.R + 60), Math.Min(255, _color.G + 60), Math.Min(255, _color.B + 60));
                var dark = Color.Argb(255, _color.R / 2, _color.G / 2, _color.B / 2);
                _p.SetShader(new LinearGradient(0, 0, 0, h, new int[] { light.ToArgb(), _color.ToArgb(), dark.ToArgb() }, new float[] { 0f, 0.45f, 1f }, Shader.TileMode.Clamp));
                canvas.DrawRoundRect(new RectF(inset, inset, inset + fw, h - inset), r, r, _p);
                _p.SetShader(null);
            }

            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 1.2f * _d;
            _p.Color = Ui.GoldDim;
            canvas.DrawRoundRect(new RectF(_d * 0.6f, _d * 0.6f, w - _d * 0.6f, h - _d * 0.6f), r, r, _p);
            if (!string.IsNullOrEmpty(Text)) canvas.DrawText(Text, w / 2f, h / 2f - (_text.Descent() + _text.Ascent()) / 2, _text);
            if (_shown != _target || _ghost > _shown) PostInvalidateOnAnimation();
        }
    }

    /// <summary>Lays children left to right and wraps to new lines (chips, small cards).</summary>
    public sealed class FlowLayout : ViewGroup
    {
        public FlowLayout(Context ctx) : base(ctx)
        {
        }

        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            var maxW = MeasureSpec.GetSize(widthMeasureSpec);
            int x = 0, y = 0, lineH = 0;
            for (var i = 0; i < ChildCount; i++)
            {
                var c = GetChildAt(i);
                MeasureChild(c, MeasureSpec.MakeMeasureSpec(maxW, MeasureSpecMode.AtMost), heightMeasureSpec);
                var lp = c.LayoutParameters as MarginLayoutParams;
                var cw = c.MeasuredWidth + (lp?.RightMargin ?? 0);
                var ch = c.MeasuredHeight + (lp?.TopMargin ?? 0);
                if (x + cw > maxW && x > 0)
                {
                    x = 0;
                    y += lineH;
                    lineH = 0;
                }

                x += cw;
                lineH = Math.Max(lineH, ch);
            }

            SetMeasuredDimension(maxW, y + lineH);
        }

        protected override void OnLayout(bool changed, int l, int t, int r, int b)
        {
            var maxW = r - l;
            int x = 0, y = 0, lineH = 0;
            for (var i = 0; i < ChildCount; i++)
            {
                var c = GetChildAt(i);
                var lp = c.LayoutParameters as MarginLayoutParams;
                var cw = c.MeasuredWidth + (lp?.RightMargin ?? 0);
                var ch = c.MeasuredHeight + (lp?.TopMargin ?? 0);
                if (x + cw > maxW && x > 0)
                {
                    x = 0;
                    y += lineH;
                    lineH = 0;
                }

                c.Layout(x, y + (lp?.TopMargin ?? 0), x + c.MeasuredWidth, y + (lp?.TopMargin ?? 0) + c.MeasuredHeight);
                x += cw;
                lineH = Math.Max(lineH, ch);
            }
        }

        protected override LayoutParams GenerateLayoutParams(LayoutParams p) => new MarginLayoutParams(p);
        public override LayoutParams GenerateLayoutParams(global::Android.Util.IAttributeSet attrs) => new MarginLayoutParams(Context, attrs);
        protected override LayoutParams GenerateDefaultLayoutParams() => new MarginLayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        protected override bool CheckLayoutParams(LayoutParams p) => p is MarginLayoutParams;
    }
}
