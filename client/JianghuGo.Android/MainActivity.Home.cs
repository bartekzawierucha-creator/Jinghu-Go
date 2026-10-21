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
            var ui = NewScreen(null, art: Resource.Drawable.bg_market, tab: TabHome);
            var page = ui.Content;
            var gen = _screen;
            var flow = new CultivationFlow(_api, c.Id);

            // realm line
            var head = ui.HRow(page, 48, 0); // the realm's hanzi use a taller CJK line
            var emblem = ui.Icon(head, "emblem_qi_condensation", 36);
            var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
            head.AddView(titles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8), Gravity = GravityFlags.CenterVertical });
            var realm = ui.Text("…", titles, 16, Ui.Gold, bold: true, maxLines: 1);
            // one line on every phone: the realm name shrinks instead of wrapping out of the header
            realm.SetAutoSizeTextTypeUniformWithConfiguration(11, 16, 1, (int)global::Android.Util.ComplexUnitType.Sp);
            realm.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(24));
            var pathLine = "Ścieżka " + PathName(c.Path) + " · " + string.Join(" · ", c.Traits.Select(t => t.NamePl));
            var sub = ui.Text(pathLine, titles, 11, Ui.Muted, maxLines: 1);
            void ShowPower() => sub.Text = (_power > 0 ? "⚔ Moc " + _power.ToString("N0") + " · " : string.Empty) + pathLine;
            ShowPower();

            // 2.2: the ring in the middle, everything else as icons on both sides of it (instead of rows of buttons)
            var mid = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            ui.AddWeighted(page, mid, 1f, 2);
            CultivationFlow flowRef = null;
            System.Action renderRef = null;
            ui.IconRow(mid, new List<(string, string, System.Action, bool, string)>
            {
                ("Opowieść", "ink_ico_journey", () => Run(() => ShowQuestsAsync(3)), _board?.Story is { Completed: true, Finished: false }, null),
                ("Osiągnięcia", "ink_ico_ranking", () => Run(() => ShowQuestsAsync(1)), _board?.Achievements.Any(q => q.Completed && !q.Claimed) == true, null),
                ("Sezon", "ink_ico_achievement", () => Run(() => ShowQuestsAsync(4)), (_board?.Pass?.Claimable ?? 0) > 0, null),
                ("Kronika", "ink_ico_event", () => Run(ShowChronicleAsync), false, null),
            }, 62, vertical: true);
            var charge = new QiChargeView(this);
            mid.AddView(charge, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
            ui.IconRow(mid, new List<(string, string, System.Action, bool, string)>
            {
                ("Rozwój", "ink_ico_meridian", () => { if (flowRef?.State != null) CultivationSheet(flowRef, renderRef); }, false, null),
                ("Odosobnienie", "ink_ico_dao", () => { if (flowRef?.State != null) RetreatSheet(flowRef, renderRef); }, false, null),
                ("Posiadłość", "ink_ico_place", () => Run(() => ShowEstateAsync(0)), false, null),
                ("Kuźnia", "ink_ico_forge", () => Run(() => ShowBagAsync(1)), false, null),
                ("Targ", "ink_ico_market", () => Run(() => ShowBagAsync(2)), false, null),
            }, 62, vertical: true);
            var info = ui.Text(string.Empty, page, 11, Ui.Muted, maxLines: 1);

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

            flowRef = flow;

            // the day: three quests and the check-in seal
            var daily = ui.HRow(page, 74);
            _homeDaily = daily;

            void Render()
            {
                var st = flow.State;
                if (st == null || gen != _screen) return;
                _cultivation = st;
                RenderHud();
                MaybeShowMilestone(st.RealmIndex);
                Music.PlayEra(st.Era);
                realm.Text = (string.IsNullOrEmpty(st.RealmHanzi) ? string.Empty : st.RealmHanzi + " ") + st.RealmNamePl + " · " + (st.StageName ?? "etap") + " " + st.Stage + "/" + st.StagesInRealm;
                var id = ui.ArtId("emblem_" + st.RealmId);
                if (id != 0) emblem.SetImageBitmap(Ui.Art(this, id));
                charge.State = st;
                charge.Invalidate();
                var deviation = st.DeviationUntil.HasValue && st.DeviationUntil > DateTimeOffset.UtcNow ? " · odchylenie do " + st.DeviationUntil.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
                var rate = st.IsInRetreat ? st.RetreatPerHour : st.IsMeditating ? st.MeditationPerHour : st.FlowPerHour;
                var mode = st.IsInRetreat ? "Odosobnienie do " + (st.RetreatUntil?.ToLocalTime().ToString("HH:mm") ?? "?") : st.IsMeditating ? "Medytacja ×5" : "Qi samo ×1";
                // one line: small screens (Nexus 5) have room for exactly one here
                info.Text = mode + " · " + Math.Round(rate) + " Qi/h" + (st.WeekendPermille > 1000 ? " · weekend ×" + (st.WeekendPermille / 1000.0).ToString("0.#") : string.Empty)
                            + " · " + st.Qi + "/" + st.Vessel +
                            (st.TribulationRequired ? " · czeka Próba Niebios" : st.BottleneckItem != null && !st.IsPeak && st.Stage == 11 ? " · potrzebna pigułka" :
                                " · przełom " + Math.Round(st.BreakthroughChance * 100) + "%") + deviation +
                            (steps != null ? " · kroki " + st.DailySteps + "/" + st.DailyStepCap : string.Empty);
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
                    ShowBanner("PRZEŁOM!", flow.State.RealmNamePl + " · etap " + flow.State.Stage, "+1 punkt meridianu do otwarcia", "ink_popup_breakthrough");
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
                // one tap for every reward waiting: the check-in, finished daily quests and achievements
                var waiting = board.Daily.Count(q => q.Completed && !q.Claimed) + board.Achievements.Count(q => q.Completed && !q.Claimed) + (board.Checkin.CheckedInToday ? 0 : 1);
                if (waiting >= 2)
                {
                    var all = ui.Card(daily, 0.9f, 6, Ui.Cinnabar);
                    all.Clickable = true;
                    all.SetGravity(GravityFlags.Center);
                    ui.Text("Odbierz", all, 12, Ui.Cinnabar, bold: true, center: true);
                    ui.Text("wszystko (" + waiting + ")", all, 10, Ui.Ink, center: true);
                    all.Click += (_, __) => Run(async () =>
                    {
                        long stones = 0;
                        var b = board;
                        if (!b.Checkin.CheckedInToday)
                        {
                            var ci = await _api.CheckinAsync(c.Id, Ct);
                            stones += ci.Stones;
                            b = ci.Board;
                            _balance = ci.Balance;
                        }

                        foreach (var q in b.Daily.Concat(b.Achievements).Where(q => q.Completed && !q.Claimed).ToList())
                        {
                            var r = await _api.ClaimQuestAsync(c.Id, q.Id, Ct);
                            stones += r.Stones;
                            b = r.Board;
                            _balance = r.Balance;
                        }

                        _board = b;
                        Sfx.Play(Sfx.Coin);
                        RenderHud();
                        RenderDaily(b);
                        ShowBanner("NAGRODY", "Odebrano wszystko", "+" + stones + " kamieni duchowych", "ink_popup_chest");
                    });
                }

                foreach (var q in board.Daily)
                {
                    var card = ui.Card(daily, 1f, 6, q.Completed && !q.Claimed ? Ui.Gold : (Color?)null);
                    card.Clickable = true;
                    var quest = q;
                    // done: claim right here; not yet: go straight to where it is done
                    card.Click += (_, __) => Run(async () =>
                    {
                        if (quest.Completed && !quest.Claimed)
                        {
                            var r = await _api.ClaimQuestAsync(c.Id, quest.Id, Ct);
                            Sfx.Play(Sfx.Coin);
                            _board = r.Board;
                            _balance = r.Balance;
                            RenderHud();
                            RenderDaily(r.Board);
                            SetStatus(quest.NamePl + ": +" + r.Stones + " kamieni duchowych");
                        }
                        else if (quest.Claimed) await ShowQuestsAsync();
                        else await GoForAsync(quest.Metric);
                    });
                    ui.Text((q.Claimed ? "✓ " : string.Empty) + q.NamePl, card, 11, q.Completed ? Ui.Gold : Ui.Ink, bold: true);
                    ui.MeterIn(card, q.Completed ? Ui.Gold : Ui.Jade, q.Progress + "/" + q.Target, q.Progress, q.Target, 14);
                    ui.Text(q.Completed && !q.Claimed ? "Odbierz!" : q.Claimed ? "✓" : "Idź ›  +" + q.Stones, card, 10, q.Completed && !q.Claimed ? Ui.Cinnabar : Ui.Muted, bold: true);
                }

                if (!board.Checkin.CheckedInToday && waiting < 2) // with "claim all" the check-in goes with it
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

            renderRef = Render;
            RenderDaily(_board);
            Run(async () =>
            {
                await flow.LoadAsync(Ct);
                Render();
                ShowOfflineSummary(flow, Render);
                if (_power == 0) _power = (await _api.GetCombatScoutAsync(c.Id, Ct)).MyPower;
                if (gen == _screen) ShowPower();
                RenderDaily(await _api.GetQuestsAsync(c.Id, Ct));
            });
        }

        // ---- Zadania ------------------------------------------------------------------------------------------

        private Task ShowQuestsAsync() => ShowQuestsAsync(0);

        private int _questPage;

        private async Task ShowQuestsAsync(int tab)
        {
            var c = _me;
            var board = await _api.GetQuestsAsync(c.Id, Ct);
            _board = board;
            // 2.2: three tabs; the story (with weekly and seasonal events) and the season pass open from the home icons
            var title = tab == 3 ? "Opowieść" : tab == 4 ? "Sezon" : "Zadania";
            var ui = NewScreen(title, BackToTab, Resource.Drawable.tile_menu_quests, tab >= 3 ? "WYDARZENIA I NAGRODY" : "DZIEŃ " + board.Day);
            var page = ui.Content;
            var claimableAch = board.Achievements.Count(a => a.Completed && !a.Claimed);
            if (tab <= 2)
                ui.Tabs(page, new[] { "Dziś", "Osiągnięcia", "Tytuły" }, tab, i => Run(() => ShowQuestsAsync(i)),
                    badges: new[] { board.Daily.Count(q => q.Completed && !q.Claimed) + (board.Checkin.CheckedInToday ? 0 : 1), claimableAch, 0 });

            Pager listPager = null;
            async Task Claim(QuestDto q)
            {
                // stay on the same page of the list after claiming
                _questPage = listPager?.Page ?? 0;
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
                else if (!q.Completed && !secretHidden && GoesSomewhere(q.Metric)) ui.Btn("Idź", () => Run(() => GoForAsync(q.Metric)), bottom, secondary: true, sp: 12, heightDp: 30);
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
                listPager = ui.Pages(page, list.Count, 1, 4, (i, cell) => QuestCard(cell, list[i], true), startPage: Math.Min(_questPage, Math.Max(0, (list.Count - 1) / 4)));
                _questPage = 0;
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
            else if (tab == 4)
            {
                var pass = board.Pass;
                if (pass == null)
                {
                    ui.Text("Przepustka sezonu jest nieczynna.", page, 13, Ui.Muted, center: true);
                }
                else
                {
                    var head = ui.Card(page, 0f, 8, pass.Claimable > 0 ? Ui.Gold : Ui.Jade);
                    head.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(118)) { TopMargin = Px(6) };
                    ui.Text("Sezon " + pass.Season + " · do " + pass.EndsOn + " · Żetony Losu: " + board.DestinyTokens, head, 12, Ui.Gold, bold: true);
                    var inLevel = pass.Level >= pass.Levels ? pass.XpPerLevel : pass.Xp % pass.XpPerLevel;
                    ui.MeterIn(head, Ui.Gold, "Poziom " + pass.Level + " / " + pass.Levels + " · " + inLevel + " / " + pass.XpPerLevel + " PD", inLevel, pass.XpPerLevel, 16);
                    ui.Text("PD za: walki, medytację, rzemiosło, zbiory, wieżę, wyprawy, przełomy, kroki i meldunki. Sezon zeruje się co kwartał.", head, 10, Ui.Muted, maxLines: 2);
                    if (pass.Claimable > 0)
                        ui.Btn("Odbierz nagrody (" + pass.Claimable + ")", () => Run(async () =>
                        {
                            var r = await _api.ClaimQuestAsync(c.Id, "pass", Ct);
                            Sfx.Play(Sfx.Coin);
                            _board = r.Board;
                            _balance = r.Balance;
                            await ShowQuestsAsync(4);
                            ShowBanner("PRZEPUSTKA SEZONU", "Nagrody odebrane", "+" + r.Stones + " kamieni", "menu_quests");
                        }), page, sp: 13, heightDp: 38);
                    var rewards = pass.Rewards;
                    var startAt = Math.Max(0, Math.Min(pass.Level, rewards.Count - 1));
                    ui.Pages(page, rewards.Count, 1, 5, (i, cell) =>
                    {
                        var r = rewards[i];
                        var row = ui.Card(cell, 1f, 6, r.Reached && !r.Claimed ? Ui.Gold : (Color?)null, 0);
                        var extra = (r.ItemNamePl != null ? " + " + r.ItemQuantity + "× " + r.ItemNamePl : string.Empty) + (r.Tokens > 0 ? " + " + r.Tokens + " Żeton Losu" : string.Empty);
                        ui.Text((r.Claimed ? "✓ " : string.Empty) + "Poziom " + r.Level + ": " + r.Stones + " KD" + extra, row, 12, r.Claimed ? Ui.Muted : r.Reached ? Ui.Gold : Ui.Ink, bold: r.Tokens > 0 || r.ItemNamePl != null);
                    }, startPage: startAt / 5);
                }
            }
            else
            {
                if (board.Story is { } st)
                {
                    var story = ui.Card(page, 1.4f, 10, st.Completed && !st.Finished ? Ui.Gold : Ui.GoldDim);
                    ui.Text("Rozdział " + st.Chapter + " z " + st.Chapters + ": " + st.TitlePl, story, 14, Ui.Gold, bold: true);
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
                    ui.Text("Wydarzenie tygodnia: " + wk.NamePl + " (do " + wk.Until.Substring(5) + ")", card, 12, Ui.Gold, bold: true);
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

        /// <summary>Quests that point to one place in the game.</summary>
        private static bool GoesSomewhere(string metric) => metric is "battles_won" or "bosses_won" or "tower_won" or "arena_won" or "pvp_won" or "crafts" or "upgrades"
            or "items_gathered" or "world_qi" or "steps" or "meditation_minutes" or "breakthroughs" or "pills_used" or "world_boss_fights" or "spirits_won"
            or "harvests" or "expeditions" or "realm" or "night_wins" or "full_moon_wins" or "market_sales";

        /// <summary>Opens the screen where a quest's metric is advanced (a training list, the tower, the forge, the map...).</summary>
        private async Task GoForAsync(string metric)
        {
            switch (metric)
            {
                case "battles_won":
                case "night_wins":
                case "full_moon_wins":
                    _combatSub = 0;
                    _tab = TabCombat;
                    await ShowCombatTabAsync(0);
                    break;
                case "tower_won":
                    _tab = TabCombat;
                    await ShowCombatTabAsync(2);
                    break;
                case "bosses_won":
                case "world_boss_fights":
                    _tab = TabCombat;
                    await ShowCombatTabAsync(3);
                    break;
                case "pvp_won":
                    _tab = TabCombat;
                    await ShowCombatTabAsync(4);
                    break;
                case "arena_won":
                    _tab = TabCombat;
                    await ShowCombatTabAsync(5);
                    break;
                case "crafts":
                case "upgrades":
                    await ShowBagAsync(1);
                    break;
                case "pills_used":
                    await ShowBagAsync(0);
                    break;
                case "market_sales":
                    await ShowBagAsync(2);
                    break;
                case "items_gathered":
                case "world_qi":
                case "steps":
                    GoTab(TabWorld);
                    break;
                case "spirits_won":
                    _tab = TabHero;
                    await ShowArtsAsync(0);
                    break;
                case "harvests":
                case "expeditions":
                    await ShowEstateAsync(metric == "harvests" ? 0 : 2);
                    break;
                default:
                    GoTab(TabHome);
                    SetStatus(metric == "meditation_minutes" ? "Medytuj — każda minuta się liczy." : "Zbieraj Qi i spróbuj przełomu.");
                    break;
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

        /// <summary>Sound, music and phone reminders (2.0).</summary>
        private void ShowSettingsSheet()
        {
            var body = Sheet("Ustawienia", 0.8f, out var close, "menu_notifications");
            void Toggle(string label, bool on, System.Action flip)
            {
                _ui.Btn(label + ": " + (on ? "włączone" : "wyłączone"), () =>
                {
                    flip();
                    close();
                    ShowSettingsSheet();
                }, body, secondary: !on, sp: 13, heightDp: 42);
            }

            Toggle("Dźwięki", !Sfx.Muted, () =>
            {
                Sfx.Muted = !Sfx.Muted;
                if (Sfx.Muted) Music.Stop();
                else Music.Enabled = Music.Enabled;
            });
            Toggle("Muzyka er", Music.Enabled, () => Music.Enabled = !Music.Enabled);
            Toggle("Przypomnienia na telefonie", Alarms.Enabled(_prefs), () =>
            {
                var next = !Alarms.Enabled(_prefs);
                _prefs.Edit().PutBoolean(Alarms.PrefKey, next).Apply();
                if (next && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && !Alarms.Allowed(this))
                    RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, Alarms.PermissionRequestCode);
            });
            var acct = _ui.HRow(body, 40, 4);
            _ui.Btn("Kod odzyskiwania", () => Run(async () =>
            {
                var code = await _api.NewRecoveryCodeAsync(Ct);
                close();
                var sheet = Sheet("Kod odzyskiwania", 0.45f, out _, "menu_settings");
                var t = _ui.Text(code, sheet, 22, Ui.Gold, bold: true, center: true);
                t.SetTextIsSelectable(true);
                _ui.Text("Zapisz go w bezpiecznym miejscu — pokazujemy go tylko raz. Z nim i e-mailem ustawisz nowe hasło. Nowy kod unieważnia poprzedni.",
                    sheet, 12, Ui.Ink, center: true, maxLines: 4);
            }), acct, secondary: true, sp: 12, heightDp: 40);
            _ui.Btn("Zmień hasło", () =>
            {
                close();
                var sheet = Sheet("Zmień hasło", 0.5f, out var closeSheet, "menu_settings");
                var cur = _ui.Field("Obecne hasło", password: true, parent: sheet);
                var next = _ui.Field("Nowe hasło (co najmniej 10 znaków)", password: true, parent: sheet);
                _ui.Box(sheet, 1f, marginDp: 0);
                _ui.Btn("Zmień", () => Run(async () =>
                {
                    await _api.ChangePasswordAsync(cur.Text, next.Text, Ct);
                    closeSheet();
                    SetStatus("Hasło zmienione — inne urządzenia zostały wylogowane.");
                }), sheet, sp: 14, heightDp: 44);
            }, acct, secondary: true, sp: 12, heightDp: 40);
            var gdpr = _ui.HRow(body, 40, 4);
            _ui.Btn("Pobierz moje dane", () => Run(async () =>
            {
                var json = await _api.ExportAsync(Ct);
                var where = SaveExport(json);
                SetStatus("Dane zapisane: " + where);
            }), gdpr, secondary: true, sp: 12, heightDp: 40);
            _ui.Btn("Usuń konto", () =>
            {
                close();
                var sheet = Sheet("Usuń konto", 0.5f, out var closeSheet, "menu_settings");
                _ui.Text("Konto zostanie zamknięte na zawsze: postacie stracą imiona, wiadomości i listy znikną, nie zalogujesz się ponownie.", sheet, 12, Ui.Cinnabar, maxLines: 4);
                var pass = _ui.Field("Hasło, aby potwierdzić", password: true, parent: sheet);
                _ui.Box(sheet, 1f, marginDp: 0);
                _ui.Btn("Usuń na zawsze", () => Run(async () =>
                {
                    await _api.DeleteAccountAsync(pass.Text, Ct);
                    closeSheet();
                    await _api.LogoutAsync(Ct);
                    ShowLogin();
                    SetStatus("Konto usunięte.");
                }), sheet, sp: 14, heightDp: 44);
            }, gdpr, secondary: true, sp: 12, heightDp: 40);
            _ui.Text("Przypomnienia: koniec odosobnienia, gotowe partie w kotle i kuźni, plony, powrót uczniów i koniec polowania. Nic nie opuszcza telefonu.",
                body, 11, Ui.Muted, maxLines: 3);
            _ui.Text("Ikony: game-icons.net (Lorc, Delapouite i in.), CC BY 3.0 · Pogoda: Open-Meteo · Miejsca: © OpenStreetMap", body, 9, Ui.Muted, center: true, maxLines: 2);
            _ui.Text("Jianghu Go " + (PackageManager?.GetPackageInfo(PackageName, 0)?.VersionName ?? string.Empty), body, 10, Ui.Muted, center: true);
        }

        /// <summary>Writes the data export to Downloads (Android 10+) or the app's own files folder; returns where.</summary>
        private string SaveExport(string json)
        {
            var name = "jianghu-go-dane-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json";
            var bytes = System.Text.Encoding.UTF8.GetBytes(json ?? "{}");
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                var values = new global::Android.Content.ContentValues();
                values.Put(global::Android.Provider.MediaStore.IMediaColumns.DisplayName, name);
                values.Put(global::Android.Provider.MediaStore.IMediaColumns.MimeType, "application/json");
                values.Put(global::Android.Provider.MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);
                var uri = ContentResolver.Insert(global::Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
                using (var stream = ContentResolver.OpenOutputStream(uri))
                    stream.Write(bytes, 0, bytes.Length);
                return "Pobrane/" + name;
            }

            var dir = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir.AbsolutePath;
            var path = System.IO.Path.Combine(dir, name);
            System.IO.File.WriteAllBytes(path, bytes);
            return path;
        }

        /// <summary>The Jianghu chronicle: notable deeds of all cultivators (2.3).</summary>
        private async Task ShowChronicleAsync()
        {
            var news = (await _api.GetNewsAsync(Ct)).Items;
            var body = Sheet("Kronika Jianghu", 0.8f, out _, "menu_notifications");
            _ui.Pages(body, news.Count, 1, 6, (i, cell) =>
            {
                var n = news[i];
                var card = _ui.Card(cell, 1f, 6, null, 0);
                _ui.Text(n.TextPl, card, 12, Ui.Ink, maxLines: 2);
                _ui.Text(n.At.ToLocalTime().ToString("dd.MM HH:mm"), card, 10, Ui.Muted);
            }, empty: "Kronika jeszcze milczy.");
        }

        private async Task ShowInboxAsync()
        {
            var c = _me;
            var pageDto = await _api.GetNotificationsAsync(false, 40, null, Ct);
            var ui = NewScreen("Wieści", BackToTab, Resource.Drawable.place_altar, "Z CAŁEGO JIANGHU");
            var items = pageDto.Items;
            var bar = ui.HRow(ui.Content, 34, 4);
            ui.Btn("Ustawienia", ShowSettingsSheet, bar, secondary: true, sp: 12, heightDp: 34, icon: "gi_settings");
            ui.Btn("Przyjaciele", () => Run(ShowFriendsAsync), bar, secondary: true, sp: 12, heightDp: 34, icon: "gi_friends");
            ui.Btn("Kronika", () => Run(ShowChronicleAsync), bar, secondary: true, sp: 12, heightDp: 34, icon: "gi_chronicle");
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
