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
            var open = tab == 2 ? (await _api.GetOpenBattlesAsync(c.Id, Ct)).Items : new List<BattleDto>();
            var ui = NewScreen(null, art: Resource.Drawable.arena_courtyard, tab: TabCombat);
            var page = ui.Content;
            ui.Tabs(page, new[] { "Trening", "Bossowie", "Pojedynki" }, tab, i => Run(() => ShowCombatTabAsync(i)));

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
                : slot.Kind == "attack" ? "tech_custom_attack" : slot.Kind == "movement" ? "tech_custom_movement" : "tech_custom_defense";

            void Act(int? slot, string direction) => Run(async () =>
            {
                if (!await combat.ActAsync(slot, direction, Ct)) SetStatus("Tura już minęła — odświeżono.");
                if (gen == _screen) Render();
            });

            // 8 slots as a 4×2 grid; the movement slot splits into ⬅ / ➡
            var slots = combat.Battle.MySlots.OrderBy(s => s.Slot).ToList();
            for (var r = 0; r < 2; r++)
            {
                var row = ui.HRow(page, 54, 4);
                for (var k = 0; k < 4; k++)
                {
                    var idx = r * 4 + k;
                    if (idx >= slots.Count || slots[idx].TechniqueId == null)
                    {
                        var empty = new View(this);
                        row.AddView(empty, new LinearLayout.LayoutParams(0, Px(54), 1f) { LeftMargin = k > 0 ? Px(6) : 0 });
                        continue;
                    }

                    var slot = slots[idx];
                    var cost = slot.QiCost > 0 ? "\n" + slot.QiCost + " Qi" : string.Empty;
                    if (slot.Kind == "movement")
                    {
                        var pair = ui.Box(row, 1f, horizontal: true, marginDp: 6);
                        buttons.Add(ui.Btn("⬅", () => Act(slot.Slot, "retreat"), pair, secondary: true, sp: 16, heightDp: 54));
                        buttons.Add(ui.Btn("➡", () => Act(slot.Slot, "advance"), pair, secondary: true, sp: 16, heightDp: 54));
                        continue;
                    }

                    var b = ui.Btn(slot.NamePl + cost, () => Act(slot.Slot, null), row, secondary: slot.Kind != "attack", sp: 10, heightDp: 54, icon: TechIcon(slot));
                    b.Tag = slot.Slot;
                    buttons.Add(b);
                }
            }

            var controls = ui.HRow(page, 42, 6);
            buttons.Add(ui.Btn("Odpoczynek +Qi", () => Act(null, null), controls, secondary: true, sp: 12, heightDp: 42));
            ui.Btn("Odśwież", () => Run(async () =>
            {
                await combat.RefreshAsync(Ct);
                Render();
            }), controls, secondary: true, sp: 12, heightDp: 42);
            var confirmForfeit = false;
            Button forfeit = null;
            forfeit = ui.Btn("🏳 Poddaj się", () =>
            {
                if (!combat.Battle.IsActive) return;
                if (!confirmForfeit)
                {
                    confirmForfeit = true;
                    forfeit.Text = "Dotknij ponownie";
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
                        (b.IsMine(e) ? "Ty" : "Wróg") + ": " + ActionName(e.Action) + (e.Hit == true ? " " + e.Damage : e.Hit == false ? " pudło" : string.Empty)));
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
