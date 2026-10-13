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
    /// <summary>Combat hub (training, bosses, duels) and the duel screen: stage on top, 8 technique slots below.</summary>
    public sealed partial class MainActivity
    {
        private CombatCatalogDto _catalog;

        private async Task ShowCombatTabAsync(int tab)
        {
            var c = _me;
            _catalog ??= await _api.GetCombatCatalogAsync(Ct);
            var open = tab == 3 ? (await _api.GetOpenBattlesAsync(c.Id, Ct)).Items : new List<BattleDto>();
            var hunt = tab == 1 ? (await _api.GetHuntAsync(c.Id, Ct)).Hunt : null;
            var ui = NewScreen(null, art: Resource.Drawable.arena_courtyard, tab: TabCombat);
            var page = ui.Content;
            ui.Tabs(page, new[] { "Trening", "Polowanie", "Bossowie", "Pojedynki" }, tab, i => Run(() => ShowCombatTabAsync(i)));

            if (tab == 0)
            {
                var npcs = _catalog.Npcs.Where(n => !n.WorldOnly).OrderBy(n => n.Tier).ThenBy(n => n.RewardQi).ToList();
                ui.Pages(page, npcs.Count, 2, 2, (i, cell) =>
                {
                    var n = npcs[i];
                    ui.TileIn(cell, n.NamePl, new string('★', Math.Clamp(n.Tier, 1, 5)) + (n.Tameable ? "  🐾" : string.Empty) + "  ·  +" + n.RewardQi + " Qi",
                        NpcLooks.For(n.Id).TileArt, () => Run(() => StartTrainingAsync(n.Id)), pixelArt: true, accent: n.Tier >= 3 ? Ui.Violet : Ui.Cinnabar, titleSp: 13);
                });
                ui.Text("★ zagrożenie  ·  🐾 można oswoić po zwycięstwie", page, 11, Ui.Muted, center: true);
            }
            else if (tab == 1)
            {
                Hunt(ui, page, hunt);
            }
            else if (tab == 2)
            {
                var bosses = _catalog.Npcs.Where(n => n.Boss).OrderBy(n => n.Tier).ToList();
                ui.Pages(page, bosses.Count, 2, 2, (i, cell) =>
                {
                    var n = bosses[i];
                    ui.TileIn(cell, n.NamePl, new string('★', Math.Clamp(n.Tier, 1, 5)) + "  ·  +" + n.RewardQi + " Qi", NpcLooks.For(n.Id).TileArt, () =>
                    {
                        SetStatus(n.NamePl + " czeka w legowisku 👹 w świecie — znajdź je na mapie.");
                        GoTab(TabWorld);
                    }, pixelArt: true, accent: Ui.Cinnabar, titleSp: 13, badge: "BOSS");
                });
                ui.Text("Bossowie walczą tylko w swoich legowiskach — szukaj 👹 na mapie.", page, 11, Ui.Muted, center: true, maxLines: 2);
            }
            else
            {
                ui.Text("Wyzwij kultywatora w Świecie (w okolicy) albo członka sekty.", page, 12, Ui.Muted, center: true, maxLines: 2);
                ui.Pages(page, open.Count, 1, 4, (i, cell) =>
                {
                    var b = open[i];
                    var card = ui.Card(cell, 1f, 8, b.Status == "pending_acceptance" ? Ui.Gold : (Color?)null, 0);
                    var row = ui.HRow(card, 44, 0);
                    ui.Icon(row, b.Kind == "pvp" ? "menu_battle" : "map_beast", 36);
                    var col = ui.Box(row, 1f, marginDp: 8);
                    ui.Text((b.Opponent?.Name ?? "?") + (b.Kind == "pvp" ? "  ·  pojedynek" : "  ·  trening"), col, 14, Ui.Ink, bold: true);
                    var waiting = b.Status == "pending_acceptance";
                    ui.Text(waiting ? (b.MySide == "b" ? "Wyzwanie do przyjęcia" : "Czeka na przeciwnika") : "Tura " + b.Turn + " · trwa", col, 11, Ui.Muted);
                    ui.Btn(waiting && b.MySide == "b" ? "Przyjmij" : "Otwórz", () => Run(async () =>
                    {
                        if (waiting && b.MySide == "b") await _api.AcceptChallengeAsync(b.Id, Ct);
                        var combat = new CombatFlow(_api);
                        await combat.OpenAsync(b.Id, Ct);
                        ShowBattle(combat);
                    }), row, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(96), Px(38)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Brak trwających walk.");
            }
        }

        /// <summary>
        /// Idle hunt: pick a training opponent and the character fights it on its own, one battle every few minutes for
        /// a few hours, even with the game closed. Rewards are real (Qi, loot, quests, pet XP) and share the daily cap.
        /// </summary>
        private void Hunt(Ui ui, LinearLayout page, HuntDto hunt)
        {
            var c = _me;
            if (hunt == null)
            {
                ui.Text("Wybierz przeciwnika: twoja postać walczy sama co 5 min przez 4 h — także przy zamkniętej grze. Nagrody wliczają się do dziennego limitu.", page, 12, Ui.Muted, center: true, maxLines: 3);
                var npcs = _catalog.Npcs.Where(n => !n.WorldOnly).OrderBy(n => n.Tier).ThenBy(n => n.RewardQi).ToList();
                ui.Pages(page, npcs.Count, 2, 2, (i, cell) =>
                {
                    var n = npcs[i];
                    ui.TileIn(cell, n.NamePl, new string('★', Math.Clamp(n.Tier, 1, 5)) + "  ·  +" + n.RewardQi + " Qi / wygrana", NpcLooks.For(n.Id).TileArt, () => Run(async () =>
                    {
                        await _api.StartHuntAsync(c.Id, n.Id, Ct);
                        Sfx.Play(Sfx.Chime);
                        await ShowCombatTabAsync(1);
                        SetStatus("Polowanie rozpoczęte — wróć za jakiś czas po łupy.");
                    }), pixelArt: true, accent: Ui.Jade, titleSp: 13);
                });
                return;
            }

            var card = ui.Card(page, 1f, 10, Ui.Jade);
            var art = new ImageView(this);
            var bmp = new global::Android.Graphics.Drawables.BitmapDrawable(Resources, Ui.Art(this, NpcLooks.For(hunt.NpcId).TileArt));
            bmp.SetFilterBitmap(false);
            art.SetImageDrawable(bmp);
            art.SetScaleType(ImageView.ScaleType.FitCenter);
            card.AddView(art, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            ui.Text("Polowanie: " + hunt.NpcNamePl, card, 16, Ui.Gold, bold: true, center: true);
            var now = DateTimeOffset.UtcNow;
            var total = (hunt.EndsAt - hunt.StartedAt).TotalMinutes;
            var done = Math.Clamp((now - hunt.StartedAt).TotalMinutes, 0, total);
            ui.MeterIn(card, Ui.Jade, hunt.Finished ? "Zakończone" : "Zostało " + MeditationForecast.FormatMinutes((int)Math.Ceiling(total - done)), done, total, 18);
            var stats = ui.HRow(card, 46, 4);
            foreach (var (label, value, color) in new[] { ("Walki", hunt.Battles.ToString(), Ui.Ink), ("Wygrane", hunt.Wins.ToString(), Ui.Jade),
                         ("Porażki", hunt.Losses.ToString(), Ui.Cinnabar), ("Qi", "+" + hunt.Qi, Ui.QiBlue) })
            {
                var col = ui.Box(stats, 1f, marginDp: 2);
                ui.Text(value, col, 18, color, bold: true, center: true);
                ui.Text(label, col, 11, Ui.Muted, center: true);
            }

            ui.Text(hunt.NextBattleAt is { } next && !hunt.Finished ? "Następna walka o " + next.ToLocalTime().ToString("HH:mm") + " · co " + hunt.MinutesPerBattle + " min"
                : "Odbierz wynik i zacznij nowe polowanie.", card, 11, Ui.Muted, center: true);
            var row = ui.HRow(page, 46, 6);
            ui.Btn("Odśwież", () => Run(() => ShowCombatTabAsync(1)), row, secondary: true, sp: 13, heightDp: 46);
            ui.Btn(hunt.Finished ? "Odbierz łupy" : "Zakończ i odbierz", () => Run(async () =>
            {
                var final = (await _api.ClaimHuntAsync(c.Id, Ct)).Hunt;
                await ShowCombatTabAsync(1);
                Sfx.Play(Sfx.Coin);
                ShowBanner("POLOWANIE", final.Wins + " zwycięstw z " + final.Battles + " walk", "+" + final.Qi + " Qi · łupy trafiły do sakwy", "menu_battle");
                RefreshHud();
            }), row, sp: 14, heightDp: 46);
        }

        /// <summary>Starts a training fight; a fight left unfinished earlier is resumed instead (one battle at a time).</summary>
        private async Task StartTrainingAsync(string npcId)
        {
            var combat = new CombatFlow(_api);
            try
            {
                await combat.StartPveAsync(_me.Id, npcId, Ct);
            }
            catch (ApiException e) when (e.Code == "already_in_battle")
            {
                var open = (await _api.GetOpenBattlesAsync(_me.Id, Ct)).Items.FirstOrDefault(b => b.IsActive);
                if (open == null) throw;
                await combat.OpenAsync(open.Id, Ct);
                ShowBattle(combat);
                SetStatus("Najpierw dokończ trwającą walkę z: " + open.Opponent.Name);
                return;
            }

            ShowBattle(combat);
        }

        // ---- the duel -----------------------------------------------------------------------------------------

        private void ShowBattle(CombatFlow combat)
        {
            var c = _me;
            var ui = NewScreen("Pojedynek", () => GoTab(TabCombat), subtitle: combat.Battle.Opponent.Name.ToUpperInvariant(), chrome: false);
            var page = ui.Content;
            var gen = _screen;
            var stage = new BattleStageView(this, combat.Battle.Kind == "pvp" ? "pvp" : combat.Battle.NpcId);
            ui.AddWeighted(page, stage, 1f, 0);
            var ticker = ui.Text(string.Empty, page, 12, Ui.Ink, maxLines: 2);
            var buttons = new List<View>();
            var rewardShown = combat.Battle.Status == "finished";

            string TechIcon(CombatSlotDto slot) =>
                slot.TechniqueId != null && ui.ArtId("tech_" + slot.TechniqueId) != 0 ? "tech_" + slot.TechniqueId
                : slot.Kind == "attack" ? "tech_custom_attack" : slot.Kind == "special" ? "tech_custom_movement" : "tech_custom_defense";

            void Act(int? slot, string direction) => Run(async () =>
            {
                if (!await combat.ActAsync(slot, direction, Ct)) SetStatus("Tura już minęła — odświeżono.");
                if (gen == _screen) Render();
            });

            // 8 slots as a 4×2 grid (slot 7 = the special skill)
            var slots = combat.Battle.MySlots.OrderBy(s => s.Slot).ToList();
            for (var r = 0; r < 2; r++)
            {
                var row = ui.HRow(page, 58, 4);
                for (var k = 0; k < 4; k++)
                {
                    var idx = r * 4 + k;
                    if (idx >= slots.Count || slots[idx].TechniqueId == null)
                    {
                        var empty = new View(this);
                        row.AddView(empty, new LinearLayout.LayoutParams(0, Px(58), 1f) { LeftMargin = k > 0 ? Px(6) : 0 });
                        continue;
                    }

                    var slot = slots[idx];
                    var cost = slot.QiCost > 0 ? slot.QiCost + " Qi" : null;
                    var b = ui.SkillTile(row, TechIcon(slot), (slot.Kind == "special" ? "★ " : string.Empty) + slot.NamePl, cost, slot.Kind is "attack" or "special", () => Act(slot.Slot, null));
                    b.Tag = slot.Slot;
                    buttons.Add(b);
                }
            }

            var controls = ui.HRow(page, 42, 6);
            buttons.Add(ui.Btn("Odpocznij", () => Act(null, null), controls, secondary: true, sp: 12, heightDp: 42));
            // Auto-battle: the server plays each turn for the player (same AI as the enemies); tap again to take over.
            var auto = false;
            Button autoBtn = null;
            autoBtn = ui.Btn("▶ Auto", () =>
            {
                auto = !auto;
                autoBtn.Text = auto ? "⏸ Auto" : "▶ Auto";
                Sfx.Play(Sfx.Click);
                if (auto) AutoLoop();
            }, controls, secondary: true, sp: 12, heightDp: 42);
            autoBtn.LayoutParameters = new LinearLayout.LayoutParams(Px(74), Px(42)) { LeftMargin = Px(6) };

            async void AutoLoop()
            {
                while (auto && gen == _screen && combat.Battle.IsActive && !_lifetime.IsCancellationRequested)
                {
                    try
                    {
                        if (!combat.Battle.MyActionSubmitted && !_paused)
                        {
                            await combat.AutoAsync(Ct);
                            if (gen == _screen) Render();
                        }

                        await Task.Delay(Math.Max(1200, combat.Battle.LastTurnEvents.Count * 760 + 500), Ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                    catch (ApiException e)
                    {
                        if (gen == _screen) SetStatus(Describe(e));
                        auto = false;
                    }
                }

                auto = false;
                if (gen == _screen) autoBtn.Text = "▶ Auto";
            }
            var confirmForfeit = false;
            Button forfeit = null;
            forfeit = ui.Btn("Poddaj", () =>
            {
                if (!combat.Battle.IsActive) return;
                if (!confirmForfeit)
                {
                    confirmForfeit = true;
                    forfeit.Text = "Na pewno?";
                    return;
                }

                Run(async () =>
                {
                    await combat.ForfeitAsync(Ct);
                    if (gen == _screen) Render();
                });
            }, controls, secondary: true, sp: 12, heightDp: 42);
            buttons.Add(forfeit);
            ui.Btn("Wyjdź", () => GoTab(TabCombat), controls, secondary: true, sp: 12, heightDp: 42);

            void Render()
            {
                var b = combat.Battle;
                stage.Show(b);
                ticker.Text = b.Status == "pending_acceptance" ? "Czekasz, aż przeciwnik przyjmie wyzwanie…"
                    : b.Status == "declined" ? "Przeciwnik odrzucił wyzwanie." : b.Status == "expired" ? "Wyzwanie wygasło."
                    : !b.IsActive ? "WYNIK: " + ResultName(b) + (b.RewardQi > 0 ? "  (+" + b.RewardQi + " Qi)" : string.Empty)
                    : b.LastTurnEvents.Count == 0 ? "Tura " + b.Turn + " · Qi " + b.Me.Qi + "/" + b.Me.MaxQi + " — przeciwnicy mierzą się wzrokiem…"
                    : "Tura " + b.Turn + " · " + string.Join("  ·  ", b.LastTurnEvents.Select(e =>
                        (b.IsMine(e) ? "Ty" : "Wróg") + ": " + ActionName(e.Action) + (e.Hit == true ? " " + e.Damage : e.Hit == false ? " pudło" : string.Empty)
                        + (e.Healed > 0 ? " +" + e.Healed + " HP" : string.Empty)));
                foreach (var button in buttons)
                {
                    var on = b.IsActive && !b.MyActionSubmitted;
                    if (on && button.Tag != null)
                    {
                        var s = b.MySlots.FirstOrDefault(x => x.Slot == (int)button.Tag);
                        on = s != null && s.Unavailable == null;
                    }

                    button.Enabled = on;
                    button.Alpha = on ? 1f : 0.4f;
                }

                if (!rewardShown && !b.IsActive && b.Status == "finished")
                {
                    rewardShown = true;
                    var won = b.Result == "won";
                    var shownGen = _screen;
                    _frame.PostDelayed(() =>
                    {
                        if (shownGen != _screen) return;
                        if (won) ShowLoot("Zwycięstwo!", b.RewardQi, new List<(string, long)>(), b.TamedPetNpcId);
                        else ShowBanner(b.Result == "draw" ? "REMIS" : "PORAŻKA", "Przeciwnik: " + b.Opponent.Name, "Trenuj meridiany i techniki, wróć silniejszy.", "menu_battle");
                        RefreshHud();
                    }, b.LastTurnEvents.Count * 760 + 1500);
                }
            }

            // PvP: the other player acts on their own phone, so poll while waiting
            async void Poll()
            {
                while (gen == _screen && combat.Battle.Kind == "pvp" && (combat.Battle.Status == "pending_acceptance" || combat.Battle.IsActive))
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
                        if (gen != _screen || _paused) continue;
                        await combat.RefreshAsync(Ct);
                        if (gen == _screen) Render();
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                    catch (ApiException)
                    {
                        // keep polling; the next refresh may succeed
                    }
                }
            }

            Poll();
            Render();
        }
    }
}
