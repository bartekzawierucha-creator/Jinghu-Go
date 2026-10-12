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

        private async void Start()
        {
            UnityEngine.Application.targetFrameRate = 60;
            _api = new ApiClient(new UnityWebRequestTransport(serverUrl), new NewtonsoftJsonSerializer(), new PlayerPrefsTokenStore());
            _api.SessionExpired += ShowLogin;
            _canvas = UiKit.CreateCanvas();

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
            var email = UiKit.TextField(s, "E-mail");
            var password = UiKit.TextField(s, "Hasło", password: true);
            UiKit.ActionButton(s, "Zaloguj", () => Run(async () =>
            {
                await _api.LoginAsync(email.text, password.text, _lifetime.Token);
                await ShowHomeAsync();
            }));
            UiKit.ActionButton(s, "Załóż konto", () => Run(async () =>
            {
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
                default: return "Błąd: " + e.Code + (e.CorrelationId != null ? " (" + e.CorrelationId + ")" : string.Empty);
            }
        }
    }
}
