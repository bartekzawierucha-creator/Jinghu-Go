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
            var head = ui.HRow(page, 40, 0);
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

            // the day: three quests and the check-in seal
            var daily = ui.HRow(page, 74);
            _homeDaily = daily;

            void Render()
            {
                var st = flow.State;
                if (st == null || gen != _screen) return;
                _cultivation = st;
                RenderHud();
                realm.Text = st.RealmNamePl + " · etap " + st.Stage + "/" + st.StagesInRealm;
                var id = ui.ArtId("emblem_" + st.RealmId);
                if (id != 0) emblem.SetImageBitmap(Ui.Art(this, id));
                charge.State = st;
                charge.Invalidate();
                var deviation = st.DeviationUntil.HasValue && st.DeviationUntil > DateTimeOffset.UtcNow ? "  ·  odchylenie Qi do " + st.DeviationUntil.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
                info.Text = "Szansa przełomu: " + Math.Round(st.BreakthroughChance * 100) + "%" +
                            (st.IsMeditating ? "   ·   Medytujesz od " + st.MeditationStartedAt.Value.ToLocalTime().ToString("HH:mm") : string.Empty) +
                            "\nDziś z medytacji: " + st.DailyMeditationQi + " / " + st.DailyMeditationQiCap + deviation +
                            (steps != null ? "   ·   kroki " + st.DailySteps + "/" + st.DailyStepCap : string.Empty);
                meditate.Text = st.IsMeditating ? "Zakończ" : "Medytuj";
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
            ui.Tabs(page, new[] { "Dziś", "Osiągnięcia", "Tytuły" }, tab, i => Run(() => ShowQuestsAsync(i)),
                badges: new[] { board.Daily.Count(q => q.Completed && !q.Claimed) + (board.Checkin.CheckedInToday ? 0 : 1), claimableAch, 0 });

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
                var card = ui.Card(cell, 1f, 8, q.Completed && !q.Claimed ? Ui.Gold : (Color?)null, 0);
                var top = ui.HRow(card, 22, 0);
                ui.Text((q.Claimed ? "✓ " : string.Empty) + q.NamePl, top, 14, q.Claimed ? Ui.Muted : q.Completed ? Ui.Gold : Ui.Ink, bold: true);
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
                var list = board.Achievements.OrderBy(a => a.Claimed).ThenByDescending(a => a.Completed).ThenByDescending(a => a.Progress / (double)Math.Max(1, a.Target)).ToList();
                ui.Pages(page, list.Count, 1, 4, (i, cell) => QuestCard(cell, list[i], true));
            }
            else
            {
                if (board.Titles.Count == 0)
                {
                    ui.Box(page, 1f);
                    ui.Text("Nie masz jeszcze tytułów — zdobywa się je za osiągnięcia.", page, 14, Ui.Muted, center: true, maxLines: 3);
                    ui.Box(page, 1f);
                }
                else
                {
                    ui.Text("Razem w walce: " + StatsText(board.TitleBonuses), page, 12, Ui.Jade, bold: true, center: true, maxLines: 2);
                    ui.Pages(page, board.Titles.Count, 2, 4, (i, cell) =>
                    {
                        var card = ui.Card(cell, 1f, 8, Ui.Gold, 0);
                        card.SetGravity(GravityFlags.Center);
                        ui.Text("「" + board.Titles[i] + "」", card, 15, Ui.Gold, bold: true, center: true, maxLines: 2);
                        var bonus = board.Achievements.FirstOrDefault(a => a.TitlePl == board.Titles[i])?.Bonus;
                        if (bonus != null) ui.Text(StatsText(bonus, ", "), card, 11, Ui.Ink, center: true, maxLines: 2);
                    });
                }
            }
        }

        // ---- Wieści -------------------------------------------------------------------------------------------

        private async Task ShowInboxAsync()
        {
            var c = _me;
            var pageDto = await _api.GetNotificationsAsync(false, 40, null, Ct);
            var ui = NewScreen("Wieści", BackToTab, Resource.Drawable.place_altar, "Z CAŁEGO JIANGHU");
            var items = pageDto.Items;
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
