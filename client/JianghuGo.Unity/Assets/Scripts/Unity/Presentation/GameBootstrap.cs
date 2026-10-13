using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JianghuGo.Client.Application;
using JianghuGo.Unity.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace JianghuGo.Unity.Presentation
{
    /// <summary>
    /// Composition root and screen router for the Stage 2 flow:
    /// Login/Register -> (no characters) Character creation -> Character sheet.
    /// Attach to an empty GameObject in the Bootstrap scene; set ServerUrl per build profile.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private string serverUrl = "http://localhost:8080";

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ApiClient _api;
        private Canvas _canvas;
        private RectTransform _screen;
        private Text _status;

        private const string ServerUrlKey = "jianghu.server_url";

        private async void Start()
        {
            UnityEngine.Application.targetFrameRate = 60;
            _canvas = UiKit.CreateCanvas();
            if (!ConnectTo(PlayerPrefs.GetString(ServerUrlKey, serverUrl)))
            {
                ShowLogin();
                return;
            }

            try
            {
                if (await _api.TryResumeAsync(_lifetime.Token)) await ShowHomeAsync();
                else ShowLogin();
            }
            catch (ApiException e)
            {
                ShowLogin();
                SetStatus(Describe(e));
            }
        }

        /// <summary>
        /// (Re)creates the API client for a server address. The address is chosen on the login screen and remembered,
        /// so one APK works with a local test server (LAN, development build) and a deployed HTTPS server.
        /// </summary>
        private bool ConnectTo(string url)
        {
            try
            {
                var transport = new UnityWebRequestTransport(url.Trim());
                if (_api != null) _api.SessionExpired -= ShowLogin;
                _api = new ApiClient(transport, new NewtonsoftJsonSerializer(), new PlayerPrefsTokenStore());
                _api.SessionExpired += ShowLogin;
                serverUrl = url.Trim();
                PlayerPrefs.SetString(ServerUrlKey, serverUrl);
                PlayerPrefs.Save();
                return true;
            }
            catch (ArgumentException)
            {
                return false; // e.g. http:// in a release build
            }
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private RectTransform NewScreen(string title)
        {
            if (_screen != null) Destroy(_screen.gameObject);
            _screen = UiKit.Panel(_canvas.transform, title);
            UiKit.Label(_screen, title, 64, UiKit.Cinnabar);
            _status = UiKit.Label(_screen, string.Empty, 32, UiKit.Cinnabar);
            return _screen;
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        private void ShowLogin()
        {
            var s = NewScreen("Jianghu Go");
            var server = UiKit.TextField(s, "Adres serwera, np. http://192.168.1.10:8080");
            server.text = serverUrl;
            var email = UiKit.TextField(s, "E-mail");
            var password = UiKit.TextField(s, "Hasło", password: true);

            bool Connect()
            {
                if (server.text.Trim() == serverUrl && _api != null) return true;
                if (ConnectTo(server.text)) return true;
                SetStatus("Ta wersja aplikacji wymaga adresu https://");
                return false;
            }

            UiKit.ActionButton(s, "Zaloguj", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.LoginAsync(email.text, password.text, _lifetime.Token);
                await ShowHomeAsync();
            }));
            UiKit.ActionButton(s, "Załóż konto", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.RegisterAsync(email.text, password.text, _lifetime.Token);
                await _api.LoginAsync(email.text, password.text, _lifetime.Token);
                await ShowHomeAsync();
            }));
        }

        private async System.Threading.Tasks.Task ShowHomeAsync()
        {
            var characters = await _api.GetCharactersAsync(_lifetime.Token);
            if (characters.Count == 0) await ShowCreationAsync();
            else ShowCharacter(characters[0]);
        }

        private async System.Threading.Tasks.Task ShowCreationAsync()
        {
            var flow = new CharacterCreationFlow(_api);
            await flow.StartAsync(_lifetime.Token);
            var s = NewScreen("Narodziny wojownika");
            var sheet = UiKit.Label(s, string.Empty, 34);
            var name = UiKit.TextField(s, "Imię postaci");
            void Render()
            {
                var d = flow.Draft;
                sheet.text =
                    string.Join("\n", d.Traits.Select(t => (flow.LockedCategories.Contains(t.Category) ? "[X] " : "[ ] ") + t.NamePl + " (" + t.Rarity + ")")) +
                    "\n\n" + string.Join("   ", d.Stats.Select(st => st.Id + " " + st.Value)) +
                    "\n\nŻetony Losu: " + d.DestinyTokens + "   koszt losowania: " + flow.NextRerollCost;
            }

            flow.DraftChanged += _ => Render();
            foreach (var category in new[] { "background", "bloodline", "constitution" })
            {
                UiKit.ActionButton(s, "Zablokuj/odblokuj: " + category, () =>
                {
                    if (!flow.ToggleLock(category)) SetStatus("Możesz zablokować najwyżej " + flow.Draft.MaxLockedTraits + " cechy.");
                    Render();
                });
            }

            UiKit.ActionButton(s, "Losuj ponownie", () => Run(async () =>
            {
                if (!await flow.RerollAsync(_lifetime.Token)) SetStatus("Szkic zmienił się na innym urządzeniu — odświeżono.");
            }));
            foreach (var path in new[] { "qi", "body", "flame", "poison" })
            {
                UiKit.ActionButton(s, "Ścieżka: " + path, () => Run(async () => ShowCharacter(await flow.FinalizeAsync(name.text, path, _lifetime.Token))));
            }

            Render();
        }

        private void ShowCharacter(CharacterDto c)
        {
            var s = NewScreen(c.Name);
            UiKit.Label(s, "Ścieżka: " + c.Path + "   " + string.Join("  ", c.Traits.Select(t => t.NamePl)), 32);
            UiKit.Label(s, string.Join("   ", c.Stats.Select(st => st.Id + " " + st.Value)), 30);
            var cultivation = UiKit.Label(s, "…", 38);
            var inbox = UiKit.Label(s, string.Empty, 30);
            var flow = new CultivationFlow(_api, c.Id);

            void Render(CultivationDto st)
            {
                var deviation = st.DeviationUntil.HasValue && st.DeviationUntil > DateTimeOffset.UtcNow
                    ? "\nOdchylenie Qi do " + st.DeviationUntil.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
                cultivation.text = st.RealmNamePl + " — etap " + st.Stage + "/" + st.StagesInRealm +
                                   "\nQi: " + st.Qi + " / " + st.QiRequired + "   szansa przełomu: " + Math.Round(st.BreakthroughChance * 100) + "%" +
                                   (st.IsMeditating ? "\nMedytujesz od " + st.MeditationStartedAt.Value.ToLocalTime().ToString("HH:mm") : string.Empty) +
                                   "\nDzisiaj z medytacji: " + st.DailyMeditationQi + " / " + st.DailyMeditationQiCap + deviation;
            }

            flow.StateChanged += Render;
            UiKit.ActionButton(s, "Medytuj / zakończ medytację", () => Run(async () =>
            {
                if (flow.State.IsMeditating)
                {
                    var r = await flow.FinishMeditationAsync(_lifetime.Token);
                    SetStatus("+" + r.QiGained + " Qi (" + r.MinutesCounted + " min)" + (r.DailyCapReached ? " — dzienny limit" : string.Empty));
                }
                else
                {
                    await flow.StartMeditationAsync(_lifetime.Token);
                }
            }));
            UiKit.ActionButton(s, "Próba przełomu", () => Run(async () =>
            {
                var r = await flow.BreakthroughAsync(_lifetime.Token);
                SetStatus(r == null ? "Stan zmienił się na innym urządzeniu — odświeżono." : r.Success ? "Przełom udany!" : "Przełom nieudany — odchylenie Qi.");
            }));
            UiKit.ActionButton(s, "Eksploruj świat", () => Run(async () =>
            {
                var location = GetComponent<UnityLocationSource>();
                if (location == null) location = gameObject.AddComponent<UnityLocationSource>(); // not "??": Unity overrides == for destroyed objects
                var exploration = new ExplorationFlow(_api, location, c.Id);
                await exploration.StartAsync(_lifetime.Token);
                ShowWorld(exploration, c);
            }));

            foreach (var npc in new[] { ("training_dummy", "Manekin"), ("grey_wolf", "Szary Wilk"), ("mountain_bandit", "Bandyta") })
            {
                UiKit.ActionButton(s, "Walcz: " + npc.Item2, () => Run(async () =>
                {
                    var combat = new CombatFlow(_api);
                    await combat.StartPveAsync(c.Id, npc.Item1, _lifetime.Token);
                    ShowBattle(combat, c);
                }));
            }

            UiKit.ActionButton(s, "Sekta i terytoria", () => Run(async () =>
            {
                var sect = new SectFlow(_api, c.Id);
                await sect.LoadAsync(_lifetime.Token);
                ShowSect(sect, c);
            }));
            UiKit.ActionButton(s, "Laboratorium Dao", () => Run(async () =>
            {
                var lab = new DaoLabFlow(_api, c.Id);
                await lab.LoadAsync(_lifetime.Token);
                var economy = new EconomyFlow(_api, c.Id);
                await economy.LoadAsync(_lifetime.Token);
                ShowDaoLab(lab, economy, c);
            }));
            UiKit.ActionButton(s, "Sakwa i ekwipunek", () => Run(async () =>
            {
                var economy = new EconomyFlow(_api, c.Id);
                await economy.LoadAsync(_lifetime.Token);
                ShowBag(economy, c);
            }));
            UiKit.ActionButton(s, "Powiadomienia", () => Run(async () =>
            {
                var page = await _api.GetNotificationsAsync(true, 5, null, _lifetime.Token);
                inbox.text = page.Items.Count == 0 ? "Brak nowych powiadomień." : string.Join("\n", page.Items.Select(Describe));
                foreach (var n in page.Items) await _api.MarkNotificationReadAsync(n.Id, _lifetime.Token);
            }));
            UiKit.ActionButton(s, "Wyloguj", () => Run(async () =>
            {
                await _api.LogoutAsync(_lifetime.Token);
                ShowLogin();
            }));
            Run(() => flow.LoadAsync(_lifetime.Token));
        }

        /// <summary>
        /// Prototype world screen: list of nearby nodes instead of the scroll-style map (IMapProvider + WebMercator are
        /// ready in Core for the map renderer). Ticks on a timer only while this screen is open.
        /// </summary>
        private void ShowWorld(ExplorationFlow exploration, CharacterDto c)
        {
            var s = NewScreen("Jianghu");
            var info = UiKit.Label(s, "…", 32);
            var list = UiKit.Label(s, string.Empty, 30);
            var open = true;

            void Render()
            {
                var n = exploration.Nearby;
                info.text = (exploration.Problem == null ? "Pozycja potwierdzona" : "Pozycja: " + exploration.Problem) +
                            (n?.Event != null ? "\nWydarzenie: " + n.Event.NamePl : string.Empty) +
                            (n?.NearestFlameSite != null ? "\nNajbliższe miejsce Płomienia: " + n.NearestFlameSite.NamePl + " (" + n.NearestFlameSite.DistanceM / 1000 + " km)" : string.Empty);
                list.text = n == null ? string.Empty : string.Join("\n", n.Nodes.Take(8).Select(x =>
                    x.NamePl + " — " + x.DistanceM + " m" + (x.Harvested ? " (zebrane)" : x.InRange ? " [w zasięgu]" : string.Empty)));
            }

            exploration.Changed += Render;
            UiKit.ActionButton(s, "Zbierz / walcz z najbliższym", () => Run(async () =>
            {
                var target = exploration.Nearby?.Nodes.FirstOrDefault(x => x.InRange && !x.Harvested);
                if (target == null)
                {
                    SetStatus("Nic w zasięgu — podejdź bliżej.");
                    return;
                }

                var r = await exploration.InteractAsync(target.Key, _lifetime.Token);
                if (r.BattleId.HasValue)
                {
                    open = false;
                    var combat = new CombatFlow(_api);
                    await combat.OpenAsync(r.BattleId.Value, _lifetime.Token);
                    ShowBattle(combat, c);
                }
                else
                {
                    SetStatus("+" + r.RewardQi + " Qi");
                }
            }));
            UiKit.ActionButton(s, "Dostrój się do Płomienia", () => Run(async () =>
            {
                var site = exploration.Nearby?.NearestFlameSite;
                if (site == null || !site.InRange)
                {
                    SetStatus("Musisz stać przy jednym z 12 miejsc Płomienia.");
                    return;
                }

                var r = await exploration.AttuneAsync(site.Id, _lifetime.Token);
                SetStatus("+" + r.RewardQi + " Qi z Płomienia");
            }));
            UiKit.ActionButton(s, "Zakończ eksplorację", () => Run(async () =>
            {
                open = false;
                await exploration.StopAsync(_lifetime.Token);
                ShowCharacter(c);
            }));

            async void Loop()
            {
                while (open && !_lifetime.IsCancellationRequested)
                {
                    try
                    {
                        await exploration.TickAsync(_lifetime.Token);
                    }
                    catch (ApiException e)
                    {
                        SetStatus(Describe(e));
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        await System.Threading.Tasks.Task.Delay(exploration.RecommendedInterval, _lifetime.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            Render();
            Loop();
        }

        /// <summary>
        /// Prototype battle screen: text timeline instead of the side-view pixel-art animation (Presentation layer only;
        /// CombatFlow and the server protocol stay the same when the animated view replaces this).
        /// </summary>
        private void ShowBattle(CombatFlow combat, CharacterDto c)
        {
            var s = NewScreen("Pojedynek");
            var header = UiKit.Label(s, string.Empty, 36);
            var log = UiKit.Label(s, string.Empty, 28);
            var buttons = new List<UnityEngine.UI.Button>();

            void Render(BattleDto b)
            {
                header.text = b.Me.Name + "  HP " + b.Me.Hp + "/" + b.Me.MaxHp + "  Qi " + b.Me.Qi + "/" + b.Me.MaxQi +
                              "\n" + b.Opponent.Name + "  HP " + b.Opponent.Hp + "/" + b.Opponent.MaxHp +
                              "\nTura " + b.Turn + "   dystans " + b.Distance +
                              (b.IsActive ? string.Empty : "\nWYNIK: " + b.Result + (b.RewardQi > 0 ? "  (+" + b.RewardQi + " Qi)" : string.Empty));
                log.text = string.Join("\n", b.LastTurnEvents.Select(e =>
                    (b.IsMine(e) ? "Ty" : "Wróg") + ": " + e.Action + (e.TechniqueId != null ? " " + e.TechniqueId : string.Empty) +
                    (e.Hit == true ? " → " + e.Damage + " obrażeń" : e.Hit == false ? " → pudło" : string.Empty)));
                for (var i = 0; i < buttons.Count; i++) buttons[i].interactable = b.IsActive && !b.MyActionSubmitted;
            }

            foreach (var slot in combat.Battle.MySlots.Where(x => x.TechniqueId != null))
            {
                var captured = slot;
                if (captured.Kind == "movement")
                {
                    buttons.Add(UiKit.ActionButton(s, captured.NamePl + " (naprzód)", () => Run(() => combat.ActAsync(captured.Slot, "advance", _lifetime.Token))));
                    buttons.Add(UiKit.ActionButton(s, captured.NamePl + " (odwrót)", () => Run(() => combat.ActAsync(captured.Slot, "retreat", _lifetime.Token))));
                }
                else
                {
                    buttons.Add(UiKit.ActionButton(s, captured.NamePl + " [" + captured.Kind + "]", () => Run(() => combat.ActAsync(captured.Slot, null, _lifetime.Token))));
                }
            }

            buttons.Add(UiKit.ActionButton(s, "Odpoczynek (+Qi)", () => Run(() => combat.ActAsync(null, null, _lifetime.Token))));
            UiKit.ActionButton(s, "Wróć", () => ShowCharacter(c));
            combat.BattleChanged += Render;
            Render(combat.Battle);
        }

        // ---- economy (prototype list screens; balances, chances and fees always come from the server) -----------

        private void ShowBag(EconomyFlow economy, CharacterDto c)
        {
            var s = NewScreen("Sakwa — " + c.Name);
            var summary = UiKit.Label(s, "…", 32);
            var list = UiKit.Column(s, "Items");

            void Render(InventoryDto inv)
            {
                summary.text = "Kamienie Duchowe: " + inv.Balance + "   Miejsca: " + inv.SlotsUsed + "/" + inv.Capacity +
                               (inv.Bonuses.Count > 0 ? "\nBonusy: " + string.Join("  ", inv.Bonuses.Select(b => b.Key + " +" + b.Value)) : string.Empty);
                foreach (Transform child in list) Destroy(child.gameObject);
                foreach (var st in inv.Stacks)
                {
                    var stack = st;
                    if (stack.Type == "consumable")
                        UiKit.ActionButton(list, "Użyj: " + stack.NamePl + " ×" + stack.Quantity, () => Run(async () =>
                        {
                            var r = await economy.UseAsync(stack.ItemId, 1, _lifetime.Token);
                            Render(economy.Inventory);
                            SetStatus("+" + r.QiGranted + " Qi (wkrótce na koncie kultywacji)");
                        }));
                    else
                        UiKit.Label(list, stack.NamePl + " ×" + stack.Quantity, 30);
                }

                foreach (var it in inv.Items)
                {
                    var item = it;
                    var caption = (item.IsEquipped ? "[założone] " : item.State == "listed" ? "[na targu] " : string.Empty) +
                                  item.NamePl + (item.UpgradeLevel > 0 ? " +" + item.UpgradeLevel : string.Empty) +
                                  "  " + string.Join(" ", item.Stats.Select(x => x.Key + "+" + x.Value));
                    if (item.State == "listed") { UiKit.Label(list, caption, 30); continue; }
                    UiKit.ActionButton(list, caption, () => Run(async () =>
                    {
                        var ok = await economy.ToggleEquipAsync(item, _lifetime.Token);
                        Render(economy.Inventory);
                        if (!ok) SetStatus("Przedmiot zmienił się na innym urządzeniu — odświeżono.");
                    }));
                    var next = economy.NextUpgrade(item);
                    if (next != null)
                        UiKit.ActionButton(list, "  Ulepsz do +" + next.Level + " (" + next.Stones + " KD, " + next.SuccessPermille / 10 + "%)", () => Run(async () =>
                        {
                            var r = await economy.UpgradeAsync(item, _lifetime.Token);
                            Render(economy.Inventory);
                            SetStatus(r == null ? "Odświeżono." : r.Success ? "Udane! Teraz +" + r.Level : "Nieudane — poziom +" + r.Level + " zachowany.");
                        }));
                }
            }

            UiKit.ActionButton(s, "Rzemiosło i alchemia", () => ShowCrafting(economy, c));
            UiKit.ActionButton(s, "Targ", () => Run(async () => await ShowMarketAsync(economy, c)));
            UiKit.ActionButton(s, "Wróć", () => ShowCharacter(c));
            Render(economy.Inventory);
        }

        private void ShowCrafting(EconomyFlow economy, CharacterDto c)
        {
            var s = NewScreen("Kuźnia i alchemia");
            var balance = UiKit.Label(s, string.Empty, 32);
            void Render(InventoryDto inv) => balance.text = "Kamienie Duchowe: " + inv.Balance;
            Render(economy.Inventory);
            foreach (var r in economy.Recipes.Items)
            {
                var recipe = r;
                var inputs = string.Join(", ", recipe.Inputs.Select(i => i.Item + "×" + i.Quantity));
                UiKit.ActionButton(s, recipe.Output.Item + " ← " + inputs + " + " + recipe.Stones + " KD" +
                                      (recipe.Kind == "alchemy" ? " (" + recipe.SuccessPermille / 10 + "%+)" : string.Empty), () => Run(async () =>
                {
                    if (!economy.CanAfford(recipe, 1)) { SetStatus("Brakuje składników lub kamieni."); return; }
                    var result = await economy.CraftAsync(recipe.Id, 1, _lifetime.Token);
                    Render(economy.Inventory);
                    SetStatus(result.Successes > 0 ? "Wytworzono: " + result.OutputItem : "Alchemia nie powiodła się — składniki przepadły.");
                }));
            }

            UiKit.ActionButton(s, "Wróć do sakwy", () => ShowBag(economy, c));
        }

        private async System.Threading.Tasks.Task ShowMarketAsync(EconomyFlow economy, CharacterDto c)
        {
            var page = await economy.BrowseAsync(null, null, _lifetime.Token);
            var mine = await economy.MyListingsAsync(_lifetime.Token);
            var s = NewScreen("Targ");
            var rules = economy.MarketRules;
            UiKit.Label(s, "Opłata za wystawienie " + rules.ListingFeePermille / 10.0 + "% (min. " + rules.MinListingFee + "), podatek od sprzedaży " +
                           rules.SaleTaxPermille / 10.0 + "%, oferta ważna " + rules.ListingHours + " h", 28);
            foreach (var l in page.Items)
            {
                var listing = l;
                if (listing.SellerCharacterId == c.Id) continue;
                UiKit.ActionButton(s, "Kup: " + listing.ItemNamePl + (listing.UpgradeLevel > 0 ? " +" + listing.UpgradeLevel : string.Empty) +
                                      " ×" + listing.Quantity + " — " + listing.Price + " KD (" + listing.SellerName + ")", () => Run(async () =>
                {
                    var bought = await economy.BuyAsync(listing, _lifetime.Token);
                    SetStatus(bought == null ? "Ktoś był szybszy — oferta już nieaktualna." : "Kupiono za " + bought.Paid + " KD.");
                    await ShowMarketAsync(economy, c);
                }));
            }

            foreach (var m in mine.Where(x => x.Listing.Status == "active"))
            {
                var listing = m.Listing;
                UiKit.ActionButton(s, (listing.Expired ? "Odbierz (wygasła): " : "Wycofaj: ") + listing.ItemNamePl + " ×" + listing.Quantity, () => Run(async () =>
                {
                    await economy.CancelAsync(listing, _lifetime.Token);
                    await ShowMarketAsync(economy, c);
                }));
            }

            // Prototype selling: the first sellable bag item at a typed price.
            var price = UiKit.TextField(s, "Cena w Kamieniach Duchowych");
            UiKit.ActionButton(s, "Wystaw pierwszy przedmiot z sakwy", () => Run(async () =>
            {
                if (!long.TryParse(price.text, out var p) || p < rules.MinPrice || p > rules.MaxPrice) { SetStatus("Podaj poprawną cenę."); return; }
                var item = economy.Inventory.Items.FirstOrDefault(i => i.InBag);
                var stack = economy.Inventory.Stacks.FirstOrDefault();
                if (item != null) await economy.SellAsync(item, p, _lifetime.Token);
                else if (stack != null) await economy.SellAsync(stack.ItemId, stack.Quantity, p, _lifetime.Token);
                else { SetStatus("Sakwa jest pusta."); return; }
                await ShowMarketAsync(economy, c);
            }));
            UiKit.ActionButton(s, "Wróć do sakwy", () => ShowBag(economy, c));
        }

        // ---- Dao Lab (prototype: attack designs only; the server validates every component and the budget) -------

        private void ShowDaoLab(DaoLabFlow lab, EconomyFlow economy, CharacterDto c)
        {
            var s = NewScreen("Laboratorium Dao");
            var name = UiKit.TextField(s, "Nazwa techniki");
            var power = UiKit.TextField(s, "Moc (1–30)");
            var qi = UiKit.TextField(s, "Koszt Qi (0–20)");
            var cooldown = UiKit.TextField(s, "Odnowienie w turach (0–4)");
            var reach = UiKit.TextField(s, "Zasięg maks. (1–3)");
            var verdict = UiKit.Label(s, "Ułóż technikę i sprawdź budżet.", 28);

            DaoDesignDto Compose()
            {
                var d = DaoLabFlow.StarterStrike(name.text);
                if (int.TryParse(power.text, out var p)) d.Attack.Power = p;
                if (int.TryParse(qi.text, out var q)) d.QiCost = q;
                if (int.TryParse(cooldown.text, out var cd)) d.Cooldown = cd;
                if (int.TryParse(reach.text, out var r)) d.Attack.RangeMax = r;
                return d;
            }

            string Describe(DaoPreviewDto p) =>
                (p.Valid ? "Dozwolona" : "Niedozwolona") + " — moc " + p.Points + " / budżet " + p.Allowance +
                (p.Problems.Count > 0 ? "\n" + string.Join("\n", p.Problems) : string.Empty);

            UiKit.ActionButton(s, "Sprawdź budżet", () => Run(async () => verdict.text = Describe(await lab.PreviewAsync(Compose(), _lifetime.Token))));
            UiKit.ActionButton(s, "Stwórz technikę (opłata w KD)", () => Run(async () =>
            {
                var created = await lab.CreateAsync(Compose(), _lifetime.Token);
                if (created == null) { verdict.text = Describe(lab.LastPreview); return; }
                ShowDaoLab(lab, economy, c);
                SetStatus("Powstała technika: " + created.Technique.NamePl);
            }));

            foreach (var k in lab.Known)
            {
                var known = k;
                UiKit.ActionButton(s, "Załóż: " + known.Technique.NamePl + " (" + known.Technique.Slot + ")", () => Run(async () =>
                {
                    var ok = await lab.EquipAsync(known.Technique.Id, known.Technique.Slot, _lifetime.Token);
                    SetStatus(ok ? "Technika w zestawie walki." : "Zestaw zmienił się na innym urządzeniu — odświeżono.");
                }));
                if (known.Source == "designed")
                    UiKit.ActionButton(s, "  Spisz zwój: " + known.Technique.NamePl, () => Run(async () =>
                    {
                        await lab.InscribeAsync(known.Technique.Id, _lifetime.Token);
                        SetStatus("Zwój trafił do sakwy — możesz go wystawić na targu.");
                    }));
            }

            foreach (var it in economy.Inventory.Items.Where(i => i.BoundRef != null && i.InBag))
            {
                var scroll = it;
                UiKit.ActionButton(s, "Przeczytaj zwój (" + scroll.BoundRef + ")", () => Run(async () =>
                {
                    var learned = await lab.LearnAsync(scroll, _lifetime.Token);
                    await economy.ReloadAsync(_lifetime.Token);
                    ShowDaoLab(lab, economy, c);
                    SetStatus("Poznano technikę: " + learned.Technique.NamePl);
                }));
            }

            UiKit.ActionButton(s, "Wróć", () => ShowCharacter(c));
        }

        // ---- sects & territories (prototype list screen; every permission is checked by the server) ---------------

        private void ShowSect(SectFlow flow, CharacterDto c)
        {
            if (flow.Mine == null)
            {
                var s0 = NewScreen("Sekty");
                var name = UiKit.TextField(s0, "Nazwa nowej sekty");
                UiKit.ActionButton(s0, "Załóż sektę (opłata w KD)", () => Run(async () =>
                {
                    await flow.FoundAsync(name.text, _lifetime.Token);
                    ShowSect(flow, c);
                }));
                UiKit.ActionButton(s0, "Pokaż sekty", () => Run(async () =>
                {
                    var page = await flow.BrowseAsync(_lifetime.Token);
                    foreach (var item in page.Items)
                    {
                        var sect = item;
                        UiKit.ActionButton(s0, "Aplikuj: " + sect.Name + " (" + sect.MemberCount + ")", () => Run(async () =>
                        {
                            await flow.ApplyAsync(sect.Id, "Proszę o przyjęcie.", _lifetime.Token);
                            SetStatus("Wysłano prośbę do " + sect.Name + ".");
                        }));
                    }
                }));
                UiKit.ActionButton(s0, "Wróć", () => ShowCharacter(c));
                return;
            }

            var mine = flow.Mine;
            var s = NewScreen(mine.Sect.Name);
            UiKit.Label(s, "Twoja ranga: " + mine.MyRole + "   Skarbiec: " + mine.Treasury + " KD   Członkowie: " + mine.Sect.MemberCount + "/" + mine.Sect.MaxMembers, 30);
            UiKit.Label(s, string.Join("\n", mine.Sect.Members.Select(m => m.Name + " — " + m.Role + " (wkład " + m.Contribution + ")")), 28);
            if (flow.Territories != null)
                UiKit.Label(s, "Terytoria: " + flow.Territories.Owned + "/" + flow.Territories.Max +
                               (flow.Territories.Wars.Count > 0 ? "   Wojny: " + string.Join(", ", flow.Territories.Wars.Select(w => w.TerritoryKey + " " + w.Phase + " " + w.AttackerScore + ":" + w.DefenderScore)) : string.Empty), 28);

            foreach (var a in mine.PendingApplications)
            {
                var application = a;
                UiKit.ActionButton(s, "Przyjmij: " + application.CharacterName, () => Run(async () => { await flow.DecideAsync(application, true, _lifetime.Token); ShowSect(flow, c); }));
                UiKit.ActionButton(s, "  Odrzuć: " + application.CharacterName, () => Run(async () => { await flow.DecideAsync(application, false, _lifetime.Token); ShowSect(flow, c); }));
            }

            UiKit.ActionButton(s, "Wpłać 100 KD do skarbca", () => Run(async () => { await flow.DonateAsync(100, _lifetime.Token); ShowSect(flow, c); }));
            UiKit.ActionButton(s, "Terytorium tutaj", () => Run(async () =>
            {
                var here = await flow.HereAsync(_lifetime.Token);
                SetStatus(here.Key + ": " + (here.OwnerName ?? "niczyje") + (here.War != null ? " — wojna (" + here.War.Phase + ")" : string.Empty));
            }));
            if (mine.CanManage)
            {
                UiKit.ActionButton(s, "Zajmij terytorium tutaj", () => Run(async () => { var t = await flow.ClaimHereAsync(_lifetime.Token); ShowSect(flow, c); SetStatus("Zajęto " + t.Key + "."); }));
                UiKit.ActionButton(s, "Wypowiedz wojnę o terytorium tutaj", () => Run(async () =>
                {
                    var w = await flow.ChallengeHereAsync(_lifetime.Token);
                    ShowSect(flow, c);
                    SetStatus("Wojna o " + w.TerritoryKey + " od " + w.StartsAt.ToLocalTime().ToString("dd.MM HH:mm") + ".");
                }));
                foreach (var t in flow.Territories?.Territories ?? new List<TerritoryDto>())
                {
                    var territory = t;
                    UiKit.ActionButton(s, "Danina z " + territory.Key, () => Run(async () => { await flow.CollectTributeAsync(territory, _lifetime.Token); ShowSect(flow, c); }));
                }
            }

            UiKit.ActionButton(s, "Opuść sektę", () => Run(async () => { await flow.LeaveAsync(_lifetime.Token); ShowSect(flow, c); }));
            UiKit.ActionButton(s, "Wróć", () => ShowCharacter(c));
        }

        private static string Describe(NotificationDto n)
        {
            switch (n.Type)
            {
                case "welcome": return "Witaj w Jianghu!";
                case "breakthrough_succeeded": return "Przełom udany.";
                case "breakthrough_failed": return "Przełom nieudany.";
                case "battle_won": return "Zwycięstwo w walce!";
                case "battle_lost": return "Porażka w walce.";
                case "battle_draw": return "Walka nierozstrzygnięta.";
                case "pvp_challenge": return "Ktoś rzucił ci wyzwanie!";
                case "market_sold": return "Sprzedano przedmiot na targu.";
                case "sect_application_accepted": return "Przyjęto cię do sekty!";
                case "sect_application_rejected": return "Sekta odrzuciła twoją prośbę.";
                case "war_declared": return "Wypowiedziano wojnę o terytorium twojej sekty!";
                case "war_resolved": return "Wojna o terytorium rozstrzygnięta.";
                default: return n.Type;
            }
        }

        /// <summary>Runs a UI action, turning failures into a readable status line instead of an unhandled exception.</summary>
        private async void Run(Func<System.Threading.Tasks.Task> action)
        {
            SetStatus("…");
            try
            {
                await action();
                SetStatus(string.Empty);
            }
            catch (OperationCanceledException)
            {
                // scene is shutting down
            }
            catch (ApiException e)
            {
                SetStatus(Describe(e));
            }
        }

        private static string Describe(ApiException e)
        {
            switch (e.Code)
            {
                case "network_error": return "Brak połączenia z serwerem.";
                case "invalid_credentials": return "Błędny e-mail lub hasło.";
                case "insufficient_destiny_tokens": return "Za mało Żetonów Losu.";
                case "name_taken": return "To imię jest już zajęte.";
                case "name_invalid": return "Imię: 2–24 litery, spacje, myślniki.";
                case "insufficient_qi": return "Za mało Qi na przełom.";
                case "qi_deviation_active": return "Odchylenie Qi — odpocznij przed kolejnym przełomem.";
                case "meditation_in_progress": return "Najpierw zakończ medytację.";
                case "out_of_range": return "Przeciwnik poza zasięgiem tej techniki.";
                case "on_cooldown": return "Technika się odnawia.";
                case "already_in_battle": return "Ta postać już walczy.";
                case "relocation_cooldown": return "Wykryto skok pozycji — nagrody wrócą za kilka minut.";
                case "low_accuracy": return "Słaby sygnał GPS — wyjdź na otwartą przestrzeń.";
                case "moving_too_fast": return "Poruszasz się zbyt szybko.";
                case "too_far": return "Podejdź bliżej.";
                case "already_harvested": return "Już to zebrałeś.";
                case "attunement_cooldown": return "Płomień jeszcze się nie odnowił.";
                case "insufficient_funds": return "Za mało Kamieni Duchowych.";
                case "insufficient_items": return "Brakuje przedmiotów.";
                case "bag_full": return "Sakwa jest pełna.";
                case "stack_full": return "Stos osiągnął limit.";
                case "item_listed": return "Przedmiot jest wystawiony na targu.";
                case "item_not_in_bag": return "Najpierw zdejmij przedmiot.";
                case "max_upgrade_level": return "Maksymalny poziom ulepszenia.";
                case "own_listing": return "Nie możesz kupić własnej oferty.";
                case "listing_limit": return "Za dużo aktywnych ofert.";
                case "design_invalid": return "Technika łamie zasady Dao: " + e.Message;
                case "design_limit": return "Osiągnięto limit autorskich technik.";
                case "not_author": return "Tylko autor może spisywać zwoje tej techniki.";
                case "technique_already_known": return "Znasz już tę technikę — zwój zostaje w sakwie.";
                case "loadout_invalid": return "Ten zestaw technik jest niedozwolony.";
                case "sect_permission_denied": return "Twoja ranga w sekcie na to nie pozwala.";
                case "already_in_sect": return "Należysz już do sekty.";
                case "sect_name_taken": return "Ta nazwa sekty jest zajęta.";
                case "insufficient_treasury": return "Za mało kamieni w skarbcu sekty.";
                case "territory_owned": return "To terytorium ma właściciela — wypowiedz wojnę.";
                case "war_in_progress": return "O to terytorium toczy się już wojna.";
                case "territory_limit": return "Sekta nie utrzyma więcej terytoriów.";
                case "tribute_not_ready": return "Danina jeszcze nie jest gotowa.";
                case "leader_must_transfer": return "Najpierw przekaż przywództwo.";
                default: return "Błąd: " + e.Code + (e.CorrelationId != null ? " (" + e.CorrelationId + ")" : string.Empty);
            }
        }
    }
}
