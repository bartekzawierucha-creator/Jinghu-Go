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
    /// MVP 1.5 screens: the cultivation extras (retreat, vessel, methods, tribulation, offline summary), the path arts
    /// (flames, venoms, Gu, tempering, sword intent, branches), the estate (garden, disciples, expeditions), the arena,
    /// sect development and the cauldron / forge queue.
    /// </summary>
    public sealed partial class MainActivity
    {
        private static readonly string[] ArtsTabs = { "Płomienie", "Trucizny", "Gu", "Ciało", "Miecz", "Gałąź" };

        // ---- perks as text ------------------------------------------------------------------------------------

        private static string PerksText(IDictionary<string, int> perks, string separator = " · ")
        {
            if (perks == null) return string.Empty;
            var parts = new List<string>();
            foreach (var (k, v) in perks)
            {
                if (v == 0) continue;
                var pct = (v / 10.0).ToString("0.#");
                parts.Add(k switch
                {
                    "damagePermille" => "obrażenia +" + pct + "%",
                    "guardPermille" => "garda +" + pct + "%",
                    "effectChancePermille" => "efekty +" + pct + "%",
                    "bleedPermille" => "krwawienie +" + pct + "%",
                    "healPermille" => "leczenie +" + pct + "%",
                    "qiRegen" => "Qi/tura +" + v,
                    "qiPermille" => "maks. Qi +" + pct + "%",
                    "regenPermille" => "regeneracja " + pct + "%/tura",
                    "reflectPermille" => "odbicie " + pct + "%",
                    "lifestealPermille" => "wampiryzm " + pct + "%",
                    "firstStrikePermille" => "pierwszy cios +" + pct + "%",
                    "hpPermille" => "życie +" + pct + "%",
                    "surviveOnce" => "przetrwa śmiertelny cios",
                    "petPower" => "towarzysz " + v + " obr.",
                    "petEvery" => "co " + v + " tury",
                    _ => k + " " + v,
                });
            }

            return string.Join(separator, parts);
        }

        private static string Until(DateTimeOffset? at)
        {
            if (at is not { } t) return string.Empty;
            var left = t - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero) return "gotowe";
            return left.TotalHours >= 1 ? (int)left.TotalHours + " h " + left.Minutes + " min" : Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " min";
        }

        // ---- cultivation extras --------------------------------------------------------------------------------

        /// <summary>What happened while away: shown once, then acknowledged on the server.</summary>
        private void ShowOfflineSummary(CultivationFlow flow, System.Action render)
        {
            var s = flow.State?.Summary;
            if (s == null) return;
            var hours = Math.Max(0, (s.To - s.From).TotalHours);
            var extra = new List<string>();
            if (s.Enlightenments > 0) extra.Add(s.Enlightenments + "× oświecenie");
            if (s.AutoBreakthroughs > 0) extra.Add(s.AutoBreakthroughs + "× auto-przełom");
            if (s.VesselFull) extra.Add("naczynie pełne");
            if (s.RetreatFinished) extra.Add("odosobnienie zakończone");
            ShowBanner("GDY CIĘ NIE BYŁO", "+" + s.Qi.ToString("N0") + " Qi w " + hours.ToString("0.#") + " h", extra.Count > 0 ? string.Join(" · ", extra) : "Qi płynęło samo.", "ink_popup_chest");
            Run(async () =>
            {
                await flow.ApplyAsync(_api.AcknowledgeSummaryAsync, Ct);
                render();
            });
        }

        private void RetreatSheet(CultivationFlow flow, System.Action render)
        {
            var st = flow.State;
            if (st == null) return;
            var ui = _ui;
            var body = Sheet("Odosobnienie", 0.5f, out var close, "menu_meditate");
            Remind("retreat", st.IsInRetreat ? st.RetreatUntil : null, "Odosobnienie zakończone", "Twoje Qi czeka — wróć do Jianghu.");
            if (st.IsInRetreat)
            {
                ui.Text("Trwa odosobnienie (×8 Qi) do " + st.RetreatUntil?.ToLocalTime().ToString("dd.MM HH:mm") + " — zostało " + Until(st.RetreatUntil) + ".", body, 13, Ui.Ink, maxLines: 3);
                ui.Text("Przerwanie wcześniej grozi odchyleniem Qi.", body, 11, Ui.Cinnabar);
                ui.Box(body, 1f, marginDp: 0);
                ui.Btn("Wyjdź z odosobnienia", () => Run(async () =>
                {
                    close();
                    await flow.ApplyAsync(_api.LeaveRetreatAsync, Ct);
                    render();
                }), body, secondary: true, sp: 14, heightDp: 46);
                return;
            }

            if (st.RetreatOptions.Count == 0)
            {
                ui.Text("Odosobnienie otwiera się od 3. królestwa. Do tego czasu medytuj (×5) — Qi płynie też samo (×1).", body, 13, Ui.Muted, maxLines: 4);
                return;
            }

            ui.Text("Zamknięte drzwi: Qi płynie ×8, ale nie możesz walczyć ani zbierać. Rób to, gdy nie grasz (noc, praca).", body, 12, Ui.Ink, maxLines: 3);
            ui.Text("≈ " + Math.Round(st.RetreatPerHour) + " Qi/h · naczynie " + st.Qi + "/" + st.Vessel, body, 12, Ui.Gold, bold: true);
            var row = ui.HRow(body, 52);
            foreach (var h in st.RetreatOptions)
                ui.Btn(h + " h", () => Run(async () =>
                {
                    close();
                    await flow.ApplyAsync((id, ct) => _api.StartRetreatAsync(id, h, ct), Ct);
                    Sfx.Play(Sfx.Chime);
                    render();
                    SetStatus("Odosobnienie na " + h + " h — wróć po owoce.");
                }), row, sp: 15, heightDp: 52);
        }

        /// <summary>Vessel, crystals, accelerator, methods, auto-breakthrough, reincarnation, qualities.</summary>
        private void CultivationSheet(CultivationFlow flow, System.Action render)
        {
            var st = flow.State;
            if (st == null) return;
            var ui = _ui;
            var body = Sheet((st.RealmHanzi ?? string.Empty) + " " + st.RealmNamePl, 0.86f, out var close, "menu_breakthrough");

            async Task Apply(Func<Guid, System.Threading.CancellationToken, Task<CultivationDto>> call, string message)
            {
                close();
                await flow.ApplyAsync(call, Ct);
                render();
                RefreshHud();
                if (message != null) SetStatus(message);
            }

            var info = ui.Card(body, 1.25f, 8, Ui.Gold);
            ui.Text("Era " + st.Era + " · " + st.EraNamePl + " · królestwo " + (st.RealmIndex + 1) + "/" + st.RealmCount + " · tytuł 「" + st.RealmTitlePl + "」", info, 12, Ui.Gold, bold: true, maxLines: 2);
            ui.Text(st.RealmUnlockPl ?? string.Empty, info, 11, Ui.Ink, maxLines: 2);
            if (st.NextRealmNamePl != null) ui.Text("Dalej: " + st.NextRealmNamePl + (st.NextTribulationPl != null ? " — próba: " + st.NextTribulationPl : string.Empty), info, 11, Ui.Muted, maxLines: 2);
            var q = st.Qualities != null && st.Qualities.Count > 0 ? string.Join(" · ", st.Qualities.Select(x => x.Key + ": " + x.Value)) : "—";
            ui.Text("Jakość: " + q + (st.CoreColor != null ? " · rdzeń " + st.CoreColorPl : string.Empty) + " · demony serca " + st.Instability + "%", info, 11, st.Instability >= 50 ? Ui.Cinnabar : Ui.Jade, maxLines: 2);

            // vessel and crystals
            var vessel = ui.Card(body, 1f, 8, Ui.QiBlue);
            ui.Text("Naczynie poz. " + st.VesselLevel + " · pojemność " + st.Vessel.ToString("N0") + " Qi · kryształy " + st.Crystals, vessel, 12, Ui.Ink, bold: true);
            ui.Text("Limit offline " + st.OfflineCapHours + " h · przyspieszacz dnia: " + (st.AcceleratorAvailable ? st.AcceleratorHours + " h gotowy" : "zużyty"), vessel, 11, Ui.Muted);
            var vr = ui.HRow(vessel, 38, 2);
            ui.Btn("Naczynie +1 (" + st.VesselUpgradeCrystals + " kr.)", () => Run(() => Apply(_api.UpgradeVesselAsync, "Naczynie powiększone.")), vr, secondary: true, sp: 11, heightDp: 38);
            if (st.CompressionAvailable)
                ui.Btn("Kompresuj Qi", () => Run(() => Apply((id, ct) => _api.CompressAsync(id, st.Qi / 2, ct), "Połowa Qi skompresowana w kryształy.")), vr, secondary: true, sp: 11, heightDp: 38);
            if (st.AcceleratorAvailable)
                ui.Btn("Przyspiesz " + st.AcceleratorHours + " h", () => Run(() => Apply(_api.UseAcceleratorAsync, "Czas płynie szybciej — Qi z " + st.AcceleratorHours + " h.")), vr, sp: 11, heightDp: 38);

            // methods
            var methods = st.Methods.Where(m => m.Available || m.Known).ToList();
            ui.Text("METODY KULTYWACJI", body, 11, Ui.GoldDim, bold: true);
            ui.Pages(body, methods.Count, 1, 2, (i, cell) =>
            {
                var m = methods[i];
                var card = ui.Card(cell, 1f, 6, m.Active ? Ui.Gold : (Color?)null, 0);
                var r = ui.HRow(card, 34, 0);
                ui.Text((m.Active ? "● " : string.Empty) + m.NamePl + (m.Known ? " · mistrzostwo " + m.Mastery + "/10" : string.Empty), r, 12, m.Active ? Ui.Gold : Ui.Ink, bold: true)
                    .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                if (!m.Active)
                    ui.Btn(m.Known ? "Użyj" : m.ItemId != null ? "Naucz się" : "Wybierz", () => Run(() => m.Known || m.ItemId == null
                        ? Apply((id, ct) => _api.SetMethodAsync(id, m.Id, ct), "Metoda: " + m.NamePl)
                        : Apply((id, ct) => _api.LearnMethodAsync(id, m.Id, ct), "Poznano: " + m.NamePl)), r, sp: 11, heightDp: 32)
                        .LayoutParameters = new LinearLayout.LayoutParams(Px(92), Px(32)) { Gravity = GravityFlags.CenterVertical };
                ui.Text(m.DescriptionPl, card, 10, Ui.Muted, maxLines: 2);
            }, empty: "Brak metod.");

            var last = ui.HRow(body, 40, 4);
            if (st.AutoBreakthroughAvailable)
            {
                var on = st.AutoBreakthroughPermille.HasValue;
                ui.Btn(on ? "Auto-przełom: wył." : "Auto-przełom ≥70%", () => Run(() => Apply((id, ct) => _api.SetAutoBreakthroughAsync(id, on ? (int?)null : 700, ct),
                    on ? "Auto-przełom wyłączony." : "Auto-przełom przy szansie ≥ 70%.")), last, secondary: true, sp: 11, heightDp: 40);
            }

            if (st.CanReincarnate)
                ui.Btn("Reinkarnacja (+" + st.KarmaOnReincarnation + " karmy)", () => Run(() => Apply(_api.ReincarnateAsync, "Odrodzenie — karma " + (st.Karma + st.KarmaOnReincarnation))), last, sp: 11, heightDp: 40);
            ui.Text("Demony " + st.Instability + "/100" + (st.DemonPenaltyPermille > 0 ? " (−" + (st.DemonPenaltyPermille / 10.0).ToString("0.#") + "% przełomu)" : string.Empty)
                + " · karma " + st.Karma + " (+" + (st.KarmaFlowPermille / 10.0).ToString("0.#") + "% Qi) · reinkarnacje " + st.Reincarnations
                + "\noświecenia " + st.Enlightenments + " · Qi łącznie " + st.LifetimeQi.ToString("N0"), body, 10, Ui.Muted, center: true, maxLines: 2);
        }

        /// <summary>Stage 12 (Perfection): the heavenly tribulation is a real battle against the sky.</summary>
        private async Task StartTribulationAsync(CultivationFlow flow)
        {
            var st = flow.State;
            var combat = new CombatFlow(_api);
            if (st.TribulationBattleId is { } running)
            {
                await combat.OpenAsync(running, Ct);
                ShowBattle(combat);
                return;
            }

            var started = await _api.StartTribulationAsync(_me.Id, Ct);
            flow.Replace(started.State);
            PlayOverlay(Resource.Drawable.fx_breakthrough, 256, 9);
            Sfx.Play(Sfx.Breakthrough, 0.7f);
            await combat.OpenAsync(started.BattleId, Ct);
            ShowBattle(combat);
            SetStatus("Niebo otwiera się: " + (st.NextTribulationPl ?? "Niebiańska Próba") + ". Przetrwaj!");
        }

        // ---- path arts ----------------------------------------------------------------------------------------

        private async Task ShowArtsAsync(int sub)
        {
            var c = _me;
            var arts = await _api.GetArtsAsync(c.Id, Ct);
            var ui = HeroScreen(5, Resource.Drawable.bg_hero);
            var page = ui.Content;
            ui.Tabs(page, ArtsTabs, sub, i => Run(() => ShowArtsAsync(i)), 30);

            if (arts.Notices.Count > 0)
            {
                var n = ui.Card(page, 0f, 6, Ui.Gold);
                n.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(44)) { TopMargin = Px(4) };
                n.Clickable = true;
                ui.Text(arts.Notices[arts.Notices.Count - 1].TextPl + (arts.Notices.Count > 1 ? "  (+" + (arts.Notices.Count - 1) + ")" : string.Empty), n, 11, Ui.Gold, maxLines: 2);
                n.Click += (_, __) => Run(async () =>
                {
                    await _api.DismissArtsNoticesAsync(c.Id, Ct);
                    await ShowArtsAsync(sub);
                });
            }

            async Task Reload(string message = null)
            {
                await ShowArtsAsync(sub);
                if (message != null) SetStatus(message);
                RefreshHud();
            }

            switch (sub)
            {
                case 1:
                    ArtsToxin(ui, page, arts, Reload);
                    break;
                case 2:
                    ArtsGu(ui, page, arts, Reload);
                    break;
                case 3:
                    ArtsTemper(ui, page, arts, Reload);
                    break;
                case 4:
                    ArtsSword(ui, page, arts, Reload);
                    break;
                case 5:
                    ArtsBranch(ui, page, arts, Reload);
                    break;
                default:
                    ArtsFlames(ui, page, arts, Reload);
                    break;
            }
        }

        private void ArtsFlames(Ui ui, LinearLayout page, ArtsDto arts, Func<string, Task> reload)
        {
            var c = _me;
            var f = arts.Flames;
            var head = ui.Card(page, 0f, 8, Ui.Cinnabar);
            head.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(84)) { TopMargin = Px(4) };
            ui.Text("Płomienie: " + f.Owned + "/18 · gniazda " + f.Slots + "/" + f.SlotsMax + " · alchemia +" + arts.AlchemyPermille / 10 + "% · kuźnia +" + arts.ForgePermille / 10 + "%", head, 12, Ui.Gold, bold: true, maxLines: 2);
            var state = f.AbsorbingId != null ? "Pochłanianie: " + f.All.FirstOrDefault(x => x.Id == f.AbsorbingId)?.NamePl + " · " + Until(f.AbsorbUntil) + " · szansa " + f.AbsorbChancePermille / 10 + "%"
                : f.Subdued != null ? "Ujarzmiony: " + f.All.FirstOrDefault(x => x.Id == f.Subdued)?.NamePl + " — pochłoń go!"
                : f.BurnedUntil.HasValue ? "Poparzone meridiany: " + Until(f.BurnedUntil)
                : "Ślady żaru: " + f.Traces + " (" + f.TracesPerUse + " na trop) · kryształy ognia: " + f.FeedItems;
            ui.Text(state, head, 11, f.BurnedUntil.HasValue ? Ui.Cinnabar : Ui.Ink, maxLines: 2);
            var actions = ui.HRow(page, 40, 4);
            ui.Btn("Podążaj tropem", () => Run(async () =>
            {
                var r = await _api.FollowTracesAsync(c.Id, Ct);
                Sfx.Play(r.FoundFlameId != null ? Sfx.Breakthrough : Sfx.Click, 0.6f);
                await reload(r.FoundFlameId != null ? "Odnaleziono: " + r.FoundNamePl + "!" : "Trop wystygł…");
            }), actions, secondary: f.Traces < f.TracesPerUse, sp: 12, heightDp: 40, icon: "map_flame");
            if (f.Subdued != null)
                ui.Btn("Pochłoń", () => Run(async () =>
                {
                    await _api.AbsorbFlameAsync(c.Id, Ct);
                    Sfx.Play(Sfx.Chime);
                    await reload("Pochłanianie rozpoczęte.");
                }), actions, sp: 12, heightDp: 40);

            var list = f.All.Where(x => x.State != "unknown").Concat(f.All.Where(x => x.State == "unknown")).ToList();
            ui.Pages(page, list.Count, 2, 3, (i, cell) =>
            {
                var fl = list[i];
                var known = fl.State != "unknown";
                var color = fl.State == "owned" ? (fl.Slotted ? Ui.Gold : Ui.Cinnabar) : known ? Ui.Violet : Ui.GoldDim;
                var card = ui.Card(cell, 1f, 6, color, 0);
                ui.Text("#" + fl.Rank + " " + (known ? fl.NamePl : "???"), card, 12, color, bold: true, maxLines: 1);
                ui.Text(fl.State switch
                {
                    "owned" => "poz. " + fl.Level + (fl.Slotted ? " · w gnieździe" : string.Empty) + (fl.FusedWith != null ? " · fuzja" : string.Empty),
                    "seen" => "odnaleziony · pokonaj ducha",
                    "subdued" => "ujarzmiony",
                    "absorbing" => "pochłanianie…",
                    _ => fl.SourcePl,
                }, card, 10, Ui.Muted, maxLines: 2);
                card.Clickable = true;
                card.Click += (_, __) => FlameSheet(arts, fl, reload);
            });
            ui.Text("Sumaryczne premie sztuk: " + PerksText(arts.TotalPerks), page, 10, Ui.Jade, maxLines: 2, center: true);
        }

        private void FlameSheet(ArtsDto arts, FlameDto fl, Func<string, Task> reload)
        {
            var c = _me;
            var ui = _ui;
            var f = arts.Flames;
            var body = Sheet("#" + fl.Rank + " " + (fl.State == "unknown" ? "Nieznany płomień" : fl.NamePl), 0.6f, out var close, "map_flame");
            ui.Text("Źródło: " + fl.SourcePl + " · min. królestwo " + (fl.MinRealm + 1) + " · pochłanianie " + fl.AbsorbHours + " h", body, 11, Ui.Muted, maxLines: 2);
            ui.Text("Cechy: " + StatsText(fl.Stats) + "\nPremie: " + PerksText(fl.Perks), body, 12, Ui.Ink, maxLines: 4);
            if (fl.AlchemyPermille > 0 || fl.ForgePermille > 0)
                ui.Text("Kocioł +" + fl.AlchemyPermille / 10 + "% · kuźnia +" + fl.ForgePermille / 10 + "%", body, 11, Ui.Jade);
            ui.Box(body, 1f, marginDp: 0);
            var row = ui.HRow(body, 44, 4);
            async Task Do(Func<Task> action, string message)
            {
                close();
                await action();
                await reload(message);
            }

            if (fl.State == "seen")
                ui.Btn("Walcz z duchem", () => Run(async () =>
                {
                    close();
                    var r = await _api.SubdueFlameAsync(c.Id, fl.Id, Ct);
                    var combat = new CombatFlow(_api);
                    await combat.OpenAsync(r.BattleId, Ct);
                    ShowBattle(combat);
                }), row, sp: 13, heightDp: 44);
            if (fl.State == "owned")
            {
                ui.Btn(fl.Slotted ? "Wyjmij" : "Do gniazda", () => Run(() => Do(async () =>
                {
                    var slots = f.Slotted.Where(id => id != fl.Id).ToList();
                    if (!fl.Slotted)
                    {
                        if (slots.Count >= f.Slots) slots.RemoveAt(0);
                        slots.Add(fl.Id);
                    }

                    await _api.SetFlameSlotsAsync(c.Id, slots, Ct);
                }, fl.Slotted ? "Płomień uśpiony." : fl.NamePl + " płonie w gnieździe.")), row, sp: 12, heightDp: 44);
                if (fl.Level < f.MaxLevel)
                    ui.Btn("Karm ×5", () => Run(() => Do(() => _api.FeedFlameAsync(c.Id, fl.Id, 5, Ct), "Płomień nakarmiony.")), row, secondary: true, sp: 12, heightDp: 44);
                var partner = arts.Flames.All.FirstOrDefault(o => o.State == "owned" && o.Id != fl.Id && o.Slotted && o.FusedWith == null && o.Level >= f.MaxLevel / 2);
                if (fl.FusedWith == null && partner != null && fl.Level >= f.MaxLevel / 2)
                    ui.Btn("Fuzja z " + partner.NamePl, () => Run(() => Do(() => _api.FuseFlamesAsync(c.Id, fl.Id, partner.Id, Ct), "Płomienie stopione (+" + f.FusionBonusPermille / 10 + "%).")), row, secondary: true, sp: 11, heightDp: 44);
            }
        }

        private void ArtsToxin(Ui ui, LinearLayout page, ArtsDto arts, Func<string, Task> reload)
        {
            var c = _me;
            var t = arts.Toxin;
            var head = ui.Card(page, 0f, 8, Ui.Jade);
            head.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(92)) { TopMargin = Px(4) };
            ui.Text("Ciało Toksyn poz. " + t.Level + "/" + t.MaxLevel + " · ryzyko dawki " + t.FailPermille / 10 + "%", head, 13, Ui.Gold, bold: true);
            ui.MeterIn(head, Ui.Jade, t.NextXp.HasValue ? t.Xp + " / " + t.NextXp : "maks.", t.Xp, t.NextXp ?? Math.Max(1, t.Xp), 14);
            var poisoned = t.PoisonedUntil.HasValue && t.PoisonedUntil > DateTimeOffset.UtcNow;
            ui.Text(poisoned ? "☠ Zatruty jeszcze " + Until(t.PoisonedUntil) + " (odtrutki: " + t.Antidotes + ")"
                : t.ActiveCoating != null ? "Powłoka: " + t.Coatings.FirstOrDefault(x => x.Id == t.ActiveCoating)?.NamePl + " · " + t.CoatingCharges + " walk" : "Brak powłoki na ostrzu.", head, 11, poisoned ? Ui.Cinnabar : Ui.Ink);
            if (poisoned && t.Antidotes > 0)
                ui.Btn("Wypij odtrutkę", () => Run(async () =>
                {
                    await _api.CurePoisonAsync(c.Id, Ct);
                    await reload("Trucizna zneutralizowana.");
                }), page, secondary: true, sp: 12, heightDp: 36);

            var owned = t.Venoms.Where(v => v.Owned > 0).ToList();
            ui.Text("JADY W SAKWIE — dotknij, by wchłonąć", page, 11, Ui.GoldDim, bold: true);
            ui.Pages(page, owned.Count, 2, 1, (i, cell) =>
            {
                var v = owned[i];
                var card = ui.Card(cell, 1f, 6, Ui.RarityOf(v.Rarity).Color, 0);
                ui.Text(v.NamePl + " ×" + v.Owned, card, 12, Ui.Ink, bold: true);
                ui.Text("+" + v.Xp + " XP · wchłonięto " + v.Absorbed, card, 10, Ui.Muted);
                card.Clickable = true;
                card.Click += (_, __) => Run(async () =>
                {
                    var r = await _api.AbsorbVenomAsync(c.Id, v.Item, (int)Math.Min(5, v.Owned), Ct);
                    Sfx.Play(r.Poisoned ? Sfx.Defeat : Sfx.Chime, 0.6f);
                    await reload(r.Poisoned ? "Wchłonięto " + r.Absorbed + ", ale jad cię zatruł!" : "Wchłonięto " + r.Absorbed + " (+" + r.XpGained + " XP).");
                });
            }, empty: "Brak jadów — zbieraj je z bestii, bagien i wypraw uczniów.", weight: 0.9f);

            ui.Text("POWŁOKI", page, 11, Ui.GoldDim, bold: true);
            ui.Pages(page, t.Coatings.Count, 1, 1, (i, cell) =>
            {
                var co = t.Coatings[i];
                var card = ui.Card(cell, 1f, 6, co.Craftable ? Ui.Gold : (Color?)null, 0);
                var r = ui.HRow(card, 30, 0);
                ui.Text(co.NamePl + " · " + co.Battles + " walk", r, 12, co.Craftable ? Ui.Gold : Ui.Ink, bold: true)
                    .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                if (co.Craftable)
                    ui.Btn("Nałóż", () => Run(async () =>
                    {
                        await _api.ApplyCoatingAsync(c.Id, co.Id, Ct);
                        Sfx.Play(Sfx.Chime);
                        await reload(co.NamePl + " na ostrzu.");
                    }), r, sp: 11, heightDp: 30).LayoutParameters = new LinearLayout.LayoutParams(Px(72), Px(30));
                ui.Text(string.Join(", ", co.Venoms.Select(v => v.NamePl + " " + v.Owned + "/1")) + " · " + PerksText(co.Perks), card, 10, Ui.Muted, maxLines: 2);
            }, weight: 0.9f);
        }

        private void ArtsGu(Ui ui, LinearLayout page, ArtsDto arts, Func<string, Task> reload)
        {
            var c = _me;
            var g = arts.Gu;
            ui.Text("Gniazdo Gu: " + g.Worms.Count + "/" + g.Slots + " · zioła " + g.Herbs + " (wyklucie " + g.HatchHerbs + ", karmienie " + g.FeedHerbs + ")", page, 12, Ui.Gold, bold: true, center: true);
            ui.Pages(page, g.Slots, 2, 2, (i, cell) =>
            {
                var w = g.Worms.FirstOrDefault(x => x.Slot == i);
                var card = ui.Card(cell, 1f, 6, w != null ? Ui.Violet : (Color?)null, 0);
                if (w == null)
                {
                    ui.Text("Puste gniazdo " + (i + 1), card, 12, Ui.Muted, bold: true);
                    ui.Text("Wykluj Gu poniżej.", card, 10, Ui.Muted);
                    return;
                }

                ui.Text(w.NamePl + " · poz. " + w.Level + "/" + g.MaxLevel + " · ranga " + w.Rank, card, 12, Ui.Gold, bold: true, maxLines: 2);
                ui.Text(w.GrowingUntil.HasValue ? "Trawi: " + Until(w.GrowingUntil) : "Głodny", card, 10, w.GrowingUntil.HasValue ? Ui.Muted : Ui.Jade);
                var r = ui.HRow(card, 30, 2);
                if (!w.GrowingUntil.HasValue)
                    ui.Btn("Karm", () => Run(async () =>
                    {
                        await _api.FeedGuAsync(c.Id, w.Slot, Ct);
                        await reload("Gu karmiony — rośnie.");
                    }), r, sp: 11, heightDp: 30);
                ui.Btn("Uwolnij", () => Run(async () =>
                {
                    await _api.ReleaseGuAsync(c.Id, w.Slot, Ct);
                    await reload("Gu uwolniony.");
                }), r, secondary: true, sp: 11, heightDp: 30);
            });
            ui.Text("WYKLUJ", page, 11, Ui.GoldDim, bold: true);
            ui.Pages(page, g.Types.Count, 1, 2, (i, cell) =>
            {
                var type = g.Types[i];
                var card = ui.Card(cell, 1f, 6, null, 0);
                var r = ui.HRow(card, 30, 0);
                ui.Text(type.NamePl, r, 12, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                ui.Btn("Wykluj", () => Run(async () =>
                {
                    await _api.HatchGuAsync(c.Id, type.Id, Ct);
                    Sfx.Play(Sfx.Chime);
                    await reload(type.NamePl + " wykluty.");
                }), r, sp: 11, heightDp: 30).LayoutParameters = new LinearLayout.LayoutParams(Px(76), Px(30));
                ui.Text(type.DescriptionPl + " · na poziom: " + PerksText(type.PerLevel), card, 10, Ui.Muted, maxLines: 2);
            }, weight: 0.9f);
        }

        private void ArtsTemper(Ui ui, LinearLayout page, ArtsDto arts, Func<string, Task> reload)
        {
            var c = _me;
            var t = arts.Temper;
            var head = ui.Card(page, 0f, 8, Ui.Gold);
            head.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(120)) { TopMargin = Px(4) };
            ui.Text("Hartowanie Ciała " + (t.Badge ?? "+0") + " / +" + t.MaxLevel + (t.StageNamePl != null ? " · " + t.StageNamePl : string.Empty), head, 15, Ui.Gold, bold: true);
            ui.MeterIn(head, Ui.Gold, t.Xp + " / " + t.XpNeeded, t.Xp, Math.Max(1, t.XpNeeded), 16);
            ui.Text("Szansa " + t.ChancePermille / 10 + "%" + (t.NeedsPill ? " · próg: Pigułka Przekroczenia Ciała (" + t.Pills + ")" : string.Empty)
                    + (t.NextStageNamePl != null ? " · dalej " + t.NextStageNamePl + " od +" + t.NextStageAt : string.Empty), head, 11, t.NeedsPill && t.Pills == 0 ? Ui.Cinnabar : Ui.Ink, maxLines: 2);
            ui.Text("Hartowanie rośnie z kroków (" + t.StepsPerXp + " kroków = 1), z ciosów przyjętych w walce (próby niebios ×2) i z kąpieli.", head, 10, Ui.Muted, maxLines: 2);
            var can = t.Xp >= t.XpNeeded && t.Level < t.MaxLevel;
            ui.Btn(can ? "Hartuj do +" + (t.Level + 1) : "Za mało hartowania", () => Run(async () =>
            {
                var r = await _api.AdvanceTemperAsync(c.Id, Ct);
                Sfx.Play(r.Success ? Sfx.Breakthrough : Sfx.Defeat, 0.7f);
                if (r.Success) PlayOverlay(Resource.Drawable.fx_breakthrough, 256, 9);
                await reload(r.Success ? "Ciało zahartowane: " + r.State.Temper.Badge : "Nieudane — połowa hartowania przepadła.");
            }), page, secondary: !can, sp: 14, heightDp: 44);
            ui.Text(t.Bath != null ? "Kąpiel trwa: " + Until(t.BathUntil) : "KĄPIELE", page, 11, Ui.GoldDim, bold: true);
            ui.Pages(page, t.Baths.Count, 1, 2, (i, cell) =>
            {
                var b = t.Baths[i];
                var card = ui.Card(cell, 1f, 6, b.Affordable && t.Bath == null ? Ui.Gold : (Color?)null, 0);
                var r = ui.HRow(card, 30, 0);
                ui.Text(b.NamePl + " · " + b.Hours + " h · +" + b.Xp, r, 12, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                if (b.Affordable && t.Bath == null)
                    ui.Btn("Wejdź", () => Run(async () =>
                    {
                        await _api.StartBathAsync(c.Id, b.Id, Ct);
                        Sfx.Play(Sfx.Chime);
                        await reload(b.NamePl + " — wróć za " + b.Hours + " h.");
                    }), r, sp: 11, heightDp: 30).LayoutParameters = new LinearLayout.LayoutParams(Px(70), Px(30));
                ui.Text(string.Join(", ", b.Items.Select(x => x.NamePl + " " + x.Owned + "/" + x.Quantity)), card, 10, b.Affordable ? Ui.Jade : Ui.Cinnabar, maxLines: 2);
            });
        }

        private void ArtsSword(Ui ui, LinearLayout page, ArtsDto arts, Func<string, Task> reload)
        {
            var c = _me;
            var s = arts.Sword;
            var card = ui.Card(page, 1f, 10, Ui.QiBlue);
            ui.Text("Intencja Miecza " + (s.Badge ?? "⚔0") + " / ⚔" + s.MaxLevel, card, 18, Ui.Gold, bold: true, center: true);
            var swords = new string('⚔', s.Level) + new string('·', Math.Max(0, s.MaxLevel - s.Level));
            ui.Text(swords, card, 26, Ui.QiBlue, bold: true, center: true);
            ui.MeterIn(card, Ui.QiBlue, s.NextXp.HasValue ? s.Xp + " / " + s.NextXp : "Szyk Dziewięciu Mieczy", s.Xp, s.NextXp ?? Math.Max(1, s.Xp), 16);
            ui.Text("Każdy miecz: więcej obrażeń i szybszy pierwszy cios. Komplet dziewięciu otwiera Szyk Mieczy. Echa mieczy znajdziesz przy Pomnikach Miecza, u bandytów i w Grobowcu Miecza.",
                card, 11, Ui.Muted, maxLines: 4, center: true);
            ui.Box(card, 1f, marginDp: 0);
            var row = ui.HRow(page, 46, 4);
            foreach (var n in new[] { 1, 5, 20 })
            {
                var can = s.Echoes >= n;
                ui.Btn("Ofiaruj " + n + " ech", () => Run(async () =>
                {
                    if (!can)
                    {
                        SetStatus("Masz tylko " + s.Echoes + " ech miecza.");
                        return;
                    }

                    await _api.OfferEchoesAsync(c.Id, n, Ct);
                    Sfx.Play(Sfx.Chime);
                    await reload("+" + n * s.XpPerEcho + " intencji.");
                }), row, secondary: !can, sp: 12, heightDp: 46);
            }

            ui.Text("Echa w sakwie: " + s.Echoes + " · " + s.XpPerEcho + " intencji za echo", page, 11, Ui.Muted, center: true);
        }

        private void ArtsBranch(Ui ui, LinearLayout page, ArtsDto arts, Func<string, Task> reload)
        {
            var c = _me;
            var b = arts.Branch;
            ui.Text("Gałąź ścieżki " + PathName(arts.Path) + " · stopień " + b.Tier + "/" + b.TierRealms.Count + " (od królestw " + string.Join(", ", b.TierRealms.Select(r => r + 1)) + ")", page, 12, Ui.Gold, bold: true, center: true, maxLines: 2);
            foreach (var o in b.Options)
            {
                var chosen = b.Chosen == o.Id;
                var card = ui.Card(page, 1f, 10, chosen ? Ui.Gold : (Color?)null);
                ui.Text((chosen ? "● " : string.Empty) + o.NamePl, card, 16, chosen ? Ui.Gold : Ui.Ink, bold: true);
                ui.Text(o.DescriptionPl, card, 12, Ui.Muted, maxLines: 2);
                ui.Text("Na stopień: " + PerksText(o.PerTier), card, 11, Ui.Jade, maxLines: 2);
                if (!chosen)
                    ui.Btn(b.Chosen == null ? "Wybierz" : "Zmień (" + b.RespecStones + " KD)", () => Run(async () =>
                    {
                        await _api.ChooseBranchAsync(c.Id, o.Id, Ct);
                        Sfx.Play(Sfx.Breakthrough, 0.6f);
                        await reload("Gałąź: " + o.NamePl);
                    }), card, secondary: b.Chosen != null, sp: 12, heightDp: 36);
            }

            if (b.Capstones != null && b.Capstones.Count > 0)
            {
                var chosenCap = b.Capstones.FirstOrDefault(x => x.Id == b.Capstone);
                ui.Btn(chosenCap != null ? "Ostateczna: " + chosenCap.NamePl : "Umiejętność ostateczna (" + (b.CapstoneRealm + 1) + ". królestwo)", () =>
                {
                    var body = Sheet("Umiejętność ostateczna", 0.7f, out var close, "menu_hero");
                    ui.Text("Otwiera się w " + (b.CapstoneRealm + 1) + ". królestwie. Pierwszy wybór za darmo, zmiana kosztuje " + (b.RespecStones * 10) + " KD.", body, 11, Ui.Muted, maxLines: 2);
                    foreach (var cap in b.Capstones)
                    {
                        var card = ui.Card(body, 1f, 8, cap.Id == b.Capstone ? Ui.Gold : (Color?)null);
                        ui.Text(cap.NamePl, card, 14, cap.Id == b.Capstone ? Ui.Gold : Ui.Ink, bold: true);
                        ui.Text(cap.DescriptionPl, card, 11, Ui.Muted, maxLines: 2);
                        if (cap.Id != b.Capstone && arts.Realm >= b.CapstoneRealm)
                            ui.Btn("Wybierz", () => Run(async () =>
                            {
                                await _api.ChooseCapstoneAsync(c.Id, cap.Id, Ct);
                                close();
                                Sfx.Play(Sfx.Breakthrough, 0.8f);
                                await reload("Umiejętność ostateczna: " + cap.NamePl);
                            }), card, secondary: true, sp: 12, heightDp: 34);
                    }
                }, page, secondary: true, sp: 12, heightDp: 36);
            }

            if (arts.Realm >= b.SecondPathRealm)
            {
                var names = new Dictionary<string, string> { ["qi"] = "Miecz Qi", ["body"] = "Hartowanie ciała", ["flame"] = "Płomień", ["poison"] = "Trucizna" };
                ui.Btn(b.SecondPath != null && names.TryGetValue(b.SecondPath, out var sn) ? "Druga ścieżka: " + sn : "Wybierz drugą ścieżkę", () =>
                {
                    var body = Sheet("Druga ścieżka", 0.6f, out var close, "menu_hero");
                    ui.Text("Sztuki drugiej ścieżki działają w 60% zamiast 30%. Pierwszy wybór za darmo, zmiana kosztuje " + (b.RespecStones * 10) + " KD.", body, 11, Ui.Muted, maxLines: 3);
                    foreach (var kv in names)
                    {
                        if (kv.Key == c.Path) continue;
                        var card = ui.Card(body, 1f, 8, kv.Key == b.SecondPath ? Ui.Gold : (Color?)null);
                        ui.Text(kv.Value, card, 14, kv.Key == b.SecondPath ? Ui.Gold : Ui.Ink, bold: true);
                        if (kv.Key != b.SecondPath)
                            ui.Btn("Wybierz", () => Run(async () =>
                            {
                                await _api.ChooseSecondPathAsync(c.Id, kv.Key, Ct);
                                close();
                                await reload("Druga ścieżka: " + kv.Value);
                            }), card, secondary: true, sp: 12, heightDp: 34);
                    }
                }, page, secondary: true, sp: 12, heightDp: 36);
            }
        }

        // ---- estate ------------------------------------------------------------------------------------------

        private async Task ShowEstateAsync(int tab)
        {
            var c = _me;
            var e = await _api.GetEstateAsync(c.Id, Ct);
            ShowEstate(e, tab);
        }

        private void ShowEstate(EstateDto e, int tab)
        {
            var c = _me;
            Remind("crops", e.Plots.Where(pl => pl.Seed != null && !pl.Ready).Select(pl => pl.ReadyAt).Where(t0 => t0 != null).DefaultIfEmpty(null).Min(),
                "Plony dojrzały", "Ogród posiadłości czeka na zbiór.");
            Remind("expeditions", e.Disciples.Where(dd => dd.Expedition != null && !dd.Returned).Select(dd => dd.Until).Where(t1 => t1 != null).DefaultIfEmpty(null).Max(),
                "Uczniowie wrócili", "Wyprawa zakończona — odbierz łupy.");
            var ui = NewScreen("Posiadłość", BackToTab, Resource.Drawable.bg_estate, "OGRÓD · UCZNIOWIE · WYPRAWY");
            var page = ui.Content;
            ui.Tabs(page, new[] { "Ogród", "Uczniowie", "Wyprawy" }, tab, i => Run(() => ShowEstateAsync(i)),
                badges: new[] { e.Plots.Count(p => p.Ready), e.Disciples.Count(d => d.Returned), 0 });

            void Gained(EstateDto r, string title)
            {
                ShowEstate(r, tab);
                if (r.Gained.Count > 0 || r.StonesGained > 0)
                    ShowLoot(title, 0, r.Gained.Select(g => (g.Item, (long)g.Quantity)).ToList(), null);
                if (r.StonesGained > 0) SetStatus("+" + r.StonesGained + " kamieni duchowych");
                RefreshHud();
            }

            if (tab == 0)
            {
                var ready = e.Plots.Count(p => p.Ready);
                if (ready > 0)
                    ui.Btn("Zbierz plony (" + ready + ")", () => Run(async () =>
                    {
                        var r = await _api.HarvestAsync(c.Id, Ct);
                        Sfx.Play(Sfx.Coin);
                        Gained(r, "Plony");
                    }), page, sp: 14, heightDp: 44);
                ui.Pages(page, e.Plots.Count, 3, 3, (i, cell) =>
                {
                    var p = e.Plots[i];
                    var card = ui.Card(cell, 1f, 6, p.Ready ? Ui.Gold : p.Seed != null ? Ui.Jade : (Color?)null, 0);
                    card.SetGravity(GravityFlags.Center);
                    ui.Text(p.Seed == null ? "🌱 wolne" : p.CropNamePl, card, 12, p.Ready ? Ui.Gold : Ui.Ink, bold: true, center: true, maxLines: 2);
                    ui.Text(p.Seed == null ? "posadź" : p.Ready ? "gotowe!" : Until(p.ReadyAt), card, 10, Ui.Muted, center: true);
                    if (p.Seed != null) return;
                    card.Clickable = true;
                    card.Click += (_, __) =>
                    {
                        var body = Sheet("Poletko " + (p.Slot + 1), 0.55f, out var close, "menu_sect");
                        ui.Pages(body, e.Crops.Count, 1, 3, (k, cc) =>
                        {
                            var crop = e.Crops[k];
                            var cardC = ui.Card(cc, 1f, 6, crop.SeedsOwned > 0 && crop.Available ? Ui.Jade : (Color?)null, 0);
                            var r = ui.HRow(cardC, 30, 0);
                            ui.Text(crop.NamePl + " · " + crop.Hours + " h", r, 12, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                            if (crop.SeedsOwned > 0 && crop.Available)
                                ui.Btn("Posadź", () => Run(async () =>
                                {
                                    close();
                                    var r2 = await _api.PlantAsync(c.Id, p.Slot, crop.Seed, Ct);
                                    Sfx.Play(Sfx.Chime, 0.6f);
                                    ShowEstate(r2, 0);
                                }), r, sp: 11, heightDp: 30).LayoutParameters = new LinearLayout.LayoutParams(Px(76), Px(30));
                            ui.Text(crop.SeedNamePl + ": " + crop.SeedsOwned + (crop.Available ? string.Empty : " · od królestwa " + (crop.MinRealm + 1)) + " · plon: "
                                    + string.Join(", ", crop.Yields.Select(y => y.NamePl + " " + y.Min + "-" + y.Max)), cardC, 10, Ui.Muted, maxLines: 2);
                        });
                    };
                });
                ui.Text("Poletka: " + e.Plots.Count + "/" + e.PlotsMax + " — więcej z wyższym królestwem. Nasiona: wyprawy, golemy, ukryte miejsca.", page, 10, Ui.Muted, center: true, maxLines: 2);
            }
            else if (tab == 1)
            {
                var back = e.Disciples.Count(d => d.Returned);
                var row = ui.HRow(page, 44, 4);
                if (back > 0)
                    ui.Btn("Odbierz łupy (" + back + ")", () => Run(async () =>
                    {
                        var r = await _api.CollectExpeditionsAsync(c.Id, Ct);
                        Sfx.Play(Sfx.Coin);
                        Gained(r, "Łupy z wypraw");
                    }), row, sp: 13, heightDp: 44);
                if (e.RecruitPrice.HasValue)
                    ui.Btn("Przyjmij ucznia (" + e.RecruitPrice + " KD)", () => Run(async () =>
                    {
                        var r = await _api.RecruitDiscipleAsync(c.Id, Ct);
                        Sfx.Play(Sfx.Chime);
                        ShowEstate(r, 1);
                        RefreshHud();
                    }), row, secondary: true, sp: 12, heightDp: 44);
                ui.Pages(page, e.Disciples.Count, 1, 4, (i, cell) =>
                {
                    var d = e.Disciples[i];
                    var card = ui.Card(cell, 1f, 6, d.Returned ? Ui.Gold : d.Expedition != null ? Ui.Jade : (Color?)null, 0);
                    ui.Text(d.Name + " · " + d.TalentNamePl + " · poz. " + d.Level, card, 13, Ui.Ink, bold: true);
                    ui.Text(d.Expedition == null ? "W posiadłości — wyślij na wyprawę." : d.Returned ? "Wrócił z: " + d.ExpeditionNamePl : d.ExpeditionNamePl + " · " + Until(d.Until), card, 11, d.Returned ? Ui.Gold : Ui.Muted);
                    if (d.XpNext > 0) ui.MeterIn(card, Ui.Jade, d.Xp + "/" + d.XpNext + " XP", d.Xp, d.XpNext, 12);
                }, empty: "Brak uczniów — przyjmij pierwszego.");
                ui.Text("Uczniowie: " + e.Disciples.Count + "/" + e.DiscipleSlots + " (maks. " + e.DiscipleSlotsMax + ")", page, 10, Ui.Muted, center: true);
            }
            else
            {
                var idle = e.Disciples.Where(d => d.Expedition == null).ToList();
                ui.Text(idle.Count == 0 ? "Wszyscy uczniowie są w drodze." : "Wolni uczniowie: " + string.Join(", ", idle.Select(d => d.Name + " (" + d.Level + ")")), page, 11, Ui.Gold, center: true, maxLines: 2);
                ui.Pages(page, e.Expeditions.Count, 1, 3, (i, cell) =>
                {
                    var x = e.Expeditions[i];
                    var card = ui.Card(cell, 1f, 6, x.Available ? null : (Color?)Ui.GoldDim, 0);
                    var r = ui.HRow(card, 30, 0);
                    ui.Text(x.NamePl + " · " + x.Hours + " h", r, 13, x.Available ? Ui.Ink : Ui.Muted, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                    var pick = idle.Where(d => d.Level >= x.MinLevel).OrderByDescending(d => d.Talent == x.Talent).ThenByDescending(d => d.Level).FirstOrDefault();
                    if (x.Available && pick != null)
                        ui.Btn("Wyślij " + pick.Name, () => Run(async () =>
                        {
                            var r2 = await _api.StartExpeditionAsync(c.Id, pick.Id, x.Id, Ct);
                            Sfx.Play(Sfx.Chime, 0.6f);
                            ShowEstate(r2, 2);
                            SetStatus(pick.Name + " wyrusza: " + x.NamePl);
                        }), r, sp: 11, heightDp: 30).LayoutParameters = new LinearLayout.LayoutParams(Px(120), Px(30));
                    ui.Text(x.DescriptionPl + " · " + x.StonesMin + "-" + x.StonesMax + " KD · talent: " + x.TalentNamePl
                            + (x.Available ? string.Empty : " · od królestwa " + (x.MinRealm + 1)) + (x.MinLevel > 1 ? " · uczeń poz. " + x.MinLevel : string.Empty), card, 10, Ui.Muted, maxLines: 2);
                    ui.Text(string.Join(", ", x.Drops.Select(dr => dr.NamePl)), card, 10, Ui.Jade, maxLines: 1);
                });
            }
        }

        // ---- arena -------------------------------------------------------------------------------------------

        private void Arena(Ui ui, LinearLayout page, ArenaDto a)
        {
            var c = _me;
            var head = ui.Card(page, 0f, 8, Ui.Gold);
            head.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(96)) { TopMargin = Px(4) };
            ui.Text("ARENA · liga " + a.LeagueNamePl + " · " + a.Rating + " pkt · #" + a.Rank, head, 14, Ui.Gold, bold: true);
            ui.Text(a.Wins + " zwycięstw · " + a.Losses + " porażek · walki dziś: " + a.AttemptsLeft + "/" + a.AttemptsPerDay
                    + (a.NextLeagueNamePl != null ? " · do ligi " + a.NextLeagueNamePl + ": " + (a.NextLeagueRating - a.Rating) + " pkt" : string.Empty), head, 11, Ui.Ink, maxLines: 2);
            if (a.WeeklyClaimable)
                ui.Btn("Nagroda tygodnia: " + a.WeeklyStones + " KD", () => Run(async () =>
                {
                    await _api.ClaimArenaWeekAsync(c.Id, Ct);
                    Sfx.Play(Sfx.Coin);
                    ShowBanner("ARENA", "Liga " + a.LeagueNamePl, "+" + a.WeeklyStones + " kamieni duchowych", "menu_battle");
                    await ShowCombatTabAsync(5);
                    RefreshHud();
                }), head, sp: 12, heightDp: 32);
            ui.Pages(page, a.Opponents.Count, 1, 4, (i, cell) =>
            {
                var o = a.Opponents[i];
                var card = ui.Card(cell, 1f, 6, null, 0);
                var row = ui.HRow(card, 40, 0);
                ui.Icon(row, "menu_battle", 34);
                var col = ui.Box(row, 1f, marginDp: 8);
                ui.Text(o.Name + (o.Badges.Count > 0 ? " " + string.Join(" ", o.Badges) : string.Empty), col, 13, Ui.Ink, bold: true);
                ui.Text((o.Title != null ? "「" + o.Title + "」 · " : string.Empty) + o.LeagueNamePl + " · " + o.Rating, col, 10, Ui.Muted);
                ui.Btn("Walcz", () => Run(async () =>
                {
                    if (a.AttemptsLeft <= 0)
                    {
                        SetStatus("Dzisiejsze walki na arenie wykorzystane.");
                        return;
                    }

                    await StartOrResumeAsync(() => _api.ChallengeArenaAsync(c.Id, o.CharacterId, Ct));
                }), row, sp: 12, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(80), Px(36)) { Gravity = GravityFlags.CenterVertical };
            }, empty: "Na arenie jeszcze pusto — wróć później.");
            var board = ui.Card(page, 0.8f, 8);
            ui.Text("NAJLEPSI", board, 11, Ui.GoldDim, bold: true);
            foreach (var r in a.Top.Take(4))
                ui.Text("#" + r.Rank + "  " + r.Name + "  ·  " + r.Value, board, 11, r.CharacterId == c.Id ? Ui.Gold : Ui.Ink);
        }

        // ---- sect development --------------------------------------------------------------------------------

        private async Task ShowSectDevelopmentAsync(SectFlow flow)
        {
            var c = _me;
            var ui = NewScreen(null, art: Resource.Drawable.bg_sect, tab: TabSect);
            var page = ui.Content;
            ui.Tabs(page, new[] { "Sekta", "Członkowie", "Terytoria", "Ranking", "Rozwój" }, 4, i => Run(() => ShowSectTabAsync(i)));
            if (flow.Mine == null)
            {
                ui.Box(page, 1f);
                ui.Text("Dołącz do sekty, by budować pawilony, wykonywać misje i walczyć z bossem sekty.", page, 14, Ui.Muted, center: true, maxLines: 3);
                ui.Box(page, 1f);
                return;
            }

            var sectId = flow.Mine.Sect.Id;
            var d = await _api.GetSectDevelopmentAsync(sectId, c.Id, Ct);
            async Task Reload(string message)
            {
                await ShowSectDevelopmentAsync(flow);
                if (message != null) SetStatus(message);
            }

            ui.Text("Skarbiec " + d.Treasury.ToString("N0") + " KD · tydzień " + d.Week, page, 12, Ui.Gold, bold: true, center: true);
            var nextArt = d.Arts.FirstOrDefault(a => !a.Unlocked);
            var arts = d.Arts.Where(a => a.Unlocked).Select(a => a.NamePl).ToList();
            ui.Text("Poziom sekty " + d.Level + (d.NextLevelXp.HasValue ? " (" + d.Xp.ToString("N0") + " / " + d.NextLevelXp.Value.ToString("N0") + " PD)" : " (szczyt)")
                + " · miejsc " + d.MaxMembers + (arts.Count > 0 ? " · sztuki: " + string.Join(", ", arts) : string.Empty)
                + (nextArt != null ? " · na poz. " + nextArt.Level + ": " + nextArt.NamePl : string.Empty)
                + (d.EraPerkPermille > 0 ? " · budynki +" + d.EraPerkPermille / 10 + "% na erę" : string.Empty), page, 10, Ui.Muted, center: true, maxLines: 2);
            var boss = d.Boss;
            var bc = ui.Card(page, 0f, 8, boss.Defeated ? Ui.Jade : Ui.Cinnabar);
            bc.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(92)) { TopMargin = Px(4) };
            ui.Text("BOSS SEKTY: " + boss.NamePl, bc, 13, Ui.Gold, bold: true);
            ui.MeterIn(bc, boss.Defeated ? Ui.Jade : Ui.Cinnabar, boss.Defeated ? "ZAPIECZĘTOWANY" : "pieczęcie " + boss.SealsBroken + " / " + boss.Seals, boss.SealsBroken, boss.Seals, 14);
            var br = ui.HRow(bc, 34, 2);
            ui.Text("Walczyło " + boss.Fighters + " · nagroda: " + boss.TreasuryReward + " KD do skarbca + " + boss.MemberStones + " KD każdemu", br, 10, Ui.Muted, maxLines: 2)
                .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
            if (!boss.Defeated && boss.AttemptsLeft > 0)
                ui.Btn("Walcz (" + boss.AttemptsLeft + ")", () => Run(async () =>
                {
                    var r = await _api.FightSectBossAsync(sectId, c.Id, Ct);
                    var combat = new CombatFlow(_api);
                    await combat.OpenAsync(r.BattleId, Ct);
                    ShowBattle(combat);
                }), br, sp: 11, heightDp: 32).LayoutParameters = new LinearLayout.LayoutParams(Px(96), Px(32));

            ui.Text("MISJE TYGODNIA", page, 11, Ui.GoldDim, bold: true);
            foreach (var m in d.Missions)
            {
                var card = ui.Card(page, 0.7f, 6, m.Completed && !m.Claimed ? Ui.Gold : (Color?)null);
                var r = ui.HRow(card, 26, 0);
                ui.Text((m.Claimed ? "✓ " : string.Empty) + m.NamePl + " · " + m.TreasuryReward + " KD", r, 12, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
                if (m.Completed && !m.Claimed && d.CanBuild)
                    ui.Btn("Odbierz", () => Run(async () =>
                    {
                        await _api.ClaimSectMissionAsync(sectId, c.Id, m.Id, Ct);
                        Sfx.Play(Sfx.Coin);
                        await Reload("Misja zaliczona — skarbiec rośnie.");
                    }), r, sp: 11, heightDp: 26).LayoutParameters = new LinearLayout.LayoutParams(Px(76), Px(26));
                ui.MeterIn(card, Ui.Jade, m.Progress + " / " + m.Target, m.Progress, m.Target, 12);
            }

            ui.Text("PAWILONY" + (d.CanBuild ? string.Empty : " (buduje mistrz i starsi)"), page, 11, Ui.GoldDim, bold: true);
            ui.Pages(page, d.Buildings.Count, 2, 1, (i, cell) =>
            {
                var b = d.Buildings[i];
                var card = ui.Card(cell, 1f, 6, b.Level > 0 ? Ui.Gold : (Color?)null, 0);
                ui.Text(b.NamePl + " " + b.Level + "/" + b.MaxLevel, card, 12, Ui.Gold, bold: true);
                ui.Text(b.DescriptionPl, card, 10, Ui.Muted, maxLines: 2);
                if (d.CanBuild && b.NextCost.HasValue)
                    ui.Btn("Buduj · " + b.NextCost + " KD", () => Run(async () =>
                    {
                        await _api.BuildAsync(sectId, c.Id, b.Id, Ct);
                        Sfx.Play(Sfx.Breakthrough, 0.5f);
                        await Reload(b.NamePl + " rozbudowany.");
                    }), card, secondary: b.NextCost > d.Treasury, sp: 10, heightDp: 30);
            });
        }

        // ---- cauldron / forge queue --------------------------------------------------------------------------

        private void CraftQueueRow(EconomyFlow economy, Ui ui, LinearLayout page)
        {
            var c = _me;
            var row = ui.HRow(page, 30, 2);
            var text = ui.Text("Kolejka: …", row, 11, Ui.Muted, maxLines: 2);
            text.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { Gravity = GravityFlags.CenterVertical };
            var collect = ui.Btn("Odbierz", () => { }, row, sp: 11, heightDp: 28);
            collect.LayoutParameters = new LinearLayout.LayoutParams(Px(80), Px(28)) { Gravity = GravityFlags.CenterVertical };
            collect.Visibility = ViewStates.Gone;
            var gen = _screen;
            Run(async () =>
            {
                var q = await _api.GetCraftQueueAsync(c.Id, Ct);
                if (gen != _screen) return;
                foreach (var craftKind in new[] { "alchemy", "smithing" })
                {
                    var lastReady = q.Entries.Where(qe => (qe.Kind == "alchemy") == (craftKind == "alchemy") && !qe.Ready).Select(qe => (DateTimeOffset?)qe.ReadyAt).DefaultIfEmpty(null).Max();
                    Remind("craft_" + craftKind, lastReady, craftKind == "alchemy" ? "Kocioł gotowy" : "Kuźnia gotowa", "Partia czeka na odbiór.");
                }
                text.Text = q.Entries.Count == 0 ? "Kocioł i kuźnia wolne — ⏳ wytwarza partię sam, także offline." :
                    string.Join("  ·  ", q.Entries.Select(e => (e.Kind == "alchemy" ? "Kocioł" : "Kuźnia") + ": " + e.Times + "× " + e.OutputNamePl + " " + (e.Ready ? "gotowe" : Until(e.ReadyAt))));
                if (q.Entries.Any(e => e.Ready))
                {
                    collect.Visibility = ViewStates.Visible;
                    collect.Click += (_, __) => Run(async () =>
                    {
                        var r = await _api.CollectCraftQueueAsync(c.Id, Ct);
                        await economy.LoadAsync(Ct);
                        ShowBag(economy, 1, null, null, 0);
                        Sfx.Play(Sfx.Chime);
                        ShowLoot("Z kotła i kuźni", 0, r.Collected.Where(x => x.Quantity > 0).Select(x => (x.OutputItem, (long)x.Quantity)).ToList(), null);
                        SetStatus(string.Join(", ", r.Collected.Select(x => x.Successes + "/" + x.Times + " udanych")));
                    });
                }
            });
        }

        /// <summary>Largest batch (up to 10) the bag and wallet can pay for right now.</summary>
        private static int QueueTimes(EconomyFlow economy, RecipeDto recipe)
        {
            var n = 0;
            while (n < 10 && economy.CanAfford(recipe, n + 1)) n++;
            return n;
        }
    }
}
