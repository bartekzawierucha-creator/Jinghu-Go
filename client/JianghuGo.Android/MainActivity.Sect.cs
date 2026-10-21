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
    /// <summary>Sect hub: the sect, its members, territories and wars, and the leaderboards.</summary>
    public sealed partial class MainActivity
    {
        private async Task ShowSectTabAsync(int tab, string metric = null)
        {
            var c = _me;
            var flow = new SectFlow(_api, c.Id);
            await flow.LoadAsync(Ct);
            if (tab == 3)
            {
                await ShowRankingAsync(flow, metric);
                return;
            }

            if (tab == 4)
            {
                await ShowSectDevelopmentAsync(flow);
                return;
            }

            // 2.2: the sect is one screen with icons; members and territories open as their own screens
            var mine = flow.Mine;
            var ui = tab == 0
                ? NewScreen(null, art: Resource.Drawable.bg_sect, tab: TabSect)
                : NewScreen(tab == 1 ? "Członkowie" : "Terytoria", () => Run(() => ShowSectTabAsync(0)), Resource.Drawable.bg_sect, tab == 1 ? "TWOJA SEKTA" : "ZIEMIE I WOJNY", tab: TabSect);
            var page = ui.Content;
            if (tab == 0)
            {
                var needSect = mine == null ? "w sekcie" : null;
                ui.IconRow(page, new List<(string, string, System.Action, bool, string)>
                {
                    ("Członkowie", "ink_nav_social", () => Run(() => ShowSectTabAsync(1)), (mine?.PendingApplications?.Count ?? 0) > 0, needSect),
                    ("Terytoria", "ink_ico_place", () => Run(() => ShowSectTabAsync(2)), (flow.Territories?.Wars?.Count ?? 0) > 0, null),
                    ("Ranking", "ink_ico_ranking", () => Run(() => ShowSectTabAsync(3)), false, null),
                    ("Rozwój", "ink_nav_sect", () => Run(() => ShowSectTabAsync(4)), false, needSect),
                }, 58);
            }

            async Task Reload(int t, string message = null)
            {
                await ShowSectTabAsync(t);
                if (message != null) SetStatus(message);
                RefreshHud();
            }

            if (mine == null)
            {
                // no sect: found one or apply
                var found = ui.Card(page, 0f, 10, Ui.Gold);
                found.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(150)) { TopMargin = Px(6) };
                ui.Text("Nie należysz do żadnej sekty", found, 15, Ui.Gold, bold: true);
                ui.Text("Załóż własną (opłata w KD) albo poproś o przyjęcie do istniejącej.", found, 12, Ui.Muted, maxLines: 2);
                var name = ui.Field("Nazwa nowej sekty", parent: found);
                ui.Btn("Załóż sektę", () => Run(async () =>
                {
                    await flow.FoundAsync(name.Text.Trim(), Ct);
                    Sfx.Play(Sfx.Breakthrough, 0.6f);
                    await Reload(0, "Sekta założona.");
                }), page, sp: 14, heightDp: 44, icon: "gi_pagoda");

                // what joining gives, so the screen says why it matters instead of standing empty
                var why = ui.Card(page, 0f, 10, null);
                ui.Text("Co daje sekta", why, 14, Ui.Ink, bold: true);
                foreach (var line in new[]
                {
                    "Bossy sekty — wspólna walka i nagrody dla wszystkich",
                    "Terytoria — zajmujcie miejsca mocy i toczcie wojny",
                    "Rozwój — budynki sekty dają premie każdemu członkowi",
                })
                {
                    ui.Text("·  " + line, why, 12, Ui.Muted, maxLines: 2);
                }

                var sects = (await flow.BrowseAsync(Ct)).Items;
                var pending = (await _api.MyApplicationsAsync(c.Id, Ct)).Items;
                ui.Pages(page, sects.Count, 1, 4, (i, cell) =>
                {
                    var s = sects[i];
                    var mineApp = pending.FirstOrDefault(a => a.SectId == s.Id);
                    var card = ui.Card(cell, 1f, 6, mineApp != null ? Ui.Gold : (Color?)null, 0);
                    var row = ui.HRow(card, 42, 0);
                    ui.Icon(row, "menu_sect", 34);
                    ui.Text(s.Name + "  ·  " + s.MemberCount + " członków", row, 13, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8), Gravity = GravityFlags.CenterVertical };
                    ui.Btn(mineApp != null ? "Wycofaj" : "Poproś", () => Run(async () =>
                    {
                        if (mineApp != null)
                        {
                            await _api.WithdrawApplicationAsync(c.Id, mineApp.Id, Ct);
                            await Reload(0, "Wycofano prośbę do " + s.Name + ".");
                            return;
                        }

                        await flow.ApplyAsync(s.Id, "Proszę o przyjęcie.", Ct);
                        await Reload(0, "Wysłano prośbę do " + s.Name + ".");
                    }), row, secondary: true, sp: 12, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(84), Px(36)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Nie ma jeszcze żadnej sekty — bądź pierwszy.");
                return;
            }

            if (tab == 0)
            {
                var card = ui.Card(page, 1f, 12, Ui.Gold);
                var row = ui.HRow(card, 56, 0);
                ui.Icon(row, "emblem_sect_crane", 54);
                var col = ui.Box(row, 1f, marginDp: 10);
                ui.Text(mine.Sect.Name, col, 20, Ui.Gold, bold: true);
                ui.Text("Twoja ranga: " + RoleName(mine.MyRole), col, 12, Ui.Muted);
                var stats = ui.HRow(card, 56, 8);
                Stat(ui, stats, "Skarbiec", mine.Treasury + " KD");
                Stat(ui, stats, "Członkowie", mine.Sect.MemberCount + "/" + mine.Sect.MaxMembers);
                Stat(ui, stats, "Terytoria", flow.Territories != null ? flow.Territories.Owned + "/" + flow.Territories.Max : "—");
                ui.Box(card, 1f, marginDp: 0);
                if (mine.PendingApplications != null && mine.PendingApplications.Count > 0)
                    ui.Btn("Prośby o przyjęcie (" + mine.PendingApplications.Count + ")", () =>
                    {
                        var body = Sheet("Prośby o przyjęcie", 0.55f, out var close, "menu_sect");
                        var apps = mine.PendingApplications;
                        ui.Pages(body, apps.Count, 1, 4, (i, cell) =>
                        {
                            var a = apps[i];
                            var r = ui.HRow(cell, 42, 0);
                            ui.Text(a.CharacterName, r, 14, Ui.Ink, bold: true);
                            ui.Btn("✔", () => Run(async () =>
                            {
                                await flow.DecideAsync(a, true, Ct);
                                close();
                                await Reload(0, a.CharacterName + " przyjęty.");
                            }), r, sp: 14, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(56), Px(38));
                            ui.Btn("✘", () => Run(async () =>
                            {
                                await flow.DecideAsync(a, false, Ct);
                                close();
                                await Reload(0);
                            }), r, secondary: true, sp: 14, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(56), Px(38)) { LeftMargin = Px(6) };
                        });
                    }, card, sp: 13, heightDp: 42);
                ui.Btn("Czat sekty", () => OpenSectChat(mine.Sect.Id), page, sp: 14, heightDp: 44, icon: "menu_chat");
                var acts = ui.HRow(page, 44);
                ui.Btn("Wpłać 100 KD", () => Run(async () =>
                {
                    await flow.DonateAsync(100, Ct);
                    Sfx.Play(Sfx.Coin);
                    await Reload(0, "Dziękujemy za wkład.");
                }), acts, sp: 12, heightDp: 44, icon: "menu_donate");
                if (mine.MyRole == "leader")
                    ui.Btn("Siedziba tutaj", () => Run(async () =>
                    {
                        await _api.SetSeatAsync(c.Id, mine.Sect.Id, Ct);
                        await Reload(0, "Siedziba sekty ustawiona w twojej okolicy.");
                    }), acts, secondary: true, sp: 12, heightDp: 44);
                var confirm = false;
                Button leave = null;
                leave = ui.Btn("Opuść", () =>
                {
                    if (!confirm)
                    {
                        confirm = true;
                        leave.Text = "Na pewno?";
                        return;
                    }

                    Run(async () =>
                    {
                        await flow.LeaveAsync(Ct);
                        await Reload(0, "Opuściłeś sektę.");
                    });
                }, acts, secondary: true, sp: 12, heightDp: 44);
            }
            else if (tab == 1)
            {
                var members = mine.Sect.Members.OrderBy(m => m.Role == "leader" ? 0 : m.Role == "elder" ? 1 : 2).ThenByDescending(m => m.Contribution).ToList();
                ui.Pages(page, members.Count, 1, 5, (i, cell) =>
                {
                    var m = members[i];
                    var card = ui.Card(cell, 1f, 6, m.CharacterId == c.Id ? Ui.Gold : (Color?)null, 0);
                    card.Clickable = true;
                    var row = ui.HRow(card, 40, 0);
                    ui.Icon(row, m.Role == "leader" ? "emblem_sect_tiger" : "menu_sect", 32);
                    var col = ui.Box(row, 1f, marginDp: 8);
                    ui.Text(m.Name + (m.CharacterId == c.Id ? " (ty)" : string.Empty), col, 14, Ui.Ink, bold: true);
                    ui.Text(RoleName(m.Role) + " · wkład " + m.Contribution, col, 11, Ui.Muted);
                    if (m.CharacterId == c.Id) return;
                    card.Click += (_, __) =>
                    {
                        var body = Sheet(m.Name, 0.42f, out var close, "menu_sect");
                        ui.Text(RoleName(m.Role) + " · wkład " + m.Contribution, body, 13, Ui.Muted);
                        ui.Box(body, 1f, marginDp: 0);
                        ui.Btn("Wyzwij na pojedynek", () => Run(async () =>
                        {
                            close();
                            var battle = await _api.ChallengeAsync(c.Id, m.CharacterId, Ct);
                            var combat = new CombatFlow(_api);
                            await combat.OpenAsync(battle.Id, Ct);
                            ShowBattle(combat);
                        }), body, sp: 14, heightDp: 44, icon: "menu_battle");
                        var outranks = mine.MyRole == "leader" || (mine.MyRole == "elder" && m.Role == "member");
                        var r2 = ui.HRow(body, 42);
                        if (mine.MyRole == "leader")
                        {
                            var newRole = m.Role == "elder" ? "member" : "elder";
                            ui.Btn(newRole == "elder" ? "Mianuj starszym" : "Zdegraduj", () => Run(async () =>
                            {
                                await _api.SetRoleAsync(c.Id, mine.Sect.Id, m.CharacterId, newRole, Ct);
                                close();
                                await Reload(1);
                            }), r2, secondary: true, sp: 12, heightDp: 42);
                            ui.Btn("Przekaż przywództwo", () => Run(async () =>
                            {
                                await _api.SetRoleAsync(c.Id, mine.Sect.Id, m.CharacterId, "leader", Ct);
                                close();
                                await Reload(1);
                            }), r2, secondary: true, sp: 12, heightDp: 42);
                        }

                        if (outranks)
                            ui.Btn("Wyrzuć z sekty", () => Run(async () =>
                            {
                                await _api.KickAsync(c.Id, mine.Sect.Id, m.CharacterId, Ct);
                                close();
                                await Reload(1, m.Name + " opuścił sektę.");
                            }), body, secondary: true, sp: 12, heightDp: 42);
                    };
                }, empty: "Sekta nie ma jeszcze członków.");
            }
            else
            {
                var t = flow.Territories;
                var info = ui.Card(page, 0f, 10);
                info.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(70)) { TopMargin = Px(6) };
                ui.Text("Terytoria: " + (t != null ? t.Owned + "/" + t.Max : "—"), info, 15, Ui.Gold, bold: true);
                ui.Text(t != null && t.Wars.Count > 0 ? "Wojny: " + string.Join(", ", t.Wars.Select(w => w.TerritoryKey + " " + w.Phase + " " + w.AttackerScore + ":" + w.DefenderScore)) : "Brak wojen.", info, 11, Ui.Muted, maxLines: 2);
                var acts = ui.HRow(page, 44);
                ui.Btn("Tutaj?", () => Run(async () =>
                {
                    var here = await flow.HereAsync(Ct);
                    SetStatus(here.Key + ": " + (here.OwnerName ?? "niczyje") + (here.War != null ? " — wojna (" + here.War.Phase + ")" : string.Empty));
                }), acts, secondary: true, sp: 12, heightDp: 44, icon: "emblem_territory");
                if (mine.CanManage)
                {
                    ui.Btn("🚩 Zajmij", () => Run(async () =>
                    {
                        var claimed = await flow.ClaimHereAsync(Ct);
                        Sfx.Play(Sfx.Chime);
                        await Reload(2, "Zajęto " + claimed.Key + ".");
                    }), acts, sp: 12, heightDp: 44);
                    ui.Btn("Wojna", () => Run(async () =>
                    {
                        var w = await flow.ChallengeHereAsync(Ct);
                        await Reload(2, "Wojna o " + w.TerritoryKey + " od " + w.StartsAt.ToLocalTime().ToString("dd.MM HH:mm") + ".");
                    }), acts, sp: 12, heightDp: 44, icon: "emblem_war");
                }

                var owned = t?.Territories ?? new List<TerritoryDto>();
                ui.Pages(page, owned.Count, 1, 4, (i, cell) =>
                {
                    var territory = owned[i];
                    var card = ui.Card(cell, 1f, 6, null, 0);
                    var row = ui.HRow(card, 40, 0);
                    ui.Icon(row, "emblem_territory", 32);
                    ui.Text(territory.Key + (territory.War != null ? "  ·  wojna" : string.Empty), row, 13, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8), Gravity = GravityFlags.CenterVertical };
                    if (mine.CanManage)
                        ui.Btn("Danina", () => Run(async () =>
                        {
                            await flow.CollectTributeAsync(territory, Ct);
                            Sfx.Play(Sfx.Coin);
                            await Reload(2, "Danina zebrana.");
                        }), row, secondary: true, sp: 12, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(80), Px(36)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Sekta nie ma jeszcze terytoriów — zajmij to, na którym stoisz.");
            }
        }

        /// <summary>
        /// Sect chat in a sheet: the latest lines (no scrolling, newest at the bottom), a field and Send; while the sheet
        /// is open it polls for new lines every few seconds.
        /// </summary>
        private void OpenSectChat(Guid sectId)
        {
            var c = _me;
            var body = Sheet("Czat sekty", 0.72f, out var close, "menu_chat");
            var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
            list.SetGravity(GravityFlags.Bottom);
            body.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            var row = _ui.HRow(body, 46, 6);
            var field = _ui.Field("Napisz do sekty…", parent: row);
            field.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
            field.SetFilters(new global::Android.Text.IInputFilter[] { new global::Android.Text.InputFilterLengthFilter(300) });
            var lines = new List<ChatMessageDto>();
            const int MaxShown = 9;

            void Render()
            {
                list.RemoveAllViews();
                if (lines.Count == 0) _ui.Text("Cisza w sekcie. Napisz pierwszy!", list, 12, Ui.Muted, center: true);
                foreach (var m in lines.Skip(Math.Max(0, lines.Count - MaxShown)))
                {
                    var mine = m.CharacterId == c.Id;
                    var t = _ui.Text(m.Name + "  " + m.At.ToLocalTime().ToString("HH:mm") + "\n" + m.Text, list, 12, mine ? Ui.Gold : Ui.Ink, maxLines: 3);
                    var bg = new global::Android.Graphics.Drawables.GradientDrawable();
                    bg.SetColor(mine ? Color.Argb(60, 200, 160, 80) : Color.Argb(110, 244, 240, 231));
                    bg.SetCornerRadius(Px(10));
                    t.Background = bg;
                    t.SetPadding(Px(8), Px(4), Px(8), Px(4));
                    ((LinearLayout.LayoutParams)t.LayoutParameters).TopMargin = Px(4);
                }
            }

            async Task Pull()
            {
                var page = await _api.GetSectChatAsync(sectId, c.Id, lines.Count > 0 ? lines[lines.Count - 1].Id : (Guid?)null, Ct);
                if (page.Items.Count == 0) return;
                lines.AddRange(page.Items);
                if (lines.Count > 60) lines.RemoveRange(0, lines.Count - 60);
                Render();
            }

            _ui.Btn("Wyślij", () => Run(async () =>
            {
                var text = field.Text?.Trim();
                if (string.IsNullOrEmpty(text)) return;
                await _api.PostSectChatAsync(sectId, c.Id, text, Ct);
                field.Text = string.Empty;
                Sfx.Play(Sfx.Click, 0.5f);
                await Pull();
            }), row, sp: 13, heightDp: 46).LayoutParameters = new LinearLayout.LayoutParams(Px(92), ViewGroup.LayoutParams.MatchParent) { LeftMargin = Px(6) };

            Render();
            async void Poll()
            {
                for (var wait = 0; wait < 20 && !body.IsAttachedToWindow; wait++) await Task.Delay(100);
                while (body.IsAttachedToWindow)
                {
                    try
                    {
                        if (!_paused) await Pull();
                        await Task.Delay(TimeSpan.FromSeconds(4), Ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                    catch (ApiException)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(8));
                    }
                }
            }

            Poll();
        }

        private void Stat(Ui ui, LinearLayout row, string label, string value)
        {
            var cell = ui.Box(row, 1f, marginDp: 6);
            cell.SetGravity(GravityFlags.Center);
            ui.Text(value, cell, 16, Ui.Ink, bold: true, center: true);
            ui.Text(label, cell, 10, Ui.Muted, center: true);
        }

        // ---- Ranking: podium and pages ------------------------------------------------------------------------

        private async Task ShowRankingAsync(SectFlow flow, string metric)
        {
            var c = _me;
            var entries = new List<(int Rank, Guid Id, string Name, string Path, string Value)>();
            (int Rank, string Value)? me = null;
            if (metric == null)
            {
                var r = await _api.GetRankingAsync(c.Id, Ct);
                entries = r.Top.Select(e => (e.Rank, e.CharacterId, e.Name, e.Path, e.RealmNamePl + " " + e.Stage)).ToList();
                if (r.Me != null) me = (r.Me.Rank, r.Me.RealmNamePl + ", etap " + r.Me.Stage);
            }
            else
            {
                var p = await _api.GetLeadersAsync(c.Id, metric, Ct);
                entries = p.Items.Select(e => (e.Rank, e.CharacterId, e.Name, e.Path, e.Value.ToString())).ToList();
                var mineRow = entries.FirstOrDefault(e => e.Id == c.Id);
                if (mineRow.Name != null) me = (mineRow.Rank, mineRow.Value);
            }

            var ui = NewScreen("Ranking", () => Run(() => ShowSectTabAsync(0)), Resource.Drawable.tile_menu_ranking, "NAJLEPSI W JIANGHU", tab: TabSect);
            var page = ui.Content;

            var metrics = new[] { ((string)null, "Kultywacja"), ("pvp_won", "Pojedynki"), ("bosses_won", "Bossowie"), ("battles_won", "Zwycięstwa") };
            ui.Tabs(page, metrics.Select(m => m.Item2).ToArray(), Array.FindIndex(metrics, m => m.Item1 == metric), i => Run(() => ShowSectTabAsync(3, metrics[i].Item1)), 30);

            // podium: 2nd, 1st, 3rd
            var podium = ui.HRow(page, 116);
            foreach (var place in new[] { 2, 1, 3 })
            {
                var e = entries.FirstOrDefault(x => x.Rank == place);
                var col = ui.Box(podium, 1f, marginDp: 6);
                col.SetGravity(GravityFlags.Bottom | GravityFlags.CenterHorizontal);
                if (e.Name == null) continue;
                var portrait = ui.Icon(col, "portrait_" + e.Path, place == 1 ? 50 : 40);
                portrait.LayoutParameters = new LinearLayout.LayoutParams(Px(place == 1 ? 50 : 40), Px(place == 1 ? 50 : 40)) { Gravity = GravityFlags.CenterHorizontal };
                ui.Text(e.Name, col, 11, Ui.Ink, bold: true, center: true);
                var stand = new TextView(this) { Text = place == 1 ? "🥇" : place == 2 ? "🥈" : "🥉", Gravity = GravityFlags.Center };
                stand.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
                var bg = new global::Android.Graphics.Drawables.GradientDrawable(global::Android.Graphics.Drawables.GradientDrawable.Orientation.TopBottom,
                    new int[] { (place == 1 ? Ui.Gold : Ui.GoldDim).ToArgb(), Color.Argb(255, 40, 30, 16).ToArgb() });
                bg.SetCornerRadius(Px(4));
                stand.Background = bg;
                col.AddView(stand, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(place == 1 ? 34 : place == 2 ? 24 : 16)) { TopMargin = Px(2) });
            }

            var rest = entries.Where(e => e.Rank > 3).ToList();
            ui.Pages(page, rest.Count, 1, 5, (i, cell) =>
            {
                var e = rest[i];
                var card = ui.Card(cell, 1f, 6, e.Id == c.Id ? Ui.Gold : (Color?)null, 0);
                var row = ui.HRow(card, 34, 0);
                ui.Text("#" + e.Rank, row, 13, Ui.Gold, bold: true).LayoutParameters = new LinearLayout.LayoutParams(Px(40), ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.CenterVertical };
                ui.Icon(row, "portrait_" + e.Path, 28);
                ui.Text(e.Name + (e.Id == c.Id ? " (ty)" : string.Empty), row, 13, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8), Gravity = GravityFlags.CenterVertical };
                ui.Text(e.Value, row, 11, Ui.Muted).LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.CenterVertical };
            }, empty: entries.Count == 0 ? "Tablica jest jeszcze pusta — bądź pierwszy." : " ");
            var meRow = ui.Card(page, 0f, 8, Ui.Gold);
            meRow.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(44)) { TopMargin = Px(4) };
            ui.Text(me != null ? "Twoje miejsce: #" + me.Value.Rank + " — " + me.Value.Value : "Jeszcze cię nie ma na tej tablicy.", meRow, 14, Ui.Gold, bold: true);
        }
    }
}
