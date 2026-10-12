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
            foreach (var npc in new[] { ("training_dummy", "Manekin"), ("grey_wolf", "Szary Wilk"), ("mountain_bandit", "Bandyta") })
            {
                UiKit.ActionButton(s, "Walcz: " + npc.Item2, () => Run(async () =>
                {
                    var combat = new CombatFlow(_api);
                    await combat.StartPveAsync(c.Id, npc.Item1, _lifetime.Token);
                    ShowBattle(combat, c);
                }));
            }

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
                default: return "Błąd: " + e.Code + (e.CorrelationId != null ? " (" + e.CorrelationId + ")" : string.Empty);
            }
        }
    }
}
