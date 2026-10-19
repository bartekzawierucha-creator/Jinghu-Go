using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>Cultivation hub (meditation, breakthrough, the day's quests), the quest board and the news.</summary>
    public sealed partial class MainActivity
    {
        private View _homeMeditate;
        private View _homeDaily;

        // ---- Kultywacja ---------------------------------------------------------------------------------------

        private void ShowHomeTab()
        {
            var c = _me;
            var ui = NewScreen(null, art: Resource.Drawable.bg_town, tab: TabHome);
            var page = ui.Content;
            var gen = _screen;
            var flow = new CultivationFlow(_api, c.Id);

            // realm line
            var head = ui.HRow(page, 48, 0); // the realm's hanzi use a taller CJK line
            var emblem = ui.Icon(head, "emblem_qi_condensation", 36);
            var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
            head.AddView(titles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8), Gravity = GravityFlags.CenterVertical });
            var realm = ui.Text("…", titles, 16, Ui.Gold, bold: true);
            var sub = ui.Text("Ścieżka " + PathName(c.Path) + " · " + string.Join(" · ", c.Traits.Select(t => t.NamePl)), titles, 11, Ui.Muted);

            // the vessel and the meditating hero take what is left of the screen
            var charge = new QiChargeView(this);
            ui.AddWeighted(page, charge, 1f, 2);
            var info = ui.Text(string.Empty, page, 12, Ui.Muted, maxLines: 2);

            var actions = ui.HRow(page, 52);
            var meditate = ui.Btn("Medytuj", () => { }, actions, sp: 16, heightDp: 52, icon: "menu_meditate");
            _homeMeditate = meditate;
            var breakthrough = ui.Btn("Przełom", () => { }, actions, secondary: true, sp: 16, heightDp: 52, icon: "menu_breakthrough");
            Button steps = null;
            if (c.Path == "body")
            {
                steps = ui.Btn("Kroki", () => Run(() => ReportStepsAsync(c.Id, flow, Render)), actions, secondary: true, sp: 14, heightDp: 52, icon: "menu_explore");
                if (_steps.Available) _steps.Start();
            }

            var more = ui.HRow(page, 40);
            ui.Btn("Odosobnienie", () => RetreatSheet(flow, Render), more, secondary: true, sp: 11, heightDp: 40);
            ui.Btn("Rozwój", () => CultivationSheet(flow, Render), more, secondary: true, sp: 11, heightDp: 40);
            ui.Btn("Posiadłość", () => Run(() => ShowEstateAsync(0)), more, secondary: true, sp: 11, heightDp: 40);

            // the day: three quests and the check-in seal
            var daily = ui.HRow(page, 74);
            _homeDaily = daily;

            void Render()
            {
                var st = flow.State;
                if (st == null || gen != _screen) return;
                _cultivation = st;
                RenderHud();
                realm.Text = (string.IsNullOrEmpty(st.RealmHanzi) ? string.Empty : st.RealmHanzi + " ") + st.RealmNamePl + " · " + (st.StageName ?? "etap") + " " + st.Stage + "/" + st.StagesInRealm;
                var id = ui.ArtId("emblem_" + st.RealmId);
                if (id != 0) emblem.SetImageBitmap(Ui.Art(this, id));
                charge.State = st;
                charge.Invalidate();
                var deviation = st.DeviationUntil.HasValue && st.DeviationUntil > DateTimeOffset.UtcNow ? "  ·  odchylenie Qi do " + st.DeviationUntil.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
                var rate = st.IsInRetreat ? st.RetreatPerHour : st.IsMeditating ? st.MeditationPerHour : st.FlowPerHour;
                var mode = st.IsInRetreat ? "Odosobnienie do " + (st.RetreatUntil?.ToLocalTime().ToString("HH:mm") ?? "?") : st.IsMeditating ? "Medytacja ×5" : "Qi płynie samo ×1";
                info.Text = mode + " · " + Math.Round(rate) + " Qi/h" + (st.WeekendPermille > 1000 ? " · weekend ×" + (st.WeekendPermille / 1000.0).ToString("0.#") : string.Empty)
                            + " · naczynie " + st.Qi + "/" + st.Vessel +
                            (st.TribulationRequired ? "\n⚡ Doskonałość — czeka Niebiańska Próba" : st.BottleneckItem != null && !st.IsPeak && st.Stage == 11 ? "\nWąskie gardło: potrzebna pigułka przełomu" :
                                "\nSzansa przełomu: " + Math.Round(st.BreakthroughChance * 100) + "%") + deviation +
                            (steps != null ? "   ·   kroki " + st.DailySteps + "/" + st.DailyStepCap : string.Empty);
                meditate.Text = st.IsMeditating ? "Zakończ" : "Medytuj";
                breakthrough.Text = st.TribulationRequired ? "Próba Niebios" : "Przełom";
            }

            meditate.Click += (_, __) => Run(async () =>
            {
                if (flow.State == null) return;
                if (flow.State.IsMeditating)
                {
                    var r = await flow.FinishMeditationAsync(Ct);
                    Sfx.Play(Sfx.Chime);
                    SetStatus("+" + r.QiGained + " Qi (" + r.MinutesCounted + " min)" + (r.DailyCapReached ? " — dzienny limit" : string.Empty));
                    RefreshHud();
                }
                else
                {
                    await flow.StartMeditationAsync(Ct);
                }

                Render();
            });
            breakthrough.Click += (_, __) => Run(async () =>
            {
                if (flow.State?.TribulationRequired == true)
                {
                    await StartTribulationAsync(flow);
                    return;
                }

                var r = await flow.BreakthroughAsync(Ct);
                Render();
                if (r == null)
                {
                    SetStatus("Stan zmienił się na innym urządzeniu — odświeżono.");
                    return;
                }

                if (r.Success)
                {
                    PlayOverlay(Resource.Drawable.fx_breakthrough, 256, 9);
                    Sfx.Play(Sfx.Breakthrough);
                    ShowBanner("PRZEŁOM!", flow.State.RealmNamePl + " · etap " + flow.State.Stage, "+1 punkt meridianu do otwarcia", "emblem_" + flow.State.RealmId);
                }
                else
                {
                    Sfx.Play(Sfx.Defeat);
                    SetStatus("Przełom nieudany — odchylenie Qi.");
                }

                RefreshHud();
            });

            void RenderDaily(QuestBoardDto board)
            {
                if (gen != _screen || board == null) return;
                daily.RemoveAllViews();
                foreach (var q in board.Daily)
                {
                    var card = ui.Card(daily, 1f, 6, q.Completed && !q.Claimed ? Ui.Gold : (Color?)null);
                    card.Clickable = true;
                    card.Click += (_, __) => Run(ShowQuestsAsync);
                    ui.Text((q.Claimed ? "✓ " : string.Empty) + q.NamePl, card, 11, q.Completed ? Ui.Gold : Ui.Ink, bold: true);
                    ui.MeterIn(card, q.Completed ? Ui.Gold : Ui.Jade, q.Progress + "/" + q.Target, q.Progress, q.Target, 14);
                    ui.Text(q.Completed && !q.Claimed ? "Odbierz!" : "+" + q.Stones, card, 10, q.Completed && !q.Claimed ? Ui.Cinnabar : Ui.Muted, bold: true);
                }

                if (!board.Checkin.CheckedInToday)
                {
                    var seal = ui.Card(daily, 0.8f, 6, Ui.Cinnabar);
                    seal.Clickable = true;
                    seal.SetGravity(GravityFlags.Center);
                    ui.Icon(seal, "menu_checkin", 30).LayoutParameters = new LinearLayout.LayoutParams(Px(30), Px(30)) { Gravity = GravityFlags.CenterHorizontal };
                    ui.Text("+" + board.Checkin.NextReward, seal, 11, Ui.Gold, bold: true, center: true);
                    seal.Click += (_, __) => Run(async () =>
                    {
                        var r = await _api.CheckinAsync(c.Id, Ct);
                        Sfx.Play(Sfx.Coin);
                        _board = r.Board;
                        _balance = r.Balance;
                        RenderHud();
                        RenderDaily(r.Board);
                        ShowBanner("MELDUNEK", "Seria: " + r.Streak + " " + (r.Streak == 1 ? "dzień" : "dni"), "+" + r.Stones + " kamieni duchowych", "menu_checkin");
                    });
                }
            }

            RenderDaily(_board);
            Run(async () =>
            {
                await flow.LoadAsync(Ct);
                Render();
                ShowOfflineSummary(flow, Render);
                RenderDaily(await _api.GetQuestsAsync(c.Id, Ct));
            });
        }

        // ---- Zadania ------------------------------------------------------------------------------------------

        private Task ShowQuestsAsync() => ShowQuestsAsync(0);

        private async Task ShowQuestsAsync(int tab)
        {
            var c = _me;
            var board = await _api.GetQuestsAsync(c.Id, Ct);
            _board = board;
            var ui = NewScreen("Zadania", BackToTab, Resource.Drawable.tile_menu_quests, "DZIEŃ " + board.Day);
            var page = ui.Content;
            var claimableAch = board.Achievements.Count(a => a.Completed && !a.Claimed);
            var storyReady = board.Story != null && board.Story.Completed && !board.Story.Finished ? 1 : 0;
            ui.Tabs(page, new[] { "Dziś", "Osiągnięcia", "Tytuły", "Opowieść" }, tab, i => Run(() => ShowQuestsAsync(i)),
                badges: new[] { board.Daily.Count(q => q.Completed && !q.Claimed) + (board.Checkin.CheckedInToday ? 0 : 1), claimableAch,
                    0, storyReady + board.Seasonal.Count(q => q.Completed && !q.Claimed) + WeeklyClaimable(board.Weekly) });

            async Task Claim(QuestDto q)
            {
                var r = await _api.ClaimQuestAsync(c.Id, q.Id, Ct);
                Sfx.Play(Sfx.Coin);
                _board = r.Board;
                _balance = r.Balance;
                await ShowQuestsAsync(tab);
                ShowBanner(q.TitlePl != null ? "NOWY TYTUŁ" : "NAGRODA", q.TitlePl != null ? "「" + q.TitlePl + "」" : q.NamePl, "+" + r.Stones + " kamieni (razem " + r.Balance + ")", "menu_quests");
            }

            void QuestCard(LinearLayout cell, QuestDto q, bool achievement)
            {
                var rim = q.Completed && !q.Claimed ? Ui.Gold : q.Secret ? Ui.Violet : (Color?)null;
                var card = ui.Card(cell, 1f, 8, rim, 0);
                var top = ui.HRow(card, 22, 0);
                var secretHidden = q.Secret && !q.Completed;
                ui.Text((q.Claimed ? "✓ " : string.Empty) + (q.Secret ? "🔒 " : string.Empty) + (secretHidden ? "Tajne osiągnięcie" : q.NamePl), top, 14,
                    q.Claimed ? Ui.Muted : q.Completed ? Ui.Gold : secretHidden ? Ui.Violet : Ui.Ink, bold: true);
                ui.Text("+" + q.Stones, top, 12, Ui.Gold, bold: true).Gravity = GravityFlags.End;
                ui.Text(q.DescriptionPl + (q.TitlePl != null ? "  ·  「" + q.TitlePl + "」" + (q.Bonus != null ? " " + StatsText(q.Bonus, ", ") : string.Empty) : string.Empty), card, 11, Ui.Muted, maxLines: 2);
                ui.Box(card, 1f, marginDp: 0);
                var bottom = ui.HRow(card, 30, 2);
                var meterBox = ui.Box(bottom, 1.4f, marginDp: 0);
                meterBox.SetGravity(GravityFlags.CenterVertical);
                ui.MeterIn(meterBox, achievement ? Ui.Violet : Ui.Jade, q.Progress + " / " + q.Target, q.Progress, q.Target, 16);
                if (q.Completed && !q.Claimed) ui.Btn("Odbierz", () => Run(() => Claim(q)), bottom, sp: 12, heightDp: 30);
            }

            if (tab == 0)
            {
                var checkin = ui.Card(page, 0.55f, 8, board.Checkin.CheckedInToday ? (Color?)null : Ui.Cinnabar);
                var row = ui.HRow(checkin, 44, 0);
                ui.Icon(row, "menu_checkin", 40);
                var col = ui.Box(row, 1f, marginDp: 8);
                ui.Text("Meldunek — seria " + board.Checkin.Streak + " (rekord " + board.Checkin.BestStreak + ")", col, 14, Ui.Gold, bold: true);
                ui.Text(board.Checkin.CheckedInToday ? "Jutro: +" + board.Checkin.NextReward + " kamieni" : "Dziś: +" + board.Checkin.NextReward + " kamieni", col, 11, Ui.Muted);
                if (!board.Checkin.CheckedInToday)
                    ui.Btn("Zamelduj", () => Run(async () =>
                    {
                        var r = await _api.CheckinAsync(c.Id, Ct);
                        Sfx.Play(Sfx.Coin);
                        _board = r.Board;
                        _balance = r.Balance;
                        await ShowQuestsAsync(0);
                        ShowBanner("MELDUNEK", "Seria: " + r.Streak, "+" + r.Stones + " kamieni duchowych", "menu_checkin");
                    }), row, sp: 13, heightDp: 40);
                foreach (var q in board.Daily)
                {
                    var cell = ui.Box(page, 1f);
                    QuestCard(cell, q, false);
                }

                ui.Text("Nowe zadania o północy czasu świata.", page, 11, Ui.Muted, center: true);
            }
            else if (tab == 1)
            {
                var list = board.Achievements.OrderBy(a => a.Claimed).ThenByDescending(a => a.Completed).ThenBy(a => a.Secret)
                    .ThenByDescending(a => a.Progress / (double)Math.Max(1, a.Target)).ToList();
                ui.Text("Tajne osiągnięcia: " + board.SecretsFound + " / " + board.SecretsTotal + " — zagadki podpowiadają, czego szukać.", page, 11, Ui.Violet, center: true);
                ui.Pages(page, list.Count, 1, 4, (i, cell) => QuestCard(cell, list[i], true));
            }
            else if (tab == 2)
            {
                var all = board.AllTitles ?? new List<TitleDto>();
                if (all.Count == 0)
                {
                    ui.Box(page, 1f);
                    ui.Text("Nie masz jeszcze tytułów — zdobywa się je za osiągnięcia, królestwa, płomienie i arenę.", page, 14, Ui.Muted, center: true, maxLines: 3);
                    ui.Box(page, 1f);
                }
                else
                {
                    var chosen = all.FirstOrDefault(t => t.Selected);
                    ui.Text("Nad postacią: " + (chosen != null ? "「" + chosen.NamePl + "」" : "brak") + "  ·  premie tytułów: " + StatsText(board.TitleBonuses), page, 12, Ui.Jade, bold: true, center: true, maxLines: 2);
                    var ordered = all.OrderByDescending(t => t.Selected).ThenByDescending(t => RarityRank(t.Rarity)).ToList();
                    ui.Pages(page, ordered.Count, 2, 4, (i, cell) =>
                    {
                        var t = ordered[i];
                        var color = Ui.RarityOf(TitlePalette(t.Rarity)).Color;
                        var card = ui.Card(cell, 1f, 6, t.Selected ? Ui.Gold : color, 0);
                        card.SetGravity(GravityFlags.Center);
                        ui.Text("「" + t.NamePl + "」", card, 13, color, bold: true, center: true, maxLines: 2);
                        ui.Text(TitleSourceName(t.Source), card, 10, Ui.Muted, center: true);
                        card.Clickable = true;
                        card.Click += (_, __) => Run(async () =>
                        {
                            await _api.SelectTitleAsync(c.Id, t.Selected ? null : t.Id, Ct);
                            Sfx.Play(Sfx.Chime, 0.6f);
                            await ShowQuestsAsync(2);
                            SetStatus(t.Selected ? "Tytuł ukryty." : "Nad twoją postacią: 「" + t.NamePl + "」");
                        });
                    });
                    ui.Text("Dotknij tytułu, aby nosić go nad postacią (widzą go inni gracze i rywale w walce).", page, 11, Ui.Muted, center: true, maxLines: 2);
                }
            }
            else
            {
                if (board.Story is { } st)
                {
                    var story = ui.Card(page, 1.4f, 10, st.Completed && !st.Finished ? Ui.Gold : Ui.GoldDim);
                    ui.Text("ROZDZIAŁ " + st.Chapter + " / " + st.Chapters + " — " + st.TitlePl.ToUpperInvariant(), story, 14, Ui.Gold, bold: true);
                    ui.Text(st.TextPl, story, 12, Ui.Ink, maxLines: 5);
                    ui.Box(story, 1f, marginDp: 0);
                    ui.Text("Cel: " + st.GoalPl + "  ·  nagroda: " + st.Stones + " KD" + (st.ItemNamePl != null ? " + " + st.ItemQuantity + "× " + st.ItemNamePl : string.Empty), story, 11, Ui.Jade, maxLines: 2);
                    var srow = ui.HRow(story, 32, 2);
                    var mb = ui.Box(srow, 1.4f, marginDp: 0);
                    mb.SetGravity(GravityFlags.CenterVertical);
                    ui.MeterIn(mb, Ui.Gold, st.Finished ? "Opowieść ukończona" : st.Progress + " / " + st.Target, st.Finished ? 1 : st.Progress, st.Finished ? 1 : st.Target, 16);
                    if (st.Completed && !st.Finished)
                        ui.Btn("Dalej", () => Run(async () =>
                        {
                            var r = await _api.ClaimQuestAsync(c.Id, st.Id, Ct);
                            Sfx.Play(Sfx.Breakthrough, 0.6f);
                            _board = r.Board;
                            _balance = r.Balance;
                            await ShowQuestsAsync(3);
                            ShowBanner("ROZDZIAŁ " + st.Chapter, st.TitlePl, "+" + r.Stones + " kamieni" + (st.ItemNamePl != null ? " · " + st.ItemQuantity + "× " + st.ItemNamePl : string.Empty), "menu_quests");
                        }), srow, sp: 12, heightDp: 32);
                }

                if (board.Weekly is { } wk && wk.Personal != null)
                {
                    var card = ui.Card(page, 1.2f, 8, WeeklyClaimable(wk) > 0 ? Ui.Gold : Ui.Jade);
                    ui.Text("WYDARZENIE TYGODNIA — " + wk.NamePl.ToUpperInvariant() + " (do " + wk.Until.Substring(5) + ")", card, 12, Ui.Gold, bold: true);
                    ui.Text(wk.DescriptionPl, card, 11, Ui.Ink, maxLines: 2);
                    var prow = ui.HRow(card, 30, 2);
                    var pb = ui.Box(prow, 1.4f, marginDp: 0);
                    pb.SetGravity(GravityFlags.CenterVertical);
                    ui.MeterIn(pb, Ui.Gold, "Ty: " + wk.Personal.Progress + " / " + wk.Personal.Target + " · " + wk.Personal.Stones + " KD", wk.Personal.Progress, wk.Personal.Target, 14);
                    if (wk.Personal.Completed && !wk.Personal.Claimed)
                        ui.Btn("Odbierz", () => Run(() => Claim(wk.Personal)), prow, sp: 12, heightDp: 30);
                    if (wk.CommunityTarget > 0)
                    {
                        var crow = ui.HRow(card, 30, 2);
                        var cb = ui.Box(crow, 1.4f, marginDp: 0);
                        cb.SetGravity(GravityFlags.CenterVertical);
                        var reached = wk.Tiers.Count(t => t.Reached);
                        ui.MeterIn(cb, Ui.Jade, "Całe Jianghu: " + wk.CommunityTotal + " / " + wk.CommunityTarget + " · próg " + reached + "/" + wk.Tiers.Count,
                            Math.Min(wk.CommunityTotal, wk.CommunityTarget), Math.Max(1, wk.CommunityTarget), 14);
                        var tier = wk.Tiers.FirstOrDefault(t => t.Reached && !t.Claimed);
                        if (tier != null && wk.MyContribution >= wk.MinContribution)
                            ui.Btn("+" + tier.Stones + " KD", () => Run(() => Claim(new QuestDto { Id = wk.Id + "_t" + tier.Tier })), crow, sp: 12, heightDp: 30);
                        else if (wk.MyContribution < wk.MinContribution)
                            ui.Text("wkład min. " + wk.MinContribution, crow, 10, Ui.Muted);
                    }
                }

                ui.Text(board.Seasonal.Count > 0 ? "WYDARZENIA SEZONOWE" : "Brak wydarzeń sezonowych — zajrzyj w święta.", page, 12, Ui.Gold, bold: true, center: true);
                var seasonal = board.Seasonal;
                ui.Pages(page, seasonal.Count, 1, 1, (i, cell) => QuestCard(cell, seasonal[i], true));
            }
        }

        private static int WeeklyClaimable(WeeklyDto w) => w?.Personal == null ? 0
            : (w.Personal.Completed && !w.Personal.Claimed ? 1 : 0) + (w.MyContribution >= w.MinContribution ? w.Tiers.Count(t => t.Reached && !t.Claimed) : 0);

        private static int RarityRank(string r) => r switch { "mythic" => 5, "legendary" => 4, "epic" => 3, "rare" => 2, _ => 1 };

        /// <summary>Titles reuse the item rarity colours.</summary>
        private static string TitlePalette(string r) => r switch { "rare" => "spirit", "epic" => "earth", "legendary" => "heavenly", "mythic" => "divine", _ => "mortal" };

        private static string TitleSourceName(string s) => s switch
        {
            "achievement" => "osiągnięcie",
            "secret" => "tajne osiągnięcie",
            "season" => "wydarzenie",
            "arena" => "arena",
            "cultivation" => "królestwo",
            "arts" => "sztuki ścieżki",
            _ => s,
        };

        // ---- Wieści -------------------------------------------------------------------------------------------

        private async Task ShowInboxAsync()
        {
            var c = _me;
            var pageDto = await _api.GetNotificationsAsync(false, 40, null, Ct);
            var ui = NewScreen("Wieści", BackToTab, Resource.Drawable.place_altar, "Z CAŁEGO JIANGHU");
            var items = pageDto.Items;
            var on = Alarms.Enabled(_prefs);
            ui.Btn(on ? "🔔 Przypomnienia na telefonie: włączone" : "🔕 Przypomnienia na telefonie: wyłączone", () =>
            {
                var next = !Alarms.Enabled(_prefs);
                _prefs.Edit().PutBoolean(Alarms.PrefKey, next).Apply();
                if (next && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && !Alarms.Allowed(this))
                    RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, Alarms.PermissionRequestCode);
                Run(async () =>
                {
                    await ShowInboxAsync();
                    SetStatus(next ? "Przypomnę o końcu odosobnienia, partiach, plonach i wyprawach." : "Przypomnienia wyłączone.");
                });
            }, ui.Content, secondary: true, sp: 12, heightDp: 34);
            ui.Btn("👥 Przyjaciele i poczta", () => Run(ShowFriendsAsync), ui.Content, secondary: true, sp: 12, heightDp: 34);
            ui.Pages(ui.Content, items.Count, 1, 5, (i, cell) =>
            {
                var n = items[i];
                var card = ui.Card(cell, 1f, 8, n.ReadAt == null ? Ui.Gold : (Color?)null, 0);
                var row = ui.HRow(card, 40, 0);
                ui.Icon(row, NotificationIcon(n.Type), 34);
                var col = ui.Box(row, 1f, marginDp: 8);
                ui.Text(Describe(n), col, 14, n.ReadAt == null ? Ui.Gold : Ui.Ink, bold: n.ReadAt == null);
                var who = PayloadString(n, "challengerName");
                ui.Text((who != null ? who + " · " : string.Empty) + n.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"), col, 11, Ui.Muted);
                if (n.Type == "pvp_challenge" && Guid.TryParse(PayloadString(n, "battleId"), out var battleId))
                    ui.Btn("Przyjmij", () => Run(async () =>
                    {
                        await _api.AcceptChallengeAsync(battleId, Ct);
                        var combat = new CombatFlow(_api);
                        await combat.OpenAsync(battleId, Ct);
                        ShowBattle(combat);
                    }), row, sp: 12, heightDp: 36);
            }, empty: "Cisza w Jianghu — brak wieści.");
            foreach (var n in items.Where(x => x.ReadAt == null)) await _api.MarkNotificationReadAsync(n.Id, Ct);
            _unread = 0;
            RenderHud();
        }
    }
}
