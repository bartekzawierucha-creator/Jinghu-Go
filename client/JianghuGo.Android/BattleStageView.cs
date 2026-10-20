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

    /// <summary>Animation and overlay art per cultivation path (each path has its own hero sheet since 1.4).</summary>
    public static class HeroArt
    {
        public static readonly string[] Paths = { "qi", "body", "flame", "poison" };

        /// <summary>Path of the character being played (set when a character is selected).</summary>
        public static string CurrentPath { get; set; }

        public static string Normalize(string path) => Array.IndexOf(Paths, path) >= 0 ? path : "qi";

        /// <summary>Drawable id of anim_hero_{path}_{part} (Qi path as the fallback).</summary>
        public static int Id(Context ctx, string path, string part)
        {
            var id = ctx.Resources.GetIdentifier("anim_hero_" + Normalize(path) + "_" + part, "drawable", ctx.PackageName);
            return id != 0 ? id : ctx.Resources.GetIdentifier("anim_hero_qi_" + part, "drawable", ctx.PackageName);
        }

        public static Anim Load(Context ctx, string path, string part, float fps, bool loop) => new Anim(Ui.Art(ctx, Id(ctx, path, part)), 320, fps, loop);

        /// <summary>A light overlay strip (ovl_*: video frames, brightness as alpha), or null when the art is missing.</summary>
        public static Anim Overlay(Context ctx, string name, int cellW, float fps, bool loop)
        {
            var id = ctx.Resources.GetIdentifier(name, "drawable", ctx.PackageName);
            return id == 0 ? null : new Anim(Ui.Art(ctx, id), cellW, fps, loop);
        }
    }

    /// <summary>
    /// Side-view duel stage: arena backdrop, the two fighters as animated sprites at a fixed duel spacing, name plates
    /// with HP/Qi, the opponent's telegraphed intent, and the server's turn timeline played back as lunges, strikes,
    /// guards, hits and defeats with light effects, screen shake and floating numbers. Purely presentational: every
    /// number and every decision comes from the server.
    /// </summary>
    public sealed class BattleStageView : View
    {
        private const long StepMs = 760;
        private readonly float _d;
        private readonly Paint _p = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _pixel = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _glow = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _flash = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Bitmap _backdrop;
        private readonly Dictionary<string, Anim> _hero = new Dictionary<string, Anim>();
        private readonly Dictionary<string, Anim> _foe = new Dictionary<string, Anim>();
        private readonly Dictionary<string, Anim> _fx = new Dictionary<string, Anim>();
        private readonly Dictionary<string, Anim> _ovl = new Dictionary<string, Anim>();
        private readonly List<Cue> _cues = new List<Cue>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly List<(long At, float Power)> _shakes = new List<(long, float)>();
        private readonly float _foeScale;
        private readonly bool _foeFlip;
        private readonly Paint _foePaint = new Paint(PaintFlags.FilterBitmap);
        private readonly NpcLooks.Look _look;
        private readonly string _kind;
        private readonly Anim _heroAura;
        private readonly long _born = SystemClock.UptimeMillis();
        private long _endAt = -1;
        private bool _endSoundPlayed;

        public BattleStageView(Context ctx, string npcId, string heroPath, string kind = "pve", string opponentName = null) : base(ctx)
        {
            _d = ctx.Resources.DisplayMetrics.Density;
            _kind = kind ?? "pve";
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _text.SetShadowLayer(4, 0, 2, Color.Black);
            _flash.SetColorFilter(new LightingColorFilter(Color.Rgb(255, 255, 255).ToArgb(), Color.Rgb(150, 150, 150).ToArgb()));

            var look = NpcLooks.For(npcId);
            _backdrop = Ui.Art(ctx, _kind == "tower" ? Resource.Drawable.arena_tower : look.Arena);

            var path = HeroArt.Normalize(heroPath);
            LoadHero(ctx, _hero, path);
            _heroAura = HeroArt.Overlay(ctx, "ovl_aura_" + path, 200, 9, true);

            Anim A(int res, float fps, bool loop, int first = 0, int count = -1) => new Anim(Ui.Art(ctx, res), 320, fps, loop, first, count);
            _look = look;
            var own = npcId == null ? 0 : ctx.Resources.GetIdentifier("anim_" + npcId + "_idle", "drawable", ctx.PackageName);
            if (own != 0)
            {
                // this enemy has its own sprite sheets: idle / attack / defeat (hit = the first defeat frames)
                int Res(string part) => ctx.Resources.GetIdentifier("anim_" + npcId + "_" + part, "drawable", ctx.PackageName);
                _foe["idle"] = A(own, 7 * look.Speed, true);
                _foe["attack"] = A(Res("attack"), 11 * look.Speed, false);
                _foe["hit"] = A(Res("defeat"), 10, false, 0, 2);
                _foe["defeat"] = A(Res("defeat"), 8, false);
                look = look.WithOwnSprites();
                _look = look;
            }
            else switch (look.Base)
            {
                case "dummy":
                    _foe["idle"] = A(Resource.Drawable.anim_dummy_idle, 6, true);
                    _foe["attack"] = A(Resource.Drawable.anim_dummy_attack, 14, false, 3, 6);
                    _foe["hit"] = A(Resource.Drawable.anim_dummy_defeat, 12, false, 0, 3);
                    _foe["defeat"] = A(Resource.Drawable.anim_dummy_defeat, 9, false);
                    break;
                case "wolf":
                    _foe["idle"] = A(Resource.Drawable.anim_wolf_idle, 8 * look.Speed, true);
                    _foe["attack"] = A(Resource.Drawable.anim_wolf_attack, 10 * look.Speed, false);
                    _foe["hit"] = A(Resource.Drawable.anim_wolf_defeat, 10, false, 0, 2);
                    _foe["defeat"] = A(Resource.Drawable.anim_wolf_defeat, 8, false);
                    break;
                case "hero":
                    // another cultivator: a hero sheet (a path picked from the name), mirrored to face left
                    var other = HeroArt.Paths[Math.Abs((opponentName ?? "?").GetHashCode()) % HeroArt.Paths.Length];
                    LoadHero(ctx, _foe, other);
                    _foeFlip = true;
                    break;
                default:
                    _foe["idle"] = A(Resource.Drawable.anim_bandit_idle, 7 * look.Speed, true);
                    _foe["attack"] = A(Resource.Drawable.anim_bandit_attack, 12 * look.Speed, false);
                    _foe["hit"] = A(Resource.Drawable.anim_bandit_defeat, 12, false, 0, 4);
                    _foe["defeat"] = A(Resource.Drawable.anim_bandit_defeat, 9, false);
                    break;
            }

            // the world boss towers over a cultivator
            _foeScale = npcId == "storm_qilin" ? 1.45f : look.Scale;
            if (look.Tint is { } tint)
                _foePaint.SetColorFilter(new LightingColorFilter(tint.Mul, tint.Add));

            Anim F(int res, float fps) => new Anim(Ui.Art(ctx, res), 256, fps, false);
            _fx["qi"] = F(Resource.Drawable.fx2_qi_burst, 14);
            _fx["slash"] = F(Resource.Drawable.fx2_slash, 14);
            _fx["parry"] = F(Resource.Drawable.fx2_shield_break, 12);
            _fx["flame"] = F(Resource.Drawable.fx2_fire_burst, 13);
            _fx["poison"] = F(Resource.Drawable.fx2_poison_burst, 12);
            _fx["gather"] = F(Resource.Drawable.fx_qi_gather, 10);
            _fx["blue"] = F(Resource.Drawable.fx_blue, 13);
            _fx["gold"] = F(Resource.Drawable.fx_gold, 13);
            _fx["red"] = F(Resource.Drawable.fx_red, 13);

            void O(string key, string name, int cellW, float fps, bool loop)
            {
                var a = HeroArt.Overlay(ctx, name, cellW, fps, loop);
                if (a != null) _ovl[key] = a;
            }

            O("hit", "ovl_hit_white", 192, 22, false);
            O("crit", "ovl_hit_crit", 192, 18, false);
            O("hit_qi", "ovl_hit_qi", 192, 20, false);
            O("hit_heavy", "ovl_hit_heavy", 192, 20, false);
            O("heal", "ovl_heal", 200, 12, false);
            O("stun", "ovl_status_stun", 96, 10, true);
            O("bleed", "ovl_status_bleed", 96, 10, true);
            O("weaken", "ovl_status_weaken", 96, 10, true);
            O("low_hp", "ovl_low_hp", 180, 8, true);
            O("victory", "ovl_victory", 270, 10, false);
            if (_kind == "boss" || npcId == "storm_qilin" || npcId == "thunder_eagle") O("lightning", "ovl_lightning", 200, 14, false);
            if (_kind == "boss") O("rain", "ovl_rain", 270, 14, true);
            if (_kind == "tower") O("mist", "ovl_mist", 270, 8, true);
        }

        private static void LoadHero(Context ctx, Dictionary<string, Anim> set, string path)
        {
            set["idle"] = HeroArt.Load(ctx, path, "idle", 7, true);
            set["attack"] = HeroArt.Load(ctx, path, "attack", 14, false);
            set["palm"] = HeroArt.Load(ctx, path, "palm", 13, false);
            set["special"] = HeroArt.Load(ctx, path, "special", 11, false);
            set["defend"] = HeroArt.Load(ctx, path, "defend", 9, false);
            set["hit"] = HeroArt.Load(ctx, path, "hit", 11, false);
            set["dash"] = HeroArt.Load(ctx, path, "dash", 6, false);
            set["defeat"] = HeroArt.Load(ctx, path, "defeat", 7, false);
            set["victory"] = HeroArt.Load(ctx, path, "victory", 3, true);
            set["rest"] = HeroArt.Load(ctx, path, "meditate", 4, false);
        }

        public BattleDto Battle { get; private set; }

        /// <summary>An animation (or effect) a fighter starts at a given time; <see cref="Move"/> = lunge distance toward the foe (0-1).</summary>
        private sealed class Cue
        {
            public bool Hero;
            public bool Fx;
            public bool Overlay;
            public float Size = 0.62f;
            public float Move;
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
                    var heroLost = battle.Me.Hp <= 0 || battle.Result == "lost";
                    var foeLost = battle.Opponent.Hp <= 0 || battle.Result == "won";
                    if (heroLost) _cues.Add(new Cue { Hero = true, Anim = _hero["defeat"], At = t });
                    if (foeLost) _cues.Add(new Cue { Hero = false, Anim = _foe["defeat"], At = t });
                    if (battle.Result == "won")
                    {
                        _cues.Add(new Cue { Hero = true, Anim = _hero["victory"], At = t + 600 });
                        if (_ovl.TryGetValue("victory", out var v)) _cues.Add(new Cue { Hero = true, Fx = true, Overlay = true, Size = -1, Anim = v, At = t + 900 });
                    }

                    _endAt = t + 1100;
                }
            }
            else if (previous == null && !battle.IsActive)
            {
                _endAt = 0;
            }

            PostInvalidateOnAnimation();
        }

        private void Impact(bool onHero, long at, string fx, string light, float shake, float size = 0.62f)
        {
            if (fx != null && _fx.TryGetValue(fx, out var f)) _cues.Add(new Cue { Hero = onHero, Fx = true, Anim = f, At = at, Size = size });
            if (light != null && _ovl.TryGetValue(light, out var o)) _cues.Add(new Cue { Hero = onHero, Fx = true, Overlay = true, Anim = o, At = at, Size = size * 0.9f });
            if (shake > 0) _shakes.Add((at, shake));
        }

        private void Schedule(TurnEventDto e, bool mine, long at)
        {
            var actor = mine ? _hero : _foe;
            var target = mine ? _foe : _hero;
            var tech = e.TechniqueId ?? string.Empty;
            switch (e.Action)
            {
                case "attack":
                {
                    var ranged = tech.Contains("palm") || tech.Contains("qi") || tech.Contains("blast") || tech.Contains("needle") || tech.Contains("dagger") || tech.Contains("dart") || tech.Contains("lance") || tech.Contains("rain");
                    var anim = ranged && actor.ContainsKey("palm") ? actor["palm"] : actor["attack"];
                    // melee: lunge most of the way to the foe; ranged: a short step
                    _cues.Add(new Cue { Hero = mine, Anim = anim, At = at, Move = ranged ? 0.18f : 0.62f });
                    var impact = at + anim.DurationMs * 55 / 100;
                    if (!mine && _ovl.TryGetValue("lightning", out var bolt)) _cues.Add(new Cue { Hero = true, Fx = true, Overlay = true, Anim = bolt, At = impact - 200, Size = 1.1f });
                    if (e.Hit == true)
                    {
                        _cues.Add(new Cue { Hero = !mine, Anim = target["hit"], At = impact });
                        var crit = e.Crit || e.Exploit || e.Momentum >= 3;
                        var light = crit ? "crit" : tech.Contains("fist") || tech.Contains("mountain") || tech.Contains("claw") ? "hit_heavy" : ranged ? "hit_qi" : "hit";
                        // 1.7: the screen shakes with the weight of the blow (share of the target's health); crits hit hardest
                        var targetMax = Math.Max(1, mine ? Battle.Opponent.MaxHp : Battle.Me.MaxHp);
                        var weight = Math.Min(1.3f, 0.35f + (e.Damage * 4f / targetMax));
                        Impact(!mine, impact, mine ? TechniqueFx(tech) : _look.AttackFx, light, crit ? Math.Max(1f, weight) : weight);
                        if (mine) Sfx.Play(Sfx.Hit); else Sfx.Play(Sfx.Hurt);
                        _floaters.Add(new Floater { OnHero = !mine, Text = "-" + e.Damage, Color = mine ? Color.Rgb(255, 230, 140) : Color.Rgb(255, 110, 90), Size = crit ? 32 : 26, At = impact });
                        var mech = e.Mechanic switch
                        {
                            "charge" => "ŁADUNEK QI!",
                            "ember_burst" => "WYBUCH ŻARU!",
                            "blossom" => "KWITNIENIE JADU!",
                            "fury" => "FURIA!",
                            _ => null,
                        };
                        if (mech != null) _floaters.Add(new Floater { OnHero = e.Mechanic == "fury" ? !mine : mine, Text = mech, Color = Ui.Gold, Size = 17, At = impact + 60 });
                        if (e.Crit) _floaters.Add(new Floater { OnHero = !mine, Text = "KRYTYK!", Color = Color.Rgb(255, 80, 60), Size = 16, At = impact + 120 });
                        else if (e.Exploit) _floaters.Add(new Floater { OnHero = !mine, Text = "SŁABOŚĆ!", Color = Ui.Gold, Size = 15, At = impact + 160 });
                        else if (e.Momentum >= 2) _floaters.Add(new Floater { OnHero = mine, Text = "ROZPĘD ×" + e.Momentum, Color = Ui.Gold, Size = 14, At = impact + 160 });
                    }
                    else if (e.Hit == false)
                    {
                        _cues.Add(new Cue { Hero = !mine, Anim = (mine ? _foe : _hero).ContainsKey("dash") ? (mine ? _foe : _hero)["dash"] : target["idle"], At = impact - 120, Move = -0.25f });
                        _floaters.Add(new Floater { OnHero = !mine, Text = "unik", Color = Color.Rgb(220, 220, 220), Size = 18, At = impact });
                    }

                    break;
                }
                case "defend":
                case "defense":
                case "counter_stance":
                    if (actor.ContainsKey("defend")) _cues.Add(new Cue { Hero = mine, Anim = actor["defend"], At = at });
                    Impact(mine, at + 150, "parry", null, 0, 0.5f);
                    _floaters.Add(new Floater { OnHero = mine, Text = e.Action == "counter_stance" ? "kontra" : "garda", Color = Ui.Gold, Size = 18, At = at + 200 });
                    break;
                case "rest":
                    if (actor.ContainsKey("rest")) _cues.Add(new Cue { Hero = mine, Anim = actor["rest"], At = at });
                    _cues.Add(new Cue { Hero = mine, Fx = true, Anim = _fx["gather"], At = at });
                    _floaters.Add(new Floater { OnHero = mine, Text = "+Qi", Color = Ui.QiBlue, Size = 18, At = at + 200 });
                    break;
                case "special":
                {
                    // the special skill: power-up pose in a full aura, then the strike and/or healing
                    var cast = actor.ContainsKey("special") ? actor["special"] : actor["attack"];
                    _cues.Add(new Cue { Hero = mine, Anim = cast, At = at });
                    if (mine && _heroAura != null) _cues.Add(new Cue { Hero = true, Fx = true, Overlay = true, Anim = _heroAura, At = at, Size = -2 });
                    else _cues.Add(new Cue { Hero = mine, Fx = true, Anim = _fx["gold"], At = at });
                    var strikeAt = at + cast.DurationMs * 70 / 100;
                    if (e.Hit != null)
                    {
                        var sAnim = actor.ContainsKey("palm") ? actor["palm"] : actor["attack"];
                        _cues.Add(new Cue { Hero = mine, Anim = sAnim, At = strikeAt, Move = 0.3f });
                        var sImpact = strikeAt + sAnim.DurationMs * 55 / 100;
                        if (e.Hit == true)
                        {
                            _cues.Add(new Cue { Hero = !mine, Anim = target["hit"], At = sImpact });
                            Impact(!mine, sImpact, mine ? TechniqueFx(tech) : _look.AttackFx, "crit", 1.1f, 0.8f);
                            _floaters.Add(new Floater { OnHero = !mine, Text = "-" + e.Damage, Color = Color.Rgb(255, 210, 90), Size = 32, At = sImpact });
                            Sfx.Play(mine ? Sfx.Hit : Sfx.Hurt);
                        }
                        else
                        {
                            _floaters.Add(new Floater { OnHero = !mine, Text = "unik", Color = Color.Rgb(220, 220, 220), Size = 18, At = sImpact });
                        }
                    }

                    if (e.Healed > 0)
                    {
                        if (_ovl.TryGetValue("heal", out var heal)) _cues.Add(new Cue { Hero = mine, Fx = true, Overlay = true, Anim = heal, At = at + 200, Size = -2 });
                        _floaters.Add(new Floater { OnHero = mine, Text = "+" + e.Healed, Color = Ui.Jade, Size = 26, At = at + 600 });
                    }
                    else if (e.Hit == null)
                    {
                        _floaters.Add(new Floater { OnHero = mine, Text = "★", Color = Ui.Gold, Size = 22, At = at + 200 });
                    }

                    break;
                }
                case "stunned":
                    _floaters.Add(new Floater { OnHero = mine, Text = "ogłuszony", Color = Ui.Violet, Size = 16, At = at });
                    break;
                case "bleed":
                    _floaters.Add(new Floater { OnHero = mine, Text = "-" + e.Damage, Color = Color.Rgb(200, 60, 60), Size = 18, At = at });
                    break;
                case "burn":
                    _floaters.Add(new Floater { OnHero = mine, Text = "🔥-" + e.Damage, Color = Color.Rgb(255, 140, 40), Size = 18, At = at });
                    break;
                case "poison":
                    _floaters.Add(new Floater { OnHero = mine, Text = "☠-" + e.Damage, Color = Color.Rgb(120, 200, 80), Size = 18, At = at });
                    break;
            }
        }

        /// <summary>Impact effect per technique family (fire, venom, blades, thunder, blood, plain Qi).</summary>
        private static string TechniqueFx(string tech)
        {
            if (tech.Contains("flame") || tech.Contains("fire") || tech.Contains("sun") || tech.Contains("phoenix") || tech.Contains("ember") || tech.Contains("inferno")) return "flame";
            if (tech.Contains("venom") || tech.Contains("poison") || tech.Contains("toxic") || tech.Contains("miasma") || tech.Contains("serpent") || tech.Contains("scorpion")) return "poison";
            if (tech.Contains("thunder") || tech.Contains("lightning") || tech.Contains("frost") || tech.Contains("ice")) return "blue";
            if (tech.Contains("sword") || tech.Contains("blade") || tech.Contains("dagger") || tech.Contains("thrust") || tech.Contains("claw") || tech.Contains("beak")) return "slash";
            if (tech.Contains("blood") || tech.Contains("demon") || tech.Contains("soul")) return "red";
            return "qi";
        }

        /// <summary>The body animation a fighter shows now (latest started cue, else idle or the held final pose) and its lunge.</summary>
        private (Anim Anim, long Elapsed, float Move) Current(bool hero, long now)
        {
            Cue best = null;
            foreach (var c in _cues)
            {
                if (c.Fx || c.Hero != hero || c.At > now) continue;
                var set = hero ? _hero : _foe;
                var held = c.Anim == set["defeat"] || (set.ContainsKey("victory") && c.Anim == set["victory"]);
                var done = !c.Anim.Loop && now - c.At >= c.Anim.DurationMs;
                if (done && !held) continue;
                if (best == null || c.At >= best.At) best = c;
            }

            if (best != null)
            {
                var k = Math.Clamp((now - best.At) / (float)Math.Max(1, best.Anim.DurationMs), 0, 1);
                return (best.Anim, now - best.At, best.Move * (float)Math.Sin(Math.PI * k));
            }

            var fighter = hero ? Battle.Me : Battle.Opponent;
            var idle = hero ? _hero : _foe;
            if (!Battle.IsActive && fighter.Hp <= 0 && _cues.Count == 0) return (idle["defeat"], 100000, 0);
            if (!Battle.IsActive && hero && Battle.Result == "won" && _cues.Count == 0 && idle.ContainsKey("victory")) return (idle["victory"], now, 0);
            return (idle["idle"], now, 0);
        }

        private bool Flashing(bool hero, long now) =>
            _cues.Any(c => !c.Fx && c.Hero == hero && c.Anim == (hero ? _hero : _foe)["hit"] && now >= c.At && now - c.At < 90);

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Battle == null || Width == 0) return;
            var now = SystemClock.UptimeMillis();
            float w = Width, h = Height;

            // screen shake (decays over 260 ms per impact)
            float sx = 0, sy = 0;
            foreach (var (at, power) in _shakes)
            {
                var k = (now - at) / 260f;
                if (k < 0 || k > 1) continue;
                var amp = power * 7 * _d * (1 - k);
                sx += amp * (float)Math.Sin(now * 0.09);
                sy += amp * 0.6f * (float)Math.Cos(now * 0.13);
            }

            canvas.Save();
            canvas.Translate(sx, sy);
            var src = CoverRect(_backdrop.Width, _backdrop.Height, w, h);
            canvas.DrawBitmap(_backdrop, src, new RectF(-12 * _d, -12 * _d, w + 12 * _d, h + 12 * _d), new Paint(PaintFlags.FilterBitmap));
            _p.SetShader(new LinearGradient(0, 0, 0, h, new int[] { Color.Argb(30, 0, 0, 0).ToArgb(), Color.Argb(70, 0, 0, 0).ToArgb(), Color.Argb(210, 6, 8, 10).ToArgb() }, new float[] { 0, 0.55f, 1 }, Shader.TileMode.Clamp));
            canvas.DrawRect(-12 * _d, -12 * _d, w + 12 * _d, h + 12 * _d, _p);
            _p.SetShader(null);
            if (_ovl.TryGetValue("mist", out var mist)) DrawOverlay(canvas, mist, now - _born, new RectF(0, h * 0.55f, w, h), 150);

            var gap = w * 0.22f; // no distance any more: the fighters keep a fixed duel spacing
            // Fighters stand further back in the arena: smaller, with the floor line a little higher (depth).
            var ground = h * 0.86f;
            var cellH = Math.Min(h * 0.54f, w * 0.42f); // the 320x256 cell; characters fill ~70% of it
            var cellW = cellH * 320f / 256f;
            var footInCell = cellH * 246f / 256f;
            var (ha, he, hm) = Current(true, now);
            var (fa, fe, fm) = Current(false, now);
            var heroX = w / 2 - gap + hm * gap * 2 * 0.62f;
            var foeX = w / 2 + gap - fm * gap * 2 * 0.62f;
            var bodyY = ground - cellH * 0.36f;

            // the hero's path aura: faint while fighting, full during the special skill (drawn as a cue)
            if (_heroAura != null && Battle.IsActive)
                DrawOverlay(canvas, _heroAura, now - _born, AuraRect(heroX, ground, cellH), 95);

            DrawShadow(canvas, heroX, ground, cellW * 0.16f);
            DrawShadow(canvas, foeX, ground, cellW * 0.2f * _foeScale);
            canvas.DrawBitmap(ha.Strip, ha.Frame(he), new RectF(heroX - cellW / 2, ground - footInCell, heroX + cellW / 2, ground - footInCell + cellH), Flashing(true, now) ? _flash : _pixel);
            var fw = cellW * _foeScale;
            var fh = cellH * _foeScale;
            if (_look.Aura is { } aura)
            {
                var pulse = (float)(0.5 + 0.5 * Math.Sin(now / 260.0));
                var ar = fw * (0.32f + 0.04f * pulse);
                var ay = ground - fh * 0.38f;
                _p.SetShader(new RadialGradient(foeX, ay, ar, new int[] { Color.Argb((int)(90 + 60 * pulse), aura.R, aura.G, aura.B).ToArgb(), Color.Argb(0, aura.R, aura.G, aura.B).ToArgb() }, null, Shader.TileMode.Clamp));
                canvas.DrawCircle(foeX, ay, ar, _p);
                _p.SetShader(null);
            }

            canvas.Save();
            if (_foeFlip) canvas.Scale(-1, 1, foeX, ground);
            canvas.DrawBitmap(fa.Strip, fa.Frame(fe), new RectF(foeX - fw / 2, ground - footInCell * _foeScale, foeX + fw / 2, ground - footInCell * _foeScale + fh),
                Flashing(false, now) ? _flash : _foePaint);
            canvas.Restore();

            // status loops over the heads
            if (Battle.IsActive)
            {
                StatusOverlays(canvas, Battle.Me, heroX, ground - cellH * 0.78f, cellH, now);
                StatusOverlays(canvas, Battle.Opponent, foeX, ground - fh * 0.78f, cellH, now);
            }

            // effects over the fighters
            foreach (var c in _cues)
            {
                if (!c.Fx || c.At > now || (!c.Anim.Loop && now - c.At >= c.Anim.DurationMs) || (c.Anim.Loop && now - c.At > 1600)) continue;
                var x = c.Hero ? heroX : foeX;
                if (c.Size == -1)
                {
                    DrawOverlay(canvas, c.Anim, now - c.At, new RectF(0, 0, w, h), 230);
                    continue;
                }

                if (c.Size == -2)
                {
                    DrawOverlay(canvas, c.Anim, now - c.At, AuraRect(x, ground, cellH), 255);
                    continue;
                }

                if (c.Overlay && c.Anim.Strip.Height > c.Anim.CellW * 1.5f && c.Size > 1)
                {
                    // tall one-shot (lightning) from the sky down onto the target
                    var lw = cellH * 0.9f;
                    DrawOverlay(canvas, c.Anim, now - c.At, new RectF(x - lw / 2, ground - lw * 1.78f, x + lw / 2, ground + cellH * 0.05f), 255);
                    continue;
                }

                var size = cellH * c.Size;
                var r = new RectF(x - size / 2, bodyY - size / 2, x + size / 2, bodyY + size / 2);
                if (c.Overlay) DrawOverlay(canvas, c.Anim, now - c.At, r, 255);
                else canvas.DrawBitmap(c.Anim.Strip, c.Anim.Frame(now - c.At), r, _pixel);
            }

            if (_ovl.TryGetValue("rain", out var rain)) DrawOverlay(canvas, rain, now - _born, new RectF(0, 0, w, h), 140);
            canvas.Restore();

            foreach (var f in _floaters)
            {
                var k = (now - f.At) / 1000f;
                if (k < 0 || k > 1.2f) continue;
                _text.TextSize = f.Size * _d * (k < 0.08f ? 1.35f - k * 4 : 1f);
                _text.Color = f.Color;
                _text.Alpha = (int)(255 * Math.Clamp(1.2f - k, 0, 1));
                canvas.DrawText(f.Text, f.OnHero ? heroX : foeX, ground - cellH * 0.86f - k * 40 * _d, _text);
                _text.Alpha = 255;
            }

            // low health: a red pulse around the screen
            if (Battle.IsActive && Battle.Me.MaxHp > 0 && Battle.Me.Hp * 10 < Battle.Me.MaxHp * 3 && _ovl.TryGetValue("low_hp", out var low))
                DrawOverlay(canvas, low, now - _born, new RectF(0, 0, w, h), 200);

            Plate(canvas, Battle.Me, 10 * _d, 10 * _d, Ui.Jade, left: true);
            Plate(canvas, Battle.Opponent, w - 10 * _d, 10 * _d, Ui.Cinnabar, left: false);
            var momentum = Battle.Me.EffectMagnitude("momentum");
            if (Battle.IsActive && momentum > 0)
            {
                _text.TextSize = 11 * _d;
                _text.Color = Ui.Gold;
                _text.TextAlign = Paint.Align.Left;
                canvas.DrawText("ROZPĘD ×" + momentum, 14 * _d, 70 * _d, _text);
                _text.TextAlign = Paint.Align.Center;
            }

            var animating = _cues.Any(c => !c.Fx && !c.Anim.Loop && now - c.At < c.Anim.DurationMs);
            if (Battle.IsActive && !animating && Battle.OpponentIntent != null) Intent(canvas, Battle.OpponentIntent, foeX, ground - fh * 0.98f - 18 * _d);

            if (Battle.TowerFloor != null || Battle.Kind == "boss")
            {
                _text.TextSize = 12 * _d;
                _text.Color = Ui.Gold;
                canvas.DrawText(Battle.Kind == "boss" ? "BOSS ŚWIATA · ZADANE " + Battle.DamageDealt : "WIEŻA PRÓB · PIĘTRO " + Battle.TowerFloor, w / 2, 70 * _d, _text);
            }

            _text.TextSize = 13 * _d;
            _text.Color = Ui.Gold;
            canvas.DrawText("TURA " + Battle.Turn, w / 2, h - 8 * _d, _text);

            if (!Battle.IsActive && _endAt >= 0 && now >= _endAt)
            {
                var won = Battle.Result == "won";
                var draw = Battle.Result == "draw";
                _p.Color = Color.Argb(150, 0, 0, 0);
                canvas.DrawRect(0, h * 0.36f, w, h * 0.6f, _p);
                _text.TextSize = 34 * _d;
                _text.Color = won ? Ui.Gold : draw ? Ui.Ink : Ui.Cinnabar;
                canvas.DrawText(won ? "ZWYCIĘSTWO" : draw ? "REMIS" : "PORAŻKA", w / 2, h * 0.5f, _text);
                if (!_endSoundPlayed)
                {
                    _endSoundPlayed = true;
                    Sfx.Play(won ? Sfx.Victory : Sfx.Defeat);
                }
                if (Battle.RewardQi > 0)
                {
                    _text.TextSize = 14 * _d;
                    _text.Color = Ui.Ink;
                    canvas.DrawText("+" + Battle.RewardQi + " Qi", w / 2, h * 0.57f, _text);
                }
            }

            _cues.RemoveAll(c => !c.Anim.Loop && now - c.At > c.Anim.DurationMs + 4000 && c.Anim != _hero["defeat"] && c.Anim != _foe["defeat"] && c.Anim != _hero["victory"]);
            _cues.RemoveAll(c => c.Anim.Loop && c.Fx && now - c.At > 1600);
            _floaters.RemoveAll(f => now - f.At > 1300);
            _shakes.RemoveAll(s => now - s.At > 300);
            PostInvalidateDelayed(33); // ~30 fps: idle loops always move
        }

        private RectF AuraRect(float x, float ground, float cellH)
        {
            var ah = cellH * 1.05f;
            var aw = ah * 200f / 356f;
            return new RectF(x - aw / 2, ground - ah * 0.94f, x + aw / 2, ground + ah * 0.06f);
        }

        private void DrawOverlay(Canvas canvas, Anim a, long elapsed, RectF dst, int alpha)
        {
            _glow.Alpha = alpha;
            canvas.DrawBitmap(a.Strip, a.Frame(elapsed), dst, _glow);
        }

        private void StatusOverlays(Canvas canvas, FighterDto f, float x, float headY, float cellH, long now)
        {
            if (f?.Effects == null) return;
            var size = cellH * 0.34f;
            void One(string effect, string key, float dy)
            {
                if (!f.Has(effect) || !_ovl.TryGetValue(key, out var a)) return;
                DrawOverlay(canvas, a, now - _born, new RectF(x - size / 4, headY + dy - size / 2, x + size / 4, headY + dy + size / 2), 230);
            }

            One("stun", "stun", 0);
            One("bleed", "bleed", cellH * 0.3f);
            One("burn", "bleed", cellH * 0.3f);
            One("weaken", "weaken", cellH * 0.3f);
            One("poison", "weaken", cellH * 0.3f);
        }

        /// <summary>The opponent's telegraphed move: a small banner over its head.</summary>
        private void Intent(Canvas canvas, IntentPreviewDto intent, float x, float y)
        {
            var (label, color) = intent.Action switch
            {
                "attack" => ("⚔ ATAK", Ui.Cinnabar),
                "defend" => ("🛡 GARDA", Ui.QiBlue),
                "counter" => ("↺ KONTRA", Ui.Violet),
                "special" => ("★ SPECJAŁ", Ui.Gold),
                "stunned" => ("✦ OGŁUSZONY", Ui.Muted),
                _ => ("… ODPOCZYNEK", Ui.Jade),
            };
            _text.TextSize = 11 * _d;
            var tw = Math.Max(_text.MeasureText(label), 40 * _d) + 16 * _d;
            var top = Math.Max(52 * _d + 30 * _d, y - 20 * _d);
            var x0 = Math.Clamp(x - tw / 2, 4 * _d, Width - tw - 4 * _d);
            var r = new RectF(x0, top, x0 + tw, top + 20 * _d);
            var pulse = (float)(0.6 + 0.4 * Math.Sin(SystemClock.UptimeMillis() / 220.0));
            _p.Color = Color.Argb(215, 10, 8, 8);
            canvas.DrawRoundRect(r, 10 * _d, 10 * _d, _p);
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = 1.5f * _d;
            _p.Color = Color.Argb((int)(255 * pulse), color.R, color.G, color.B);
            canvas.DrawRoundRect(r, 10 * _d, 10 * _d, _p);
            _p.SetStyle(Paint.Style.Fill);
            _text.Color = color;
            canvas.DrawText(label, r.CenterX(), r.Bottom - 6 * _d, _text);
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
            var badges = f.Badges != null && f.Badges.Count > 0 ? " " + string.Join(" ", f.Badges) : string.Empty;
            canvas.DrawText(f.Name + badges, left ? l + 8 * _d : l + pw - 8 * _d, y + 16 * _d, _text);
            if (!string.IsNullOrEmpty(f.Title))
            {
                // the chosen title floats above the plate, coloured by its rarity
                var tc = Ui.RarityOf(TitleRarity(f.TitleRarity)).Color;
                _text.TextSize = 10 * _d;
                var label = "「" + f.Title + "」";
                var tw = _text.MeasureText(label) + 14 * _d;
                var tl = left ? l : l + pw - tw;
                var tr = new RectF(tl, y - 17 * _d, tl + tw, y - 2 * _d);
                _p.Color = Color.Argb(210, 8, 10, 12);
                canvas.DrawRoundRect(tr, 6 * _d, 6 * _d, _p);
                _p.SetStyle(Paint.Style.Stroke);
                _p.Color = tc;
                canvas.DrawRoundRect(tr, 6 * _d, 6 * _d, _p);
                _p.SetStyle(Paint.Style.Fill);
                _text.Color = tc;
                _text.TextAlign = Paint.Align.Center;
                canvas.DrawText(label, tr.CenterX(), tr.Bottom - 4 * _d, _text);
                _text.TextSize = 13 * _d;
                _text.Color = Ui.Ink;
            }
            _text.TextAlign = Paint.Align.Center;
            Bar(canvas, new RectF(l + 8 * _d, y + 22 * _d, l + pw - 8 * _d, y + 31 * _d), f.Hp, f.MaxHp, hpColor);
            Bar(canvas, new RectF(l + 8 * _d, y + 34 * _d, l + pw - 8 * _d, y + 40 * _d), f.Qi, f.MaxQi, Ui.QiBlue);
            _text.TextSize = 9 * _d;
            _text.Color = Ui.Ink;
            canvas.DrawText(f.Hp + " / " + f.MaxHp, l + pw / 2, y + 30 * _d, _text);

            // 1.7: every effect on the fighter with its turns left (path resources show their value)
            var chips = EffectChips(f);
            if (chips.Length > 0)
            {
                _text.TextSize = 11 * _d;
                _text.TextAlign = left ? Paint.Align.Left : Paint.Align.Right;
                _text.Color = Ui.Gold;
                canvas.DrawText(chips, left ? l + 8 * _d : l + pw - 8 * _d, y + 54 * _d, _text);
                _text.TextAlign = Paint.Align.Center;
                _text.TextSize = 13 * _d;
                _text.Color = Ui.Ink;
            }
        }

        private static string EffectChips(FighterDto f)
        {
            if (f?.Effects == null || f.Effects.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (var g in f.Effects.GroupBy(e => e.Type))
            {
                var e = g.First();
                var stacks = g.Count() > 1 ? "×" + g.Count() : string.Empty;
                var label = g.Key switch
                {
                    "bleed" => "🩸" + stacks,
                    "burn" => "🔥" + stacks,
                    "poison" => "☠" + stacks,
                    "stun" => "💫",
                    "weaken" => "↓atk",
                    "slow" => "🐢",
                    "frail" => "✧",
                    "shield" => "🛡" + e.Magnitude,
                    "empower" => "↑atk",
                    "fortify" => "◆def",
                    "haste" => "»",
                    "charge" => "⚡" + e.Magnitude + "%",
                    "fury" => "🔴" + e.Magnitude,
                    "momentum" => "➶" + e.Magnitude,
                    _ => null,
                };
                if (label == null) continue;
                var turns = g.Key is "charge" or "fury" or "momentum" or "stun" or "shield" ? string.Empty : "(" + g.Max(x => x.TurnsLeft) + ")";
                parts.Add(label + turns);
            }

            return string.Join(" ", parts);
        }

        /// <summary>Title plates use the item rarity palette: common/rare/epic/legendary/mythic → mortal…divine.</summary>
        private static string TitleRarity(string r) => r switch
        {
            "rare" => "spirit",
            "epic" => "earth",
            "legendary" => "heavenly",
            "mythic" => "divine",
            _ => "mortal",
        };

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

    /// <summary>
    /// How each enemy looks on the stage until it gets its own sprite sheet: a base animation set (beast, humanoid,
    /// construct), a colour grade, a size and an arena; bosses also get a pulsing aura. Unknown ids fall back by name.
    /// </summary>
    public static class NpcLooks
    {
        public readonly struct Tint
        {
            public Tint(int mul, int add) { Mul = mul; Add = add; }
            public int Mul { get; }
            public int Add { get; }
        }

        public sealed class Look
        {
            public string Base = "bandit";
            public float Scale = 1f;
            public float Speed = 1f;
            public Tint? Tint;
            public Color? Aura;
            public int Arena = Resource.Drawable.arena_fortress;
            public int TileArt = Resource.Drawable.tile_bandit;
            public string AttackFx = "slash";

            /// <summary>With its own sprite sheets the enemy needs no colour grade; bosses stay a bit larger.</summary>
            public Look WithOwnSprites() => new Look
            {
                Base = "own", Scale = Aura != null ? 1.12f : 1f, Speed = Speed, Tint = null, Aura = Aura, Arena = Arena, TileArt = TileArt, AttackFx = AttackFx,
            };
        }

        private static Tint T(int r, int g, int b, int ar = 0, int ag = 0, int ab = 0) =>
            new Tint(Color.Rgb(r, g, b).ToArgb(), Color.Rgb(ar, ag, ab).ToArgb());

        public static Look For(string npcId)
        {
            switch (npcId)
            {
                case null:
                case "pvp": return new Look { Base = "hero", Arena = Resource.Drawable.arena_temple, AttackFx = "qi" };
                case "training_dummy": return new Look { Base = "dummy", Arena = Resource.Drawable.arena_courtyard, TileArt = Resource.Drawable.tile_dummy };
                case "grey_wolf": return Beast(Resource.Drawable.arena_bamboo, 1.25f, null);
                case "wild_boar": return Tile(Resource.Drawable.tile_wild_boar, Beast(Resource.Drawable.arena_plains, 1.3f, T(200, 150, 110, 30, 10, 0), speed: 0.85f));
                case "bamboo_viper": return Tile(Resource.Drawable.tile_bamboo_viper, Beast(Resource.Drawable.arena_bamboo, 1.0f, T(120, 220, 120), fx: "poison", speed: 1.3f));
                case "fire_fox": return Tile(Resource.Drawable.tile_fire_fox, Beast(Resource.Drawable.arena_mountain, 1.1f, T(255, 150, 80, 60, 10, 0), fx: "flame", speed: 1.2f));
                case "frost_spider": return Tile(Resource.Drawable.tile_frost_spider, Beast(Resource.Drawable.arena_swamp, 1.15f, T(150, 200, 255, 10, 30, 60), fx: "blue", speed: 1.2f));
                case "thunder_eagle": return Tile(Resource.Drawable.tile_thunder_eagle, Beast(Resource.Drawable.arena_mountain, 1.2f, T(220, 220, 140, 40, 40, 70), fx: "blue", speed: 1.35f));
                case "ancient_tortoise": return Tile(Resource.Drawable.tile_ancient_tortoise, Boss(Beast(Resource.Drawable.arena_tortoise_lake, 1.55f, T(130, 180, 140, 10, 30, 10), speed: 0.6f), Ui.Jade));
                case "jiao_dragon": return Tile(Resource.Drawable.tile_jiao_dragon, Boss(Beast(Resource.Drawable.arena_dragon_abyss, 1.6f, T(120, 170, 255, 0, 20, 70), fx: "blue", speed: 1.1f), Ui.QiBlue));
                case "mountain_bandit": return Human(Resource.Drawable.arena_plains, 1f, null);
                case "demonic_cultivator": return Tile(Resource.Drawable.tile_demonic_cultivator, Human(Resource.Drawable.arena_ruins, 1.05f, T(190, 120, 220, 30, 0, 40), fx: "red"));
                case "sect_traitor": return Tile(Resource.Drawable.tile_sect_traitor, Human(Resource.Drawable.arena_city, 1f, T(170, 170, 200, 10, 10, 30), fx: "gold"));
                case "corpse_puppet": return Tile(Resource.Drawable.tile_corpse_puppet, Human(Resource.Drawable.arena_swamp, 1.1f, T(140, 170, 140, 20, 30, 20), fx: "poison", speed: 0.75f));
                case "bandit_chief": return Tile(Resource.Drawable.tile_bandit_chief, Boss(Human(Resource.Drawable.arena_bandit_fortress, 1.3f, T(255, 210, 160, 30, 15, 0), fx: "gold"), Ui.Gold));
                case "blood_demon_lord": return Tile(Resource.Drawable.tile_blood_demon_lord, Boss(Human(Resource.Drawable.arena_blood_gate, 1.45f, T(255, 110, 110, 70, 0, 0), fx: "red"), Ui.Cinnabar));
                case "storm_qilin": return Tile(Resource.Drawable.tile_storm_qilin, Boss(Beast(Resource.Drawable.arena_mountain, 1.5f, null, fx: "blue", speed: 1f), Ui.QiBlue));
                case "flame_spirit_lesser": return Tile(Resource.Drawable.tile_flame_spirit_lesser, Human(Resource.Drawable.arena_ruins, 0.9f, T(255, 170, 90, 70, 20, 0), fx: "flame", speed: 1.2f));
                case "flame_spirit_greater": return Tile(Resource.Drawable.tile_flame_spirit_greater, Human(Resource.Drawable.arena_ruins, 1.2f, T(255, 140, 70, 80, 20, 0), fx: "flame"));
                case "flame_spirit_primal": return Tile(Resource.Drawable.tile_flame_spirit_primal, Boss(Beast(Resource.Drawable.arena_ruins, 1.5f, T(255, 130, 60, 90, 25, 0), fx: "flame", speed: 1.1f), Ui.Cinnabar));
                case "heavenly_tribulation": return Tile(Resource.Drawable.tile_heavenly_tribulation, Boss(Human(Resource.Drawable.arena_mountain, 1.3f, T(200, 170, 255, 40, 20, 80), fx: "blue"), Ui.Violet));
                case "flame_tribulation": return Tile(Resource.Drawable.tile_flame_tribulation, Boss(Human(Resource.Drawable.arena_mountain, 1.3f, T(255, 150, 80, 80, 20, 0), fx: "flame"), Ui.Cinnabar));
                case "heart_demon_tribulation": return Tile(Resource.Drawable.tile_heart_demon_tribulation, Boss(Human(Resource.Drawable.arena_mountain, 1.15f, T(120, 100, 130, 40, 0, 20), fx: "red"), Ui.Violet));
                case "stone_golem": return Tile(Resource.Drawable.tile_stone_golem, new Look { Base = "dummy", Scale = 1.4f, Tint = T(150, 150, 160, 20, 20, 25), Arena = Resource.Drawable.arena_temple, TileArt = Resource.Drawable.tile_dummy, AttackFx = "slash" });
            }

            // unknown (newer content): guess by name
            var id = npcId ?? string.Empty;
            return id.Contains("wolf") || id.Contains("beast") || id.Contains("fox") || id.Contains("tiger") || id.Contains("serpent")
                ? Beast(Resource.Drawable.arena_bamboo, 1.2f, T(200, 200, 200))
                : Human(Resource.Drawable.arena_fortress, 1f, T(200, 200, 200));
        }

        private static Look Beast(int arena, float scale, Tint? tint, string fx = "slash", float speed = 1f) =>
            new Look { Base = "wolf", Scale = scale, Tint = tint, Arena = arena, TileArt = Resource.Drawable.tile_wolf, AttackFx = fx, Speed = speed };

        private static Look Human(int arena, float scale, Tint? tint, string fx = "slash", float speed = 1f) =>
            new Look { Base = "bandit", Scale = scale, Tint = tint, Arena = arena, TileArt = Resource.Drawable.tile_bandit, AttackFx = fx, Speed = speed };

        private static Look Tile(int art, Look look)
        {
            look.TileArt = art;
            return look;
        }

        private static Look Boss(Look look, Color aura)
        {
            look.Aura = aura;
            return look;
        }
    }
}
