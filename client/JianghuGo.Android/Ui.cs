using System;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace JianghuGo.Android
{
    /// <summary>Builds the screens from code (no XML layouts): ink on paper with cinnabar seals.</summary>
    public sealed class Ui
    {
        public static readonly Color Paper = Color.Rgb(237, 227, 204);
        public static readonly Color PaperDark = Color.Rgb(226, 212, 182);
        public static readonly Color Ink = Color.Rgb(31, 26, 23);
        public static readonly Color Cinnabar = Color.Rgb(179, 41, 31);
        public static readonly Color Jade = Color.Rgb(66, 125, 64);

        private readonly Context _ctx;
        private readonly float _density;

        public Ui(Context ctx, LinearLayout content)
        {
            _ctx = ctx;
            _density = ctx.Resources.DisplayMetrics.Density;
            Content = content;
        }

        public LinearLayout Content { get; }

        public int Dp(float dp) => (int)(dp * _density + 0.5f);

        private LinearLayout.LayoutParams Row(int height = ViewGroup.LayoutParams.WrapContent, int top = 6)
        {
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, height);
            lp.TopMargin = Dp(top);
            return lp;
        }

        public TextView Label(string text, float sp = 16, Color? color = null, bool bold = false, ViewGroup parent = null)
        {
            var t = new TextView(_ctx) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
            t.SetTextColor(color ?? Ink);
            if (bold) t.SetTypeface(Typeface.Serif, TypefaceStyle.Bold);
            else t.SetTypeface(Typeface.Serif, TypefaceStyle.Normal);
            (parent ?? Content).AddView(t, Row());
            return t;
        }

        public TextView Heading(string text) => Label(text, 20, Cinnabar, bold: true);

        public EditText Field(string hint, bool password = false, bool number = false, string value = null)
        {
            var e = new EditText(_ctx) { Hint = hint, Text = value ?? string.Empty };
            e.SetSingleLine(true);
            e.SetTextColor(Ink);
            e.SetHintTextColor(Color.Rgb(130, 120, 105));
            e.InputType = password ? InputTypes.ClassText | InputTypes.TextVariationPassword
                : number ? InputTypes.ClassNumber
                : InputTypes.ClassText;
            var bg = new GradientDrawable();
            bg.SetColor(Color.Rgb(250, 246, 236));
            bg.SetStroke(Dp(1), Color.Rgb(160, 145, 120));
            bg.SetCornerRadius(Dp(4));
            e.Background = bg;
            e.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
            Content.AddView(e, Row());
            return e;
        }

        public Button Action(string caption, System.Action onClick, bool secondary = false, ViewGroup parent = null)
        {
            var b = new Button(_ctx) { Text = caption };
            b.SetAllCaps(false);
            b.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 16);
            b.SetTextColor(secondary ? Ink : Paper);
            var bg = new GradientDrawable();
            bg.SetColor(secondary ? PaperDark : Cinnabar);
            bg.SetCornerRadius(Dp(6));
            if (secondary) bg.SetStroke(Dp(1), Color.Rgb(160, 145, 120));
            b.Background = bg;
            b.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
            b.Click += (_, __) => onClick();
            (parent ?? Content).AddView(b, Row(top: 8));
            return b;
        }

        /// <summary>A labelled horizontal bar (HP, Qi, cultivation progress).</summary>
        public (TextView Label, ProgressBar Bar) Meter(string caption, Color color)
        {
            var label = Label(caption, 14);
            var bar = new ProgressBar(_ctx, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 1000 };
            bar.ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(color);
            bar.ProgressBackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Color.Rgb(200, 186, 160));
            Content.AddView(bar, Row(Dp(12), 2));
            return (label, bar);
        }

        public static void SetMeter((TextView Label, ProgressBar Bar) m, string caption, double value, double max)
        {
            m.Label.Text = caption;
            m.Bar.Progress = max <= 0 ? 0 : (int)Math.Clamp(value / max * 1000, 0, 1000);
        }

        public LinearLayout Column()
        {
            var col = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            Content.AddView(col, Row());
            return col;
        }

        public void Divider()
        {
            var v = new View(_ctx);
            v.SetBackgroundColor(Color.Rgb(190, 175, 145));
            Content.AddView(v, Row(Dp(1), 12));
        }

        public void Add(View view, int heightDp)
        {
            Content.AddView(view, Row(Dp(heightDp), 8));
        }
    }
}
