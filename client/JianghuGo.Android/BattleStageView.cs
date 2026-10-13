using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>
    /// Side-view duel stage: location backdrop, the two fighters as pixel-art sprites spaced by the battle distance,
    /// name plates with HP/Qi, and the server's turn timeline played back as lunges, impact effects, shakes and
    /// floating numbers. Purely presentational: every number shown comes from the server's battle state.
    /// </summary>
    public sealed class BattleStageView : View
    {
        private const long StepMs = 520;
        private readonly float _d;
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _pixel = new Paint();
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Bitmap _backdrop, _hero, _enemy, _fxBlue, _fxRed, _fxGold;
        private readonly bool _enemyFacesRight;
        private readonly List<Beat> _beats = new List<Beat>();
        private float _distanceShown = -1;

        public BattleStageView(Context ctx, string npcId) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _pixel.FilterBitmap = false;
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(4, 0, 2, Color.Black);
            _backdrop = Ui.Art(ctx, npcId switch
            {
                "training_dummy" => Resource.Drawable.loc_village,
                "grey_wolf" => Resource.Drawable.loc_bamboo,
                "mountain_bandit" => Resource.Drawable.loc_mountains,
                null => Resource.Drawable.loc_temple,
                _ => Resource.Drawable.loc_fortress,
            });
            _hero = Ui.Art(ctx, Resource.Drawable.sprite_hero);
            _enemy = Ui.Art(ctx, npcId switch
            {
                "training_dummy" => Resource.Drawable.sprite_dummy,
                "grey_wolf" => Resource.Drawable.sprite_wolf,
                "mountain_bandit" => Resource.Drawable.sprite_bandit,
                _ => Resource.Drawable.sprite_brute,
            });
            _enemyFacesRight = npcId == "grey_wolf";
            _fxBlue = Ui.Art(ctx, Resource.Drawable.fx_blue);
            _fxRed = Ui.Art(ctx, Resource.Drawable.fx_red);
            _fxGold = Ui.Art(ctx, Resource.Drawable.fx_gold);
        }

        public BattleDto Battle { get; private set; }

        /// <summary>Shows a new battle state; when the turn advanced, plays back what happened in it.</summary>
        public void Show(BattleDto battle)
        {
            var previous = Battle;
            Battle = battle;
            if (previous != null && battle.Turn != previous.Turn || previous != null && previous.IsActive && !battle.IsActive)
            {
                var start = SystemClock.UptimeMillis();
                var i = 0;
                foreach (var e in battle.LastTurnEvents)
                {
                    _beats.Add(new Beat(e, battle.IsMine(e), start + i * StepMs));
                    i++;
                }
            }

            PostInvalidateOnAnimation();
        }

        private sealed class Beat
        {
            public Beat(TurnEventDto e, bool mine, long at)
            {
                Event = e;
                Mine = mine;
                At = at;
            }

            public TurnEventDto Event { get; }
            public bool Mine { get; }
            public long At { get; }
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Battle == null || Width == 0) return;
            var now = SystemClock.UptimeMillis();
            float w = Width, h = Height;

            // backdrop: cover-crop the location art, darken towards the ground
            var src = CoverRect(_backdrop.Width, _backdrop.Height, w, h);
            canvas.DrawBitmap(_backdrop, src, new RectF(0, 0, w, h), new Paint(PaintFlags.FilterBitmap));
            _p.SetShader(new LinearGradient(0, 0, 0, h, new int[] { Color.Argb(40, 0, 0, 0).ToArgb(), Color.Argb(90, 0, 0, 0).ToArgb(), Color.Argb(230, 6, 8, 10).ToArgb() }, new float[] { 0, 0.55f, 1 }, Shader.TileMode.Clamp));
            canvas.DrawRect(0, 0, w, h, _p);
            _p.SetShader(null);

            // positions from the battle distance (1..3), eased
            var target = Math.Clamp(Battle.Distance, 1, 4);
            _distanceShown = _distanceShown < 0 ? target : _distanceShown + (target - _distanceShown) * 0.12f;
            var gap = w * (0.14f + 0.1f * _distanceShown);
            var ground = h * 0.86f;
            var heroX = w / 2 - gap;
            var foeX = w / 2 + gap;

            float heroDx = 0, foeDx = 0, heroShake = 0, foeShake = 0;
            float heroFlash = 0, foeFlash = 0;
            foreach (var b in _beats)
            {
                var k = (now - b.At) / (float)StepMs;
                if (k < 0 || k > 1.6f) continue;
                var lunge = k < 0.5f ? (float)Math.Sin(k / 0.5f * Math.PI) : 0f;
                var attacking = b.Event.Action == "attack";
                var hit = b.Event.Hit == true;
                var shake = hit && k > 0.25f && k < 0.75f ? (float)Math.Sin(k * 60) * 5 * _d : 0;
                var flash = hit && k > 0.25f && k < 0.6f ? 1f : 0f;
                if (b.Mine)
                {
                    if (attacking) heroDx += lunge * gap * 0.5f;
                    foeShake += shake;
                    foeFlash = Math.Max(foeFlash, flash);
                }
                else
                {
                    if (attacking) foeDx -= lunge * gap * 0.5f;
                    heroShake += shake;
                    heroFlash = Math.Max(heroFlash, flash);
                }
            }

            var spriteH = h * 0.5f;
            DrawShadow(canvas, heroX + heroDx, ground, spriteH * 0.3f);
            DrawShadow(canvas, foeX + foeDx, ground, spriteH * 0.34f);
            DrawSprite(canvas, _hero, heroX + heroDx + heroShake, ground, spriteH, flip: false, heroFlash, Battle.Me.Hp <= 0);
            DrawSprite(canvas, _enemy, foeX + foeDx + foeShake, ground, _enemyFacesRight ? spriteH * 0.62f : spriteH * 0.92f, flip: _enemyFacesRight, foeFlash, Battle.Opponent.Hp <= 0);

            // impact effects and floating numbers
            foreach (var b in _beats)
            {
                var k = (now - b.At) / (float)StepMs;
                if (k < 0.2f || k > 1.8f) continue;
                var e = b.Event;
                var tx = b.Mine ? foeX : heroX;
                var ty = ground - spriteH * 0.55f;
                if (e.Action == "attack" && k < 0.9f)
                {
                    var fx = e.Hit == true ? (b.Mine ? _fxBlue : _fxRed) : _fxGold;
                    var a = (int)(255 * Math.Clamp(1 - (k - 0.2f) / 0.7f, 0, 1));
                    var size = spriteH * (0.55f + 0.3f * k);
                    var paint = new Paint { FilterBitmap = false, Alpha = a };
                    canvas.DrawBitmap(fx, null, new RectF(tx - size / 2, ty - size / 2, tx + size / 2, ty + size / 2), paint);
                }

                var label = Floating(e);
                if (label != null)
                {
                    var rise = (k - 0.2f) * 46 * _d;
                    _text.TextSize = (e.Hit == true ? 26 : 18) * _d;
                    _text.Color = e.Hit == true ? (b.Mine ? Color.Rgb(255, 230, 140) : Color.Rgb(255, 110, 90)) : Color.Rgb(220, 220, 220);
                    _text.Alpha = (int)(255 * Math.Clamp(1.8f - k, 0, 1));
                    canvas.DrawText(label, tx, ty - spriteH * 0.35f - rise, _text);
                    _text.Alpha = 255;
                }
            }

            // name plates
            Plate(canvas, Battle.Me, 10 * _d, 10 * _d, Ui.Jade, left: true);
            Plate(canvas, Battle.Opponent, w - 10 * _d, 10 * _d, Ui.Cinnabar, left: false);

            // turn seal
            _text.TextSize = 13 * _d;
            _text.Color = Ui.Gold;
            canvas.DrawText("TURA " + Battle.Turn + "  ·  DYSTANS " + Battle.Distance, w / 2, h - 10 * _d, _text);

            if (!Battle.IsActive && _beats.All(b => now - b.At > StepMs * 1.8f))
            {
                var won = Battle.Result == Battle.MySide + "_won";
                var draw = Battle.Result == "draw";
                _p.Color = Color.Argb(150, 0, 0, 0);
                canvas.DrawRect(0, h * 0.36f, w, h * 0.6f, _p);
                _text.TextSize = 34 * _d;
                _text.Color = won ? Ui.Gold : draw ? Ui.Ink : Ui.Cinnabar;
                canvas.DrawText(won ? "ZWYCIĘSTWO" : draw ? "REMIS" : "PORAŻKA", w / 2, h * 0.5f, _text);
                if (Battle.RewardQi > 0)
                {
                    _text.TextSize = 14 * _d;
                    _text.Color = Ui.Ink;
                    canvas.DrawText("+" + Battle.RewardQi + " Qi", w / 2, h * 0.57f, _text);
                }
            }

            _beats.RemoveAll(b => now - b.At > StepMs * 2);
            if (_beats.Count > 0 || Math.Abs(_distanceShown - target) > 0.01f) PostInvalidateOnAnimation();
        }

        private static string Floating(TurnEventDto e)
        {
            if (e.Action == "attack") return e.Hit == true ? "-" + e.Damage : e.Hit == false ? "unik" : null;
            return e.Action switch
            {
                "defend" or "defense" => "garda",
                "rest" => "+Qi",
                "out_of_range" => "za daleko",
                "stunned" => "ogłuszony",
                _ => null,
            };
        }

        private static Rect CoverRect(int bw, int bh, float w, float h)
        {
            var scale = Math.Max(w / bw, h / bh);
            var sw = (int)(w / scale);
            var sh = (int)(h / scale);
            var x = (bw - sw) / 2;
            var y = (bh - sh) / 2;
            return new Rect(x, y, x + sw, y + sh);
        }

        private void DrawShadow(Canvas canvas, float x, float ground, float rx)
        {
            _p.SetShader(new RadialGradient(x, ground, Math.Max(rx, 1), new int[] { Color.Argb(160, 0, 0, 0).ToArgb(), Color.Argb(0, 0, 0, 0).ToArgb() }, null, Shader.TileMode.Clamp));
            canvas.Save();
            canvas.Scale(1f, 0.25f, x, ground);
            canvas.DrawCircle(x, ground, rx, _p);
            canvas.Restore();
            _p.SetShader(null);
        }

        private void DrawSprite(Canvas canvas, Bitmap bmp, float cx, float ground, float height, bool flip, float flash, bool fallen)
        {
            var width = height * bmp.Width / bmp.Height;
            var dst = new RectF(cx - width / 2, ground - height, cx + width / 2, ground);
            canvas.Save();
            if (flip) canvas.Scale(-1, 1, cx, ground);
            if (fallen) canvas.Rotate(flip ? -80 : 80, cx, ground);
            canvas.DrawBitmap(bmp, null, dst, _pixel);
            if (flash > 0)
            {
                var tint = new Paint { FilterBitmap = false };
                tint.SetColorFilter(new PorterDuffColorFilter(Color.Argb(170, 255, 255, 255), PorterDuff.Mode.SrcAtop));
                canvas.DrawBitmap(bmp, null, dst, tint);
            }

            canvas.Restore();
        }

        private void Plate(Canvas canvas, FighterDto f, float x, float y, Color hpColor, bool left)
        {
            var pw = Width * 0.44f;
            var ph = 46 * _d;
            var l = left ? x : x - pw;
            var rect = new RectF(l, y, l + pw, y + ph);
            _p.Color = Color.Argb(200, 8, 10, 12);
            canvas.DrawRoundRect(rect, 8 * _d, 8 * _d, _p);
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 1.2f * _d;
            _p.Color = Ui.GoldDim;
            canvas.DrawRoundRect(rect, 8 * _d, 8 * _d, _p);
            _p.SetStyle(Paint.Style.Fill);
            _text.TextSize = 13 * _d;
            _text.Color = Ui.Ink;
            _text.TextAlign = left ? Paint.Align.Left : Paint.Align.Right;
            canvas.DrawText(f.Name, left ? l + 8 * _d : l + pw - 8 * _d, y + 16 * _d, _text);
            _text.TextAlign = Paint.Align.Center;
            Bar(canvas, new RectF(l + 8 * _d, y + 22 * _d, l + pw - 8 * _d, y + 31 * _d), f.Hp, f.MaxHp, hpColor);
            Bar(canvas, new RectF(l + 8 * _d, y + 34 * _d, l + pw - 8 * _d, y + 40 * _d), f.Qi, f.MaxQi, Ui.QiBlue);
            _text.TextSize = 9 * _d;
            _text.Color = Ui.Ink;
            canvas.DrawText(f.Hp + " / " + f.MaxHp, l + pw / 2, y + 30 * _d, _text);
        }

        private void Bar(Canvas canvas, RectF r, int value, int max, Color color)
        {
            _p.Color = Color.Argb(255, 20, 22, 24);
            canvas.DrawRoundRect(r, r.Height() / 2, r.Height() / 2, _p);
            var f = max <= 0 ? 0 : Math.Clamp(value / (float)max, 0, 1);
            if (f <= 0) return;
            var fill = new RectF(r.Left, r.Top, r.Left + r.Width() * f, r.Bottom);
            _p.SetShader(new LinearGradient(0, r.Top, 0, r.Bottom, Color.Argb(255, Math.Min(255, color.R + 70), Math.Min(255, color.G + 70), Math.Min(255, color.B + 70)), color, Shader.TileMode.Clamp));
            canvas.DrawRoundRect(fill, r.Height() / 2, r.Height() / 2, _p);
            _p.SetShader(null);
        }
    }
}
