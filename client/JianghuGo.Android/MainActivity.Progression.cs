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
    /// <summary>
    /// MVP 1.1 screens: daily quests and check-in, achievements and titles, meridians, rankings, the spirit pet, the
    /// hero's equipment silhouette, the mentor's tutorial, and the reward / level-up popups. All numbers come from the
    /// server; these screens only show them and send intents.
    /// </summary>
    public sealed partial class MainActivity
    {
        private const string TutorialKey = "jianghu.tutorial.v1";
        private static readonly string[] Stats = { "strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate" };

        // ---- character screen helpers ---------------------------------------------------------------------------

        /// <summary>Training opponents from the server catalog (world bosses are fought only in the world).</summary>
        private async Task RenderTrainingAsync(CharacterDto c, LinearLayout fights)
        {
            var gen = _screen;
            var catalog = await _api.GetCombatCatalogAsync(Ct);
            if (gen != _screen) return;
            fights.RemoveAllViews();
            LinearLayout row = null;
            var i = 0;
            foreach (var npc in catalog.Npcs.Where(n => !n.WorldOnly).OrderBy(n => n.Tier).ThenBy(n => n.RewardQi))
            {
                var n = npc;
                if (i++ % 3 == 0) row = _ui.Columns(fights);
                var look = NpcLooks.For(n.Id);
                _ui.Tile(n.NamePl, new string('★', Math.Clamp(n.Tier, 1, 5)) + (n.Tameable ? "  🐾" : string.Empty), look.TileArt, () => Run(() => StartTrainingAsync(c, n.Id)), parent: row, heightDp: 128, pixelArt: true, accent: n.Tier >= 3 ? Ui.Violet : Ui.Cinnabar, titleSp: 12);
            }

            _ui.Label("★ = poziom zagrożenia · 🐾 = można oswoić po zwycięstwie. Bossowie czekają w świecie.", 11, Ui.Muted, parent: fights);
        }

        /// <summary>Starts a training fight; a fight left unfinished earlier is resumed instead (one battle at a time).</summary>
        private async Task StartTrainingAsync(CharacterDto c, string npcId)
        {
            var combat = new CombatFlow(_api);
            try
            {
                await combat.StartPveAsync(c.Id, npcId, Ct);
            }
            catch (ApiException e) when (e.Code == "already_in_battle")
            {
                var open = (await _api.GetOpenBattlesAsync(c.Id, Ct)).Items.FirstOrDefault(b => b.IsActive);
                if (open == null) throw;
                await combat.OpenAsync(open.Id, Ct);
                ShowBattle(combat, c);
                SetStatus("Najpierw dokończ trwającą walkę z: " + open.Opponent.Name);
                return;
            }

            ShowBattle(combat, c);
        }

        /// <summary>Daily check-in banner and the title earned by achievements, under the hero card.</summary>
        private async Task RenderDailyAsync(CharacterDto c, LinearLayout card, TextView traitsText)
        {
            var gen = _screen;
            var board = await _api.GetQuestsAsync(c.Id, Ct);
            if (gen != _screen) return;
            if (board.Titles.Count > 0) traitsText.Text = "「" + board.Titles[board.Titles.Count - 1] + "」  ·  " + traitsText.Text;
            var done = board.Daily.Count(q => q.Completed && !q.Claimed);
            if (!board.Checkin.CheckedInToday)
            {
                Button b = null;
                b = _ui.WithIcon(_ui.Action("Dzienny meldunek: +" + board.Checkin.NextReward + " kamieni (seria " + (board.Checkin.Streak + 1) + ")", () => Run(async () =>
                {
                    var r = await _api.CheckinAsync(c.Id, Ct);
                    b.Visibility = ViewStates.Gone;
                    Sfx.Play(Sfx.Coin);
                    ShowBanner("MELDUNEK", "Seria: " + r.Streak + " " + (r.Streak == 1 ? "dzień" : "dni"), "+" + r.Stones + " kamieni duchowych", "menu_checkin");
                }), parent: card), "menu_checkin", 26);
            }

            if (done > 0)
                _ui.WithIcon(_ui.Action("Ukończone zadania do odebrania: " + done, () => Run(() => ShowQuestsAsync(c)), secondary: true, parent: card), "menu_quests", 26);
        }

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
                overlay.Animate().Alpha(0f).SetDuration(160).WithEndAction(new Java.Lang.Runnable(() => _frame.RemoveView(overlay))).Start();
                onClose?.Invoke();
            };
            close = doClose;
            _ui.Action(button, doClose, parent: panel);
            overlay.Click += (_, __) => doClose();
            _frame.AddView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
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

        // ---- tutorial -------------------------------------------------------------------------------------------

        /// <summary>First run: the mentor (Mistrz Lao) walks the new cultivator through the basics, one card at a time.</summary>
        private void MaybeShowTutorial(CharacterDto c)
        {
            CurrentCharacterId = c.Id;
            if (_prefs.GetBoolean(TutorialKey, false)) return;
            var pages = new[]
            {
                ("Mistrz Lao", "Witaj, " + c.Name + ". Jianghu to twoje własne ulice — każdy krok, każde źródło Qi i każda bestia są prawdziwym miejscem wokół ciebie.", Resource.Drawable.portrait),
                ("Medytacja", "Zacznij od medytacji. Naczynie Qi pokazuje, ile brakuje do pełni etapu i ile minut jeszcze potrzeba. Pełne naczynie = szansa na przełom.", Resource.Drawable.menu_meditate),
                ("Przełom i meridiany", "Każdy zdobyty etap otwiera punkt meridianu. Wzmocnij nim wybraną cechę — premie liczą się w każdej walce.", Resource.Drawable.menu_meridians),
                ("Wyprawa", "Eksploruj świat: zbieraj zioła i rudy, czerp Qi ze źródeł, walcz z bestiami. Bossowie czekają w legowiskach — tylko dla odważnych.", Resource.Drawable.menu_explore),
                ("Zadania", "Codziennie trzy zadania i meldunek. Osiągnięcia dają tytuły. Zwycięż bestię z 🐾, a może zostanie twoim towarzyszem. Powodzenia, uczniu.", Resource.Drawable.menu_quests),
            };

            void Show(int i)
            {
                if (i >= pages.Length)
                {
                    using (var e = _prefs.Edit())
                    {
                        e.PutBoolean(TutorialKey, true);
                        e.Apply();
                    }

                    return;
                }

                var (title, text, art) = pages[i];
                var body = Popup(title, art, out _, i == pages.Length - 1 ? "Ruszam w drogę" : "Dalej  (" + (i + 1) + "/" + pages.Length + ")", () => _frame.PostDelayed(() => Show(i + 1), 200));
                _ui.Label(text, 15, Ui.Ink, parent: body).Gravity = GravityFlags.Center;
            }

            Show(0);
        }

        // ---- quests ---------------------------------------------------------------------------------------------

        private async Task ShowQuestsAsync(CharacterDto c)
        {
            var board = await _api.GetQuestsAsync(c.Id, Ct);
            var ui = NewScreen("Zadania", () => ShowCharacter(c), Resource.Drawable.place_altar, "DZIEŃ " + board.Day);

            var checkin = ui.Panel();
            ui.WithIcon(ui.Label("Meldunek — seria " + board.Checkin.Streak + " (rekord " + board.Checkin.BestStreak + ")", 16, Ui.Gold, bold: true, parent: checkin), "menu_checkin", 30);
            if (board.Checkin.CheckedInToday) ui.Label("Dziś już się zameldowałeś. Jutro: +" + board.Checkin.NextReward + " kamieni.", 13, Ui.Muted, parent: checkin);
            else ui.Action("Zamelduj się: +" + board.Checkin.NextReward + " kamieni", () => Run(async () =>
            {
                var r = await _api.CheckinAsync(c.Id, Ct);
                Sfx.Play(Sfx.Coin);
                await ShowQuestsAsync(c);
                ShowBanner("MELDUNEK", "Seria: " + r.Streak, "+" + r.Stones + " kamieni duchowych", "menu_checkin");
            }), parent: checkin);

            void QuestCard(QuestDto q, bool achievement)
            {
                var panel = ui.Panel(padDp: 10);
                var color = q.Claimed ? Ui.Muted : q.Completed ? Ui.Gold : Ui.Ink;
                ui.Label((q.Claimed ? "✓ " : string.Empty) + q.NamePl, 16, color, bold: true, parent: panel);
                ui.Label(q.DescriptionPl + "   ·   nagroda: " + q.Stones + " kamieni" + (q.TitlePl != null ? " + tytuł 「" + q.TitlePl + "」" : string.Empty), 12, Ui.Muted, parent: panel);
                var meter = ui.Meter(string.Empty, achievement ? Ui.Violet : Ui.Jade, panel, 16);
                Ui.SetMeter(meter, q.Progress + " / " + q.Target, q.Progress, q.Target);
                if (q.Completed && !q.Claimed)
                {
                    ui.Action("Odbierz nagrodę", () => Run(async () =>
                    {
                        var r = await _api.ClaimQuestAsync(c.Id, q.Id, Ct);
                        Sfx.Play(Sfx.Coin);
                        await ShowQuestsAsync(c);
                        ShowBanner(q.TitlePl != null ? "NOWY TYTUŁ" : "NAGRODA", q.TitlePl != null ? "「" + q.TitlePl + "」" : q.NamePl, "+" + r.Stones + " kamieni (razem " + r.Balance + ")", "icon_stones");
                    }), parent: panel);
                }
            }

            ui.Heading("Dzisiejsze zadania");
            foreach (var q in board.Daily) QuestCard(q, false);
            ui.Label("Nowe zadania o północy czasu świata.", 11, Ui.Muted);

            ui.Heading("Tytuły");
            if (board.Titles.Count == 0) ui.Label("Brak — zdobądź osiągnięcie z tytułem.", 13, Ui.Muted);
            else
            {
                var flow = ui.Flow();
                foreach (var t in board.Titles) ui.Chip("「" + t + "」", Ui.Gold, flow);
            }

            ui.Heading("Osiągnięcia");
            foreach (var q in board.Achievements.OrderBy(a => a.Claimed).ThenByDescending(a => a.Completed).ThenByDescending(a => a.Progress / (double)Math.Max(1, a.Target)))
                QuestCard(q, true);
        }

        // ---- meridians ------------------------------------------------------------------------------------------

        private async Task ShowMeridiansAsync(CharacterDto c)
        {
            var m = await _api.GetMeridiansAsync(c.Id, Ct);
            var ui = NewScreen("Meridiany", () => ShowCharacter(c), Resource.Drawable.place_qiarray, "DRZEWO KANAŁÓW QI");
            var head = ui.Panel();
            ui.WithIcon(ui.Label("Wolne punkty: " + m.Free + "   (zdobyte " + m.PointsEarned + ", otwarte " + m.PointsSpent + ")", 17, m.Free > 0 ? Ui.Gold : Ui.Ink, bold: true, parent: head), "menu_meridians", 34);
            ui.Label("Każdy osiągnięty etap kultywacji daje 1 punkt. Królestwo dodaje +" + m.RealmBonus + " do wszystkich cech. Premie działają w walce.", 12, Ui.Muted, parent: head);

            foreach (var stat in Stats)
            {
                var st = stat;
                var points = m.Allocated.TryGetValue(st, out var p) ? p : 0;
                var row = ui.Panel(padDp: 8);
                var line = ui.Columns(row);
                var name = ui.WithIcon(ui.Label(StatName(st) + "   +" + (points + m.RealmBonus), 15, points > 0 ? Ui.Gold : Ui.Ink, bold: points > 0), "icon_" + st, 26);
                ((ViewGroup)name.Parent).RemoveView(name);
                line.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2f) { Gravity = GravityFlags.CenterVertical });
                var dots = new TextView(this) { Text = new string('●', Math.Min(points, 12)) + (points > 12 ? "…" : string.Empty) };
                dots.SetTextColor(Ui.QiBlue);
                line.AddView(dots, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical });
                if (m.Free > 0)
                {
                    var open = ui.Action("+1", () => Run(async () =>
                    {
                        await _api.OpenMeridianAsync(c.Id, st, m.PointsSpent, Ct);
                        Sfx.Play(Sfx.Chime);
                        await ShowMeridiansAsync(c);
                    }), parent: line);
                    open.LayoutParameters = new LinearLayout.LayoutParams(_ui.Dp(64), ViewGroup.LayoutParams.WrapContent);
                }
            }

            if (m.PointsSpent > 0)
                ui.Action("Zamknij wszystkie meridiany (koszt " + m.ResetQiCost + " Qi)", () => Run(async () =>
                {
                    await _api.ResetMeridiansAsync(c.Id, Ct);
                    await ShowMeridiansAsync(c);
                    SetStatus("Meridiany zamknięte — punkty wróciły.");
                }), secondary: true);
        }

        // ---- rankings -------------------------------------------------------------------------------------------

        private async Task ShowRankingAsync(CharacterDto c, string metric)
        {
            var ui = NewScreen("Rankingi", () => ShowCharacter(c), Resource.Drawable.loc_temple, "SŁAWA W JIANGHU");
            LinearLayout tabs = null;
            var tab = 0;
            foreach (var (m, label) in new[] { ((string)null, "Kultywacja"), ("pvp_won", "Pojedynki"), ("bosses_won", "Bossowie"), ("battles_won", "Zwycięstwa") })
            {
                var mm = m;
                if (tab++ % 2 == 0) tabs = ui.Columns();
                ui.Action(label, () => Run(() => ShowRankingAsync(c, mm)), secondary: mm != metric, parent: Cell(tabs)).SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            }

            void Row(int rank, string name, string path, string value, bool me)
            {
                var panel = ui.Panel(padDp: 8);
                var medal = rank == 1 ? "🥇" : rank == 2 ? "🥈" : rank == 3 ? "🥉" : "#" + rank;
                ui.WithIcon(ui.Label(medal + "  " + name + (me ? "  (ty)" : string.Empty), 15, me ? Ui.Gold : Ui.Ink, bold: rank <= 3 || me, parent: panel), "portrait_" + path, 30);
                ui.Label(value, 12, Ui.Muted, parent: panel);
            }

            if (metric == null)
            {
                var r = await _api.GetRankingAsync(c.Id, Ct);
                if (r.Me != null) ui.WithIcon(ui.Label("Twoje miejsce: #" + r.Me.Rank + " — " + r.Me.RealmNamePl + ", etap " + r.Me.Stage, 15, Ui.Gold, bold: true), "menu_ranking", 34);
                foreach (var e in r.Top) Row(e.Rank, e.Name, e.Path, e.RealmNamePl + " · etap " + e.Stage, e.CharacterId == c.Id);
                if (r.Top.Count == 0) ui.Label("Nikt jeszcze nie zaczął kultywacji.", 14, Ui.Muted);
            }
            else
            {
                var page = await _api.GetLeadersAsync(c.Id, metric, Ct);
                foreach (var e in page.Items) Row(e.Rank, e.Name, e.Path, e.Value + (metric == "bosses_won" ? " pokonanych bossów" : " zwycięstw"), e.CharacterId == c.Id);
                if (page.Items.Count == 0) ui.Label("Tablica jest jeszcze pusta — bądź pierwszy.", 14, Ui.Muted);
            }
        }

        // ---- spirit pet -----------------------------------------------------------------------------------------

        private async Task ShowPetAsync(CharacterDto c)
        {
            var env = await _api.GetPetAsync(c.Id, Ct);
            var ui = NewScreen("Duchowy towarzysz", () => ShowCharacter(c), Resource.Drawable.loc_bamboo, "OSWOJONA BESTIA");
            var pet = env.Pet;
            if (pet == null)
            {
                ui.WithIcon(ui.Label("Nie masz jeszcze towarzysza.", 16, Ui.Gold, bold: true), "menu_pet", 34);
                ui.Label("Pokonaj bestię oznaczoną 🐾 (w treningu lub w świecie). Po zwycięstwie jest szansa (wyższa z Losem), że bestia pójdzie za tobą. Towarzysz rośnie z każdą wygraną i dodaje część swoich cech do twoich w walce.", 14, Ui.Muted);
                return;
            }

            var look = NpcLooks.For(pet.NpcId);
            ui.Tile(pet.NamePl, "Poziom " + pet.Level, look.TileArt, () => { }, heightDp: 200, pixelArt: true, accent: Ui.Jade, titleSp: 20);
            var panel = ui.Panel();
            var xp = ui.Meter(string.Empty, Ui.Jade, panel, 18);
            Ui.SetMeter(xp, pet.XpToNextLevel > 0 ? "XP " + pet.Xp + " / " + (pet.Xp + pet.XpToNextLevel) : "Poziom maksymalny", pet.XpToNextLevel > 0 ? pet.Xp : 1, pet.XpToNextLevel > 0 ? pet.Xp + pet.XpToNextLevel : 1);
            ui.Label("Premie w walce:", 14, Ui.Gold, bold: true, parent: panel);
            var flow = ui.Flow(panel);
            foreach (var kv in pet.Bonuses) ui.WithIcon(ui.Chip(StatName(kv.Key) + " +" + kv.Value, Ui.Jade, flow), "icon_" + kv.Key, 18);
            ui.Label("Oswojony " + pet.TamedAt.ToLocalTime().ToString("dd.MM.yyyy"), 12, Ui.Muted, parent: panel);
            var confirm = false;
            Button release = null;
            release = ui.Action("Uwolnij towarzysza", () =>
            {
                if (!confirm)
                {
                    confirm = true;
                    release.Text = "Na pewno? Dotknij ponownie, by uwolnić";
                    return;
                }

                Run(async () =>
                {
                    await _api.ReleasePetAsync(c.Id, Ct);
                    await ShowPetAsync(c);
                    SetStatus(pet.NamePl + " wraca na wolność.");
                });
            }, secondary: true);
        }

        // ---- hero & equipment -----------------------------------------------------------------------------------

        private async Task ShowHeroAsync(CharacterDto c)
        {
            var economy = new EconomyFlow(_api, c.Id);
            await economy.LoadAsync(Ct);
            var inv = economy.Inventory;
            PetDto pet = null;
            MeridiansDto meridians = null;
            try
            {
                pet = (await _api.GetPetAsync(c.Id, Ct)).Pet;
                meridians = await _api.GetMeridiansAsync(c.Id, Ct);
            }
            catch (ApiException)
            {
                // optional extras
            }

            var ui = NewScreen(c.Name, () => ShowCharacter(c), Resource.Drawable.bg_hero, "POSTAĆ I EKWIPUNEK");
            var doll = new HeroDollView(this) { PetNpcId = pet?.NpcId };
            foreach (var slot in HeroDollView.Slots)
            {
                var item = inv.Equipped.TryGetValue(slot, out var id) ? inv.Items.FirstOrDefault(i => i.Id == id) : null;
                doll.Equipped[slot] = item;
            }

            ui.Add(doll, 340);
            var detail = ui.Panel(padDp: 10);
            var detailText = ui.Label("Dotknij slotu, by zobaczyć przedmiot.", 13, Ui.Muted, parent: detail);
            doll.SlotTapped += slot =>
            {
                Sfx.Play(Sfx.Click);
                var item = doll.Equipped.TryGetValue(slot, out var it) ? it : null;
                detailText.Text = item == null
                    ? HeroDollView.SlotName(slot) + ": pusto — załóż przedmiot w Sakwie."
                    : item.NamePl + (item.UpgradeLevel > 0 ? " +" + item.UpgradeLevel : string.Empty) + " · " + Ui.RarityOf(item.Rarity).Name + "\n" +
                      string.Join("  ·  ", item.Stats.Select(kv => StatName(kv.Key) + " +" + kv.Value));
                detailText.SetTextColor(item == null ? Ui.Muted : Ui.Ink);
            };

            // total combat stats: base + equipment + meridians/realm + pet
            ui.WithIcon(ui.Heading("Cechy w walce"), "menu_hero", 26);
            var table = ui.Panel(padDp: 10);
            foreach (var st in c.Stats)
            {
                var eq = inv.Bonuses.TryGetValue(st.Id, out var e) ? e : 0;
                var mer = meridians == null ? 0 : (meridians.Allocated.TryGetValue(st.Id, out var a) ? a : 0) + meridians.RealmBonus;
                var pb = pet != null && pet.Bonuses.TryGetValue(st.Id, out var b) ? b : 0;
                var total = st.Value + eq + mer + pb;
                var parts = new List<string> { st.Value.ToString() };
                if (eq > 0) parts.Add("ekw. +" + eq);
                if (mer > 0) parts.Add("meridiany +" + mer);
                if (pb > 0) parts.Add("towarzysz +" + pb);
                ui.WithIcon(ui.Label(StatName(st.Id) + ":  " + total + (parts.Count > 1 ? "   (" + string.Join(", ", parts) + ")" : string.Empty), 14, total > st.Value ? Ui.Gold : Ui.Ink, parent: table), "icon_" + st.Id, 20);
            }

            ui.Action("🎒 Otwórz sakwę", () => ShowBag(economy, c), secondary: true);
        }
    }
}
