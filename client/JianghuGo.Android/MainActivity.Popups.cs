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
            overlay.SetBackgroundColor(Color.Argb(170, 0, 0, 0));
            var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
            panel.SetGravity(GravityFlags.CenterHorizontal);
            panel.Background = new FrameDrawable(Resources.DisplayMetrics.Density, Color.Argb(250, 16, 20, 24), Ui.Gold, Ui.Gold);
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
                img.SetImageBitmap(Ui.Art(this, accentArt));
                img.SetScaleType(ImageView.ScaleType.FitCenter);
                panel.AddView(img, new LinearLayout.LayoutParams(_ui.Dp(84), _ui.Dp(84)));
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
        private void ShowLoot(string title, long qi, List<(string ItemId, long Quantity)> items, string tamedNpcId)
        {
            var body = Popup(title, Resource.Drawable.icon_stones, out _, "Zabierz");
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

        private static string Pretty(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('_', ' ');

        private Guid? CurrentCharacterId { get; set; }

        // ---- tutorial: the mentor points at the real controls ---------------------------------------------------

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
