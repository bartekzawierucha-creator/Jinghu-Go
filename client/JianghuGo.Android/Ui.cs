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
    public sealed partial class Ui
    {
        // palette
        public static readonly Color Night = Color.Rgb(236, 231, 220);
        public static readonly Color Paper = Color.Rgb(236, 231, 220);       // screen background (rice paper)
        public static readonly Color PaperDark = Color.Rgb(222, 216, 203);   // inset background
        public static readonly Color PanelFill = Color.Argb(232, 245, 241, 232);
        public static readonly Color Ink = Color.Rgb(30, 28, 26);            // main text (ink on paper)
        public static readonly Color Muted = Color.Rgb(110, 104, 96);
        public static readonly Color Gold = Color.Rgb(158, 38, 28);
        public static readonly Color GoldDim = Color.Rgb(84, 80, 74);
        public static readonly Color Cinnabar = Color.Rgb(178, 40, 30);
        public static readonly Color CinnabarDeep = Color.Rgb(118, 22, 16);
        public static readonly Color Jade = Color.Rgb(40, 118, 80);
        public static readonly Color QiBlue = Color.Rgb(38, 96, 160);
        public static readonly Color Violet = Color.Rgb(104, 58, 150);

        private static readonly Dictionary<int, Bitmap> Bitmaps = new Dictionary<int, Bitmap>();
        private readonly Context _ctx;
        private readonly float _density;

        public Ui(Context ctx, LinearLayout content)
        {
            _ctx = ctx;
            _density = ctx.Resources.DisplayMetrics.Density;
            Content = content;
        }

        public LinearLayout Content { get; set; }
        public Context Context => _ctx;

        public int Dp(float dp) => (int)(dp * _density + 0.5f);

        private static Typeface _display, _displayMedium;

        /// <summary>Cinzel (OFL, bundled) for titles, buttons and numbers; the system serif for reading text.</summary>
        public static Typeface Display => _display ?? Typeface.Create(Typeface.Serif, TypefaceStyle.Bold);
        public static Typeface DisplayMedium => _displayMedium ?? Display;
        // Body text in a clean sans-serif (readable at small sizes); the Cinzel display face is for headings and titles.
        public static Typeface Body => Typeface.Create("sans-serif", TypefaceStyle.Normal);

        public static void LoadFonts(Context ctx)
        {
            try
            {
                _display = Typeface.CreateFromAsset(ctx.Assets, "fonts/Cinzel-Bold.ttf");
                _displayMedium = Typeface.CreateFromAsset(ctx.Assets, "fonts/Cinzel-Medium.ttf");
            }
            catch (Exception)
            {
                _display = null; // fall back to the system serif
            }
        }

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
            t.SetShadowLayer(3, 0, 1, Color.Argb(170, 250, 247, 240));
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
            bg.SetColor(Color.Argb(200, 244, 240, 231));
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
            b.SetShadowLayer(2, 0, 1, Color.Argb(170, 250, 247, 240));
            b.Background = ButtonBackground(secondary);
            b.StateListAnimator = null;
            b.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
            b.Click += (_, __) => onClick();
            (parent ?? Content).AddView(b, Row(top: 8));
            return b;
        }

        public Drawable ButtonBackground(bool secondary)
        {
            var states = new StateListDrawable();
            states.AddState(new[] { global::Android.Resource.Attribute.StatePressed }, new ButtonDrawable(_density, secondary, true));
            states.AddState(new int[0], new ButtonDrawable(_density, secondary, false));
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
        public FrameLayout Tile(string title, string subtitle, int artRes, System.Action onClick, ViewGroup parent = null, int heightDp = 120, bool pixelArt = false, Color? accent = null, float titleSp = 17)
        {
            var tile = new FrameLayout(_ctx) { Clickable = true, Focusable = true, ClipToOutline = true };
            tile.Background = new FrameDrawable(_density, Color.Argb(255, 244, 240, 231), accent ?? GoldDim, accent ?? Gold);
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
            shade.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Argb(0, 0, 0, 0).ToArgb(), Color.Argb(60, 0, 0, 0).ToArgb(), Color.Argb(235, 244, 240, 231).ToArgb() });
            tile.AddView(shade, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var text = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
            text.SetPadding(Dp(12), Dp(8), Dp(12), Dp(10));
            var t = new TextView(_ctx) { Text = title };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, titleSp);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(Ink);
            t.SetShadowLayer(4, 0, 1, Color.Argb(170, 250, 247, 240));
            t.BreakStrategy = global::Android.Text.BreakStrategy.Simple;
            t.HyphenationFrequency = global::Android.Text.HyphenationFrequency.None;
            text.AddView(t);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = new TextView(_ctx) { Text = subtitle };
                s.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
                s.SetTypeface(Body, TypefaceStyle.Normal);
                s.SetTextColor(Gold);
                s.SetShadowLayer(3, 0, 1, Color.Argb(170, 250, 247, 240));
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

        /// <summary>Drawable id by name ("item_jade"), or 0 when the art is missing.</summary>
        public int ArtId(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (InkAlias.TryGetValue(name, out var ink)) name = ink;
            return _ctx.Resources.GetIdentifier(name, "drawable", _ctx.PackageName);
        }

        /// <summary>Old gold/pixel menu icons mapped to the brush-ink set (2.2 paper theme).</summary>
        public static readonly Dictionary<string, string> InkAlias = new Dictionary<string, string>
        {
            ["menu_meditate"] = "ink_nav_home", ["menu_explore"] = "ink_nav_world", ["menu_battle"] = "ink_nav_combat",
            ["menu_hero"] = "ink_nav_hero", ["menu_sect"] = "ink_nav_sect", ["menu_bag"] = "ink_nav_bag",
            ["menu_chat"] = "ink_nav_social", ["gi_friends"] = "ink_nav_social", ["gi_mail"] = "ink_nav_social",
            ["menu_quests"] = "ink_ico_quest", ["menu_scroll"] = "ink_ico_quest", ["gi_orders"] = "ink_ico_quest",
            ["menu_market"] = "ink_ico_market", ["menu_daolab"] = "ink_ico_alchemy", ["menu_forge"] = "ink_ico_forge",
            ["menu_meridians"] = "ink_ico_meridian", ["menu_pet"] = "ink_ico_beast", ["gi_evolve"] = "ink_ico_beast",
            ["menu_settings"] = "ink_ico_settings", ["gi_settings"] = "ink_ico_settings",
            ["menu_tower"] = "ink_ico_tower", ["gi_pagoda"] = "ink_ico_tower", ["menu_ranking"] = "ink_ico_ranking",
            ["menu_breakthrough"] = "ink_ico_dao", ["menu_notifications"] = "ink_ico_event", ["menu_checkin"] = "ink_ico_journey",
            ["menu_war"] = "ink_ico_demon", ["menu_worldboss"] = "ink_ico_demon", ["gi_wanted"] = "ink_ico_hunt", ["gi_bounty"] = "ink_ico_hunt",
            ["menu_donate"] = "ink_ico_shop", ["menu_tribute"] = "ink_ico_shop", ["gi_pass"] = "ink_ico_achievement",
            ["menu_territory"] = "ink_ico_place", ["gi_chronicle"] = "ink_ico_achievement",
            // techniques: 61 techniques share 25 brush glyphs
            ["tech_palm_strike"] = "ink_tech_palm",
            ["tech_mirror_palm"] = "ink_tech_palm",
            ["tech_nine_suns_palm"] = "ink_tech_palm",
            ["tech_sword_thrust"] = "ink_tech_sword",
            ["tech_sword_qi_wave"] = "ink_tech_sword",
            ["tech_sword_intent_wave"] = "ink_tech_sword",
            ["tech_dao_severing_sword"] = "ink_tech_sword",
            ["tech_qi_blast"] = "ink_tech_qi_blast",
            ["tech_spirit_lance"] = "ink_tech_qi_blast",
            ["tech_first_dawn_ray"] = "ink_tech_qi_blast",
            ["tech_flying_daggers"] = "ink_tech_daggers",
            ["tech_shadow_dart"] = "ink_tech_daggers",
            ["tech_iron_fist"] = "ink_tech_fist",
            ["tech_dragon_elephant_fist"] = "ink_tech_fist",
            ["tech_titan_crush"] = "ink_tech_fist",
            ["tech_chaos_body_strike"] = "ink_tech_fist",
            ["tech_flame_palm"] = "ink_tech_flame",
            ["tech_inferno_burst"] = "ink_tech_flame",
            ["tech_ember_aura"] = "ink_tech_flame",
            ["tech_world_ending_flame"] = "ink_tech_flame",
            ["tech_venom_needle"] = "ink_tech_needle",
            ["tech_heart_rot_needle"] = "ink_tech_needle",
            ["tech_corpse_flower_sting"] = "ink_tech_needle",
            ["tech_flowing_water"] = "ink_tech_siphon",
            ["tech_qi_siphon"] = "ink_tech_siphon",
            ["tech_karma_reversal"] = "ink_tech_siphon",
            ["tech_iron_shirt"] = "ink_tech_shield",
            ["tech_golden_bell"] = "ink_tech_shield",
            ["tech_vajra_body"] = "ink_tech_shield",
            ["tech_immovable_mountain"] = "ink_tech_shield",
            ["tech_primordial_shell"] = "ink_tech_shield",
            ["tech_dao_barrier"] = "ink_tech_shield",
            ["tech_cloud_step"] = "ink_tech_step",
            ["tech_swallow_glide"] = "ink_tech_step",
            ["tech_ghost_step"] = "ink_tech_step",
            ["tech_void_step_guard"] = "ink_tech_step",
            ["tech_tiger_claw"] = "ink_tech_claw",
            ["tech_crane_beak_strike"] = "ink_tech_claw",
            ["tech_thunder_palm"] = "ink_tech_thunder",
            ["tech_mountain_splitter"] = "ink_tech_mountain",
            ["tech_heavenly_sword_rain"] = "ink_tech_sword_rain",
            ["tech_heaven_sword_array"] = "ink_tech_sword_rain",
            ["tech_phoenix_wing"] = "ink_tech_phoenix",
            ["tech_phoenix_rebirth"] = "ink_tech_phoenix",
            ["tech_phoenix_nirvana"] = "ink_tech_phoenix",
            ["tech_lotus_shield"] = "ink_tech_lotus",
            ["tech_sky_burning_lotus"] = "ink_tech_lotus",
            ["tech_miasma_cloud"] = "ink_tech_poison_cloud",
            ["tech_thousand_poisons"] = "ink_tech_poison_cloud",
            ["tech_myriad_venom_rain"] = "ink_tech_poison_cloud",
            ["tech_scorpion_tail"] = "ink_tech_scorpion",
            ["tech_gu_swarm"] = "ink_tech_gu",
            ["tech_ten_thousand_gu"] = "ink_tech_gu",
            ["tech_heavenly_seal"] = "ink_tech_seal",
            ["tech_thousand_world_seal"] = "ink_tech_talisman",
            ["tech_cloud_mirror"] = "ink_tech_mirror",
            ["tech_star_mirror"] = "ink_tech_mirror",
            ["tech_turtle_reversal"] = "ink_tech_mirror",
            ["tech_star_piercing_finger"] = "ink_tech_star",
            ["tech_smoke_veil"] = "ink_tech_smoke",
            ["tech_vajra_reincarnation"] = "ink_tech_reincarnation",
            ["tech_custom_attack"] = "ink_tech_palm",
            ["tech_custom_movement"] = "ink_tech_step",
            ["tech_custom_defense"] = "ink_tech_shield",
        };

        /// <summary>Puts an icon on the left of a label or button (no-op when the art is missing).</summary>
        public T WithIcon<T>(T view, string artName, float sizeDp = 34) where T : TextView
        {
            var id = ArtId(artName);
            if (id == 0) return view;
            var d = new BitmapDrawable(_ctx.Resources, Art(_ctx, id));
            d.SetBounds(0, 0, Dp(sizeDp), Dp(sizeDp));
            view.SetCompoundDrawables(d, null, null, null);
            view.CompoundDrawablePadding = Dp(10);
            view.Gravity = GravityFlags.CenterVertical | GravityFlags.Start;
            return view;
        }

        /// <summary>A small rounded tag (stat values, costs, rarities).</summary>
        public TextView Chip(string text, Color color, ViewGroup parent)
        {
            var t = new TextView(_ctx) { Text = text };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
            t.SetTypeface(Display, TypefaceStyle.Bold);
            t.SetTextColor(color);
            var bg = new GradientDrawable();
            bg.SetColor(Color.Argb(200, 244, 240, 231));
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

    /// <summary>
    /// Lacquer panel (2.1 look): soft drop shadow, a lit-from-above gradient, a bevel highlight, a double gold rim and
    /// diamond corner ornaments. Everything is drawn in code, so it scales to any size and density.
    /// </summary>
    public sealed class FrameDrawable : Drawable
    {
        private readonly Paint _fill = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _shadow = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _rim = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _inner = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _bevel = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _corner = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _gem = new Paint(PaintFlags.AntiAlias);
        private readonly Color _fillColor;
        private readonly float _d;

        public FrameDrawable(float density, Color fill, Color rim, Color corner)
        {
            _d = density;
            _fillColor = fill;
            _shadow.Color = Color.Argb(36, 0, 0, 0);
            // paper theme: frames are ink lines; the seal red is kept only for the small corner accents
            if (rim == Ui.Gold) rim = Color.Argb(190, 46, 43, 39);
            _rim.Color = rim;
            _rim.SetStyle(Paint.Style.Stroke);
            _rim.StrokeWidth = 1.3f * density;
            _inner.Color = Color.Argb(70, rim.R, rim.G, rim.B);
            _inner.SetStyle(Paint.Style.Stroke);
            _inner.StrokeWidth = 0.8f * density;
            _bevel.Color = Color.Argb(70, 255, 255, 255);
            _bevel.SetStyle(Paint.Style.Stroke);
            _bevel.StrokeWidth = 1f * density;
            _corner.Color = corner;
            _corner.SetStyle(Paint.Style.Stroke);
            _corner.StrokeWidth = 2f * density;
            _corner.StrokeCap = Paint.Cap.Round;
            _gem.Color = corner;
        }

        protected override void OnBoundsChange(Rect bounds)
        {
            base.OnBoundsChange(bounds);
            Color Shift(Color c, int k) => Color.Argb(c.A, Math.Clamp(c.R + k, 0, 255), Math.Clamp(c.G + k, 0, 255), Math.Clamp(c.B + k, 0, 255));
            _fill.SetShader(new LinearGradient(0, bounds.Top, 0, bounds.Bottom,
                new int[] { Shift(_fillColor, 16).ToArgb(), _fillColor.ToArgb(), Shift(_fillColor, -6).ToArgb() }, new float[] { 0f, 0.45f, 1f }, Shader.TileMode.Clamp));
        }

        public override void Draw(Canvas canvas)
        {
            var b = Bounds;
            var radius = 8 * _d;
            var r = new RectF(b.Left + _d, b.Top + _d, b.Right - _d, b.Bottom - (2.5f * _d));
            // drop shadow under the panel (inside our bounds)
            canvas.DrawRoundRect(new RectF(r.Left + _d, r.Top + (2 * _d), r.Right + (0.5f * _d), r.Bottom + (2 * _d)), radius, radius, _shadow);
            canvas.DrawRoundRect(r, radius, radius, _fill);
            // bevel: a faint lit edge along the top
            canvas.DrawLine(r.Left + radius, r.Top + (1.4f * _d), r.Right - radius, r.Top + (1.4f * _d), _bevel);
            canvas.DrawRoundRect(r, radius, radius, _rim);
            var i = 3.2f * _d;
            canvas.DrawRoundRect(new RectF(r.Left + i, r.Top + i, r.Right - i, r.Bottom - i), radius - (2 * _d), radius - (2 * _d), _inner);

            var len = 12 * _d;
            var inset = 3 * _d;
            float l = r.Left + inset, t = r.Top + inset, rr = r.Right - inset, bb = r.Bottom - inset;
            void Corner(float x, float y, float dx, float dy)
            {
                canvas.DrawLine(x, y + (dy * radius * 0.5f), x, y + (dy * len), _corner);
                canvas.DrawLine(x + (dx * radius * 0.5f), y, x + (dx * len), y, _corner);
                // a small diamond "gem" at the corner
                var g = 2.4f * _d;
                using var path = new Path();
                path.MoveTo(x + (dx * g), y);
                path.LineTo(x, y + (dy * g));
                path.LineTo(x - (dx * g * 0.2f), y - (dy * g * 0.2f));
                path.Close();
                canvas.DrawPath(path, _gem);
            }

            if (r.Width() > 3 * len && r.Height() > 2 * len)
            {
                Corner(l, t, 1, 1);
                Corner(rr, t, -1, 1);
                Corner(l, bb, 1, -1);
                Corner(rr, bb, -1, -1);
            }
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

    /// <summary>
    /// Lacquer button (2.1 look): cinnabar or night lacquer with a glossy upper half, a double gold rim and a warm glow when
    /// pressed.
    /// </summary>
    public sealed class ButtonDrawable : Drawable
    {
        private readonly Paint _fill = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _gloss = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _rim = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _inner = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _glow = new Paint(PaintFlags.AntiAlias);
        private readonly bool _secondary;
        private readonly bool _pressed;
        private readonly float _d;

        public ButtonDrawable(float density, bool secondary, bool pressed)
        {
            _d = density;
            _secondary = secondary;
            _pressed = pressed;
            _rim.SetStyle(Paint.Style.Stroke);
            _rim.StrokeWidth = (pressed ? 2f : 1.3f) * density;
            _rim.Color = secondary && !pressed ? Ui.GoldDim : Ui.Gold;
            _inner.SetStyle(Paint.Style.Stroke);
            _inner.StrokeWidth = 0.8f * density;
            _inner.Color = Color.Argb(secondary ? 40 : 70, 255, 255, 255);
            _glow.Color = Color.Argb(pressed ? 60 : 0, 178, 40, 30);
        }

        protected override void OnBoundsChange(Rect b)
        {
            base.OnBoundsChange(b);
            var top = _secondary ? Color.Argb(240, 248, 245, 238) : Color.Rgb(186, 52, 38);
            var bottom = _secondary ? Color.Argb(240, 244, 240, 231) : Ui.CinnabarDeep;
            if (_pressed)
            {
                (top, bottom) = (bottom, top);
            }

            // int[] overloads only: the two-colour overload taking ints binds to Java's packed @ColorLong one and crashes
            _fill.SetShader(new LinearGradient(0, b.Top, 0, b.Bottom, new int[] { top.ToArgb(), bottom.ToArgb() }, null, Shader.TileMode.Clamp));
            _gloss.SetShader(new LinearGradient(0, b.Top, 0, b.Top + (b.Height() * 0.55f),
                new int[] { Color.Argb(_pressed ? 10 : 46, 255, 255, 255).ToArgb(), Color.Argb(0, 255, 255, 255).ToArgb() }, null, Shader.TileMode.Clamp));
        }

        public override void Draw(Canvas canvas)
        {
            var b = Bounds;
            var radius = 8 * _d;
            var r = new RectF(b.Left + _d, b.Top + _d, b.Right - _d, b.Bottom - _d);
            canvas.DrawRoundRect(r, radius, radius, _fill);
            canvas.DrawRoundRect(new RectF(r.Left + _d, r.Top + _d, r.Right - _d, r.Top + (r.Height() * 0.55f)), radius, radius, _gloss);
            if (_pressed) canvas.DrawRoundRect(r, radius, radius, _glow);
            canvas.DrawRoundRect(r, radius, radius, _rim);
            var i = 2.6f * _d;
            canvas.DrawRoundRect(new RectF(r.Left + i, r.Top + i, r.Right - i, r.Bottom - i), radius - (2 * _d), radius - (2 * _d), _inner);
        }

        public override void SetAlpha(int alpha) => _fill.Alpha = alpha;
        public override void SetColorFilter(ColorFilter colorFilter) => _fill.SetColorFilter(colorFilter);
#pragma warning disable CS0672, CS0618
        public override int Opacity => (int)Format.Translucent;
#pragma warning restore CS0672, CS0618

        public override void GetOutline(Outline outline) => outline.SetRoundRect(Bounds, 8 * _d);
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
            _text.SetShadowLayer(3, 0, 1, Color.Argb(170, 250, 247, 240));
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
            _p.Color = Color.Argb(230, 244, 240, 231);
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
