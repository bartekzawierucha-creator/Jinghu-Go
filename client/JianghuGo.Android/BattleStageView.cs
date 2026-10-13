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
    /// <summary>A horizontal strip of equally sized animation frames (pixel art, drawn without filtering).</summary>
    public sealed class Anim
    {
        public Anim(Bitmap strip, int cellW, float fps, bool loop, int first = 0, int count = -1)
        {
            Strip = strip;
            CellW = cellW;
            Fps = fps;
            Loop = loop;
            First = first;
            Count = count < 0 ? strip.Width / cellW - first : count;
        }

        public Bitmap Strip { get; }
        public int CellW { get; }
        public float Fps { get; }
        public bool Loop { get; }
        public int First { get; }
        public int Count { get; }
        public long DurationMs => (long)(Count * 1000 / Fps);

        public Rect Frame(long elapsedMs)
        {
            var i = (int)(elapsedMs * Fps / 1000);
            i = Loop ? i % Count : Math.Min(i, Count - 1);
            var x = (First + i) * CellW;
            return new Rect(x, 0, x + CellW, Strip.Height);
        }
    }

    /// <summary>
    /// Side-view duel stage: location backdrop, the two fighters as animated pixel-art sprites spaced by the battle
    /// distance, name plates with HP/Qi, and the server's turn timeline played back as attack, guard, hit and defeat
    /// animations with impact effects and floating numbers. Purely presentational: every number comes from the server.
    /// </summary>
    public sealed class BattleStageView : View
    {
        private const long StepMs = 760;
        private readonly float _d;
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _pixel = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Bitmap _backdrop;
        private readonly Dictionary<string, Anim> _hero = new Dictionary<string, Anim>();
        private readonly Dictionary<string, Anim> _foe = new Dictionary<string, Anim>();
        private readonly Dictionary<string, Anim> _fx = new Dictionary<string, Anim>();
        private readonly List<Cue> _cues = new List<Cue>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly float _foeScale;
        private readonly bool _foeFlip;
        private float _distanceShown = -1;
        private long _endAt = -1;

        public BattleStageView(Context ctx, string npcId) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(4, 0, 2, Color.Black);
            _backdrop = Ui.Art(ctx, npcId switch
            {
                "training_dummy" => Resource.Drawable.arena_courtyard,
                "grey_wolf" => Resource.Drawable.arena_bamboo,
                "mountain_bandit" => Resource.Drawable.arena_mountain,
                null or "pvp" => Resource.Drawable.arena_temple,
                _ => Resource.Drawable.arena_fortress,
            });

            Anim A(int res, float fps, bool loop, int first = 0, int count = -1) => new Anim(Ui.Art(ctx, res), 320, fps, loop, first, count);
            _hero["idle"] = A(Resource.Drawable.anim_hero_idle, 7, true);
            _hero["attack"] = A(Resource.Drawable.anim_hero_attack, 13, false);
            _hero["palm"] = A(Resource.Drawable.anim_hero_palm, 13, false);
            _hero["defend"] = A(Resource.Drawable.anim_hero_defend, 11, false);
            _hero["hit"] = A(Resource.Drawable.anim_hero_hit, 12, false, 0, 6);
            _hero["dash"] = A(Resource.Drawable.anim_hero_dash, 13, false);
            _hero["defeat"] = A(Resource.Drawable.anim_hero_defeat, 9, false);
            _hero["rest"] = A(Resource.Drawable.anim_hero_meditate, 8, false);

            switch (npcId)
            {
                case "training_dummy":
                    _foe["idle"] = A(Resource.Drawable.anim_dummy_idle, 6, true);
                    _foe["attack"] = A(Resource.Drawable.anim_dummy_attack, 14, false, 3, 6);
                    _foe["hit"] = A(Resource.Drawable.anim_dummy_defeat, 12, false, 0, 3);
                    _foe["defeat"] = A(Resource.Drawable.anim_dummy_defeat, 9, false);
                    _foeScale = 1f;
                    break;
                case "grey_wolf":
                    _foe["idle"] = A(Resource.Drawable.anim_wolf_idle, 8, true);
                    _foe["attack"] = A(Resource.Drawable.anim_wolf_attack, 10, false);
                    _foe["hit"] = A(Resource.Drawable.anim_wolf_defeat, 10, false, 0, 2);
                    _foe["defeat"] = A(Resource.Drawable.anim_wolf_defeat, 8, false);
                    _foeScale = 1.25f;
                    break;
                case "pvp":
                    // another cultivator: the hero's own animations, mirrored to face left
                    foreach (var kv in _hero) _foe[kv.Key] = kv.Value;
                    _foeFlip = true;
                    _foeScale = 1f;
                    break;
                default:
                    _foe["idle"] = A(Resource.Drawable.anim_bandit_idle, 7, true);
                    _foe["attack"] = A(Resource.Drawable.anim_bandit_attack, 12, false);
                    _foe["hit"] = A(Resource.Drawable.anim_bandit_defeat, 12, false, 0, 4);
                    _foe["defeat"] = A(Resource.Drawable.anim_bandit_defeat, 9, false);
                    _foeScale = 1f;
                    break;
            }

            Anim F(int res, float fps) => new Anim(Ui.Art(ctx, res), 256, fps, false);
            _fx["qi"] = F(Resource.Drawable.fx_qi_hit, 14);
            _fx["slash"] = F(Resource.Drawable.fx_enemy_slash, 14);
            _fx["parry"] = F(Resource.Drawable.fx_parry, 13);
            _fx["flame"] = F(Resource.Drawable.fx_flame, 13);
            _fx["poison"] = F(Resource.Drawable.fx_poison, 12);
            _fx["gather"] = F(Resource.Drawable.fx_qi_gather, 10);
        }

        public BattleDto Battle { get; private set; }

        /// <summary>An animation (or effect) a fighter starts at a given time.</summary>
        private sealed class Cue
        {
            public bool Hero;
            public bool Fx;
            public Anim Anim;
            public long At;
        }

        private sealed class Floater
        {
            public bool OnHero;
            public string Text;
            public Color Color;
            public float Size;
            public long At;
        }

        /// <summary>Shows a new battle state; when the turn advanced, plays back what happened in it.</summary>
        public void Show(BattleDto battle)
        {
            var previous = Battle;
            Battle = battle;
            if (previous != null && (battle.Turn != previous.Turn || previous.IsActive && !battle.IsActive))
            {
                var t = SystemClock.UptimeMillis();
                foreach (var e in battle.LastTurnEvents)
                {
                    Schedule(e, battle.IsMine(e), t);
                    t += StepMs;
                }

                if (!battle.IsActive)
                {
                    var heroLost = battle.Me.Hp <= 0 || battle.Result != null && battle.Result != "draw" && battle.Result != battle.MySide + "_won";
                    var foeLost = battle.Opponent.Hp <= 0 || battle.Result == battle.MySide + "_won";
                    if (heroLost) _cues.Add(new Cue { Hero = true, Anim = _hero["defeat"], At = t });
                    if (foeLost) _cues.Add(new Cue { Hero = false, Anim = _foe["defeat"], At = t });
                    _endAt = t + 1100;
                }
            }
            else if (previous == null && !battle.IsActive)
            {
                _endAt = 0;
            }

            PostInvalidateOnAnimation();
        }

        private void Schedule(TurnEventDto e, bool mine, long at)
        {
            var actor = mine ? _hero : _foe;
            switch (e.Action)
            {
                case "attack":
                    var tech = e.TechniqueId ?? string.Empty;
                    var anim = actor.ContainsKey("palm") && (tech.Contains("palm") || tech.Contains("fist") || tech.Contains("qi")) ? actor["palm"] : actor["attack"];
                    _cues.Add(new Cue { Hero = mine, Anim = anim, At = at });
                    var impact = at + anim.DurationMs * 55 / 100;
                    if (e.Hit == true)
                    {
                        var target = mine ? _foe : _hero;
                        _cues.Add(new Cue { Hero = !mine, Anim = target["hit"], At = impact });
                        var fx = !mine ? "slash" : tech.Contains("flame") ? "flame" : tech.Contains("venom") || tech.Contains("poison") ? "poison" : "qi";
                        _cues.Add(new Cue { Hero = !mine, Fx = true, Anim = _fx[fx], At = impact });
                        _floaters.Add(new Floater { OnHero = !mine, Text = "-" + e.Damage, Color = mine ? Color.Rgb(255, 230, 140) : Color.Rgb(255, 110, 90), Size = 26, At = impact });
                    }
                    else if (e.Hit == false)
                    {
                        if (!mine) _cues.Add(new Cue { Hero = true, Anim = _hero["dash"], At = impact - 120 });
                        _floaters.Add(new Floater { OnHero = !mine, Text = "unik", Color = Color.Rgb(220, 220, 220), Size = 18, At = impact });
                    }

                    break;
                case "defend":
                case "defense":
                case "counter_stance":
                    if (actor.ContainsKey("defend")) _cues.Add(new Cue { Hero = mine, Anim = actor["defend"], At = at });
                    _cues.Add(new Cue { Hero = mine, Fx = true, Anim = _fx["parry"], At = at + 150 });
                    _floaters.Add(new Floater { OnHero = mine, Text = e.Action == "counter_stance" ? "kontra" : "garda", Color = Ui.Gold, Size = 18, At = at + 200 });
                    break;
                case "rest":
                    _cues.Add(new Cue { Hero = mine, Fx = true, Anim = _fx["gather"], At = at });
                    _floaters.Add(new Floater { OnHero = mine, Text = "+Qi", Color = Ui.QiBlue, Size = 18, At = at + 200 });
                    break;
                case "advance":
                case "retreat":
                    if (actor.ContainsKey("dash")) _cues.Add(new Cue { Hero = mine, Anim = actor["dash"], At = at });
                    break;
                case "out_of_range":
                    _floaters.Add(new Floater { OnHero = mine, Text = "za daleko", Color = Color.Rgb(220, 220, 220), Size = 16, At = at });
                    break;
                case "stunned":
                    _floaters.Add(new Floater { OnHero = mine, Text = "ogłuszony", Color = Ui.Violet, Size = 16, At = at });
                    break;
                case "bleed":
                    _floaters.Add(new Floater { OnHero = mine, Text = "-" + e.Damage, Color = Color.Rgb(200, 60, 60), Size = 18, At = at });
                    break;
            }
        }

        /// <summary>The body animation a fighter shows now: the latest started cue, else idle (or the held defeat pose).</summary>
        private (Anim Anim, long Elapsed) Current(bool hero, long now)
        {
            Cue best = null;
            foreach (var c in _cues)
            {
                if (c.Fx || c.Hero != hero || c.At > now) continue;
                var done = !c.Anim.Loop && now - c.At >= c.Anim.DurationMs;
                var isDefeat = c.Anim == (hero ? _hero : _foe)["defeat"];
                if (done && !isDefeat) continue;
                if (best == null || c.At >= best.At) best = c;
            }

            if (best != null) return (best.Anim, now - best.At);
            var fighter = hero ? Battle.Me : Battle.Opponent;
            var set = hero ? _hero : _foe;
            if (!Battle.IsActive && fighter.Hp <= 0 && _cues.Count == 0) return (set["defeat"], 100000);
            return (set["idle"], now);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Battle == null || Width == 0) return;
            var now = SystemClock.UptimeMillis();
            float w = Width, h = Height;

            var src = CoverRect(_backdrop.Width, _backdrop.Height, w, h);
            canvas.DrawBitmap(_backdrop, src, new RectF(0, 0, w, h), new Paint(PaintFlags.FilterBitmap));
            _p.SetShader(new LinearGradient(0, 0, 0, h, new int[] { Color.Argb(30, 0, 0, 0).ToArgb(), Color.Argb(70, 0, 0, 0).ToArgb(), Color.Argb(210, 6, 8, 10).ToArgb() }, new float[] { 0, 0.55f, 1 }, Shader.TileMode.Clamp));
            canvas.DrawRect(0, 0, w, h, _p);
            _p.SetShader(null);

            var target = Math.Clamp(Battle.Distance, 1, 4);
            _distanceShown = _distanceShown < 0 ? target : _distanceShown + (target - _distanceShown) * 0.12f;
            var gap = w * (0.13f + 0.08f * _distanceShown);
            var ground = h * 0.9f;
            var cellH = h * 0.78f;               // the 320x256 cell; characters fill ~70% of it
            var cellW = cellH * 320f / 256f;
            var footInCell = cellH * 246f / 256f;
            var heroX = w / 2 - gap;
            var foeX = w / 2 + gap;

            DrawShadow(canvas, heroX, ground, cellW * 0.16f);
            DrawShadow(canvas, foeX, ground, cellW * 0.2f * _foeScale);
            var (ha, he) = Current(true, now);
            var (fa, fe) = Current(false, now);
            canvas.DrawBitmap(ha.Strip, ha.Frame(he), new RectF(heroX - cellW / 2, ground - footInCell, heroX + cellW / 2, ground - footInCell + cellH), _pixel);
            var fw = cellW * _foeScale;
            var fh = cellH * _foeScale;
            canvas.Save();
            if (_foeFlip) canvas.Scale(-1, 1, foeX, ground);
            canvas.DrawBitmap(fa.Strip, fa.Frame(fe), new RectF(foeX - fw / 2, ground - footInCell * _foeScale, foeX + fw / 2, ground - footInCell * _foeScale + fh), _pixel);
            canvas.Restore();

            // effects over the fighters
            foreach (var c in _cues)
            {
                if (!c.Fx || c.At > now || now - c.At >= c.Anim.DurationMs) continue;
                var x = c.Hero ? heroX : foeX;
                var y = ground - cellH * 0.42f;
                var size = cellH * 0.62f;
                canvas.DrawBitmap(c.Anim.Strip, c.Anim.Frame(now - c.At), new RectF(x - size / 2, y - size / 2, x + size / 2, y + size / 2), _pixel);
            }

            foreach (var f in _floaters)
            {
                var k = (now - f.At) / 1000f;
                if (k < 0 || k > 1.2f) continue;
                _text.TextSize = f.Size * _d;
                _text.Color = f.Color;
                _text.Alpha = (int)(255 * Math.Clamp(1.2f - k, 0, 1));
                canvas.DrawText(f.Text, f.OnHero ? heroX : foeX, ground - cellH * 0.78f - k * 40 * _d, _text);
                _text.Alpha = 255;
            }

            Plate(canvas, Battle.Me, 10 * _d, 10 * _d, Ui.Jade, left: true);
            Plate(canvas, Battle.Opponent, w - 10 * _d, 10 * _d, Ui.Cinnabar, left: false);
            _text.TextSize = 13 * _d;
            _text.Color = Ui.Gold;
            canvas.DrawText("TURA " + Battle.Turn + "  ·  DYSTANS " + Battle.Distance, w / 2, h - 8 * _d, _text);

            if (!Battle.IsActive && _endAt >= 0 && now >= _endAt)
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

            _cues.RemoveAll(c => !c.Anim.Loop && now - c.At > c.Anim.DurationMs + 4000 && c.Anim != _hero["defeat"] && c.Anim != _foe["defeat"]);
            _floaters.RemoveAll(f => now - f.At > 1300);
            PostInvalidateDelayed(33); // ~30 fps: idle loops always move
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
