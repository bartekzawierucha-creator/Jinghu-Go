using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;

namespace JianghuGo.Android
{
    /// <summary>
    /// Composition root. The game runs in a fixed shell (hero HUD on top, five hubs in the dock, no scrolling
    /// anywhere); screens live in the MainActivity.*.cs partials. Every rule, cost and outcome comes from the server;
    /// this layer only renders state and sends intents through the shared client core. Flow methods resume on the
    /// thread pool (ConfigureAwait(false)) but our own awaits resume on the UI thread, so screens re-render after
    /// each await.
    /// </summary>
    [Activity(Label = "Jianghu Go", MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar",
        ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
    public sealed partial class MainActivity : Activity
    {
        private const string Prefs = "jianghu";
        private const string ServerUrlKey = "jianghu.server_url";
        private const string DefaultServer = "http://192.168.1.10:8080";
        private const string ModeKey = "jianghu.mode";
        private const string OfflineMode = "offline";
        private const string OnlineMode = "online";
        private const string OnlineTokenKey = "jianghu.refresh_token";
        private const string OfflineTokenKey = "jianghu.refresh_token.offline";

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ISharedPreferences _prefs;
        private ApiClient _api;
        private HttpClient _http;
        private string _serverUrl;
        private AndroidLocationSource _location;
        private ImageView _backdrop;
        private TextView _status;
        private Ui _ui;
        private int _screen;
        private System.Action _back;
        private bool _paused;
        private bool _worldOpen;
        private System.Action _onLocation;
        private FrameLayout _frame;
        private StepCounter _steps;
        private Compass _compass;

        private CharacterDto _me;
        private ExplorationFlow _exploration;

        private CancellationToken Ct => _lifetime.Token;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _prefs = GetSharedPreferences(Prefs, FileCreationMode.Private);
            _location = new AndroidLocationSource(this);
            _location.Updated += () => RunOnUiThread(() => _onLocation?.Invoke());
            _steps = new StepCounter(this);
            _compass = new Compass(this);
            Sfx.Init(this, _prefs);
            Music.Init(this, _prefs);
            // Crash reports (2.5): remember the crash; it is sent anonymously after the next sign-in.
            global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => RememberCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => RememberCrash(e.ExceptionObject as Exception);
            Ui.LoadFonts(this);
            BuildShell();

            Run(async () =>
            {
                if (_prefs.GetString(ModeKey, OfflineMode) == OfflineMode)
                {
                    await PlayOfflineAsync();
                    return;
                }

                if (!ConnectTo(_prefs.GetString(ServerUrlKey, DefaultServer)) || _prefs.GetString(OnlineTokenKey, null) == null)
                {
                    ShowLogin();
                    return;
                }

                ShowSplash("Łączenie z " + _serverUrl + "…");
                try
                {
                    if (await _api.TryResumeAsync(Ct)) await ShowHomeAsync();
                    else ShowLogin();
                }
                catch (ApiException)
                {
                    ShowLogin();
                    throw;
                }
            });
        }

        protected override void OnPause()
        {
            base.OnPause();
            _paused = true;
            Music.Pause();
            // A break of the app resets the play-time counter.
            _sessionStarted = null;
            _restReminders = 0;
            _location.Stop(); // battery: no GPS in the background
            _compass.Stop();
            _steps.Stop();
            EmbeddedServer.Flush(); // offline mode: the app may be killed in the background
        }

        protected override void OnResume()
        {
            base.OnResume();
            _paused = false;
            Music.Resume();
            _sessionStarted ??= DateTimeOffset.UtcNow;
            ScheduleRestReminder();
            if (_worldOpen)
            {
                _location.Start();
                _compass.Start();
            }
        }

        protected override void OnDestroy()
        {
            _lifetime.Cancel();
            _location.Stop();
            _http?.Dispose();
            base.OnDestroy();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == StepCounter.PermissionRequestCode && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                _steps.Start();
            if (requestCode == AndroidLocationSource.PermissionRequestCode)
                _location.OnPermissionResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }

#pragma warning disable CS0618, CS0672 // OnBackPressed: fine for this API range; one handler for every screen.
        public override void OnBackPressed()
        {
            if (CloseTopOverlay()) return;
            if (_back != null) _back();
            else base.OnBackPressed();
        }
#pragma warning restore CS0618, CS0672

        // ---- plumbing -----------------------------------------------------------------------------------------

        /// <summary>(Re)creates the API client for a server address; the address is chosen on the login screen and remembered.</summary>
        private bool ConnectTo(string url, bool offline = false)
        {
            url = (url ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(url + "/", UriKind.Absolute, out var baseUri) || (baseUri.Scheme != "http" && baseUri.Scheme != "https")) return false;
            if (_api != null && url == _serverUrl) return true;
            var tokenKey = offline ? OfflineTokenKey : OnlineTokenKey;
            _http?.Dispose();
            _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(20) };
            if (_api != null) _api.SessionExpired -= OnSessionExpired;
            _api = new ApiClient(new HttpClientTransport(_http), new StjSerializer(), new PrefsTokenStore(_prefs, tokenKey));
            _api.SessionExpired += OnSessionExpired;
            _serverUrl = url;
            using (var e = _prefs.Edit())
            {
                if (!offline) e.PutString(ServerUrlKey, url);
                e.PutString(ModeKey, offline ? OfflineMode : OnlineMode);
                e.Apply();
            }

            return true;
        }

        private void OnSessionExpired() => RunOnUiThread(() =>
        {
            ShowLogin();
            SetStatus("Sesja wygasła — zaloguj się ponownie.");
        });
        private void RememberCrash(Exception e)
        {
            if (e == null) return;
            try
            {
                using (var edit = _prefs.Edit())
                {
                    edit.PutString("jianghu.crash.type", e.GetType().FullName);
                    edit.PutString("jianghu.crash.stack", e.StackTrace ?? string.Empty);
                    edit.Commit();
                }
            }
            catch (Exception)
            {
                // never crash while recording a crash
            }
        }

        private async Task SendPendingCrashAsync()
        {
            var type = _prefs.GetString("jianghu.crash.type", null);
            if (type == null) return;
            try
            {
                var version = PackageManager?.GetPackageInfo(PackageName, 0)?.VersionName ?? "0";
                await _api.ReportCrashAsync(version, (int)Build.VERSION.SdkInt, type, _prefs.GetString("jianghu.crash.stack", string.Empty), Ct);
            }
            catch (Exception)
            {
                // try again next time... unless it was rejected
            }

            using (var edit = _prefs.Edit())
            {
                edit.Remove("jianghu.crash.type");
                edit.Remove("jianghu.crash.stack");
                edit.Apply();
            }
        }

        private DateTimeOffset? _sessionStarted;
        private int _restReminders;

        /// <summary>Healthy play (2.5): after each full hour in the game, a gentle reminder to rest your eyes and walk.</summary>
        private void ScheduleRestReminder()
        {
            var due = _sessionStarted!.Value.AddHours(_restReminders + 1) - DateTimeOffset.UtcNow;
            _frame?.PostDelayed(() =>
            {
                if (_paused || _sessionStarted is null) return;
                var hours = (int)Math.Round((DateTimeOffset.UtcNow - _sessionStarted.Value).TotalHours);
                if (hours < _restReminders + 1) return;
                _restReminders = hours;
                SetStatus("Grasz już " + hours + " h — czas na przerwę: odpocznij oczom, przejdź się (kroki też dają Qi).");
                ScheduleRestReminder();
            }, (long)Math.Max(60_000, due.TotalMilliseconds));
        }

        /// <summary>(Re)schedules a local reminder when the player has them on (1.9).</summary>
        private void Remind(string key, DateTimeOffset? when, string title, string text)
        {
            try { Alarms.Schedule(this, _prefs, key, when, title, text); }
            catch (Exception) { /* reminders are a convenience; never break a screen */ }
        }

        private async void Run(Func<Task> action)
        {
            SetStatus("…");
            try
            {
                await action();
                if (_status.Text == "…") SetStatus(string.Empty);
            }
            catch (System.OperationCanceledException)
            {
                // activity is closing
            }
            catch (ApiException e)
            {
                SetStatus(Describe(e));
            }
            catch (Exception e)
            {
                SetStatus("Nieoczekiwany błąd: " + e.GetType().Name);
            }
        }

        private async Task PlayOfflineAsync()
        {
            ShowSplash("Budzę świat Jianghu…");
            var url = await EmbeddedServer.StartAsync(this);
            ConnectTo(url, offline: true);
            if (await _api.TryResumeAsync(Ct))
            {
                await ShowHomeAsync();
                return;
            }

            var email = _prefs.GetString("jianghu.offline_email", null);
            var password = _prefs.GetString("jianghu.offline_password", null);
            if (email == null || password == null)
            {
                email = "gracz-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@offline.jianghu.pl";
                password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
                await _api.RegisterAsync(email, password, Ct);
                using (var e = _prefs.Edit())
                {
                    e.PutString("jianghu.offline_email", email);
                    e.PutString("jianghu.offline_password", password);
                    e.Commit();
                }
            }

            await _api.LoginAsync(email, password, Ct);
            await ShowHomeAsync();
        }

        private async Task ShowHomeAsync()
        {
            var characters = await _api.GetCharactersAsync(Ct);
            if (characters.Count == 0) await ShowCreationAsync();
            else EnterGame(characters[0]);
        }

        /// <summary>Body path: reports the steps walked since the last report (hardware counter, kept per character).</summary>
        private async Task ReportStepsAsync(Guid characterId, CultivationFlow flow, System.Action render)
        {
            if (!_steps.Available)
            {
                SetStatus("Ten telefon nie ma licznika kroków.");
                return;
            }

            _steps.Start();
            if (!_steps.HasPermission)
            {
                SetStatus("Zezwól grze na liczenie kroków (aktywność fizyczna).");
                return;
            }

            var total = _steps.Total;
            if (total < 0)
            {
                SetStatus("Licznik kroków jeszcze nie odpowiedział — spróbuj za chwilę.");
                return;
            }

            var key = "jianghu.steps_base." + characterId;
            var last = _prefs.GetLong(key, -1);
            void Save(long value)
            {
                using (var e = _prefs.Edit())
                {
                    e.PutLong(key, value);
                    e.Apply();
                }
            }

            if (last < 0)
            {
                Save(total);
                SetStatus("Liczenie kroków rozpoczęte — wróć po spacerze.");
                return;
            }

            var delta = total >= last ? total - last : total; // the counter restarts after a reboot
            if (delta <= 0)
            {
                SetStatus("Brak nowych kroków od ostatniego razu.");
                return;
            }

            var r = await flow.ReportStepsAsync((int)Math.Min(delta, int.MaxValue), Ct);
            Save(total);
            render();
            SetStatus("+" + r.QiGained + " Qi z " + r.AcceptedSteps + " kroków" + (r.Clipped ? " (część odrzucona jako niemożliwa)" : string.Empty));
        }

        /// <summary>Plays a full-screen effect (e.g. a breakthrough) over the current screen, then removes it.</summary>
        private void PlayOverlay(int res, int cellW, float fps)
        {
            var anim = new Anim(Ui.Art(this, res), cellW, fps, false);
            var view = new FxOverlayView(this, anim);
            _frame.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            view.PostDelayed(() => _frame.RemoveView(view), anim.DurationMs + 900);
        }

        private static string PayloadString(NotificationDto n, string key)
        {
            if (n.Payload == null) return null;
            foreach (var kv in n.Payload)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value is System.Text.Json.JsonElement je ? (je.ValueKind == System.Text.Json.JsonValueKind.String ? je.GetString() : je.ToString()) : kv.Value?.ToString();
            return null;
        }

        private static string NotificationIcon(string type) => type switch
        {
            "pvp_challenge" or "battle_won" or "battle_lost" or "battle_draw" => "menu_battle",
            "breakthrough_succeeded" or "breakthrough_failed" => "menu_breakthrough",
            "market_sold" => "menu_market",
            "sect_application_accepted" or "sect_application_rejected" => "menu_sect",
            "war_declared" or "war_resolved" => "menu_war",
            "friend_request" or "friend_accepted" or "mail_received" => "menu_chat",
            _ => "menu_notifications",
        };

        private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Sqrt(a));
        }

        private static string NodeGlyph(string kind) => kind switch
        {
            "qi_spring" or "lotus_pond" => "💧",
            "spirit_herb" or "bamboo_grove" => "🌿",
            "ore_vein" or "jade_outcrop" => "⛏",
            "ancient_ruins" => "🏛",
            "shrine" => "⛩",
            _ when WorldMapView.IsBossLair(kind) => "👹",
            _ when WorldMapView.IsBeastLair(kind) => "🐺",
            _ => "✦",
        };

        private static string KindGlyph(string kind) => kind switch
        {
            "attack" => "⚔",
            "defense" => "🛡",
            "counter" => "☯",
            _ => "✦",
        };

        private static string CategoryName(string category) => category switch
        {
            "background" => "pochodzenie",
            "bloodline" => "krew",
            "constitution" => "konstytucja",
            _ => category,
        };

        private bool TryPrice(string text, MarketRulesDto rules, out long price)
        {
            if (long.TryParse(text, out price) && price >= rules.MinPrice && price <= rules.MaxPrice) return true;
            SetStatus("Podaj cenę od " + rules.MinPrice + " do " + rules.MaxPrice + ".");
            return false;
        }

        // ---- texts --------------------------------------------------------------------------------------------

        /// <summary>"Siła +2 · Duch −1" for a set of stat changes (empty text when there are none).</summary>
        private static string StatsText(IDictionary<string, int> mods, string separator = " · ") =>
            mods == null ? string.Empty : string.Join(separator, mods.Where(kv => kv.Value != 0).Select(kv => StatName(kv.Key) + " " + (kv.Value > 0 ? "+" : "−") + Math.Abs(kv.Value)));

        private static string StatName(string id) => id switch
        {
            "strength" => "Siła",
            "agility" => "Zręczność",
            "constitution" => "Kondycja",
            "spirit" => "Duch",
            "insight" => "Wgląd",
            "willpower" => "Wola",
            "focus" => "Skupienie",
            "fate" => "Los",
            "elementalAffinity" => "Powinowactwo żywiołów",
            _ => id,
        };

        /// <summary>What a stat does (rules combat-5): shown when a stat is tapped.</summary>
        private static string StatHint(string id) => id switch
        {
            "strength" => "obrażenia technik Siły (pięści, miecze, ciosy)",
            "agility" => "trafienie i unik (liczy się przewaga względna), inicjatywa, techniki Zręczności",
            "constitution" => "maksymalne życie",
            "spirit" => "obrażenia technik Ducha i zapas Qi w walce",
            "insight" => "szansa na przełom i na udaną alchemię",
            "willpower" => "szansa na przełom, odporność na krwawienie/ogłuszenie/osłabienie i na trafienia krytyczne",
            "focus" => "szansa na trafienie krytyczne (+50% obrażeń) i przebijanie odporności wroga",
            "fate" => "szansa na przełom, oswojenie zwierzęcia i lepszy łup (do +50%)",
            "elementalAffinity" => "siła przewagi żywiołów: wzmacnia przewagę i łagodzi słabość",
            _ => string.Empty,
        };

        private static string PathName(string id) => id switch
        {
            "qi" => "Qi",
            "body" => "Ciało",
            "flame" => "Płomień",
            "poison" => "Trucizna",
            _ => id,
        };

        private static string RoleName(string role) => role switch
        {
            "leader" => "przywódca",
            "elder" => "starszy",
            "member" => "uczeń",
            _ => role,
        };

        private static string ActionName(string action) => action switch
        {
            "attack" => "atak",
            "defense" or "defend" => "obrona",
            "counter" => "kontra",
            "move" or "advance" => "naprzód",
            "retreat" => "odwrót",
            "guard" => "garda",
            "movement" or "special" => "umiejętność specjalna",
            "rest" => "odpoczynek",
            "timeout" => "zwłoka",
            _ => action,
        };

        private static string ResultName(BattleDto b) => b.Result switch
        {
            "draw" => "remis",
            null => "-",
            "won" => "ZWYCIĘSTWO",
            "lost" => "porażka",
            _ => b.Result,
        };

        private static string DescribeProblem(string problem) => problem switch
        {
            "location_denied" => "brak zgody na lokalizację (Ustawienia → Aplikacje → Jianghu Go)",
            "location_unavailable" => "włącz lokalizację w telefonie",
            "location_starting" => "szukam sygnału GPS…",
            "low_accuracy" => "słaby sygnał GPS — wyjdź na otwartą przestrzeń",
            "moving_too_fast" => "poruszasz się zbyt szybko",
            "relocation_cooldown" => "wykryto skok pozycji — nagrody wrócą za kilka minut",
            "no_position" => "czekam na pierwszą pozycję",
            "no_session" => "wznawiam sesję eksploracji…",
            "stale" or "stale_position" => "pozycja nieaktualna — czekam na świeży odczyt",
            _ => problem,
        };

        private static string Describe(NotificationDto n) => n.Type switch
        {
            "welcome" => "Witaj w Jianghu!",
            "breakthrough_succeeded" => "Przełom udany.",
            "breakthrough_failed" => "Przełom nieudany.",
            "battle_won" => "Zwycięstwo w walce!",
            "battle_lost" => "Porażka w walce.",
            "battle_draw" => "Walka nierozstrzygnięta.",
            "pvp_challenge" => "Ktoś rzucił ci wyzwanie!",
            "market_sold" => "Sprzedano przedmiot na targu.",
            "sect_application_accepted" => "Przyjęto cię do sekty!",
            "sect_application_rejected" => "Sekta odrzuciła twoją prośbę.",
            "friend_request" => (PayloadString(n, "name") ?? "Ktoś") + " chce zostać twoim przyjacielem.",
            "friend_accepted" => (PayloadString(n, "name") ?? "Ktoś") + " przyjął twoje zaproszenie.",
            "mail_received" => "List od: " + (PayloadString(n, "name") ?? "przyjaciela") + ".",
            "war_declared" => "Wypowiedziano wojnę o terytorium twojej sekty!",
            "war_resolved" => "Wojna o terytorium rozstrzygnięta.",
            _ => n.Type,
        };

        private static string Describe(ApiException e) => e.Code switch
        {
            "no_session" => "Sesja eksploracji wygasła — wznawiam…",
            "network_error" => "Brak połączenia z serwerem — sprawdź adres i czy telefon jest w tej samej sieci.",
            "invalid_credentials" => "Błędny e-mail lub hasło.",
            "email_invalid" => "Niepoprawny adres e-mail.",
            "password_invalid" => "Hasło musi mieć co najmniej 10 znaków.",
            "email_taken" => "Ten e-mail ma już konto — zaloguj się.",
            "insufficient_destiny_tokens" => "Za mało Żetonów Losu.",
            "name_taken" => "To imię jest już zajęte.",
            "name_invalid" => "Imię: 2–24 litery, spacje, myślniki.",
            "insufficient_qi" => "Za mało Qi na przełom.",
            "qi_deviation_active" => "Odchylenie Qi — odpocznij przed kolejnym przełomem.",
            "meditation_in_progress" => "Najpierw zakończ medytację.",
            "out_of_range" => "Przeciwnik poza zasięgiem tej techniki.",
            "on_cooldown" => "Technika się odnawia.",
            "already_in_battle" => "Ta postać już walczy.",
            "relocation_cooldown" => "Wykryto skok pozycji — nagrody wrócą za kilka minut.",
            "low_accuracy" => "Słaby sygnał GPS — wyjdź na otwartą przestrzeń.",
            "moving_too_fast" => "Poruszasz się zbyt szybko.",
            "too_far" => "Podejdź bliżej.",
            "already_harvested" => "Już to zebrałeś.",
            "attunement_cooldown" => "Płomień jeszcze się nie odnowił.",
            "insufficient_funds" => "Za mało Kamieni Duchowych.",
            "not_in_stock" => "Kupiec tego nie sprzedaje.",
            "item_not_tradable" => "Tego przedmiotu nie da się sprzedać.",
            "insufficient_items" => "Brakuje przedmiotów.",
            "bag_full" => "Sakwa jest pełna.",
            "stack_full" => "Stos osiągnął limit.",
            "item_listed" => "Przedmiot jest wystawiony na targu.",
            "item_not_in_bag" => "Najpierw zdejmij przedmiot.",
            "max_upgrade_level" => "Maksymalny poziom ulepszenia.",
            "own_listing" => "Nie możesz kupić własnej oferty.",
            "listing_limit" => "Za dużo aktywnych ofert.",
            "design_invalid" => "Technika łamie zasady Dao: " + e.Message,
            "design_limit" => "Osiągnięto limit autorskich technik.",
            "not_author" => "Tylko autor może spisywać zwoje tej techniki.",
            "technique_already_known" => "Znasz już tę technikę — zwój zostaje w sakwie.",
            "loadout_invalid" => "Ten zestaw technik jest niedozwolony.",
            "technique_locked" => "Ta technika otworzy się w wyższym królestwie.",
            "sect_permission_denied" => "Twoja ranga w sekcie na to nie pozwala.",
            "not_in_sect" => "Nie należysz do sekty.",
            "already_in_sect" => "Należysz już do sekty.",
            "sect_name_taken" => "Ta nazwa sekty jest zajęta.",
            "insufficient_treasury" => "Za mało kamieni w skarbcu sekty.",
            "territory_owned" => "To terytorium ma właściciela — wypowiedz wojnę.",
            "already_owned" => "To terytorium już należy do twojej sekty.",
            "war_in_progress" => "O to terytorium toczy się już wojna.",
            "territory_limit" => "Sekta nie utrzyma więcej terytoriów.",
            "tribute_not_ready" => "Danina jeszcze nie jest gotowa.",
            "leader_must_transfer" => "Najpierw przekaż przywództwo.",
            "rate_limited" => "Za dużo prób — odczekaj chwilę.",
            "busy_retry" => "Serwer jest zajęty — spróbuj ponownie.",
            _ => "Błąd: " + e.Code + (e.CorrelationId != null ? " (" + e.CorrelationId + ")" : string.Empty),
        };
    }
}
