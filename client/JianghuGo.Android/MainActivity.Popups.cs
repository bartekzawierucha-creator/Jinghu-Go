using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>Result popups (level-up, loot, banners) and the mentor's coach-mark tutorial.</summary>
    public sealed partial class MainActivity
    {
        private const string TutorialKey = "jianghu.tutorial.v2";
        private static readonly string[] Stats = { "strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate" };

        // ---- popups ---------------------------------------------------------------------------------------------

        /// <summary>A centred lacquer card over the screen; tap anywhere (or the button) to close.</summary>
        private LinearLayout Popup(string title, int accentArt, out System.Action close, string button = "Dalej", System.Action onClose = null)
        {
            var overlay = new FrameLayout(this) { Clickable = true };
            overlay.SetBackgroundColor(Color.Argb(130, 30, 28, 26));
            var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
            panel.SetGravity(GravityFlags.CenterHorizontal);
            panel.Background = new FrameDrawable(Resources.DisplayMetrics.Density, Color.Argb(250, 244, 240, 231), Ui.Gold, Ui.Gold);
            panel.SetPadding(_ui.Dp(20), _ui.Dp(16), _ui.Dp(20), _ui.Dp(16));
            var lp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center)
            {
                LeftMargin = _ui.Dp(28),
                RightMargin = _ui.Dp(28),
            };
            overlay.AddView(panel, lp);
            if (accentArt != 0)
            {
                var img = new ImageView(this);
                var bmp = Ui.Art(this, accentArt);
                img.SetImageBitmap(bmp);
                img.SetScaleType(ImageView.ScaleType.FitCenter);
                // the tall ink illustrations (chest, scroll, breakthrough) get a real stage, icons stay small
                var tall = bmp != null && bmp.Height > bmp.Width * 1.3f;
                panel.AddView(img, new LinearLayout.LayoutParams(tall ? ViewGroup.LayoutParams.MatchParent : _ui.Dp(84), _ui.Dp(tall ? 170 : 84)));
            }

            var t = _ui.Label(title, 24, Ui.Gold, bold: true, parent: panel);
            t.Gravity = GravityFlags.Center;
            t.LetterSpacing = 0.08f;
            var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
            panel.AddView(body, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            var closed = false;
            System.Action doClose = () =>
            {
                if (closed) return;
                closed = true;
                PopOverlay(overlay);
                overlay.Animate().Alpha(0f).SetDuration(160).WithEndAction(new Java.Lang.Runnable(() => _frame.RemoveView(overlay))).Start();
                onClose?.Invoke();
            };
            close = doClose;
            _ui.Action(button, doClose, parent: panel);
            overlay.Click += (_, __) => doClose();
            _frame.AddView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            PushOverlay(overlay, doClose);
            overlay.Alpha = 0f;
            panel.ScaleX = panel.ScaleY = 0.85f;
            overlay.Animate().Alpha(1f).SetDuration(180).Start();
            panel.Animate().ScaleX(1f).ScaleY(1f).SetDuration(260).SetInterpolator(new global::Android.Views.Animations.OvershootInterpolator(1.6f)).Start();
            return body;
        }

        /// <summary>Level-up / result banner: big title, a line, a smaller line, an emblem.</summary>
        private void ShowBanner(string title, string line, string sub, string artName)
        {
            var body = Popup(title, _ui.ArtId(artName), out _);
            var a = _ui.Label(line, 17, Ui.Ink, bold: true, parent: body);
            a.Gravity = GravityFlags.Center;
            if (!string.IsNullOrEmpty(sub))
            {
                var b = _ui.Label(sub, 13, Ui.Muted, parent: body);
                b.Gravity = GravityFlags.Center;
            }
        }

        /// <summary>Loot popup: Qi, items (names resolved from the bag) and a tamed spirit beast, if any.</summary>
        private void ShowLoot(string title, long qi, List<(string ItemId, long Quantity)> items, string tamedNpcId, (int Turns, int Dealt, int Taken)? fight = null)
        {
            var body = Popup(title, Resource.Drawable.ink_popup_chest, out _, "Zabierz");
            if (fight is { } f)
            {
                // 2.8: the genre's result card — a star rating for speed and the three numbers that tell the fight
                var stars = f.Turns <= 4 ? 3 : f.Turns <= 8 ? 2 : 1;
                var st = _ui.Label(new string('★', stars) + new string('☆', 3 - stars), 26, Ui.Cinnabar, bold: true, parent: body);
                st.Gravity = GravityFlags.Center;
                st.ScaleX = st.ScaleY = 0.3f;
                st.Animate().ScaleX(1f).ScaleY(1f).SetStartDelay(200).SetDuration(360).SetInterpolator(new global::Android.Views.Animations.OvershootInterpolator(2.2f)).Start();
                var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                body.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = _ui.Dp(4), BottomMargin = _ui.Dp(6) });
                foreach (var (value, label, color) in new[] { (f.Dealt.ToString("N0"), "zadane", Ui.Cinnabar), (f.Taken.ToString("N0"), "otrzymane", Ui.Ink), (f.Turns.ToString(), "tury", Ui.Ink) })
                {
                    var cell = new LinearLayout(this) { Orientation = Orientation.Vertical };
                    cell.SetGravity(GravityFlags.CenterHorizontal);
                    row.AddView(cell, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
                    var v = _ui.Label(value, 22, color, bold: true, parent: cell);
                    v.Gravity = GravityFlags.Center;
                    var l = _ui.Label(label, 11, Ui.Muted, parent: cell);
                    l.Gravity = GravityFlags.Center;
                }
            }

            if (qi > 0) _ui.WithIcon(_ui.Label("+" + qi + " Qi", 18, Ui.QiBlue, bold: true, parent: body), "menu_meditate", 28);
            var rows = new List<(TextView Label, string ItemId, long Quantity)>();
            foreach (var (id, qty) in items)
            {
                var art = _ui.ArtId("item_" + id) != 0 ? "item_" + id : "item_slot_empty";
                rows.Add((_ui.WithIcon(_ui.Label(Pretty(id) + " ×" + qty, 15, Ui.Ink, parent: body), art, 28), id, qty));
            }

            if (tamedNpcId != null)
            {
                var pet = _ui.Label("🐾 Oswoiłeś bestię! Nowy duchowy towarzysz walczy u twego boku.", 15, Ui.Jade, bold: true, parent: body);
                pet.Gravity = GravityFlags.Center;
                Sfx.Play(Sfx.Chime);
            }

            if (qi <= 0 && items.Count == 0 && tamedNpcId == null) _ui.Label("Tym razem bez łupu.", 14, Ui.Muted, parent: body);
            if (rows.Count == 0 || CurrentCharacterId == null) return;
            var gen = _screen;
            Run(async () =>
            {
                var inv = await _api.GetInventoryAsync(CurrentCharacterId.Value, Ct);
                var names = inv.Stacks.GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.First().NamePl);
                foreach (var it in inv.Items) names[it.ItemId] = it.NamePl;
                if (gen != _screen) return;
                foreach (var r in rows)
                    if (names.TryGetValue(r.ItemId, out var name)) r.Label.Text = name + " ×" + r.Quantity;
            });
        }

        /// <summary>
        /// 2.8: a breakthrough as a scene, not a popup — the sky darkens, three brush strokes strike with the screen
        /// shaking, then the realm's name is written large. <paramref name="newRealm"/> plays the full sequence; a stage
        /// within a realm gets one stroke. The power gained floats in once it is known. Tap to close (after the strokes).
        /// </summary>
        private void ShowBreakthroughScene(string hanzi, string name, string line, bool newRealm, long powerBefore)
        {
            var d = Resources.DisplayMetrics.Density;
            var scene = new FrameLayout(this) { Clickable = true };
            scene.SetBackgroundColor(Color.Argb(232, 20, 18, 16));
            var closable = false;
            System.Action close = null;
            close = () =>
            {
                if (!closable) return;
                closable = false;
                PopOverlay(scene);
                scene.Animate().Alpha(0f).SetDuration(220).WithEndAction(new Java.Lang.Runnable(() => _frame.RemoveView(scene))).Start();
            };
            scene.Click += (_, __) => close();

            var strokes = newRealm ? 3 : 1;
            var angles = new[] { -11f, 7f, -3f };
            var tops = new[] { 0.26f, 0.40f, 0.33f };
            var h = Resources.DisplayMetrics.HeightPixels;
            for (var n = 0; n < strokes; n++)
            {
                var i = n; // each stroke's callback keeps its own index
                var img = new ImageView(this);
                if (InkArt.Stroke != null) img.SetImageBitmap(InkArt.Stroke);
                img.SetScaleType(ImageView.ScaleType.FitXy);
                img.SetColorFilter(i == strokes - 1 ? Ui.Cinnabar : Color.Rgb(236, 231, 220), PorterDuff.Mode.SrcIn);
                img.Rotation = angles[i];
                img.PivotX = 0;
                img.ScaleX = 0f;
                img.Alpha = 0.92f;
                scene.AddView(img, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(110 * d)) { TopMargin = (int)(h * tops[i]) });
                var delay = 250 + (i * 380);
                img.Animate().ScaleX(1f).SetStartDelay(delay).SetDuration(170).Start();
                _frame.PostDelayed(() =>
                {
                    Sfx.Play(i == strokes - 1 ? Sfx.Breakthrough : Sfx.Hit, 0.9f);
                    scene.PerformHapticFeedback(FeedbackConstants.LongPress);
                    _frame.Animate().TranslationX(7 * d).SetDuration(40).WithEndAction(new Java.Lang.Runnable(() =>
                        _frame.Animate().TranslationX(-5 * d).SetDuration(50).WithEndAction(new Java.Lang.Runnable(() =>
                            _frame.Animate().TranslationX(0).SetDuration(60).Start())).Start())).Start();
                }, delay + 120);
            }

            var col = new LinearLayout(this) { Orientation = Orientation.Vertical };
            col.SetGravity(GravityFlags.CenterHorizontal);
            scene.AddView(col, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center) { TopMargin = (int)(70 * d) });
            TextView Line(string text, float sp, Color color, bool display)
            {
                var t = new TextView(this) { Text = text, Gravity = GravityFlags.Center };
                t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
                t.SetTextColor(color);
                t.SetTypeface(display ? Ui.Display : Ui.Body, display ? TypefaceStyle.Bold : TypefaceStyle.Normal);
                t.SetShadowLayer(10, 0, 2, Color.Argb(200, 0, 0, 0));
                t.Alpha = 0f;
                col.AddView(t, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = (int)(20 * d), RightMargin = (int)(20 * d) });
                return t;
            }

            var reveal = 250 + (strokes * 380) + 150;
            var big = Line(string.IsNullOrEmpty(hanzi) ? "破" : hanzi, newRealm ? 56 : 44, Ui.Cinnabar, true);
            var title = Line(name, newRealm ? 30 : 24, Color.Rgb(244, 240, 231), true);
            var sub = Line(line, 15, Color.Rgb(200, 194, 184), false);
            var power = Line(string.Empty, 20, Color.Rgb(120, 200, 150), true);
            var hint = Line("dotknij, by kontynuować", 12, Color.Rgb(150, 144, 136), false);
            big.ScaleX = big.ScaleY = 1.6f;
            big.Animate().Alpha(1f).ScaleX(1f).ScaleY(1f).SetStartDelay(reveal).SetDuration(380).Start();
            title.Animate().Alpha(1f).SetStartDelay(reveal + 220).SetDuration(380).Start();
            sub.Animate().Alpha(1f).SetStartDelay(reveal + 420).SetDuration(380).Start();
            hint.Animate().Alpha(1f).SetStartDelay(reveal + 1100).SetDuration(400).Start();
            _frame.PostDelayed(() => closable = true, reveal + 600);

            _frame.AddView(scene, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            PushOverlay(scene, () =>
            {
                closable = true;
                close();
            });
            scene.Alpha = 0f;
            scene.Animate().Alpha(1f).SetDuration(260).Start();

            if (_me == null) return;
            var id = _me.Id;
            Run(async () =>
            {
                var after = (await _api.GetCombatScoutAsync(id, Ct)).MyPower;
                if (powerBefore > 0 && after > powerBefore)
                {
                    power.Text = "⚔ Moc +" + (after - powerBefore).ToString("N0");
                    power.Animate().Alpha(1f).SetStartDelay(Math.Max(0, reveal + 600 - 300)).SetDuration(380).Start();
                }

                SetPower(after);
            });
        }

        private static string Pretty(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('_', ' ');

        private Guid? CurrentCharacterId { get; set; }

        // ---- tutorial: the mentor points at the real controls ---------------------------------------------------

        private static readonly (int Realm, string Title, string Text)[] Milestones =
        {
            (3, "Fundament", "Od teraz odosobnienie (×8 Qi) i metody kultywacji. Wybierz metodę pasującą do twojej ścieżki — rośnie z każdą minutą."),
            (11, "Nowa era", "Wkraczasz w nową erę: węzły świata dają lepsze materiały, a zwoje technik są mocniejsze. Zajrzyj na Drogę 66 królestw w karcie postaci."),
            (21, "Gałąź ścieżki", "Twoja ścieżka się rozwidla — wybierz gałąź w Rozwoju. Kolejne stopnie przyjdą w 34., 45. i 56. królestwie."),
            (33, "Druga ścieżka", "Możesz przyjąć drugą ścieżkę: jej sztuki działają w 60% zamiast 30%. Wybór w Rozwoju."),
            (44, "Era Świętych", "Wyprawy uczniów sięgają dalej, a wspólne wyzwania tygodnia dają więcej. Nie zapominaj o przyjaciołach — listy mogą nieść dary."),
            (65, "Umiejętność ostateczna", "Szczyt ścieżki: wybierz umiejętność ostateczną w Rozwoju. Pierwszy wybór jest darmowy."),
        };

        /// <summary>Once per milestone realm, Mistrz Lao explains what just opened (1.9). Only after the first-run tutorial.</summary>
        private void MaybeShowMilestone(int realm)
        {
            if (!_prefs.GetBoolean(TutorialKey, false) || _overlayOpen) return;
            var due = Milestones.Where(m => realm >= m.Realm && !_prefs.GetBoolean("jianghu.milestone." + m.Realm, false)).ToList();
            if (due.Count == 0) return;
            using (var e = _prefs.Edit())
            {
                foreach (var m in due) e.PutBoolean("jianghu.milestone." + m.Realm, true);
                e.Apply();
            }

            var (_, title, text) = due[due.Count - 1];
            var view = new CoachMarkView(this, null, "Mistrz Lao — " + title, text, "Rozumiem");
            System.Action close = null;
            close = () =>
            {
                _overlayOpen = false;
                PopOverlay(view);
                _frame.RemoveView(view);
            };
            view.Done += close;
            _overlayOpen = true;
            _frame.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            PushOverlay(view, close);
        }

        private bool _overlayOpen;

        /// <summary>First run: Mistrz Lao highlights the real controls one by one (tap anywhere to continue).</summary>
        private void MaybeShowTutorial(CharacterDto c)
        {
            if (_prefs.GetBoolean(TutorialKey, false) || _tab != TabHome) return;
            var steps = new List<(Func<View> Target, string Title, string Text)>
            {
                (() => null, "Mistrz Lao", "Witaj, " + c.Name + ". Jianghu to twoje własne ulice — każdy krok, źródło Qi i bestia są prawdziwym miejscem wokół ciebie."),
                (() => _hudQi, "Twoje Qi", "Tu widzisz Qi etapu i swoje królestwo. Pełne naczynie pozwala spróbować przełomu."),
                (() => _homeMeditate, "Medytacja", "Zacznij od medytacji. Naczynie pokazuje, ile minut brakuje do 100%."),
                (() => _homeDaily, "Zadania dnia", "Trzy zadania dziennie i meldunek. Dotknij, by odebrać nagrody."),
                (() => _dockItems.TryGetValue(TabWorld, out var w) ? w.Item : null, "Świat", "Wyprawa po twojej okolicy: zioła, rudy, źródła Qi, bestie i legowiska bossów."),
                (() => _dockItems.TryGetValue(TabCombat, out var b) ? b.Item : null, "Walka", "Trening z bestiami — zwycięż bestię z 🐾, a może zostanie twoim towarzyszem."),
                (() => _dockItems.TryGetValue(TabHero, out var h) ? h.Item : null, "Bohater", "Ekwipunek, meridiany za każdy etap, techniki walki i Laboratorium Dao."),
                (() => _hudBagButton, "Sakwa", "Przedmioty, kuźnia i targ. Powodzenia, uczniu."),
            };

            void Show(int i)
            {
                if (i >= steps.Count)
                {
                    using (var e = _prefs.Edit())
                    {
                        e.PutBoolean(TutorialKey, true);
                        e.Apply();
                    }

                    return;
                }

                var (target, title, text) = steps[i];
                var view = new CoachMarkView(this, target(), title, text, i == steps.Count - 1 ? "Ruszam w drogę" : "Dalej  (" + (i + 1) + "/" + steps.Count + ")");
                System.Action close = null;
                close = () =>
                {
                    PopOverlay(view);
                    _frame.RemoveView(view);
                };
                view.Done += () =>
                {
                    close();
                    Show(i + 1);
                };
                _frame.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                PushOverlay(view, close);
            }

            _frame.PostDelayed(() => Show(0), 400);
        }
    }
}
