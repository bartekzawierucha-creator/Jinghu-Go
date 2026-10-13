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
    /// The hero's silhouette with the five equipment slots around it (weapon, robe, talisman, ring, treasure):
    /// the animated idle hero in the middle, each slot a framed medallion with the item's art and upgrade level.
    /// Tapping a slot raises <see cref="SlotTapped"/>.
    /// </summary>
    public sealed class HeroDollView : View
    {
        public static readonly string[] Slots = { "weapon", "robe", "talisman", "ring", "treasure" };

        private readonly float _d;
        private readonly Anim _idle;
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _art = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Dictionary<string, Bitmap> _icons = new Dictionary<string, Bitmap>();
        private readonly Bitmap _empty;
        private readonly Dictionary<string, RectF> _hit = new Dictionary<string, RectF>();

        public HeroDollView(Context ctx) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _idle = new Anim(Ui.Art(ctx, Resource.Drawable.anim_hero_idle), 320, 7, true);
            _empty = Ui.Art(ctx, Resource.Drawable.item_slot_empty);
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(3, 0, 1, Color.Black);
        }

        /// <summary>Equipped item per slot (null = empty).</summary>
        public Dictionary<string, ItemInstanceDto> Equipped { get; } = new Dictionary<string, ItemInstanceDto>();

        public string PetNpcId { get; set; }

        public event Action<string> SlotTapped;

        public static string SlotName(string slot) => slot switch
        {
            "weapon" => "Broń",
            "robe" => "Szata",
            "talisman" => "Talizman",
            "ring" => "Pierścień",
            "treasure" => "Skarb",
            _ => slot,
        };

        private Bitmap IconFor(ItemInstanceDto item)
        {
            if (item == null) return _empty;
            if (_icons.TryGetValue(item.ItemId, out var b)) return b;
            var id = Context.Resources.GetIdentifier("item_" + item.ItemId, "drawable", Context.PackageName);
            b = id != 0 ? Ui.Art(Context, id) : null;
            _icons[item.ItemId] = b;
            return b;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Width == 0) return;
            var now = SystemClock.UptimeMillis();
            float w = Width, h = Height, cx = w / 2;

            // pedestal glow and the hero
            _p.SetShader(new RadialGradient(cx, h * 0.55f, w * 0.38f, new int[] { Color.Argb(110, 212, 175, 96).ToArgb(), Color.Argb(0, 212, 175, 96).ToArgb() }, null, Shader.TileMode.Clamp));
            canvas.DrawCircle(cx, h * 0.55f, w * 0.38f, _p);
            _p.SetShader(null);
            var cellH = h * 0.92f;
            var cellW = cellH * 320f / 256f;
            var foot = cellH * 246f / 256f;
            var ground = h * 0.96f;
            canvas.DrawBitmap(_idle.Strip, _idle.Frame(now), new RectF(cx - cellW / 2, ground - foot, cx + cellW / 2, ground - foot + cellH), _art);

            // slots: weapon & ring right/left of the hands, talisman at the neck, robe at the chest, treasure at the feet
            var size = Math.Min(64 * _d, w * 0.19f);
            var places = new Dictionary<string, (float X, float Y)>
            {
                ["talisman"] = (cx - w * 0.33f, h * 0.18f),
                ["robe"] = (cx + w * 0.33f, h * 0.18f),
                ["weapon"] = (cx + w * 0.36f, h * 0.52f),
                ["ring"] = (cx - w * 0.36f, h * 0.52f),
                ["treasure"] = (cx + w * 0.33f, h * 0.84f),
            };
            _hit.Clear();
            foreach (var slot in Slots)
            {
                var (x, y) = places[slot];
                Equipped.TryGetValue(slot, out var item);
                var rect = new RectF(x - size / 2, y - size / 2, x + size / 2, y + size / 2);
                _hit[slot] = rect;
                var rarity = item == null ? Ui.GoldDim : Ui.RarityOf(item.Rarity).Color;
                // leader line to the body
                _p.Color = Color.Argb(90, rarity.R, rarity.G, rarity.B);
                _p.StrokeWidth = 1.2f * _d;
                canvas.DrawLine(x, y, cx + (x - cx) * 0.25f, y, _p);
                _p.Color = Color.Argb(230, 10, 13, 16);
                canvas.DrawRoundRect(rect, 10 * _d, 10 * _d, _p);
                var icon = IconFor(item);
                if (icon != null) canvas.DrawBitmap(icon, null, new RectF(rect.Left + 5 * _d, rect.Top + 5 * _d, rect.Right - 5 * _d, rect.Bottom - 5 * _d), _art);
                else
                {
                    _text.TextSize = 22 * _d;
                    _text.Color = rarity;
                    canvas.DrawText("✦", x, y + 8 * _d, _text);
                }

                _p.SetStyle(Paint.Style.Stroke);
                _p.StrokeWidth = (item == null ? 1.2f : 2.2f) * _d;
                _p.Color = rarity;
                canvas.DrawRoundRect(rect, 10 * _d, 10 * _d, _p);
                _p.SetStyle(Paint.Style.Fill);
                _text.TextSize = 10 * _d;
                _text.Color = item == null ? Ui.Muted : Ui.Ink;
                canvas.DrawText(item == null ? SlotName(slot) : item.NamePl + (item.UpgradeLevel > 0 ? " +" + item.UpgradeLevel : string.Empty), x, rect.Bottom + 12 * _d, _text);
            }

            if (PetNpcId != null)
            {
                _text.TextSize = 11 * _d;
                _text.Color = Ui.Jade;
                canvas.DrawText("🐾 towarzysz u boku", cx - w * 0.3f, h * 0.88f, _text);
            }

            PostInvalidateDelayed(60);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down) return true;
            if (e.Action != MotionEventActions.Up) return base.OnTouchEvent(e);
            foreach (var kv in _hit)
            {
                if (kv.Value.Contains(e.GetX(), e.GetY()))
                {
                    SlotTapped?.Invoke(kv.Key);
                    return true;
                }
            }

            return true;
        }
    }
}
