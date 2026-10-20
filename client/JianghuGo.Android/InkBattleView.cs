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
    /// 2.2 ink duel: small painted fighters on a monochrome ink landscape, key poses swapped in rhythm (wind-up, dash,
    /// strike, recover), ink and light effects drawn in grey ink, technique banners and a cut-in for the hero's special.
    /// The server decides what happened; this view only turns each turn event into choreography.
    /// </summary>
    public sealed class InkBattleView : View
    {
        public const long StepMs = 760; // 2.2: faster by default (×1), ×2 and ×3 via BattleClock

        // ---- poses: relative height (of the fighter's stance) and the x anchor (feet) in the image -------------------
        private sealed class PoseInfo
        {
            public float Height;
            public float Anchor = 0.5f;
        }

        private static readonly Dictionary<string, Dictionary<string, PoseInfo>> Poses = new Dictionary<string, Dictionary<string, PoseInfo>>
        {
            ["qi"] = Table(("cast", 1.0f, .5f), ("victory", 1.0f, .5f), ("stance", 1.0f, .5f), ("windup", .92f, .5f), ("dash", .62f, .5f), ("strike", .60f, .33f), ("thrust", .46f, .30f),
                ("jump", .78f, .5f), ("air_slash", .95f, .45f), ("block", .95f, .5f), ("hurt", 1.0f, .5f), ("fall", .70f, .5f)),
            ["body"] = Table(("cast", 1.0f, .5f), ("victory", 1.0f, .5f), ("stance", .92f, .5f), ("windup", .95f, .5f), ("dash", .72f, .6f), ("strike", .88f, .40f), ("thrust", .85f, .5f),
                ("jump", .95f, .5f), ("air_slash", .85f, .40f), ("block", .92f, .5f), ("hurt", 1.0f, .5f), ("fall", .72f, .5f)),
            ["flame"] = Table(("cast", 1.0f, .45f), ("victory", .95f, .5f), ("stance", .95f, .4f), ("windup", .95f, .5f), ("dash", .85f, .45f), ("strike", .80f, .30f), ("thrust", .80f, .25f),
                ("jump", 1.0f, .5f), ("air_slash", .85f, .45f), ("block", .95f, .5f), ("hurt", 1.0f, .5f), ("fall", .80f, .5f)),
            ["poison"] = Table(("cast", 1.0f, .45f), ("victory", .95f, .5f), ("stance", .95f, .45f), ("windup", .95f, .45f), ("dash", .80f, .55f), ("strike", .90f, .40f), ("thrust", .75f, .40f),
                ("jump", 1.0f, .5f), ("air_slash", .85f, .40f), ("block", .90f, .40f), ("hurt", 1.0f, .5f), ("fall", .70f, .5f)),
            ["rival"] = Table(("cast", 1.05f, .55f), ("victory", 1.0f, .5f), ("stance", 1.0f, .5f), ("windup", .98f, .5f), ("dash", .72f, .5f), ("strike", .74f, .66f), ("thrust", .70f, .64f),
                ("jump", .85f, .5f), ("air_slash", .92f, .62f), ("hurt", 1.0f, .5f), ("fall", .62f, .6f)),
        };

        private static Dictionary<string, PoseInfo> Table(params (string Name, float H, float A)[] rows) =>
            rows.ToDictionary(r => r.Name, r => new PoseInfo { Height = r.H, Anchor = r.A });

        /// <summary>Foes drawn in the ink duel (people); beasts and spirits keep the classic stage until they have ink poses.</summary>
        public static bool Supports(string npcId, string kind) =>
            kind == "pvp" || npcId == null || npcId == "pvp" || npcId is "mountain_bandit" or "sect_traitor" or "demonic_cultivator" or "bandit_chief"
                or "corpse_puppet" or "blood_demon_lord" or "heart_demon_tribulation";

        // ---- keyframed fighters ------------------------------------------------------------------------------------
        private struct Key
        {
            public long At;
            public string Pose;
            public float X;      // share of the width
            public float Lift;   // fighter heights above the ground
        }

        private sealed class Fighter
        {
            public string Set;
            public bool Hero;
            public float Home;
            public readonly List<Key> Keys = new List<Key>();
            public long FlashUntil;
            public readonly Dictionary<string, Bitmap> Bitmaps = new Dictionary<string, Bitmap>();
        }

        private sealed class Fx
        {
            public Bitmap Bmp;
            public long At;
            public long Dur;
            public float X0, Y0, X1, Y1;   // x: share of width; y: fighter heights above the ground
            public float Size;             // width in fighter heights
            public float Grow;             // extra scale at the end
            public float Rot, Spin;
            public bool Flip;
            public float Alpha = 0.9f;
            public bool FadeIn;
        }

        private sealed class Floater
        {
            public string Text;
            public float X;
            public long At;
            public bool Big;
            public bool Soft;
        }

        private readonly float _d;
        private readonly Fighter _me;
        private readonly Fighter _foe;
        private readonly string _path;
        private readonly Bitmap _bg;
        private readonly Bitmap _stroke;
        private readonly Bitmap _avatarMe;
        private readonly Bitmap _avatarFoe;
        private readonly Bitmap _cutIn;
        private readonly Dictionary<string, Bitmap> _art = new Dictionary<string, Bitmap>();
        private readonly List<Fx> _fx = new List<Fx>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly List<(long At, float Power)> _shakes = new List<(long, float)>();
        private readonly List<(long At, string Sound)> _sounds = new List<(long, string)>();
        private readonly List<(long At, string Title, string Glyph)> _banners = new List<(long, string, string)>();
        private readonly List<long> _cutIns = new List<long>();
        private readonly Paint _paint = new Paint(PaintFlags.FilterBitmap | PaintFlags.AntiAlias);
        private readonly Paint _flash = new Paint(PaintFlags.FilterBitmap);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _line = new Paint(PaintFlags.AntiAlias);
        private readonly Context _ctx;
        private long _endAt = -1;
        private bool _endSound;
        private int _variant;

        public InkBattleView(Context ctx, string npcId, string heroPath, string kind, string opponentName) : base(ctx)
        {
            _ctx = ctx;
            _d = ctx.Resources.DisplayMetrics.Density;
            _path = HeroArt.Normalize(heroPath);
            _me = new Fighter { Set = _path, Hero = true, Home = 0.25f };
            _foe = new Fighter { Set = "rival", Hero = false, Home = 0.75f };
            var bgName = (npcId ?? "pvp") switch
            {
                "mountain_bandit" or "bandit_chief" => "duel_bg_cliff",
                "sect_traitor" or "corpse_puppet" or "blood_demon_lord" or "demonic_cultivator" => "duel_bg_temple",
                "heart_demon_tribulation" => "duel_bg_cliff",
                _ => kind == "tower" ? "duel_bg_bamboo" : "duel_bg_plain",
            };
            _bg = Art(bgName);
            _stroke = Art("duel_ink_brush_stroke") ?? Art("duel_ink_slash_double");
            _avatarMe = Art("duel_avatar_" + _path);
            _avatarFoe = Art("duel_avatar_rival");
            _cutIn = Art("duel_cutin_" + _path);
            _flash.SetColorFilter(new LightingColorFilter(Color.Rgb(255, 255, 255).ToArgb(), Color.Rgb(170, 170, 170).ToArgb()));
            _text.SetTypeface(Ui.Display);
            _text.TextAlign = Paint.Align.Center;
            _line.SetStyle(Paint.Style.Stroke);
            var now = BattleClock.Now;
            Rest(_me, now);
            Rest(_foe, now);
        }

        public BattleDto Battle { get; private set; }

        private Bitmap Art(string name)
        {
            if (_art.TryGetValue(name, out var b)) return b;
            var id = _ctx.Resources.GetIdentifier(name, "drawable", _ctx.PackageName);
            b = id == 0 ? null : Ui.Art(_ctx, id);
            _art[name] = b;
            return b;
        }

        private Bitmap PoseBitmap(Fighter f, string pose)
        {
            var table = Poses[f.Set];
            if (!table.ContainsKey(pose)) pose = pose switch { "block" => "windup", "fall" => "hurt", "victory" => "stance", "cast" => "windup", _ => "stance" };
            if (!f.Bitmaps.TryGetValue(pose, out var bmp))
            {
                bmp = Art("duel_pose_" + f.Set + "_" + pose);
                f.Bitmaps[pose] = bmp;
            }

            return bmp;
        }

        // ---- choreography ------------------------------------------------------------------------------------------

        private static void Rest(Fighter f, long at)
        {
            f.Keys.Clear();
            f.Keys.Add(new Key { At = at, Pose = "stance", X = f.Home, Lift = 0 });
        }

        private static Key StateAt(Fighter f, long t)
        {
            var keys = f.Keys;
            if (keys.Count == 0) return new Key { Pose = "stance", X = f.Home };
            if (t <= keys[0].At) return keys[0];
            for (var i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (t >= a.At && t < b.At)
                {
                    var k = (t - a.At) / (float)Math.Max(1, b.At - a.At);
                    k = 1 - (1 - k) * (1 - k) * (1 - k);
                    return new Key { At = t, Pose = a.Pose, X = a.X + (b.X - a.X) * k, Lift = a.Lift + (b.Lift - a.Lift) * k };
                }
            }

            return keys[^1];
        }

        private static float Speed(Fighter f, long t)
        {
            var a = StateAt(f, t - 30);
            var b = StateAt(f, t);
            return (b.X - a.X) / 0.03f;
        }

        private static void K(Fighter f, long at, string pose, float x, float lift = 0) => f.Keys.Add(new Key { At = at, Pose = pose, X = x, Lift = lift });

        private void Shake(long at, float power) => _shakes.Add((at, power));

        private void Sound(long at, string sound) => _sounds.Add((at, sound));

        private void Text(long at, Fighter over, string text, bool big = false, bool soft = false) =>
            _floaters.Add(new Floater { Text = text, X = over == _me ? -1 : 1, At = at, Big = big, Soft = soft });

        private Fx Effect(string name, long at, long dur, float x, float y, float size, float rot = 0, bool flip = false, float x1 = float.NaN, float y1 = float.NaN, float grow = 0.15f, float alpha = 0.9f, bool fadeIn = false, float spin = 0)
        {
            var bmp = Art("duel_" + name);
            if (bmp == null) return null;
            var e = new Fx { Bmp = bmp, At = at, Dur = dur, X0 = x, Y0 = y, X1 = float.IsNaN(x1) ? x : x1, Y1 = float.IsNaN(y1) ? y : y1, Size = size, Rot = rot, Flip = flip, Grow = grow, Alpha = alpha, FadeIn = fadeIn, Spin = spin };
            _fx.Add(e);
            return e;
        }

        /// <summary>Shows a new battle state; when the turn advanced, plays its events back as a choreographed exchange.</summary>
        public void Show(BattleDto battle)
        {
            var previous = Battle;
            Battle = battle;
            if (previous != null && (battle.Turn != previous.Turn || previous.IsActive && !battle.IsActive))
            {
                var t = BattleClock.Now + 60;
                Rest(_me, t);
                Rest(_foe, t);
                foreach (var e in battle.LastTurnEvents)
                {
                    t = Schedule(e, battle.IsMine(e), t);
                }

                if (!battle.IsActive) Finish(battle, t);
            }
            else if (previous == null && !battle.IsActive)
            {
                _endAt = 0;
                var now = BattleClock.Now;
                Finish(battle, now - 1);
            }

            PostInvalidateOnAnimation();
        }

        private void Finish(BattleDto battle, long t)
        {
            var heroLost = battle.Me.Hp <= 0 || battle.Result == "lost";
            var foeLost = battle.Opponent.Hp <= 0 || battle.Result == "won";
            if (foeLost) K(_foe, t, "fall", StateAt(_foe, t).X);
            if (heroLost) K(_me, t, "fall", StateAt(_me, t).X);
            if (battle.Result == "won")
            {
                K(_me, t + 500, "victory", _me.Home);
                Effect("ink_petals", t + 700, 2600, _me.Home, 1.4f, 1.3f, alpha: 0.7f, y1: 0.3f, grow: 0.1f);
            }
            else if (battle.Result == "lost")
            {
                K(_foe, t + 500, "victory", _foe.Home);
            }

            _endAt = t + 1100;
        }

        private static string Element(string tech, string set)
        {
            tech ??= string.Empty;
            if (tech.Contains("flame") || tech.Contains("fire") || tech.Contains("sun") || tech.Contains("phoenix") || tech.Contains("ember") || tech.Contains("inferno") || tech.Contains("lotus")) return "flame";
            if (tech.Contains("venom") || tech.Contains("poison") || tech.Contains("toxic") || tech.Contains("miasma") || tech.Contains("gu") || tech.Contains("needle") || tech.Contains("rot")) return "poison";
            if (tech.Contains("thunder") || tech.Contains("lightning") || tech.Contains("storm") || tech.Contains("star")) return "thunder";
            if (tech.Contains("fist") || tech.Contains("palm") || tech.Contains("mountain") || tech.Contains("titan") || tech.Contains("vajra") || tech.Contains("body")) return "body";
            if (tech.Contains("blood") || tech.Contains("demon")) return "blood";
            return set switch { "flame" => "flame", "poison" => "poison", "body" => "body", "qi" => "qi", _ => "blade" };
        }

        private static string Burst(string element) => element switch
        {
            "flame" => "glow_fire_burst",
            "poison" => "glow_poison_burst",
            "thunder" => "glow_lightning_burst",
            "body" => "glow_gold_burst",
            "blood" => "glow_blood_burst",
            "qi" => "glow_qi_burst",
            _ => null,
        };

        private static (string Shot, string Trail, float Size) Projectile(string element, bool special) => element switch
        {
            "flame" => (special ? "ink_phoenix" : "ink_fireball", "glow_fire_trail", special ? 1.6f : 0.9f),
            "poison" => (special ? "ink_insect_swarm" : "ink_needles", "glow_poison_trail", special ? 1.5f : 1.1f),
            "body" => (special ? "ink_tiger" : "ink_palm", "glow_gold_trail", special ? 1.5f : 0.75f),
            "thunder" => ("ink_lightning", "glow_qi_trail", 0.9f),
            _ => (special ? "ink_dragon" : "ink_flying_sword", "glow_qi_trail", special ? 1.6f : 0.95f),
        };

        private static string Aura(string set) => set switch { "body" => "glow_aura_gold", "flame" => "glow_aura_fire", "poison" => "glow_aura_poison", _ => "glow_aura_jade" };

        private string TechniqueName(TurnEventDto e, bool mine)
        {
            if (mine && e.TechniqueId != null)
            {
                var slot = Battle.MySlots.FirstOrDefault(s => s.TechniqueId == e.TechniqueId);
                if (slot?.NamePl != null) return slot.NamePl;
            }

            return mine ? "Technika" : "Technika wroga";
        }

        private static string Glyph(string element) => element switch { "flame" => "火", "poison" => "毒", "body" => "拳", "thunder" => "雷", "blood" => "血", _ => "劍" };

        /// <summary>Turns one event into keyframes and effects starting at <paramref name="t"/>; returns when the next may start.</summary>
        private long Schedule(TurnEventDto e, bool mine, long t)
        {
            var a = mine ? _me : _foe;
            var d = mine ? _foe : _me;
            var dir = mine ? 1f : -1f;
            var tech = e.TechniqueId ?? string.Empty;
            var element = Element(tech, a.Set);
            var near = d.Home - dir * 0.2f;
            var variant = _variant++;
            switch (e.Action)
            {
                case "attack":
                {
                    var ranged = tech.Contains("palm") || tech.Contains("qi") || tech.Contains("blast") || tech.Contains("needle") || tech.Contains("dagger")
                                 || tech.Contains("dart") || tech.Contains("lance") || tech.Contains("rain") || element is "flame" or "poison" && a.Set != "rival";
                    long impact;
                    if (ranged)
                    {
                        K(a, t, "windup", a.Home - dir * 0.02f);
                        K(a, t + 140, "strike", a.Home + dir * 0.01f);
                        var (shot, trail, size) = Projectile(element, false);
                        impact = t + 420;
                        Effect(shot, t + 160, impact - t - 160, a.Home + dir * 0.08f, 0.55f, size, flip: !mine, x1: d.Home - dir * 0.03f, y1: 0.55f, grow: 0);
                        Effect(trail, t + 160, impact - t - 160, a.Home + dir * 0.03f, 0.55f, size * 1.4f, flip: !mine, x1: d.Home - dir * 0.08f, y1: 0.55f, grow: 0, alpha: 0.7f);
                        K(a, t + 620, "stance", a.Home);
                    }
                    else
                    {
                        var aerial = variant % 4 == 3;
                        K(a, t, "windup", a.Home - dir * 0.02f);
                        if (aerial)
                        {
                            K(a, t + 120, "jump", a.Home + dir * 0.05f, 0.25f);
                            K(a, t + 330, "air_slash", near, 0.7f);
                            impact = t + 380;
                            K(a, impact, "air_slash", near, 0.15f);
                            K(a, impact + 140, "stance", near);
                        }
                        else
                        {
                            K(a, t + 120, "dash", a.Home + dir * 0.04f, 0.04f);
                            impact = t + 300;
                            K(a, impact - 40, variant % 4 == 2 ? "thrust" : "strike", near);
                            if (variant % 4 == 1)
                            {
                                K(a, impact + 90, "windup", near + dir * 0.01f);
                                K(a, impact + 150, "strike", near + dir * 0.02f);
                            }

                            K(a, impact + 260, "stance", near);
                        }

                        K(a, impact + 380, "dash", near - dir * 0.04f, 0.08f);
                        K(a, impact + 620, "stance", a.Home);
                    }

                    if (e.Hit == true)
                    {
                        var crit = e.Crit || e.Exploit || e.Momentum >= 3;
                        d.FlashUntil = impact + 110;
                        var knock = crit ? 0.07f : 0.04f;
                        K(d, impact, "hurt", d.Home + dir * knock * 0.5f);
                        K(d, impact + 120, "hurt", d.Home + dir * knock);
                        K(d, impact + 520, "stance", d.Home);
                        var slash = a.Set == "body" ? "ink_fist_impact" : (variant % 4) switch { 0 => "ink_slash_double", 1 => "ink_slash_triple", 2 => "ink_thrust", _ => "ink_slash_upper" };
                        if (ranged) slash = null;
                        if (slash != null) Effect(slash, impact, 260, d.Home - dir * 0.03f, 0.55f, 0.95f, flip: !mine);
                        var burst = Burst(ranged || a.Set != "qi" && a.Set != "rival" ? element : "x");
                        if (burst != null) Effect(burst, impact, 320, d.Home, 0.55f, crit ? 1.5f : 1.1f, alpha: 0.85f);
                        Effect("ink_drops", impact, 300, d.Home + dir * 0.03f, 0.6f, 0.7f, alpha: 0.75f);
                        if (crit)
                        {
                            Effect("ink_cross", impact, 380, d.Home, 0.55f, 0.85f);
                            Effect("ink_crack", impact, 1600, d.Home, 0.0f, 1.4f, grow: 0, alpha: 0.7f);
                            Text(impact + 40, d, "KRYTYK");
                        }

                        Text(impact, d, e.Damage.ToString("N0"), big: crit);
                        Shake(impact, crit ? 1.2f : 0.7f);
                        Sound(impact, mine ? Sfx.Hit : Sfx.Hurt);
                        var mech = e.Mechanic switch { "charge" => "Ładunek Qi", "ember_burst" => "Wybuch żaru", "blossom" => "Kwitnienie jadu", "fury" => "Furia", _ => null };
                        if (mech != null) Text(impact + 120, a, mech, soft: true);
                    }
                    else if (e.Hit == false)
                    {
                        // the defender slips away: a short hop back and an ink stroke through empty air
                        K(d, impact - 120, "jump", d.Home + dir * 0.05f, 0.35f);
                        K(d, impact + 80, "jump", d.Home + dir * 0.09f, 0.2f);
                        K(d, impact + 240, "stance", d.Home + dir * 0.06f);
                        K(d, impact + 560, "stance", d.Home);
                        if (!ranged) Effect("ink_slash_circle", impact, 260, d.Home - dir * 0.02f, 0.5f, 0.85f, flip: !mine, alpha: 0.7f);
                        Text(impact, d, "Unik", soft: true);
                    }

                    return t + StepMs;
                }
                case "defend":
                case "defense":
                case "counter_stance":
                    K(a, t, "block", a.Home);
                    K(a, t + 600, "stance", a.Home);
                    Effect("glow_shield_jade", t + 60, 600, a.Home + dir * 0.03f, 0.45f, 1.0f, alpha: 0.85f);
                    Text(t + 100, a, e.Action == "counter_stance" ? "Kontra" : "Garda", soft: true);
                    return t + 700;
                case "rest":
                    K(a, t, "cast", a.Home);
                    K(a, t + 700, "stance", a.Home);
                    Effect(Aura(a.Set), t, 700, a.Home, 0.6f, 0.72f, grow: 0.05f, alpha: 0.6f, fadeIn: true);
                    Effect("ink_spiral", t + 100, 600, a.Home, 0.5f, 0.8f, alpha: 0.5f, spin: 200);
                    Text(t + 200, a, "+Qi", soft: true);
                    return t + 760;
                case "special":
                {
                    var name = TechniqueName(e, mine);
                    _banners.Add((t, name, Glyph(element)));
                    var cut = mine && _cutIn != null;
                    if (cut) _cutIns.Add(t + 650);
                    var start = t + (cut ? 1650 : 700);
                    K(a, t, "cast", a.Home);
                    Effect(Aura(a.Set), t, start - t + 300, a.Home, 0.6f, 0.72f, grow: 0.05f, alpha: 0.65f, fadeIn: true);
                    if (e.Hit != null)
                    {
                        var impact = start + 420;
                        if (variant % 2 == 0 || element == "thunder")
                        {
                            // from the sky: a seal under the foe, then a pillar / rain of swords
                            K(a, start, "cast", a.Home);
                            Effect("glow_seal_gold", start - 200, 700, d.Home, 0.05f, 1.2f, grow: 0, alpha: 0.7f, fadeIn: true);
                            if (element is "thunder" or "body") Effect("glow_pillar_violet", impact - 120, 360, d.Home, 1.6f, 0.9f, grow: 0);
                            else Effect("ink_sword_rain", start, impact - start + 80, d.Home + dir * 0.06f, 2.2f, 1.2f, x1: d.Home, y1: 0.7f, grow: 0);
                        }
                        else
                        {
                            K(a, start, "strike", a.Home + dir * 0.02f);
                            var (shot, trail, size) = Projectile(element, true);
                            Effect(shot, start + 40, impact - start - 40, a.Home + dir * 0.08f, 0.6f, size, flip: !mine, x1: d.Home - dir * 0.05f, y1: 0.6f, grow: 0);
                            Effect(trail, start + 40, impact - start - 40, a.Home, 0.6f, size * 1.3f, flip: !mine, x1: d.Home - dir * 0.12f, y1: 0.6f, grow: 0, alpha: 0.65f);
                        }

                        K(a, impact + 500, "stance", a.Home);
                        if (e.Hit == true)
                        {
                            d.FlashUntil = impact + 150;
                            K(d, impact, "hurt", d.Home + dir * 0.03f);
                            K(d, impact + 160, "hurt", d.Home + dir * 0.08f, 0.12f);
                            K(d, impact + 340, "hurt", d.Home + dir * 0.08f);
                            K(d, impact + 700, "stance", d.Home);
                            var burst = Burst(element) ?? "glow_qi_burst";
                            Effect(burst, impact, 420, d.Home, 0.55f, 1.6f, alpha: 0.9f);
                            Effect("ink_cross", impact, 400, d.Home, 0.55f, 0.85f);
                            Effect("ink_crack", impact, 1800, d.Home, 0.0f, 1.4f, grow: 0, alpha: 0.7f);
                            Text(impact, d, e.Damage.ToString("N0"), big: true);
                            Shake(impact, 1.4f);
                            Sound(impact, mine ? Sfx.Hit : Sfx.Hurt);
                        }
                        else
                        {
                            K(d, impact - 100, "jump", d.Home + dir * 0.07f, 0.4f);
                            K(d, impact + 200, "stance", d.Home + dir * 0.06f);
                            K(d, impact + 600, "stance", d.Home);
                            Text(impact, d, "Unik", soft: true);
                        }

                        t = impact + 600;
                    }
                    else
                    {
                        K(a, start + 300, "stance", a.Home);
                        t = start + 300;
                    }

                    if (e.Healed > 0)
                    {
                        Effect("glow_heal_rise", t - 500, 900, a.Home, 0.8f, 0.7f, y1: 1.2f, grow: 0, alpha: 0.85f, fadeIn: true);
                        Text(t - 300, a, "+" + e.Healed.ToString("N0"), soft: true);
                    }

                    return t + 150;
                }
                case "stunned":
                    Text(t, a, "Ogłuszenie", soft: true);
                    Effect("st_stun", t, 700, a.Home, 1.25f, 0.35f, grow: 0, alpha: 0.85f);
                    return t + 500;
                case "bleed":
                    Text(t, a, "-" + e.Damage, soft: true);
                    Effect("st_bleed", t, 600, a.Home, 1.25f, 0.3f, grow: 0);
                    return t + 450;
                case "burn":
                    Text(t, a, "-" + e.Damage, soft: true);
                    Effect("st_burn", t, 600, a.Home, 1.25f, 0.3f, grow: 0);
                    Effect("glow_fire_burst", t, 400, a.Home, 0.5f, 0.6f, alpha: 0.6f);
                    return t + 450;
                case "poison":
                    Text(t, a, "-" + e.Damage, soft: true);
                    Effect("st_poison", t, 600, a.Home, 1.25f, 0.3f, grow: 0);
                    Effect("ink_ground_cloud", t, 900, a.Home, 0.1f, 1.2f, grow: 0.05f, alpha: 0.55f);
                    return t + 450;
                default:
                    return t + 300;
            }
        }

        // ---- drawing -----------------------------------------------------------------------------------------------

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Battle == null || Width == 0) return;
            var now = BattleClock.Now;
            float w = Width, h = Height;
            foreach (var s in _sounds.Where(s => s.At <= now).ToList())
            {
                Sfx.Play(s.Sound);
                _sounds.Remove(s);
            }

            // a frozen moment while a cut-in plays
            var cutAt = _cutIns.LastOrDefault(c => now >= c && now < c + 1000);
            float sx = 0, sy = 0;
            foreach (var (at, power) in _shakes)
            {
                var k = (now - at) / 280f;
                if (k < 0 || k > 1) continue;
                var amp = power * 6 * _d * (1 - k);
                sx += amp * (float)Math.Sin(now * 0.11);
                sy += amp * 0.5f * (float)Math.Cos(now * 0.15);
            }

            canvas.Save();
            canvas.Translate(sx, sy);
            // landscape: cover the view; the painted ground (≈ 79 % of the picture) sits at 80 % of the view
            var ground = h * 0.8f;
            if (_bg != null)
            {
                var scale = Math.Max(w / _bg.Width, h / (_bg.Height * 0.79f));
                var bw = _bg.Width * scale;
                var bh = _bg.Height * scale;
                var top = ground - bh * 0.79f;
                canvas.DrawBitmap(_bg, null, new RectF((w - bw) / 2 - 10 * _d, top, (w + bw) / 2 + 10 * _d, top + bh), _paint);
            }
            else
            {
                canvas.DrawColor(Color.Rgb(236, 232, 224));
            }

            var ch = Math.Min(h * 0.24f, w * 0.34f); // a fighter's stance height
            var banner = _banners.LastOrDefault(b => now >= b.At && now < b.At + 900);
            if (banner.Title != null)
            {
                var k = (now - banner.At) / 900f;
                _paint.Color = Color.Argb((int)(70 * Math.Sin(k * Math.PI)), 60, 60, 64);
                canvas.DrawRect(0, 0, w, h, _paint);
                _paint.Color = Color.White;
            }

            // shadows, effects under the fighters (auras, seals), fighters, effects over them
            DrawShadow(canvas, _me, now, w, ground, ch);
            DrawShadow(canvas, _foe, now, w, ground, ch);
            DrawFx(canvas, now, w, ground, ch, under: true);
            DrawFighter(canvas, _foe, now, w, ground, ch);
            DrawFighter(canvas, _me, now, w, ground, ch);
            DrawFx(canvas, now, w, ground, ch, under: false);
            DrawFloaters(canvas, now, w, ground, ch);
            if (banner.Title != null) DrawBanner(canvas, banner, now, w, h);
            canvas.Restore();

            if (cutAt > 0) DrawCutIn(canvas, cutAt, now, w, h);
            if (Battle.IsActive && Battle.Me.MaxHp > 0 && Battle.Me.Hp * 10 < Battle.Me.MaxHp * 3 && Art("duel_screen_red_vignette") is { } vig)
            {
                _paint.Alpha = (int)(70 + 30 * Math.Sin(now / 300.0));
                canvas.DrawBitmap(vig, null, new RectF(0, 0, w, h), _paint);
                _paint.Alpha = 255;
            }

            DrawHud(canvas, w, now);
            if (!Battle.IsActive && _endAt >= 0 && now >= _endAt) DrawResult(canvas, w, h);

            _fx.RemoveAll(f => now > f.At + f.Dur + 200);
            _floaters.RemoveAll(f => now - f.At > 1400);
            _shakes.RemoveAll(s => now - s.At > 300);
            _banners.RemoveAll(b => now - b.At > 1000);
            _cutIns.RemoveAll(c => now - c > 1100);
            PostInvalidateOnAnimation();
        }

        private void DrawShadow(Canvas canvas, Fighter f, long now, float w, float ground, float ch)
        {
            var s = StateAt(f, now);
            var r = Math.Max(ch * 0.08f, ch * 0.24f - s.Lift * ch * 0.1f);
            _paint.Color = Color.Argb(55, 0, 0, 0);
            canvas.DrawOval(new RectF(s.X * w - r, ground - ch * 0.03f, s.X * w + r, ground + ch * 0.03f), _paint);
            _paint.Color = Color.White;
        }

        private void DrawFighter(Canvas canvas, Fighter f, long now, float w, float ground, float ch)
        {
            var s = StateAt(f, now);
            var v = Speed(f, now);
            // motion ghosts on fast moves
            if (Math.Abs(v) > 0.9f)
            {
                for (var j = 1; j <= 3; j++)
                {
                    var back = StateAt(f, now - j * 35);
                    DrawPose(canvas, f, back.Pose, back.X * w, ground - back.Lift * ch, ch, (int)(70 / j), false, now);
                }
            }

            var breathe = s.Pose is "stance" or "cast" ? 1 + 0.012f * (float)Math.Sin(now / 420.0 + (f.Hero ? 0 : 1.7)) : 1f;
            DrawPose(canvas, f, s.Pose, s.X * w, ground - s.Lift * ch, ch * breathe, 255, now < f.FlashUntil, now);
            DrawStatus(canvas, f, s.X * w, ground - s.Lift * ch - ch * 1.12f, ch);
        }

        private void DrawPose(Canvas canvas, Fighter f, string pose, float x, float foot, float ch, int alpha, bool flash, long now)
        {
            var bmp = PoseBitmap(f, pose);
            if (bmp == null) return;
            var table = Poses[f.Set];
            var info = table.TryGetValue(pose, out var p) ? p : table["stance"];
            var ph = ch * info.Height;
            var pw = ph * bmp.Width / bmp.Height;
            var left = x - info.Anchor * pw;
            var paint = flash ? _flash : _paint;
            paint.Alpha = alpha;
            canvas.DrawBitmap(bmp, null, new RectF(left, foot - ph, left + pw, foot), paint);
            paint.Alpha = 255;
        }

        private void DrawStatus(Canvas canvas, Fighter f, float x, float y, float ch)
        {
            var fighter = f.Hero ? Battle.Me : Battle.Opponent;
            if (fighter?.Effects == null || !Battle.IsActive) return;
            var icons = new List<Bitmap>();
            void One(string effect, string art)
            {
                if (fighter.Has(effect) && Art("duel_" + art) is { } b) icons.Add(b);
            }

            One("stun", "st_stun");
            One("bleed", "st_bleed");
            One("burn", "st_burn");
            One("poison", "st_poison");
            One("weaken", "st_weaken");
            One("shield", "st_shield");
            One("regen", "st_regen");
            var size = ch * 0.22f;
            var start = x - icons.Count * size / 2;
            for (var i = 0; i < icons.Count; i++)
            {
                var b = icons[i];
                var bh = size * b.Height / Math.Max(b.Width, b.Height);
                var bw = size * b.Width / Math.Max(b.Width, b.Height);
                canvas.DrawBitmap(b, null, new RectF(start + i * size + (size - bw) / 2, y - bh, start + i * size + (size + bw) / 2, y), _paint);
            }
        }

        private void DrawFx(Canvas canvas, long now, float w, float ground, float ch, bool under)
        {
            foreach (var e in _fx)
            {
                if (now < e.At || now > e.At + e.Dur) continue;
                var isUnder = e.Bmp == Art("duel_glow_seal_gold") || e.Bmp == Art("duel_ink_crack") || (e.FadeIn && e.Bmp.Height > e.Bmp.Width * 1.3f);
                if (isUnder != under) continue;
                var k = (now - e.At) / (float)Math.Max(1, e.Dur);
                var x = (e.X0 + (e.X1 - e.X0) * k) * w;
                var y = ground - (e.Y0 + (e.Y1 - e.Y0) * k) * ch;
                var size = e.Size * ch * (1 + e.Grow * k);
                var bw = size;
                var bh = size * e.Bmp.Height / e.Bmp.Width;
                if (e.Bmp == Art("duel_ink_crack")) bh *= 0.35f;
                var a = e.FadeIn ? Math.Min(1, k * 5) * Math.Min(1, (1 - k) * 4) : Math.Min(1, (1 - k) * 2.2f);
                _paint.Alpha = (int)(255 * e.Alpha * Math.Clamp(a, 0, 1));
                canvas.Save();
                canvas.Translate(x, y);
                if (e.Rot != 0 || e.Spin != 0) canvas.Rotate(e.Rot + e.Spin * k);
                if (e.Flip) canvas.Scale(-1, 1);
                canvas.DrawBitmap(e.Bmp, null, new RectF(-bw / 2, -bh / 2, bw / 2, bh / 2), _paint);
                canvas.Restore();
            }

            _paint.Alpha = 255;
        }

        private void DrawFloaters(Canvas canvas, long now, float w, float ground, float ch)
        {
            var live = _floaters.Where(f => now >= f.At && now - f.At < 1300).OrderBy(f => f.At).ToList();
            var stack = new Dictionary<float, int>();
            foreach (var f in live)
            {
                var k = (now - f.At) / 1300f;
                var fighter = f.X < 0 ? _me : _foe;
                var s = StateAt(fighter, now);
                stack.TryGetValue(f.X, out var n);
                stack[f.X] = n + 1;
                var size = (f.Big ? 30 : f.Soft ? 16 : 22) * _d * (k < 0.06f ? 1.3f : 1f);
                _text.TextSize = size;
                _text.Color = f.Soft ? Color.Rgb(70, 70, 72) : Color.Rgb(22, 22, 24);
                _text.Alpha = (int)(255 * Math.Clamp((1 - k) * 2.5f, 0, 1));
                var y = ground - s.Lift * ch - ch * 1.25f - k * 30 * _d - (live.Count(o => o.X == f.X && o.At > f.At)) * 24 * _d;
                canvas.DrawText(f.Text, s.X * w, y, _text);
            }

            _text.Alpha = 255;
        }

        private void DrawStroke(Canvas canvas, float cx, float cy, float width, int alpha, bool flip)
        {
            var b = _stroke;
            if (b == null) return;
            var bh = width * b.Height / b.Width;
            _paint.Alpha = alpha;
            canvas.Save();
            canvas.Translate(cx, cy);
            if (flip) canvas.Scale(-1, 1);
            canvas.DrawBitmap(b, null, new RectF(-width / 2, -bh / 2, width / 2, bh / 2), _paint);
            canvas.Restore();
            _paint.Alpha = 255;
        }

        private void DrawBanner(Canvas canvas, (long At, string Title, string Glyph) banner, long now, float w, float h)
        {
            var k = (now - banner.At) / 900f;
            var grow = Math.Min(1, k * 4);
            var a = k < 0.8f ? 1 : (1 - k) / 0.2f;
            var cy = h * 0.42f;
            DrawStroke(canvas, w / 2, cy, w * 0.95f * Math.Max(0.05f, grow), (int)(240 * a), false);
            if (k < 0.15f) return;
            _text.Color = Color.Rgb(248, 248, 248);
            _text.Alpha = (int)(255 * a);
            _text.TextSize = 30 * _d;
            canvas.DrawText(banner.Glyph + "  " + banner.Title, w / 2, cy + 10 * _d, _text);
            _text.Alpha = 255;
        }

        private void DrawCutIn(Canvas canvas, long at, long now, float w, float h)
        {
            var k = (now - at) / 1000f;
            var dim = k < 0.85f ? Math.Min(1, k * 5) : (1 - k) / 0.15f;
            _paint.Color = Color.Argb((int)(140 * dim), 20, 20, 24);
            canvas.DrawRect(0, 0, w, h, _paint);
            _paint.Color = Color.White;
            var slide = (1 - (1 - Math.Pow(1 - Math.Min(1, k * 3.5), 3))) * w * 0.8 - w * 1.4 * Math.Pow(Math.Max(0, (k - 0.82) / 0.18), 2);
            var path = new Path();
            path.MoveTo(0, h * 0.34f);
            path.LineTo(w, h * 0.24f);
            path.LineTo(w, h * 0.66f);
            path.LineTo(0, h * 0.76f);
            path.Close();
            canvas.Save();
            canvas.ClipPath(path);
            canvas.DrawColor(Color.Rgb(240, 238, 232));
            var ph = h * 0.5f;
            var pw = ph * _cutIn.Width / _cutIn.Height;
            canvas.DrawBitmap(_cutIn, null, new RectF((float)(w - pw + slide), h * 0.24f, (float)(w + slide), h * 0.24f + ph), _paint);
            canvas.Restore();
            _text.TextAlign = Paint.Align.Left;
            _text.Color = Color.Rgb(20, 20, 22);
            _text.TextSize = 30 * _d;
            var tx = (float)(16 * _d - (1 - (1 - Math.Pow(1 - Math.Min(1, Math.Max(0, k * 3.5 - 0.3)), 3))) * w * 0.8 - w * 1.4 * Math.Pow(Math.Max(0, (k - 0.82) / 0.18), 2));
            var name = _banners.LastOrDefault().Title ?? string.Empty;
            canvas.DrawText(name, tx, h * 0.62f, _text);
            _text.TextAlign = Paint.Align.Center;
        }

        private void DrawHud(Canvas canvas, float w, long now)
        {
            var top = 8 * _d;
            var av = 40 * _d;
            DrawPlate(canvas, Battle.Me, _avatarMe, 8 * _d, top, av, w, left: true);
            DrawPlate(canvas, Battle.Opponent, _avatarFoe, w - 8 * _d, top, av, w, left: false);
            // the round, in a brush circle
            _line.Color = Color.Rgb(28, 28, 30);
            _line.StrokeWidth = 2.5f * _d;
            canvas.DrawCircle(w / 2, top + av / 2, 15 * _d, _line);
            _text.Color = Color.Rgb(28, 28, 30);
            _text.TextSize = 17 * _d;
            canvas.DrawText(Math.Max(1, Battle.Turn).ToString(), w / 2, top + av / 2 + 6 * _d, _text);
            if (Battle.TowerFloor != null || Battle.Kind == "boss")
            {
                _text.TextSize = 11 * _d;
                canvas.DrawText(Battle.Kind == "boss" ? "BOSS ŚWIATA · ZADANE " + Battle.DamageDealt : "WIEŻA PRÓB · PIĘTRO " + Battle.TowerFloor, w / 2, top + av + 18 * _d, _text);
            }

            if (Battle.IsActive && Battle.OpponentIntent != null && !_fx.Any(f => now < f.At + f.Dur))
            {
                var s = StateAt(_foe, now);
                _text.TextSize = 12 * _d;
                _text.Color = Color.Rgb(60, 60, 62);
                canvas.DrawText("» " + (Battle.OpponentIntent.NamePl ?? Battle.OpponentIntent.Action), s.X * w, Height * 0.8f - Math.Min(Height * 0.24f, w * 0.34f) * 1.12f, _text);
            }
        }

        private void DrawPlate(Canvas canvas, FighterDto f, Bitmap avatar, float x, float top, float av, float w, bool left)
        {
            if (f == null) return;
            var plateW = w * 0.42f;
            var cx = left ? x + plateW / 2 : x - plateW / 2;
            // a soft paper band behind the plate keeps the ink text readable on any landscape
            _paint.Color = Color.Argb(150, 245, 242, 236);
            canvas.DrawRoundRect(new RectF(left ? x : x - plateW, top - 2 * _d, left ? x + plateW : x, top + av + 10 * _d), av / 2, av / 2, _paint);
            _paint.Color = Color.White;
            var ax = left ? x : x - av;
            if (avatar != null) canvas.DrawBitmap(avatar, null, new RectF(ax, top, ax + av, top + av), _paint);
            _line.Color = Color.Rgb(20, 20, 22);
            _line.StrokeWidth = 2 * _d;
            canvas.DrawCircle(ax + av / 2, top + av / 2, av / 2, _line);
            _text.TextAlign = left ? Paint.Align.Left : Paint.Align.Right;
            var tx = left ? ax + av + 6 * _d : ax - 6 * _d;
            _text.Color = Color.Rgb(24, 24, 26);
            _text.TextSize = 14 * _d;
            var name = f.Name ?? string.Empty;
            if (name.Length > 14) name = name[..13] + "…";
            canvas.DrawText(name, tx, top + 15 * _d, _text);
            _text.TextSize = 10 * _d;
            canvas.DrawText(Math.Max(0, f.Hp).ToString("N0") + " / " + f.MaxHp.ToString("N0"), tx, top + 28 * _d, _text);
            // thin ink health line under the stroke, Qi as a fainter line
            var lw = plateW - av - 14 * _d;
            var lx = left ? tx : tx - lw;
            var ly = top + av + 4 * _d;
            var hp = f.MaxHp > 0 ? Math.Clamp(f.Hp / (float)f.MaxHp, 0, 1) : 0;
            // 2.2: HP is a brush stroke — a pale full stroke, inked as far as the life left (red when it runs low)
            if (Art("ink_ui_hp_bar") is { } brush)
            {
                var bar = new RectF(lx, ly - 3 * _d, lx + lw, ly + 6 * _d);
                var save = canvas.Save();
                if (!left) canvas.Scale(-1, 1, bar.CenterX(), bar.CenterY()); // the foe's stroke drains towards the centre too
                _paint.Alpha = 45;
                _paint.SetColorFilter(null);
                canvas.DrawBitmap(brush, null, bar, _paint);
                canvas.ClipRect(bar.Left, bar.Top, bar.Left + bar.Width() * hp, bar.Bottom);
                _paint.Alpha = 255;
                if (hp < 0.3f) _paint.SetColorFilter(new PorterDuffColorFilter(Ui.Cinnabar, PorterDuff.Mode.SrcIn));
                canvas.DrawBitmap(brush, null, bar, _paint);
                _paint.SetColorFilter(null);
                canvas.RestoreToCount(save);
            }
            else
            {
                _paint.Color = Color.Argb(70, 0, 0, 0);
                canvas.DrawRect(lx, ly, lx + lw, ly + 3 * _d, _paint);
                _paint.Color = Color.Rgb(25, 25, 27);
                canvas.DrawRect(left ? lx : lx + lw * (1 - hp), ly, left ? lx + lw * hp : lx + lw, ly + 3 * _d, _paint);
            }
            if (f.MaxQi > 0)
            {
                _paint.Color = Color.Argb(120, 90, 90, 95);
                var qi = Math.Clamp(f.Qi / (float)f.MaxQi, 0, 1);
                canvas.DrawRect(left ? lx : lx + lw * (1 - qi), ly + 8 * _d, left ? lx + lw * qi : lx + lw, ly + 10 * _d, _paint);
            }

            _paint.Color = Color.White;
            _text.TextAlign = Paint.Align.Center;
        }

        private void DrawResult(Canvas canvas, float w, float h)
        {
            var won = Battle.Result == "won";
            var draw = Battle.Result == "draw";
            DrawStroke(canvas, w / 2, h * 0.42f, w * 0.95f, 235, false);
            _text.Color = Color.Rgb(250, 250, 250);
            _text.TextSize = 30 * _d;
            canvas.DrawText(won ? "ZWYCIĘSTWO" : draw ? "REMIS" : "PORAŻKA", w / 2, h * 0.42f + 11 * _d, _text);
            if (Battle.RewardQi > 0)
            {
                _text.Color = Color.Rgb(30, 30, 32);
                _text.TextSize = 15 * _d;
                canvas.DrawText("+" + Battle.RewardQi + " Qi", w / 2, h * 0.42f + 50 * _d, _text);
            }

            if (!_endSound)
            {
                _endSound = true;
                Sfx.Play(won ? Sfx.Victory : Sfx.Defeat);
            }
        }
    }
}
