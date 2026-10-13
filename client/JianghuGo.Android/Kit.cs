using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace JianghuGo.Android
{
    /// <summary>
    /// Fixed-viewport building blocks: every screen is laid out with weights so it fits one phone screen without
    /// scrolling; long lists become swipeable pages; details open in sheets.
    /// </summary>
    public sealed partial class Ui
    {
        /// <summary>A container that takes a share (<paramref name="weight"/>) of its parent's free space.</summary>
        public LinearLayout Box(ViewGroup parent = null, float weight = 1f, bool horizontal = false, int marginDp = 6)
        {
            parent ??= Content;
            var box = new LinearLayout(_ctx) { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
            AddWeighted(parent, box, weight, marginDp);
            return box;
        }

        /// <summary>Adds a view sized by weight along the parent's orientation (fills the other axis).</summary>
        public void AddWeighted(ViewGroup parent, View view, float weight, int marginDp = 6)
        {
            if (parent is LinearLayout ll && ll.Orientation == Orientation.Horizontal)
            {
                var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, weight);
                if (ll.ChildCount > 0) lp.LeftMargin = Dp(marginDp);
                parent.AddView(view, lp);
            }
            else if (parent is LinearLayout)
            {
                var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, weight);
                if (parent.ChildCount > 0) lp.TopMargin = Dp(marginDp);
                parent.AddView(view, lp);
            }
            else
            {
                parent.AddView(view, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }
        }

        /// <summary>Adds a fixed-height view (dp) to a vertical parent, or a weighted one to a horizontal parent.</summary>
        public void AddFixed(ViewGroup parent, View view, int heightDp, int marginDp = 6, float weightInRow = 1f)
        {
            if (parent is LinearLayout ll && ll.Orientation == Orientation.Horizontal)
            {
                var lp = new LinearLayout.LayoutParams(0, Dp(heightDp), weightInRow) { Gravity = GravityFlags.CenterVertical };
                if (ll.ChildCount > 0) lp.LeftMargin = Dp(marginDp);
                parent.AddView(view, lp);
            }
            else
            {
                var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(heightDp));
                if (parent.ChildCount > 0) lp.TopMargin = Dp(marginDp);
                parent.AddView(view, lp);
            }
        }

        /// <summary>A horizontal row of fixed height whose children share the width.</summary>
        public LinearLayout HRow(ViewGroup parent, int heightDp, int marginDp = 6)
        {
            var row = new LinearLayout(_ctx) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            AddFixed(parent, row, heightDp, marginDp);
            return row;
        }

        /// <summary>A compact button sized for fixed layouts (fixed height in a column, a share of the width in a row).</summary>
        public Button Btn(string caption, System.Action onClick, ViewGroup parent, bool secondary = false, float sp = 14, int heightDp = 44, string icon = null)
        {
            var b = new Button(_ctx) { Text = caption };
            b.SetAllCaps(false);
            b.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
            b.SetTypeface(Display, TypefaceStyle.Bold);
            b.SetTextColor(secondary ? Gold : Ink);
            b.SetShadowLayer(2, 0, 1, Color.Argb(220, 0, 0, 0));
            b.Background = ButtonBackground(secondary);
            b.StateListAnimator = null;
            b.SetPadding(Dp(8), Dp(2), Dp(8), Dp(2));
            b.SetMinHeight(0);
            b.SetMinimumHeight(0);
            b.SetMaxLines(2);
            b.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            b.Click += (_, __) =>
            {
                b.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                Sfx.Play(Sfx.Click, 0.5f);
                onClick();
            };
            if (icon != null) WithIcon(b, icon, Math.Min(28, heightDp - 14));
            AddFixed(parent, b, heightDp);
            return b;
        }

        /// <summary>A text line that never grows the layout: one or a few lines, ellipsized.</summary>
        public TextView Text(string text, ViewGroup parent, float sp = 13, Color? color = null, bool bold = false, int maxLines = 1, bool center = false)
        {
            var t = new TextView(_ctx) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
            t.SetTextColor(color ?? Ink);
            t.SetTypeface(bold ? Display : Body, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            t.SetShadowLayer(3, 0, 1, Color.Argb(200, 0, 0, 0));
            t.SetMaxLines(maxLines);
            t.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            if (center) t.Gravity = GravityFlags.Center;
            var lp = parent is LinearLayout ll && ll.Orientation == Orientation.Horizontal
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical }
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(2) };
            parent.AddView(t, lp);
            return t;
        }

        /// <summary>A framed card that fills its cell (use inside weighted boxes and pager cells).</summary>
        public LinearLayout Card(ViewGroup parent, float weight = 1f, int padDp = 10, Color? rim = null, int marginDp = 6)
        {
            var p = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            p.Background = new FrameDrawable(_density, PanelFill, rim ?? GoldDim, rim ?? Gold);
            p.SetPadding(Dp(padDp), Dp(padDp - 2), Dp(padDp), Dp(padDp - 2));
            AddWeighted(parent, p, weight, marginDp);
            return p;
        }

        /// <summary>A picture card filling its cell: art (cropped), dark gradient, title and subtitle, the whole card clickable.</summary>
        public FrameLayout TileIn(ViewGroup cell, string title, string subtitle, int artRes, System.Action onClick, bool pixelArt = false, Color? accent = null, float titleSp = 14, string badge = null)
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
                var ilp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
                if (pixelArt) ilp.BottomMargin = Dp(titleSp * 2.2f);
                tile.AddView(img, ilp);
            }

            var shade = new View(_ctx);
            shade.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Argb(0, 0, 0, 0).ToArgb(), Color.Argb(40, 0, 0, 0).ToArgb(), Color.Argb(235, 6, 8, 10).ToArgb() });
            tile.AddView(shade, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            var text = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            text.SetPadding(Dp(8), Dp(4), Dp(8), Dp(6));
            var t = new TextView(_ctx) { Text = title };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, titleSp);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(Ink);
            t.SetShadowLayer(4, 0, 1, Color.Black);
            t.SetMaxLines(2);
            t.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            text.AddView(t);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = new TextView(_ctx) { Text = subtitle };
                s.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 11);
                s.SetTypeface(Body, TypefaceStyle.Normal);
                s.SetTextColor(Gold);
                s.SetShadowLayer(3, 0, 1, Color.Black);
                s.SetMaxLines(1);
                s.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
                text.AddView(s);
            }

            tile.AddView(text, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));
            if (badge != null)
            {
                var bdg = Badge(badge);
                tile.AddView(bdg, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.End) { TopMargin = Dp(4), RightMargin = Dp(4) });
            }

            tile.Click += (_, __) =>
            {
                tile.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                Sfx.Play(Sfx.Click, 0.5f);
                onClick();
            };
            tile.Touch += (_, e) =>
            {
                if (e.Event.Action == MotionEventActions.Down) tile.Animate().ScaleX(0.96f).ScaleY(0.96f).SetDuration(80).Start();
                else if (e.Event.Action == MotionEventActions.Up || e.Event.Action == MotionEventActions.Cancel) tile.Animate().ScaleX(1f).ScaleY(1f).SetDuration(120).Start();
                e.Handled = false;
            };
            cell.AddView(tile, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            return tile;
        }

        /// <summary>A technique key for the duel grid: icon on top, short name and Qi cost under it.</summary>
        public LinearLayout SkillTile(ViewGroup row, string icon, string name, string cost, bool primary, System.Action onClick, int heightDp = 58)
        {
            var tile = new LinearLayout(_ctx) { Orientation = Orientation.Vertical, Clickable = true, Focusable = true };
            tile.SetGravity(GravityFlags.Center);
            tile.Background = ButtonBackground(!primary);
            tile.SetPadding(Dp(2), Dp(3), Dp(2), Dp(2));
            var img = new ImageView(_ctx);
            var id = ArtId(icon);
            if (id != 0) img.SetImageBitmap(Art(_ctx, id));
            img.SetScaleType(ImageView.ScaleType.FitCenter);
            tile.AddView(img, new LinearLayout.LayoutParams(Dp(26), Dp(26)));
            var t = new TextView(_ctx) { Text = name, Gravity = GravityFlags.Center };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 9);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(primary ? Ink : Gold);
            t.SetMaxLines(1);
            t.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            t.SetIncludeFontPadding(false);
            tile.AddView(t, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(2) });
            if (!string.IsNullOrEmpty(cost))
            {
                var c = new TextView(_ctx) { Text = cost, Gravity = GravityFlags.Center };
                c.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 9);
                c.SetTextColor(QiBlue);
                c.SetIncludeFontPadding(false);
                tile.AddView(c, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            }

            tile.Click += (_, __) =>
            {
                tile.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                Sfx.Play(Sfx.Click, 0.5f);
                onClick();
            };
            AddFixed(row, tile, heightDp);
            return tile;
        }

        /// <summary>Small red counter bubble.</summary>
        public TextView Badge(string text)
        {
            var t = new TextView(_ctx) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 10);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(Color.White);
            t.Gravity = GravityFlags.Center;
            var bg = new GradientDrawable();
            bg.SetColor(Cinnabar);
            bg.SetStroke(Dp(1), Gold);
            bg.SetCornerRadius(Dp(9));
            t.Background = bg;
            t.SetMinWidth(Dp(18));
            t.SetPadding(Dp(5), Dp(1), Dp(5), Dp(1));
            return t;
        }

        /// <summary>Segmented tabs (one row). Returns the row; <paramref name="onSelect"/> gets the tab index.</summary>
        public LinearLayout Tabs(ViewGroup parent, string[] labels, int selected, Action<int> onSelect, int heightDp = 38, int[] badges = null)
        {
            var row = HRow(parent, heightDp, 4);
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var frame = new FrameLayout(_ctx);
                var b = new TextView(_ctx) { Text = labels[i], Gravity = GravityFlags.Center };
                b.SetTextSize(global::Android.Util.ComplexUnitType.Sp, labels.Length > 4 ? 11 : 12);
                b.SetTypeface(Display, TypefaceStyle.Bold);
                b.SetTextColor(i == selected ? Night : Gold);
                b.SetMaxLines(1);
                b.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
                var bg = new GradientDrawable();
                if (i == selected)
                {
                    bg.SetColors(new int[] { Color.Rgb(236, 204, 130).ToArgb(), Gold.ToArgb() });
                }
                else
                {
                    bg.SetColor(Color.Argb(220, 16, 20, 24));
                }

                bg.SetStroke(Dp(1), i == selected ? Gold : GoldDim);
                bg.SetCornerRadius(Dp(heightDp / 2f));
                b.Background = bg;
                b.Clickable = true;
                b.Click += (_, __) =>
                {
                    if (index == selected) return;
                    b.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                    Sfx.Play(Sfx.Click, 0.5f);
                    onSelect(index);
                };
                frame.AddView(b, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                if (badges != null && i < badges.Length && badges[i] > 0)
                    frame.AddView(Badge(badges[i].ToString()), new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.End));
                var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
                if (i > 0) lp.LeftMargin = Dp(4);
                row.AddView(frame, lp);
            }

            return row;
        }

        /// <summary>Paged grid (no scrolling): <paramref name="columns"/>×rows cells per page, swipe or arrows to turn pages.</summary>
        public Pager Pages(ViewGroup parent, int count, int columns, int rows, Action<int, LinearLayout> build, float weight = 1f, string empty = null, int startPage = 0)
        {
            var pager = new Pager(this, count, columns, rows, build, empty, startPage);
            AddWeighted(parent, pager, weight);
            return pager;
        }

        /// <summary>A meter that fills a row.</summary>
        public MeterView MeterIn(ViewGroup parent, Color color, string caption, double value, double max, int heightDp = 16)
        {
            var m = new MeterView(_ctx, color);
            SetMeter(m, caption, value, max);
            AddFixed(parent, m, heightDp, 4);
            return m;
        }

        /// <summary>A square icon (art by name) of a fixed size.</summary>
        public ImageView Icon(ViewGroup parent, string artName, int sizeDp, bool pixel = false)
        {
            var img = new ImageView(_ctx);
            var id = ArtId(artName);
            if (id != 0)
            {
                var d = new BitmapDrawable(_ctx.Resources, Art(_ctx, id));
                d.SetFilterBitmap(!pixel);
                img.SetImageDrawable(d);
            }

            img.SetScaleType(ImageView.ScaleType.FitCenter);
            var lp = new LinearLayout.LayoutParams(Dp(sizeDp), Dp(sizeDp)) { Gravity = GravityFlags.CenterVertical };
            if (parent.ChildCount > 0) lp.LeftMargin = Dp(6);
            parent.AddView(img, lp);
            return img;
        }
    }

    /// <summary>
    /// Swipeable pages of a fixed grid. The page area takes the space it is given; a dots row with arrows sits under
    /// it. Nothing scrolls: a page shows at most columns × rows cells.
    /// </summary>
    public sealed class Pager : LinearLayout
    {
        private readonly Ui _ui;
        private readonly int _count, _columns, _rows;
        private readonly Action<int, LinearLayout> _build;
        private readonly SwipeFrame _host;
        private readonly LinearLayout _dots;
        private readonly TextView _prev, _next;
        private readonly string _empty;

        public Pager(Ui ui, int count, int columns, int rows, Action<int, LinearLayout> build, string empty, int startPage) : base(ui.Context)
        {
            _ui = ui;
            _count = count;
            _columns = Math.Max(1, columns);
            _rows = Math.Max(1, rows);
            _build = build;
            _empty = empty;
            Orientation = Orientation.Vertical;
            _host = new SwipeFrame(ui.Context, ui.Dp(36)) { Swiped = dir => Go(Page + dir, dir) };
            AddView(_host, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            var nav = new LinearLayout(ui.Context) { Orientation = Orientation.Horizontal };
            nav.SetGravity(GravityFlags.Center);
            _prev = Arrow("‹", () => Go(Page - 1, -1));
            _next = Arrow("›", () => Go(Page + 1, 1));
            _dots = new LinearLayout(ui.Context) { Orientation = Orientation.Horizontal };
            _dots.SetGravity(GravityFlags.Center);
            nav.AddView(_prev, new LayoutParams(ui.Dp(44), ui.Dp(28)));
            nav.AddView(_dots, new LayoutParams(0, ui.Dp(28), 1f));
            nav.AddView(_next, new LayoutParams(ui.Dp(44), ui.Dp(28)));
            AddView(nav, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ui.Dp(28)));
            nav.Visibility = PageCount > 1 ? ViewStates.Visible : ViewStates.Gone;
            Page = Math.Clamp(startPage, 0, PageCount - 1);
            Render(0);
        }

        public int Page { get; private set; }
        public int PerPage => _columns * _rows;
        public int PageCount => Math.Max(1, (_count + PerPage - 1) / PerPage);

        private TextView Arrow(string glyph, System.Action onClick)
        {
            var t = new TextView(_ui.Context) { Text = glyph, Gravity = GravityFlags.Center, Clickable = true };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 20);
            t.SetIncludeFontPadding(false);
            t.SetTextColor(Ui.Gold);
            t.Click += (_, __) => onClick();
            return t;
        }

        public void Go(int page, int dir)
        {
            if (page < 0 || page >= PageCount || page == Page) return;
            Page = page;
            Sfx.Play(Sfx.Click, 0.35f);
            Render(dir);
        }

        private void Render(int dir)
        {
            _host.RemoveAllViews();
            var grid = new LinearLayout(_ui.Context) { Orientation = Orientation.Vertical };
            if (_count == 0 && _empty != null)
            {
                var t = new TextView(_ui.Context) { Text = _empty, Gravity = GravityFlags.Center };
                t.SetTextColor(Ui.Muted);
                t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
                t.SetTypeface(Ui.Body, TypefaceStyle.Italic);
                grid.AddView(t, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }
            else
            {
                for (var r = 0; r < _rows; r++)
                {
                    var row = new LinearLayout(_ui.Context) { Orientation = Orientation.Horizontal };
                    var rlp = new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f);
                    if (r > 0) rlp.TopMargin = _ui.Dp(6);
                    grid.AddView(row, rlp);
                    for (var c = 0; c < _columns; c++)
                    {
                        var cell = new LinearLayout(_ui.Context) { Orientation = Orientation.Vertical };
                        var clp = new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
                        if (c > 0) clp.LeftMargin = _ui.Dp(6);
                        row.AddView(cell, clp);
                        var index = Page * PerPage + r * _columns + c;
                        if (index < _count) _build(index, cell);
                    }
                }
            }

            _host.AddView(grid, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            if (dir != 0)
            {
                grid.TranslationX = dir * _ui.Dp(60);
                grid.Alpha = 0f;
                grid.Animate().TranslationX(0).Alpha(1f).SetDuration(200).Start();
            }

            _dots.RemoveAllViews();
            for (var i = 0; i < PageCount; i++)
            {
                var dot = new View(_ui.Context);
                var bg = new GradientDrawable();
                bg.SetShape(ShapeType.Oval);
                bg.SetColor(i == Page ? Ui.Gold : Ui.GoldDim);
                dot.Background = bg;
                var size = _ui.Dp(i == Page ? 9 : 6);
                var lp = new LayoutParams(size, size) { LeftMargin = _ui.Dp(4), RightMargin = _ui.Dp(4), Gravity = GravityFlags.CenterVertical };
                _dots.AddView(dot, lp);
            }

            _prev.Alpha = Page > 0 ? 1f : 0.25f;
            _next.Alpha = Page < PageCount - 1 ? 1f : 0.25f;
        }
    }

    /// <summary>A frame that turns a horizontal swipe over its children into a page turn (taps still reach them).</summary>
    public sealed class SwipeFrame : FrameLayout
    {
        private readonly int _threshold;
        private float _x, _y;
        private bool _dragging;

        public SwipeFrame(Context ctx, int thresholdPx) : base(ctx) => _threshold = thresholdPx;

        public Action<int> Swiped { get; set; }

        public override bool OnInterceptTouchEvent(MotionEvent e)
        {
            switch (e.Action)
            {
                case MotionEventActions.Down:
                    _x = e.GetX();
                    _y = e.GetY();
                    _dragging = false;
                    break;
                case MotionEventActions.Move:
                    var dx = e.GetX() - _x;
                    var dy = e.GetY() - _y;
                    if (Math.Abs(dx) > _threshold / 2f && Math.Abs(dx) > Math.Abs(dy) * 1.5f) _dragging = true;
                    break;
            }

            return _dragging;
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down)
            {
                _x = e.GetX();
                return true;
            }

            if (e.Action == MotionEventActions.Up || e.Action == MotionEventActions.Cancel)
            {
                var dx = e.GetX() - _x;
                _dragging = false;
                if (Math.Abs(dx) >= _threshold) Swiped?.Invoke(dx < 0 ? 1 : -1);
                return true;
            }

            return true;
        }
    }
}
