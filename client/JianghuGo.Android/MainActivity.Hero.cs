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
    /// <summary>Hero hub: equipment silhouette, meridian constellation, technique slots, spirit pet and the Dao lab.</summary>
    public sealed partial class MainActivity
    {
        private static readonly string[] HeroTabs = { "Postać", "Meridiany", "Techniki", "Towarzysz", "Dao", "Sztuki" };

        private async Task ShowHeroTabAsync(int tab)
        {
            switch (tab)
            {
                case 1:
                    await ShowMeridiansAsync();
                    return;
                case 2:
                    await ShowTechniquesAsync();
                    return;
                case 3:
                    await ShowPetAsync();
                    return;
                case 4:
                    await ShowDaoLabAsync();
                    return;
                case 5:
                    await ShowArtsAsync(0);
                    return;
                default:
                    await ShowHeroAsync();
                    return;
            }
        }

        /// <summary>
        /// 2.2: the hero is one screen (the figure, gear and stats) with icons for the rest; meridians, techniques, the
        /// companion, Dao and the path arts open as their own screens with a back arrow to the hero.
        /// </summary>
        private Ui HeroScreen(int tab, int art)
        {
            if (tab == 0) return NewScreen(null, art: art, tab: TabHero);
            var sub = tab switch
            {
                1 => "PUNKT ZA KAŻDY ETAP",
                2 => "WALKA TOCZY SIĘ SAMA",
                3 => "DUCHOWA BESTIA U BOKU",
                4 => "TWOJE WŁASNE TECHNIKI",
                _ => "MOC TWOJEJ ŚCIEŻKI",
            };
            return NewScreen(HeroTabs[tab], () => Run(() => ShowHeroTabAsync(0)), art, sub, tab: TabHero);
        }

        /// <summary>Where the stats come from: the three traits with their modifiers, the path's perks and the titles' bonuses.</summary>
        private void ShowOriginsSheet() => Run(async () =>
        {
            var c = _me;
            _catalog ??= await _api.GetCombatCatalogAsync(Ct);
            _board ??= await _api.GetQuestsAsync(c.Id, Ct);
            var body = Sheet("Pochodzenie i premie", 0.78f, out _, "portrait_" + c.Path);
            foreach (var t in c.Traits)
            {
                var card = _ui.Card(body, 1f, 8, Ui.RarityOf(t.Rarity).Color);
                _ui.Text(CategoryName(t.Category).ToUpperInvariant() + " · " + t.NamePl, card, 13, Ui.Gold, bold: true);
                var mods = StatsText(t.Modifiers);
                _ui.Text(mods.Length > 0 ? mods : "Bez premii do cech.", card, 12, Ui.Ink, maxLines: 2);
            }

            var path = _ui.Card(body, 1.4f, 8, Ui.QiBlue);
            _ui.Text("ŚCIEŻKA " + PathName(c.Path).ToUpperInvariant(), path, 13, Ui.Gold, bold: true);
            _ui.Text(_catalog.Paths != null && _catalog.Paths.TryGetValue(c.Path, out var pp) ? pp.DescriptionPl : string.Empty, path, 12, Ui.Ink, maxLines: 4);
            _ui.Btn("Droga 66 królestw — co otworzy każde", () => Run(ShowRoadSheetAsync), path, secondary: true, sp: 12, heightDp: 32);
            var titles = _ui.Card(body, 1f, 8, Ui.Violet);
            _ui.Text("TYTUŁY (" + _board.Titles.Count + ")", titles, 13, Ui.Gold, bold: true);
            var tb = StatsText(_board.TitleBonuses);
            _ui.Text(tb.Length > 0 ? tb : "Zdobądź tytuły za osiągnięcia — każdy daje stałą premię do cech w walce.", titles, 12, Ui.Ink, maxLines: 3);
            if (_board.Zodiac is { } z)
            {
                var zc = _ui.Card(body, 0.8f, 8, z.LuckyToday ? Ui.Gold : Ui.GoldDim);
                _ui.Text("ZNAK URODZENIA: " + z.Hanzi + " " + z.NamePl.ToUpperInvariant(), zc, 13, Ui.Gold, bold: true);
                _ui.Text(z.DescriptionPl + " +" + z.Bonus + " " + StatName(z.Stat) + (z.LuckyToday ? " — dziś twój dzień (podwójnie)!" : " · dziś dzień: " + z.TodayNamePl)
                    + (z.PhysiqueNamePl != null ? "\n✦ " + z.PhysiqueNamePl + ": " + z.PhysiqueDescriptionPl : string.Empty), zc, 12, Ui.Ink, maxLines: 3);
            }
        });

        // ---- Postać: the silhouette with five equipment slots ---------------------------------------------------

        private async Task ShowHeroAsync()
        {
            var c = _me;
            var economy = new EconomyFlow(_api, c.Id);
            await economy.LoadAsync(Ct);
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

            var ui = HeroScreen(0, Resource.Drawable.bg_hero);
            var page = ui.Content;
            var inv = economy.Inventory;
            var doll = new HeroDollView(this) { PetNpcId = pet?.NpcId };
            foreach (var slot in HeroDollView.Slots)
                doll.Equipped[slot] = inv.Equipped.TryGetValue(slot, out var id) ? inv.Items.FirstOrDefault(i => i.Id == id) : null;
            ui.AddWeighted(page, doll, 1f);
            _power = (await _api.GetCombatScoutAsync(c.Id, Ct)).MyPower;
            _board ??= await _api.GetQuestsAsync(c.Id, Ct);
            ui.Text("⚔ MOC BOJOWA " + _power.ToString("N0"), page, 15, Ui.Cinnabar, bold: true, center: true);
            ui.IconRow(page, new List<(string, string, System.Action, bool, string)>
            {
                ("Meridiany", "ink_ico_meridian", () => Run(() => ShowHeroTabAsync(1)), meridians != null && meridians.Free > 0, null),
                ("Techniki", "ink_nav_combat", () => Run(() => ShowHeroTabAsync(2)), false, null),
                ("Towarzysz", "ink_ico_beast", () => Run(() => ShowHeroTabAsync(3)), false, null),
                ("Dao", "ink_ico_dao", () => Run(() => ShowHeroTabAsync(4)), false, Unlocked("battles_won") ? null : "1. wygrana"),
                ("Sztuki", "ink_ico_alchemy", () => Run(() => ShowHeroTabAsync(5)), false, Unlocked("battles_won") ? null : "1. wygrana"),
            }, 58);
            var info = ui.HRow(page, 34, 4);
            ui.Btn("Cechy, ścieżka i tytuły", () => ShowOriginsSheet(), info, secondary: true, sp: 12, heightDp: 34, icon: "menu_hero");
            ui.Btn("Premie", () => Run(ShowBonusesSheetAsync), info, secondary: true, sp: 12, heightDp: 34);

            // combat stats: base + equipment + meridians/realm + pet, 3×3
            var statsCard = ui.Card(page, 0f, 8);
            statsCard.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(96)) { TopMargin = Px(6) };
            LinearLayout row = null;
            var k = 0;
            // gear, pet and titles resonate with the realm: ×(10 + realm) / 10 (rules combat-5; realm bonus is 2 per realm)
            var resonance = 10 + (meridians == null ? 0 : meridians.RealmBonus / 2);
            foreach (var st in c.Stats)
            {
                if (k++ % 3 == 0) row = ui.HRow(statsCard, 24, 2);
                var eq = (inv.Bonuses.TryGetValue(st.Id, out var e) ? e : 0) * resonance / 10;
                var mer = meridians == null ? 0 : (meridians.Allocated.TryGetValue(st.Id, out var a) ? a : 0) + meridians.RealmBonus;
                var pb = (pet != null && pet.Bonuses.TryGetValue(st.Id, out var b) ? b : 0) * resonance / 10;
                var tb = (_board?.TitleBonuses != null && _board.TitleBonuses.TryGetValue(st.Id, out var tv) ? tv : 0) * resonance / 10;
                var total = st.Value + eq + mer + pb + tb;
                var cell = ui.Box(row, 1f, horizontal: true, marginDp: 4);
                var statId = st.Id;
                cell.Click += (_, _) => SetStatus(StatName(statId) + ": " + StatHint(statId));
                ui.Icon(cell, "icon_" + st.Id, 18);
                var name = StatName(st.Id);
                ui.Text((name.Length > 9 ? name.Substring(0, 8) + "." : name) + " " + total, cell, 11, total > st.Value ? Ui.Gold : Ui.Ink, bold: total > st.Value)
                    .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(4), Gravity = GravityFlags.CenterVertical };
            }

            doll.SlotTapped += slot =>
            {
                Sfx.Play(Sfx.Click);
                var current = doll.Equipped.TryGetValue(slot, out var it) ? it : null;
                var body = Sheet(HeroDollView.SlotName(slot), 0.62f, out var close, current != null && ui.ArtId("item_" + current.ItemId) != 0 ? "item_" + current.ItemId : "item_slot_empty");
                if (current != null)
                {
                    var cc = ui.Card(body, 0f, 8, Ui.RarityOf(current.Rarity).Color);
                    cc.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(108));
                    ui.Text(current.NamePl + (current.UpgradeLevel > 0 ? " +" + current.UpgradeLevel : string.Empty) + "  ·  " + Ui.RarityOf(current.Rarity).Name, cc, 15, Ui.Gold, bold: true);
                    ui.Text(string.Join("  ·  ", current.Stats.Select(x => StatName(x.Key) + " +" + x.Value)), cc, 12, Ui.Ink, maxLines: 2);
                    var acts = ui.HRow(cc, 40, 4);
                    ui.Btn("Zdejmij", () => Run(async () =>
                    {
                        close();
                        var before = _power;
                        await economy.ToggleEquipAsync(current, Ct);
                        await ShowHeroAsync();
                        PowerDelta(before);
                    }), acts, secondary: true, sp: 12, heightDp: 40);
                    var next = economy.NextUpgrade(current);
                    if (next != null)
                        ui.Btn("⚒ +" + next.Level + " (" + next.Stones + " KD, " + next.SuccessPermille / 10 + "%)", () => Run(async () =>
                        {
                            var before = _power;
                            var r = await economy.UpgradeAsync(current, Ct);
                            close();
                            Sfx.Play(r != null && r.Success ? Sfx.Breakthrough : Sfx.Defeat, 0.6f);
                            await ShowHeroAsync();
                            SetStatus(r == null ? "Odświeżono." : r.Success ? "Udane! Teraz +" + r.Level + PowerText(before) : "Nieudane — poziom +" + r.Level + " zachowany.");
                            RefreshHud();
                        }), acts, sp: 12, heightDp: 40);
                }

                var options = inv.Items.Where(i => i.Slot == slot && i.InBag).ToList();
                ui.Text("W sakwie:", body, 12, Ui.Muted);
                ui.Pages(body, options.Count, 1, 3, (i, cell) =>
                {
                    var item = options[i];
                    var card = ui.Card(cell, 1f, 6, Ui.RarityOf(item.Rarity).Color, 0);
                    var r = ui.HRow(card, 44, 0);
                    ui.Icon(r, ui.ArtId("item_" + item.ItemId) != 0 ? "item_" + item.ItemId : "item_slot_empty", 38);
                    var col = ui.Box(r, 1f, marginDp: 8);
                    ui.Text(item.NamePl + (item.UpgradeLevel > 0 ? " +" + item.UpgradeLevel : string.Empty), col, 13, Ui.Ink, bold: true);
                    ui.Text(string.Join(" ", item.Stats.Select(x => StatName(x.Key) + "+" + x.Value)), col, 11, Ui.Muted);
                    ui.Btn("Załóż", () => Run(async () =>
                    {
                        close();
                        var before = _power;
                        await economy.ToggleEquipAsync(item, Ct);
                        Sfx.Play(Sfx.Chime, 0.6f);
                        await ShowHeroAsync();
                        PowerDelta(before);
                    }), r, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(84), Px(38)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Brak przedmiotów na ten slot — wykuj je w Kuźni lub kup na targu.");
            };
        }

        /// <summary>" · moc +120" after a change that moved the combat power (empty when it did not).</summary>
        private string PowerText(long before) =>
            before <= 0 || _power == before ? string.Empty : " · moc " + (_power > before ? "+" : "−") + Math.Abs(_power - before).ToString("N0");

        private void PowerDelta(long before)
        {
            var t = PowerText(before);
            if (t.Length > 0) SetStatus("⚔ Moc " + _power.ToString("N0") + " (" + t.Substring(3) + ")");
        }

        // ---- Meridiany ----------------------------------------------------------------------------------------

        private async Task ShowMeridiansAsync()
        {
            var c = _me;
            var m = await _api.GetMeridiansAsync(c.Id, Ct);
            var ui = HeroScreen(1, Resource.Drawable.bg_meditation);
            var page = ui.Content;
            var head = ui.HRow(page, 40);
            ui.Icon(head, "menu_meridians", 34);
            var col = ui.Box(head, 1f, marginDp: 8);
            ui.Text("Wolne punkty: " + m.Free + "  (zdobyte " + m.PointsEarned + ", otwarte " + m.PointsSpent + ")", col, 14, m.Free > 0 ? Ui.Gold : Ui.Ink, bold: true);
            ui.Text("1 punkt za każdy etap · królestwo: +" + m.RealmBonus + " do wszystkich cech", col, 11, Ui.Muted);
            var view = new MeridianView(this, StatName) { State = m };
            ui.AddWeighted(page, view, 1f);
            ui.Text(m.Free > 0 ? "Dotknij punktu, by otworzyć meridian (+1 do cechy w walce)." : "Zdobądź kolejny etap kultywacji, by otworzyć następny meridian.", page, 11, Ui.Muted, center: true, maxLines: 2);
            var actions = ui.HRow(page, 42);
            ui.Btn(m.PointsSpent > 0 ? "Zamknij wszystkie (" + m.ResetQiCost + " Qi)" : "Brak otwartych meridianów", () =>
            {
                if (m.PointsSpent == 0) return;
                Run(async () =>
                {
                    await _api.ResetMeridiansAsync(c.Id, Ct);
                    await ShowMeridiansAsync();
                    SetStatus("Meridiany zamknięte — punkty wróciły.");
                    RefreshHud();
                });
            }, actions, secondary: true, sp: 12, heightDp: 42);
            view.NodeTapped += stat =>
            {
                if (m.Free <= 0)
                {
                    SetStatus("Brak wolnych punktów — osiągnij kolejny etap.");
                    return;
                }

                Run(async () =>
                {
                    var after = await _api.OpenMeridianAsync(c.Id, stat, m.PointsSpent, Ct);
                    Sfx.Play(Sfx.Chime);
                    m = after;
                    view.State = after;
                    view.Invalidate();
                    await ShowMeridiansAsync();
                    SetStatus(StatName(stat) + " +1");
                });
            };
        }

        // ---- Techniki: the 8 combat slots ---------------------------------------------------------------------

        private async Task ShowTechniquesAsync()
        {
            var c = _me;
            var loadout = await _api.GetLoadoutAsync(c.Id, Ct);
            var ui = HeroScreen(2, Resource.Drawable.bg_meditation);
            var page = ui.Content;
            ui.Text("Walka toczy się sama — postać używa tych technik. Dotknij slotu, by zmienić.", page, 11, Ui.Muted, center: true, maxLines: 2);
            ui.Btn("Ułóż najlepsze", () => Run(async () =>
            {
                var best = LoadoutAdvisor.Best(loadout);
                await _api.SetLoadoutAsync(c.Id, loadout.Version, best, Ct);
                Sfx.Play(Sfx.Chime, 0.6f);
                await ShowTechniquesAsync();
                SetStatus("Ułożono najsilniejsze odblokowane techniki.");
            }), page, sp: 13, heightDp: 40, icon: "menu_daolab");
            var slots = loadout.Slots.OrderBy(s => s.Slot).ToList();
            var grid = ui.Pages(page, slots.Count, 2, 4, (i, cell) =>
            {
                var slot = slots[i];
                var card = ui.Card(cell, 1f, 6, slot.TechniqueId == null ? (Color?)null : slot.Kind == "attack" ? Ui.Cinnabar : Ui.QiBlue, 0);
                card.Clickable = true;
                card.SetGravity(GravityFlags.CenterVertical);
                var row = ui.HRow(card, 40, 0);
                var icon = slot.TechniqueId != null && ui.ArtId("tech_" + slot.TechniqueId) != 0 ? "tech_" + slot.TechniqueId : "icon_" + slot.Kind;
                ui.Icon(row, icon, 36);
                var col = ui.Box(row, 1f, marginDp: 6);
                ui.Text(slot.NamePl ?? "— pusty —", col, 12, slot.TechniqueId == null ? Ui.Muted : Ui.Ink, bold: true, maxLines: 1);
                ui.Text(ActionName(slot.Kind) + (slot.Level > 0 ? " · poz. " + slot.Level : string.Empty) + (slot.QiCost > 0 ? " · " + slot.QiCost + " Qi" : string.Empty), col, 10, Ui.Muted);
                card.Click += (_, __) => PickTechnique(loadout, slot);
            });

            // 1.8: saved sets (PvE / PvP / Próba) — one tap to switch; kept on this phone
            var presets = ui.HRow(page, 40);
            foreach (var name in new[] { "PvE", "PvP", "Próba" })
            {
                var key = "preset_" + c.Id + "_" + name;
                var saved = _prefs.GetString(key, null);
                ui.Btn(name + (saved != null ? " ▶" : " ＋"), () => Run(async () =>
                {
                    if (saved == null)
                    {
                        _prefs.Edit()!.PutString(key, string.Join(",", slots.Select(x => x.TechniqueId ?? string.Empty)))!.Apply();
                        await ShowTechniquesAsync();
                        SetStatus("Zapisano zestaw " + name + ". Przytrzymaj, by nadpisać.");
                        return;
                    }

                    var ids = saved.Split(',').Select(x => string.IsNullOrEmpty(x) ? null : x).ToList();
                    await _api.SetLoadoutAsync(c.Id, loadout.Version, ids, Ct);
                    await ShowTechniquesAsync();
                    SetStatus("Założono zestaw " + name + ".");
                }), presets, secondary: true, sp: 12, heightDp: 38).LongClick += (_, __) =>
                {
                    _prefs.Edit()!.PutString(key, string.Join(",", slots.Select(x => x.TechniqueId ?? string.Empty)))!.Apply();
                    SetStatus("Nadpisano zestaw " + name + ".");
                };
            }
        }

        private void PickTechnique(LoadoutDto loadout, LoadoutSlotDto slot)
        {
            var c = _me;
            var body = Sheet("Slot " + (slot.Slot + 1) + " · " + ActionName(slot.Kind), 0.62f, out var close, "icon_" + slot.Kind);
            async Task Set(string techniqueId)
            {
                var ids = loadout.Slots.OrderBy(x => x.Slot).Select(x => x.Slot == slot.Slot ? techniqueId : x.TechniqueId).ToList();
                await _api.SetLoadoutAsync(c.Id, loadout.Version, ids, Ct);
                close();
                Sfx.Play(Sfx.Chime, 0.6f);
                await ShowTechniquesAsync();
                SetStatus("Zestaw zapisany.");
            }

            var options = loadout.Known.Where(k => k.Slot == slot.Kind).ToList();
            ui_Pages();

            void ui_Pages()
            {
                _ui.Pages(body, options.Count, 1, 4, (i, cell) =>
                {
                    var known = options[i];
                    var card = _ui.Card(cell, 1f, 6, known.Id == slot.TechniqueId ? Ui.Gold : (Color?)null, 0);
                    var row = _ui.HRow(card, 44, 0);
                    _ui.Icon(row, _ui.ArtId("tech_" + known.Id) != 0 ? "tech_" + known.Id : "tech_custom_" + (slot.Kind == "special" ? "movement" : slot.Kind == "attack" ? "attack" : "defense"), 38);
                    var col = _ui.Box(row, 1f, marginDp: 8);
                    _ui.Text(known.NamePl, col, 13, known.Locked ? Ui.Muted : Ui.Ink, bold: true);
                    if (known.Locked)
                    {
                        _ui.Text("🔒 od " + (known.MinRealm + 1) + ". królestwa", col, 11, Ui.Muted);
                        return;
                    }

                    _ui.Text("poz. " + known.Level + (known.LevelCap < 10 ? " (limit " + known.LevelCap + ")" : string.Empty) + " · " + known.Xp + "/" + known.XpToNextLevel + " XP"
                             + (known.DamageMin != null ? " · obrażenia " + known.DamageMin + "–" + known.DamageMax : string.Empty), col, 11, Ui.Muted);
                    if (!string.IsNullOrEmpty(known.DescriptionPl)) _ui.Text(known.DescriptionPl, col, 10, Ui.Jade, maxLines: 3);
                    if (known.Id != slot.TechniqueId)
                        _ui.Btn("Wybierz", () => Run(() => Set(known.Id)), row, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(88), Px(38)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Nie znasz jeszcze technik tego rodzaju — stwórz je w Laboratorium Dao.");
            }

            if (slot.TechniqueId != null) _ui.Btn("Opróżnij slot", () => Run(() => Set(null)), body, secondary: true, sp: 13, heightDp: 42);
        }

        // ---- Premie (1.7) -------------------------------------------------------------------------------------

        /// <summary>The Road of 66 Realms: every realm with its tribulation and what it opens for this path, starting at the current one.</summary>
        private async Task ShowRoadSheetAsync()
        {
            var road = await _api.GetRoadAsync(_me.Id, Ct);
            var body = Sheet("Droga 66 królestw", 0.85f, out _, "menu_breakthrough");
            _ui.Text("Jesteś w królestwie " + (road.Realm + 1) + " z " + road.Realms + ". Złote — przed tobą najbliższe progi.", body, 11, Ui.Muted, maxLines: 2);
            var steps = road.Steps;
            var start = Math.Max(0, steps.FindIndex(s => s.Current));
            const int PerPage = 3;
            _ui.Pages(body, steps.Count, 1, PerPage, (i, cell) =>
            {
                var s = steps[i];
                var card = _ui.Card(cell, 1f, 6, s.Current ? Ui.Gold : s.Reached ? Ui.Jade : (Color?)null, 0);
                _ui.Text((s.Index + 1) + ". " + s.NamePl + " " + s.Hanzi + " · " + s.EraPl, card, 13, s.Current ? Ui.Gold : s.Reached ? Ui.Muted : Ui.Ink, bold: true);
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(s.TribulationPl)) lines.Add("⚡ " + s.TribulationPl);
                if (!string.IsNullOrEmpty(s.UnlockPl)) lines.Add(s.UnlockPl);
                lines.AddRange(s.Unlocks);
                _ui.Text(lines.Count == 0 ? "—" : string.Join(" · ", lines), card, 11, Ui.Ink, maxLines: 4);
            }, startPage: start / PerPage);
        }

        private async Task ShowBonusesSheetAsync()
        {
            var b = await _api.GetBonusesAsync(_me.Id, Ct);
            var body = Sheet("Premie w walce", 0.8f, out _, "menu_hero");
            _ui.Text("Suma (po limitach): " + PerksText(b.TotalPerks) + (b.HpBonusPermille != 0 ? " · życie +" + b.HpBonusPermille / 10 + "%" : string.Empty), body, 12, Ui.Gold, bold: true, maxLines: 4);
            _ui.Text("W PvP: " + PerksText(b.PvpPerks), body, 11, Ui.Muted, maxLines: 3);
            _ui.Text("Rezonans ekwipunku, tytułów i towarzysza: ×" + (b.ResonancePermille / 1000.0).ToString("0.#") + " (królestwo " + (b.Realm + 1) + ")", body, 11, Ui.Jade, maxLines: 2);
            var sources = b.Sources.Where(x => x.Stats.Values.Any(v => v != 0) || x.Perks.Values.Any(v => v != 0)).ToList();
            _ui.Pages(body, sources.Count, 1, 4, (i, cell) =>
            {
                var src = sources[i];
                var card = _ui.Card(cell, 1f, 6, null, 0);
                _ui.Text(src.NamePl, card, 13, Ui.Ink, bold: true);
                var stats = string.Join("  ", src.Stats.Where(x => x.Value != 0).Select(x => StatName(x.Key) + " +" + x.Value));
                var perks = PerksText(src.Perks);
                _ui.Text(string.Join(" · ", new[] { stats, perks }.Where(t => !string.IsNullOrEmpty(t))), card, 11, Ui.Muted, maxLines: 3);
            });
        }

        // ---- Towarzysz ----------------------------------------------------------------------------------------

        private async Task ShowPetAsync()
        {
            var c = _me;
            var pet = (await _api.GetPetAsync(c.Id, Ct)).Pet;
            var ui = HeroScreen(3, Resource.Drawable.tile_menu_pet);
            var page = ui.Content;
            if (pet == null)
            {
                var card = ui.Card(page, 1f, 14);
                card.SetGravity(GravityFlags.Center);
                ui.Icon(card, "menu_pet", 72).LayoutParameters = new LinearLayout.LayoutParams(Px(72), Px(72)) { Gravity = GravityFlags.CenterHorizontal };
                ui.Text("Nie masz jeszcze towarzysza.", card, 17, Ui.Gold, bold: true, center: true);
                ui.Text("Pokonaj bestię oznaczoną 🐾 — w treningu lub w świecie. Po zwycięstwie jest szansa (wyższa z Losem), że pójdzie za tobą. Towarzysz rośnie z każdą wygraną i dodaje część swoich cech do twoich w walce.",
                    card, 13, Ui.Ink, center: true, maxLines: 6);
                ui.Btn("Do treningu", () => { _combatSub = 0; GoTab(TabCombat); }, page, sp: 14, heightDp: 46);
                return;
            }

            var tileCell = ui.Box(page, 1f);
            ui.TileIn(tileCell, pet.NamePl, "Poziom " + pet.Level, NpcLooks.For(pet.NpcId).TileArt, () => Sfx.Play(Sfx.Chime, 0.5f), pixelArt: true, accent: Ui.Jade, titleSp: 18);
            var info = ui.Card(page, 0f, 10);
            info.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(118)) { TopMargin = Px(6) };
            ui.MeterIn(info, Ui.Jade, pet.XpToNextLevel > 0 ? "XP " + pet.Xp + " / " + (pet.Xp + pet.XpToNextLevel) : "Poziom maksymalny",
                pet.XpToNextLevel > 0 ? pet.Xp : 1, pet.XpToNextLevel > 0 ? pet.Xp + pet.XpToNextLevel : 1, 18);
            ui.Text("Premie w walce: " + string.Join("  ·  ", pet.Bonuses.Select(kv => StatName(kv.Key) + " +" + kv.Value)), info, 12, Ui.Gold, bold: true, maxLines: 2);
            ui.Text((pet.StagePl ?? "Młody") + " (stadium " + (pet.Stage + 1) + "/4, poziom maks. " + pet.LevelCap + ") · oswojony " + pet.TamedAt.ToLocalTime().ToString("dd.MM.yyyy"), info, 11, Ui.Muted);
            if (pet.EvolveStones > 0)
                ui.Btn(pet.CanEvolve ? "Ewolucja: " + pet.EvolveStones + " KD + " + pet.EvolveQuantity + "× krew demona" : "Ewolucja od poziomu " + pet.LevelCap, () => Run(async () =>
                {
                    var r = await _api.EvolvePetAsync(c.Id, Ct);
                    Sfx.Play(Sfx.Breakthrough, 0.8f);
                    await ShowPetAsync();
                    ShowBanner("EWOLUCJA", pet.NamePl, r.Pet?.StagePl ?? string.Empty, "menu_hero");
                }), page, secondary: !pet.CanEvolve, sp: 13, heightDp: 42);
            var confirm = false;
            Button release = null;
            release = ui.Btn("Uwolnij towarzysza", () =>
            {
                if (!confirm)
                {
                    confirm = true;
                    release.Text = "Na pewno? Dotknij ponownie";
                    return;
                }

                Run(async () =>
                {
                    await _api.ReleasePetAsync(c.Id, Ct);
                    await ShowPetAsync();
                    SetStatus(pet.NamePl + " wraca na wolność.");
                });
            }, page, secondary: true, sp: 13, heightDp: 42);
        }

        // ---- Laboratorium Dao ---------------------------------------------------------------------------------

        private async Task ShowDaoLabAsync()
        {
            var c = _me;
            var lab = new DaoLabFlow(_api, c.Id);
            await lab.LoadAsync(Ct);
            var economy = new EconomyFlow(_api, c.Id);
            await economy.LoadAsync(Ct);
            var ui = HeroScreen(4, Resource.Drawable.bg_meditation);
            var page = ui.Content;
            var form = ui.Card(page, 1f, 10);
            ui.Text("Ułóż własną technikę ataku. Serwer liczy jej moc i pilnuje budżetu twojego poziomu.", form, 12, Ui.Muted, maxLines: 2);
            var name = ui.Field("Nazwa techniki", parent: form);
            var r1 = ui.HRow(form, 60, 2);
            var power = ui.Field("Moc 1–30", number: true, parent: ui.Box(r1, 1f, marginDp: 6));
            var qi = ui.Field("Qi 0–20", number: true, parent: ui.Box(r1, 1f, marginDp: 6));
            var r2 = ui.HRow(form, 60, 2);
            var cooldown = ui.Field("Odnowienie 0–4", number: true, parent: ui.Box(r2, 1f, marginDp: 6));
            var reach = ui.Field("Zasięg 1–3", number: true, parent: ui.Box(r2, 1f, marginDp: 6));
            var verdict = ui.Text("Ułóż technikę i sprawdź budżet.", form, 12, Ui.Ink, bold: true, maxLines: 3);

            DaoDesignDto Compose()
            {
                var d = DaoLabFlow.StarterStrike(name.Text.Trim());
                if (int.TryParse(power.Text, out var p)) d.Attack.Power = p;
                if (int.TryParse(qi.Text, out var q)) d.QiCost = q;
                if (int.TryParse(cooldown.Text, out var cd)) d.Cooldown = cd;
                if (int.TryParse(reach.Text, out var r)) d.Attack.RangeMax = r;
                return d;
            }

            string DescribePreview(DaoPreviewDto p) =>
                (p.Valid ? "✔ Dozwolona" : "✘ Niedozwolona") + " — moc " + p.Points + " / budżet " + p.Allowance +
                (p.Problems.Count > 0 ? "\n" + string.Join("; ", p.Problems) : string.Empty);

            var buttons = ui.HRow(page, 46);
            ui.Btn("Sprawdź", () => Run(async () => verdict.Text = DescribePreview(await lab.PreviewAsync(Compose(), Ct))), buttons, secondary: true, sp: 13, heightDp: 46);
            ui.Btn("Stwórz (KD)", () => Run(async () =>
            {
                var created = await lab.CreateAsync(Compose(), Ct);
                if (created == null)
                {
                    verdict.Text = DescribePreview(lab.LastPreview);
                    return;
                }

                Sfx.Play(Sfx.Breakthrough, 0.6f);
                await ShowDaoLabAsync();
                ShowBanner("NOWA TECHNIKA", created.Technique.NamePl, "Załóż ją w zakładce Techniki.", "ink_popup_technique");
                RefreshHud();
            }), buttons, sp: 13, heightDp: 46);

            var scrolls = economy.Inventory.Items.Where(i => i.BoundRef != null && i.InBag).ToList();
            var more = ui.HRow(page, 42);
            ui.Btn("Znane techniki (" + lab.Known.Count + ")", () =>
            {
                var body = Sheet("Twoje techniki Dao", 0.62f, out var close, "menu_daolab");
                ui.Pages(body, lab.Known.Count, 1, 4, (i, cell) =>
                {
                    var known = lab.Known[i];
                    var card = ui.Card(cell, 1f, 6, null, 0);
                    var row = ui.HRow(card, 40, 0);
                    ui.Text(known.Technique.NamePl + " · " + ActionName(known.Technique.Slot), row, 13, Ui.Ink, bold: true);
                    ui.Btn("Załóż", () => Run(async () =>
                    {
                        var ok = await lab.EquipAsync(known.Technique.Id, known.Technique.Slot, Ct);
                        close();
                        SetStatus(ok ? "Technika w zestawie walki." : "Zestaw zmienił się na innym urządzeniu — odświeżono.");
                    }), row, sp: 11, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(70), Px(36)) { Gravity = GravityFlags.CenterVertical };
                    if (known.Source == "designed")
                        ui.Btn("Zwój", () => Run(async () =>
                        {
                            await lab.InscribeAsync(known.Technique.Id, Ct);
                            close();
                            SetStatus("Zwój trafił do sakwy — możesz go wystawić na targu.");
                        }), row, secondary: true, sp: 11, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(70), Px(36)) { Gravity = GravityFlags.CenterVertical, LeftMargin = Px(4) };
                }, empty: "Jeszcze żadnej — stwórz pierwszą.");
            }, more, secondary: true, sp: 12, heightDp: 42);
            ui.Btn("Zwoje (" + scrolls.Count + ")", () => Run(async () =>
            {
                // 1.8: what each scroll teaches (forged techniques have a rarity and a generated description)
                var infos = new Dictionary<string, ForgedScrollDto>();
                foreach (var id in scrolls.Select(x => x.BoundRef).Distinct())
                {
                    try { infos[id] = await _api.GetScrollInfoAsync(id, Ct); }
                    catch (JianghuGo.Client.Application.ApiException) { }
                }

                var body = Sheet("Zwoje technik", 0.62f, out var close, "menu_scroll");
                ui.Pages(body, scrolls.Count, 1, 3, (i, cell) =>
                {
                    var scroll = scrolls[i];
                    var info = infos.TryGetValue(scroll.BoundRef, out var inf) ? inf : null;
                    var row = ui.HRow(cell, 44, 0);
                    ui.Icon(row, "item_technique_scroll", 36);
                    var col = ui.Box(row, 1f, marginDp: 6);
                    ui.Text(info != null ? info.NamePl + " · " + info.RarityPl : scroll.BoundRef, col, 13, info != null ? Ui.RarityOf(ForgeRarity(info.Rarity)).Color : Ui.Ink, bold: true);
                    if (info != null) ui.Text(info.DescriptionPl, col, 10, Ui.Muted, maxLines: 2);
                    ui.Btn("Przeczytaj", () => Run(async () =>
                    {
                        var learned = await lab.LearnAsync(scroll, Ct);
                        close();
                        await ShowDaoLabAsync();
                        SetStatus("Poznano technikę: " + learned.Technique.NamePl);
                    }), row, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(100), Px(38)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Brak zwojów w sakwie.");
            }), more, secondary: true, sp: 12, heightDp: 42);

            // 1.8: the forge — a daily meditation and essence transmutation
            var forge = ui.HRow(page, 42);
            var useToken = false;
            Button meditate = null;
            meditate = ui.Btn("Medytacja nad Dao", () => Run(async () =>
            {
                ForgedScrollDto s;
                try
                {
                    s = await _api.MeditateOnDaoAsync(c.Id, Ct, useToken);
                }
                catch (ApiException e) when (e.Code == "dao_meditation_done")
                {
                    // Today's is used: the next tap spends a Destiny Token (1.9).
                    useToken = true;
                    meditate.Text = "☯ Jeszcze raz (1 Żeton Losu)";
                    SetStatus("Dziś już medytowano. Kolejna medytacja kosztuje Żeton Losu.");
                    return;
                }

                Sfx.Play(Sfx.Breakthrough, 0.7f);
                await ShowDaoLabAsync();
                ShowBanner("ZWÓJ " + s.RarityPl.ToUpperInvariant(), s.NamePl, s.DescriptionPl, "item_technique_scroll");
                RefreshHud();
            }), forge, secondary: true, sp: 12, heightDp: 42, icon: "menu_meditate");
            var essence = economy.Inventory.Stacks.FirstOrDefault(x => x.ItemId == "dao_essence")?.Quantity ?? 0;
            ui.Btn("Przemiana esencji (" + essence + "/10)", () => Run(async () =>
            {
                var s = await _api.TransmuteAsync(c.Id, null, Ct);
                Sfx.Play(Sfx.Breakthrough, 0.7f);
                await ShowDaoLabAsync();
                ShowBanner("ZWÓJ " + s.RarityPl.ToUpperInvariant(), s.NamePl, s.DescriptionPl, "item_technique_scroll");
            }), forge, secondary: true, sp: 12, heightDp: 42);
        }

        /// <summary>Forge rarities on the item rarity palette (1.8).</summary>
        private static string ForgeRarity(string r) => r switch
        {
            "rare" => "spirit",
            "epic" => "earth",
            "legendary" => "heavenly",
            "mythic" => "divine",
            _ => "mortal",
        };
    }
}
