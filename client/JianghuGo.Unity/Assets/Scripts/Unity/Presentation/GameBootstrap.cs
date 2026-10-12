using System;
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
            UiKit.Label(s, "Ścieżka: " + c.Path, 40);
            UiKit.Label(s, string.Join("\n", c.Traits.Select(t => t.NamePl + " — " + t.Rarity)), 36);
            UiKit.Label(s, string.Join("\n", c.Stats.Select(st => st.Id + ": " + st.Value)), 36);
            UiKit.ActionButton(s, "Wyloguj", () => Run(async () =>
            {
                await _api.LogoutAsync(_lifetime.Token);
                ShowLogin();
            }));
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
                default: return "Błąd: " + e.Code + (e.CorrelationId != null ? " (" + e.CorrelationId + ")" : string.Empty);
            }
        }
    }
}
