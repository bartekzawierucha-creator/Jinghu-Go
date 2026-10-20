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
        private int _combatSub;

        private async Task ShowCombatTabAsync(int tab)
        {
            var c = _me;
            _combatSub = tab;
            _catalog ??= await _api.GetCombatCatalogAsync(Ct);
            var open = tab == 4 ? (await _api.GetOpenBattlesAsync(c.Id, Ct)).Items : new List<BattleDto>();
            var hunt = tab == 1 ? (await _api.GetHuntAsync(c.Id, Ct)).Hunt : null;
            var tower = tab == 2 ? await _api.GetTowerAsync(c.Id, Ct) : null;
            var boss = tab == 3 ? await _api.GetWorldBossAsync(c.Id, Ct) : null;
            var arena = tab == 5 ? await _api.GetArenaAsync(c.Id, Ct) : null;
            // 2.2: foes are shown scaled to the hero's realm (power and the real reward), so growth on both sides is visible
            var scout = tab == 0 || (tab == 1 && hunt == null) ? await _api.GetCombatScoutAsync(c.Id, Ct) : null;
            var ui = NewScreen(null, art: tab == 2 ? Resource.Drawable.arena_tower : Resource.Drawable.arena_courtyard, tab: TabCombat);
            var page = ui.Content;
            ui.Tabs(page, new[] { "Trening", "Łowy", "Wieża", "Bossy", "PvP", "Arena" }, tab, i => Run(() => ShowCombatTabAsync(i)));

            if (tab == 0)
            {
                var npcs = TrainingFoes(scout);
                ui.Text("Twoja moc: " + scout.MyPower.ToString("N0") + "  ·  wrogowie rosną razem z tobą", page, 12, Ui.Ink, bold: true, center: true);
                ui.Pages(page, npcs.Count, 2, 2, (i, cell) =>
                {
                    var n = npcs[i];
                    var (line, color) = FoeLine(n, scout);
                    ui.TileIn(cell, n.NamePl, line + (n.Tameable ? "  🐾" : string.Empty),
                        NpcLooks.For(n.Id).TileArt, () => Run(() => StartTrainingAsync(n.Id)), pixelArt: true, accent: color, titleSp: 13);
                });
                ui.Text("Moc wroga przy twoim królestwie  ·  🐾 można oswoić", page, 11, Ui.Muted, center: true);
            }
            else if (tab == 1)
            {
                Hunt(ui, page, hunt);
            }
            else if (tab == 2)
            {
                Tower(ui, page, tower);
            }
            else if (tab == 3)
            {
                WorldBoss(ui, page, boss);
                var bosses = _catalog.Npcs.Where(n => n.Boss && n.Id != _catalog.WorldBossNpcId).OrderBy(n => n.Tier).ToList();
                ui.Pages(page, bosses.Count, 2, 1, (i, cell) =>
                {
                    var n = bosses[i];
                    ui.TileIn(cell, n.NamePl, new string('★', Math.Clamp(n.Tier, 1, 5)) + "  ·  +" + n.RewardQi + " Qi", NpcLooks.For(n.Id).TileArt, () =>
                    {
                        SetStatus(n.NamePl + " czeka w legowisku 👹 w świecie — znajdź je na mapie.");
                        GoTab(TabWorld);
                    }, pixelArt: true, accent: Ui.Cinnabar, titleSp: 13, badge: "BOSS");
                });
                ui.Text("Bossowie legowisk walczą tylko w świecie — szukaj 👹 na mapie.", page, 11, Ui.Muted, center: true);
                ui.Btn("Kodeks bestii", () => Run(CodexSheetAsync), page, secondary: true, sp: 12, heightDp: 34, icon: "gi_codex");
            }
            else if (tab == 5)
            {
                Arena(ui, page, arena);
            }
            else
            {
                ui.Text("Wyzwij kultywatora w Świecie (w okolicy) albo członka sekty.", page, 12, Ui.Muted, center: true, maxLines: 2);
                ui.Btn("Listy gończe", () => Run(BountySheetAsync), page, secondary: true, sp: 12, heightDp: 34, icon: "gi_wanted");
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
                    if (waiting && b.MySide == "b")
                        ui.Btn("Odrzuć", () => Run(async () =>
                        {
                            await _api.DeclineChallengeAsync(b.Id, Ct);
                            await ShowCombatTabAsync(4);
                            SetStatus("Wyzwanie odrzucone.");
                        }), row, secondary: true, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(84), Px(38)) { Gravity = GravityFlags.CenterVertical, RightMargin = Px(4) };
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

        /// <summary>Training foes, weakest first by their power at the hero's realm.</summary>
        private List<CombatNpcDto> TrainingFoes(CombatScoutDto scout)
        {
            var power = scout.Foes.ToDictionary(f => f.NpcId, f => f.Power);
            return _catalog.Npcs.Where(n => !n.WorldOnly).OrderBy(n => power.GetValueOrDefault(n.Id)).ToList();
        }

        /// <summary>"Moc 951 · +40 Qi" and a colour from easy (jade) to deadly (red) by power against the hero's.</summary>
        private static (string Line, Color Color) FoeLine(CombatNpcDto n, CombatScoutDto scout)
        {
            var f = scout.Foes.FirstOrDefault(x => x.NpcId == n.Id);
            if (f == null) return (new string('★', Math.Clamp(n.Tier, 1, 5)), Ui.Cinnabar);
            var ratio = scout.MyPower > 0 ? f.Power * 100 / scout.MyPower : 100;
            var (label, color) = ratio < 80 ? ("łatwy", Ui.Jade) : ratio <= 115 ? ("równy", Ui.QiBlue) : ratio <= 160 ? ("trudny", Ui.Violet) : ("zabójczy", Ui.Cinnabar);
            return ("Moc " + f.Power.ToString("N0") + " · " + label + " · +" + f.RewardQi + " Qi", color);
        }

        /// <summary>Codex (2.3): every species, revealed once defeated, with the number of victories.</summary>
        private async Task CodexSheetAsync()
        {
            var seen = (await _api.GetCodexAsync(_me.Id, Ct)).Items.ToDictionary(x => x.NpcId, x => x.Victories);
            var all = _catalog.Npcs.OrderBy(n => n.Tier).ThenBy(n => n.RewardQi).ToList();
            var body = Sheet("Kodeks bestii (" + all.Count(n => seen.ContainsKey(n.Id)) + "/" + all.Count + ")", 0.85f, out _, "menu_battle");
            _ui.Pages(body, all.Count, 2, 3, (i, cell) =>
            {
                var n = all[i];
                var known = seen.TryGetValue(n.Id, out var wins);
                var card = _ui.Card(cell, 1f, 6, known ? (n.Boss ? Ui.Cinnabar : Ui.Jade) : (Color?)null, 0);
                _ui.Text(known ? n.NamePl : "???", card, 13, known ? Ui.Ink : Ui.Muted, bold: true);
                _ui.Text(new string('★', Math.Clamp(n.Tier, 1, 5)) + (n.Boss ? " · BOSS" : string.Empty) + (n.Tameable ? " · 🐾" : string.Empty), card, 11, Ui.Gold);
                _ui.Text(known ? "Pokonano " + wins + "×" : n.WorldOnly ? "Spotkasz w świecie" : "Czeka na treningu", card, 10, Ui.Muted);
            });
        }

        /// <summary>Bounty board (2.3): stones on a cultivator's head, paid to whoever beats them in a duel.</summary>
        private async Task BountySheetAsync()
        {
            var c = _me;
            var list = (await _api.GetBountiesAsync(c.Id, Ct)).Items;
            var body = Sheet("Listy gończe", 0.8f, out var close, "menu_battle");
            var row = _ui.HRow(body, 42, 4);
            var name = _ui.Field("Imię celu", parent: row);
            name.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.4f);
            var stones = _ui.Field("KD (100+)", number: true, parent: row);
            stones.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f) { LeftMargin = Px(4) };
            _ui.Btn("Wystaw list gończy", () => Run(async () =>
            {
                long.TryParse(stones.Text, out var st);
                await _api.PostBountyAsync(c.Id, name.Text?.Trim(), st, Ct);
                close();
                RefreshHud();
                SetStatus("List gończy wystawiony — kto pokona " + name.Text?.Trim() + " w pojedynku, zgarnie " + st + " KD.");
            }), body, sp: 13, heightDp: 38);
            _ui.Pages(body, list.Count, 1, 5, (i, cell) =>
            {
                var b = list[i];
                var card = _ui.Card(cell, 1f, 6, b.Mine ? Ui.Gold : Ui.Cinnabar, 0);
                var r = _ui.HRow(card, 36, 0);
                _ui.Text(b.TargetName + " — " + b.Stones.ToString("N0") + " KD", r, 13, Ui.Ink, bold: true)
                    .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                if (b.Mine)
                    _ui.Btn("Wycofaj", () => Run(async () =>
                    {
                        await _api.WithdrawBountyAsync(c.Id, b.Id, Ct);
                        close();
                        RefreshHud();
                        SetStatus("List wycofany (zwrot minus 10%).");
                    }), r, secondary: true, sp: 11, heightDp: 32).LayoutParameters = new LinearLayout.LayoutParams(Px(84), Px(32));
            }, empty: "Nikt nie jest teraz poszukiwany.");
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
                var npcs = TrainingFoes(scout);
                ui.Pages(page, npcs.Count, 2, 2, (i, cell) =>
                {
                    var n = npcs[i];
                    var (line, color) = FoeLine(n, scout);
                    ui.TileIn(cell, n.NamePl, line, NpcLooks.For(n.Id).TileArt, () => Run(async () =>
                    {
                        await _api.StartHuntAsync(c.Id, n.Id, Ct);
                        Sfx.Play(Sfx.Chime);
                        await ShowCombatTabAsync(1);
                        SetStatus("Polowanie rozpoczęte. Wróć później po łupy.");
                    }), pixelArt: true, accent: color, titleSp: 13);
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
            Remind("hunt", hunt.Finished ? null : hunt.EndsAt, "Polowanie zakończone", hunt.NpcNamePl + " — odbierz łupy.");
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

            ui.Text(hunt.NextBattleAt is { } next && !hunt.Finished ? (hunt.Battles == 0 ? "Pierwsza walka o " : "Następna walka o ") + next.ToLocalTime().ToString("HH:mm") + " · co " + hunt.MinutesPerBattle + " min"
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

        /// <summary>Tower of Trials: one floor at a time, first clears pay, milestones every few floors, a leaderboard.</summary>
        private void Tower(Ui ui, LinearLayout page, TowerDto t)
        {
            var c = _me;
            var head = ui.Card(page, 1.25f, 8, Ui.Gold);
            var top = ui.Box(head, 1f, horizontal: true, marginDp: 0);
            var art = new ImageView(this);
            art.SetImageBitmap(Ui.Art(this, Resource.Drawable.tile_tower));
            art.SetScaleType(ImageView.ScaleType.CenterCrop);
            top.AddView(art, new LinearLayout.LayoutParams(Px(72), ViewGroup.LayoutParams.MatchParent) { RightMargin = Px(8) });
            var info = ui.Box(top, 1f, marginDp: 0);
            ui.Text("WIEŻA PRÓB", info, 16, Ui.Gold, bold: true);
            ui.Text("Piętro " + t.BestFloor + " z " + t.Floors + (t.Me != null ? "  ·  #" + t.Me.Rank + " w rankingu" : string.Empty), info, 13, Ui.Ink);
            ui.MeterIn(info, Ui.Gold, t.BestFloor + " / " + t.Floors, t.BestFloor, t.Floors, 14);
            if (t.NextMilestone is { } m)
                ui.Text("Nagroda na piętrze " + m.Floor + ": " + m.Stones + " KD" + (m.Items.Count > 0 ? " + " + string.Join(", ", m.Items.Select(i => i.Quantity + "× " + (i.NamePl ?? i.ItemId))) : string.Empty),
                    info, 11, Ui.Jade, maxLines: 2);
            if (t.Next is { } next)
            {
                var row = ui.HRow(page, 92, 6);
                var cell = ui.Box(row, 1f, marginDp: 0);
                ui.TileIn(cell, "Piętro " + next.Floor + ": " + next.NamePl, (next.Guardian ? "STRAŻNIK  ·  " : string.Empty) + new string('★', Math.Clamp(next.Tier, 1, 5)) + "  ·  +" + next.Qi + " Qi",
                    NpcLooks.For(next.NpcId).TileArt, () => Run(() => StartTowerAsync()), pixelArt: true, accent: next.Guardian ? Ui.Cinnabar : Ui.Gold, titleSp: 13, badge: next.Guardian ? "BOSS" : null);
                ui.Btn("Wejdź na piętro " + next.Floor, () => Run(() => StartTowerAsync()), page, sp: 14, heightDp: 46);
            }
            else
            {
                ui.Text("Wieża zdobyta. Jesteś na szczycie!", page, 15, Ui.Gold, bold: true, center: true);
            }

            var board = ui.Card(page, 1.1f, 8);
            ui.Text("NAJWYŻEJ W WIEŻY", board, 12, Ui.GoldDim, bold: true);
            if (t.Top.Count == 0) ui.Text("Nikt jeszcze nie wszedł. Bądź pierwszy.", board, 12, Ui.Muted);
            foreach (var r in t.Top.Take(5))
                ui.Text("#" + r.Rank + "  " + r.Name + "  ·  piętro " + r.Value, board, 12, r.CharacterId == c.Id ? Ui.Gold : Ui.Ink);
        }

        private async Task StartTowerAsync()
        {
            await StartOrResumeAsync(() => _api.StartTowerAsync(_me.Id, Ct));
        }

        /// <summary>The world boss: one shared HP pool for everyone this cycle, a few attempts a day, a kill reward.</summary>
        private void WorldBoss(Ui ui, LinearLayout page, WorldBossDto b)
        {
            var c = _me;
            var card = ui.Card(page, 1.6f, 8, b.Defeated ? Ui.Jade : Ui.Cinnabar);
            var top = ui.Box(card, 1f, horizontal: true, marginDp: 0);
            var art = new ImageView(this);
            art.SetImageBitmap(Ui.Art(this, Resource.Drawable.tile_worldboss));
            art.SetScaleType(ImageView.ScaleType.CenterCrop);
            top.AddView(art, new LinearLayout.LayoutParams(Px(84), ViewGroup.LayoutParams.MatchParent) { RightMargin = Px(8) });
            var info = ui.Box(top, 1f, marginDp: 0);
            ui.Text("BOSS ŚWIATA: " + b.NamePl.ToUpperInvariant(), info, 14, Ui.Gold, bold: true);
            ui.MeterIn(info, b.Defeated ? Ui.Jade : Ui.Cinnabar, b.Defeated ? "POKONANY" : b.HpLeft + " / " + b.PoolHp + " HP", b.HpLeft, b.PoolHp, 18);
            var days = Math.Max(0, (int)Math.Ceiling((b.EndsAt - DateTimeOffset.UtcNow).TotalDays));
            ui.Text("Wspólne HP wszystkich graczy  ·  nowy boss za " + days + " d", info, 11, Ui.Muted);
            ui.Text("Twoje obrażenia: " + b.MyDamage + (b.MyRank is int rank ? "  ·  #" + rank : string.Empty) + "  ·  próby dziś: " + b.AttemptsLeft + "/" + b.AttemptsPerDay, info, 12, Ui.Ink);
            ui.Text("Nagroda za ubicie (min. " + b.MinDamageForKillReward + " obrażeń): " + b.KillStones + " KD"
                + (b.KillItems.Count > 0 ? " + " + string.Join(", ", b.KillItems.Select(i => i.Quantity + "× " + (i.NamePl ?? i.ItemId))) : string.Empty), info, 11, Ui.Jade, maxLines: 2);
            var row = ui.HRow(card, 44, 4);
            var claimCycle = b.CanClaim ? b.Cycle : b.UnclaimedCycle;
            if (claimCycle is int cycle)
            {
                ui.Btn("Odbierz nagrodę", () => Run(async () =>
                {
                    await _api.ClaimWorldBossAsync(c.Id, cycle, Ct);
                    Sfx.Play(Sfx.Coin);
                    ShowBanner("BOSS POKONANY", "Nagroda trafiła do sakwy", "+" + b.KillStones + " KD", "menu_worldboss");
                    await ShowCombatTabAsync(3);
                    RefreshHud();
                }), row, sp: 13, heightDp: 44);
            }
            else
            {
                var can = !b.Defeated && b.AttemptsLeft > 0;
                var btn = ui.Btn(b.Defeated ? (b.Claimed ? "Nagroda odebrana" : "Boss pokonany") : b.AttemptsLeft > 0 ? "Atakuj bossa" : "Wróć jutro", () => Run(async () =>
                {
                    await StartOrResumeAsync(() => _api.StartWorldBossAsync(c.Id, Ct));
                }), row, sp: 13, heightDp: 44);
                btn.Enabled = can;
                btn.Alpha = can ? 1f : 0.5f;
            }
        }

        /// <summary>Starts a battle; a fight left unfinished earlier is resumed instead (one battle at a time).</summary>
        private async Task StartOrResumeAsync(Func<Task<BattleDto>> start)
        {
            var combat = new CombatFlow(_api);
            try
            {
                var started = await start();
                await combat.OpenAsync(started.Id, Ct);
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

        // ---- the duel ---------------------------------------------------------------------------------------------

        /// <summary>Where a fight returns to and how to fight again (tower: next floor, training: the same foe...).</summary>
        private sealed record BattleOrigin(Func<Task> Back, Func<BattleDto, (string Label, Func<Task> Run)?> Again = null);

        private const string SpeedPref = "jianghu.battle_speed";

        /// <summary>Default origin by battle kind, for fights started from the combat tabs.</summary>
        private BattleOrigin OriginFor(BattleDto b) => b.Kind switch
        {
            "tower" => new BattleOrigin(() => ShowCombatTabAsync(2), r => r.Result == "won"
                ? ("Następne piętro", StartTowerAsync) : ("Spróbuj ponownie", StartTowerAsync)),
            "boss" => new BattleOrigin(() => ShowCombatTabAsync(3), _ => ("Atakuj ponownie", () => StartOrResumeAsync(() => _api.StartWorldBossAsync(_me.Id, Ct)))),
            "arena" => new BattleOrigin(() => ShowCombatTabAsync(5), _ => ("Następny rywal", () => ShowCombatTabAsync(5))),
            "pvp" => new BattleOrigin(() => ShowCombatTabAsync(4)),
            "tribulation" => new BattleOrigin(() => { GoTab(TabHome); return Task.CompletedTask; }),
            "spirit" => new BattleOrigin(() => { GoTab(TabWorld); return Task.CompletedTask; }),
            _ when b.NpcId != null && _catalog?.Npcs.FirstOrDefault(n => n.Id == b.NpcId) is { WorldOnly: false } =>
                new BattleOrigin(() => ShowCombatTabAsync(0), _ => ("Walcz ponownie", () => StartTrainingAsync(b.NpcId))),
            // lairs and wandering beasts live in the world: go back to the map
            _ when b.NpcId != null => new BattleOrigin(() => { GoTab(TabWorld); return Task.CompletedTask; }),
            _ => new BattleOrigin(() => ShowCombatTabAsync(_combatSub)),
        };

        /// <summary>
        /// The fight: always automatic (the server plays each turn with the same AI as the enemies), ×1/×2/×3 speed,
        /// and when it ends the screen offers the obvious next step (next floor, fight again) and the way back.
        /// </summary>
        private void ShowBattle(CombatFlow combat, BattleOrigin origin = null)
        {
            var c = _me;
            origin ??= OriginFor(combat.Battle);
            var title = combat.Battle.Kind == "tower" ? "Wieża Prób" : combat.Battle.Kind == "boss" ? "Boss Świata" : "Pojedynek";
            var ui = NewScreen(title, () => Run(origin.Back), subtitle: combat.Battle.Opponent.Name.ToUpperInvariant(), chrome: false);
            var page = ui.Content;
            var gen = _screen;
            var npc = combat.Battle.Kind == "pvp" ? "pvp" : combat.Battle.NpcId;
            // 2.2: people are fought in the ink duel; beasts and spirits keep the classic stage until they have ink poses
            var ink = InkBattleView.Supports(npc, combat.Battle.Kind) ? new InkBattleView(this, npc, c.Path, combat.Battle.Kind, combat.Battle.Opponent?.Name) : null;
            var classic = ink == null ? new BattleStageView(this, npc, c.Path, combat.Battle.Kind, combat.Battle.Opponent?.Name) : null;
            ui.AddWeighted(page, (View)ink ?? classic, 1f, 0);
            var stepMs = ink != null ? InkBattleView.StepMs : BattleStageView.StepMs;
            var ticker = ui.Text(string.Empty, page, 12, Ui.Ink, maxLines: 2);
            var rewardShown = combat.Battle.Status == "finished";
            BattleClock.Speed = Math.Clamp(_prefs.GetInt(SpeedPref, 1), 1, 3);

            // during the fight: speed, forfeit, leave; after it: fight again / next floor, back
            var controls = ui.HRow(page, 46, 6);
            Button speed = null;
            speed = ui.Btn("Prędkość ×" + (int)BattleClock.Speed, () =>
            {
                var next = (int)BattleClock.Speed % 3 + 1;
                BattleClock.Speed = next;
                _prefs.Edit().PutInt(SpeedPref, next).Apply();
                speed.Text = "Prędkość ×" + next;
                Sfx.Play(Sfx.Click);
            }, controls, secondary: true, sp: 13, heightDp: 46);
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
            }, controls, secondary: true, sp: 12, heightDp: 46);
            var again = ui.Btn("Walcz ponownie", () => { }, controls, sp: 13, heightDp: 46);
            again.Visibility = ViewStates.Gone;
            ui.Btn("Wyjdź", () => Run(origin.Back), controls, secondary: true, sp: 12, heightDp: 46);
            Func<Task> againRun = null;
            again.Click += (_, __) =>
            {
                if (againRun != null) Run(againRun);
            };

            // the fight plays itself: one server turn, then wait for its animation, at the chosen speed
            async void AutoLoop()
            {
                while (gen == _screen && (combat.Battle.IsActive || combat.Battle.Status == "pending_acceptance") && !_lifetime.IsCancellationRequested)
                {
                    try
                    {
                        if (combat.Battle.IsActive && !combat.Battle.MyActionSubmitted && !_paused)
                        {
                            await combat.AutoAsync(Ct);
                            if (gen == _screen) Render();
                            await Task.Delay(BattleClock.Real(Math.Max(700, combat.Battle.LastTurnEvents.Count * stepMs + 450)), Ct);
                        }
                        else
                        {
                            // PvP: the other side acts on their phone; just poll
                            await Task.Delay(TimeSpan.FromSeconds(combat.Battle.Kind == "pvp" ? 3 : 1), Ct);
                            if (gen != _screen || _paused) continue;
                            if (combat.Battle.Kind == "pvp")
                            {
                                await combat.RefreshAsync(Ct);
                                if (gen == _screen) Render();
                            }
                        }
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                    catch (ApiException e)
                    {
                        if (e.Code == "action_already_submitted" || e.Code == "turn_mismatch")
                        {
                            try { await combat.RefreshAsync(Ct); } catch (ApiException) { }
                            if (gen == _screen) Render();
                            continue;
                        }

                        if (gen == _screen) SetStatus(Describe(e));
                        return;
                    }
                }
            }

            void Render()
            {
                var b = combat.Battle;
                if (ink != null) ink.Show(b);
                else classic.Show(b);
                ticker.Text = b.Status == "pending_acceptance" ? "Czekasz, aż przeciwnik przyjmie wyzwanie…"
                    : b.Status == "declined" ? "Przeciwnik odrzucił wyzwanie." : b.Status == "expired" ? "Wyzwanie wygasło."
                    : !b.IsActive ? "WYNIK: " + ResultName(b) + (b.RewardQi > 0 ? "  (+" + b.RewardQi + " Qi)" : string.Empty)
                    : b.LastTurnEvents.Count == 0 ? "Tura " + b.Turn + " · przeciwnicy mierzą się wzrokiem…"
                    : "Tura " + b.Turn + " · " + string.Join("  ·  ", b.LastTurnEvents.Select(e =>
                        (b.IsMine(e) ? "Ty" : "Wróg") + ": " + ActionName(e.Action) + (e.Hit == true ? " " + e.Damage : e.Hit == false ? " pudło" : string.Empty)
                        + (e.Healed > 0 ? " +" + e.Healed + " HP" : string.Empty)));
                forfeit.Visibility = b.IsActive ? ViewStates.Visible : ViewStates.Gone;
                speed.Visibility = b.IsActive || b.Status == "pending_acceptance" ? ViewStates.Visible : ViewStates.Gone;

                if (!rewardShown && !b.IsActive && b.Status == "finished")
                {
                    rewardShown = true;
                    var won = b.Result == "won";
                    var shownGen = _screen;
                    var next = origin.Again?.Invoke(b);
                    _frame.PostDelayed(() =>
                    {
                        if (shownGen != _screen) return;
                        if (next is { } n)
                        {
                            again.Text = n.Label;
                            againRun = n.Run;
                            again.Visibility = ViewStates.Visible;
                            Juice.Pop(again);
                        }

                        if (won && b.RewardQi == 0 && b.Kind == "pve" && b.TamedPetNpcId == null)
                            ShowBanner("ZWYCIĘSTWO", "Przeciwnik: " + b.Opponent.Name, "Dzisiejsze nagrody z treningu wyczerpane (20 wygranych) — Qi wróci jutro. Wieża i boss świata płacą dalej.", "menu_battle");
                        else if (won) ShowLoot("Zwycięstwo!", b.RewardQi, new List<(string, long)>(), b.TamedPetNpcId);
                        else ShowBanner(b.Result == "draw" ? "REMIS" : "PORAŻKA", "Przeciwnik: " + b.Opponent.Name, "Trenuj meridiany i techniki, wróć silniejszy.", "menu_battle");
                        RefreshHud();
                    }, BattleClock.Real(b.LastTurnEvents.Count * stepMs + 1200));
                }
            }

            AutoLoop();
            Render();
        }
    }
}
