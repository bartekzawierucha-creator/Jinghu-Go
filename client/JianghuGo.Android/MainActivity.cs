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
    /// Composition root and screen router: login (with server address) → character creation → character sheet →
    /// world map / battle / bag / crafting / market / Dao lab / sect. Every rule, cost and outcome comes from the
    /// server; this layer only renders state and sends intents through the shared client core.
    /// Flow methods resume on the thread pool (ConfigureAwait(false)) but our own awaits resume on the UI thread,
    /// so screens re-render after each await instead of subscribing to flow events.
    /// </summary>
    [Activity(Label = "Jianghu Go", MainLauncher = true, Theme = "@android:style/Theme.Material.Light.NoActionBar",
        ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
    public sealed class MainActivity : Activity
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
        private TextView _title;
        private TextView _status;
        private ScrollView _scroll;
        private Ui _ui;
        private int _screen;
        private System.Action _back;
        private bool _paused;
        private bool _worldOpen;
        private System.Action _onLocation;

        private CancellationToken Ct => _lifetime.Token;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _prefs = GetSharedPreferences(Prefs, FileCreationMode.Private);
            _location = new AndroidLocationSource(this);
            _location.Updated += () => RunOnUiThread(() => _onLocation?.Invoke());

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetBackgroundColor(Ui.Paper);
            var pad = (int)(16 * Resources.DisplayMetrics.Density);
            root.SetPadding(pad, pad, pad, 0);
            _title = new TextView(this);
            _title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 26);
            _title.SetTextColor(Ui.Cinnabar);
            _title.SetTypeface(global::Android.Graphics.Typeface.Serif, global::Android.Graphics.TypefaceStyle.Bold);
            root.AddView(_title);
            _status = new TextView(this);
            _status.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            _status.SetTextColor(Ui.Cinnabar);
            root.AddView(_status);
            _scroll = new ScrollView(this);
            var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
            content.SetPadding(0, 0, 0, pad * 2);
            _scroll.AddView(content);
            root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            // Android 15+ draws edge-to-edge: keep the content clear of the status and navigation bars.
            var frame = new FrameLayout(this);
            frame.SetFitsSystemWindows(true);
            frame.SetBackgroundColor(Ui.Paper);
            frame.AddView(root);
            SetContentView(frame);
            _ui = new Ui(this, content);

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

                NewScreen("Jianghu Go");
                _ui.Label("Łączenie z " + _serverUrl + "…");
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
            _location.Stop(); // battery: no GPS in the background
            EmbeddedServer.Flush(); // offline mode: the app may be killed in the background
        }

        protected override void OnResume()
        {
            base.OnResume();
            _paused = false;
            if (_worldOpen) _location.Start();
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
            if (requestCode == AndroidLocationSource.PermissionRequestCode)
                _location.OnPermissionResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }

#pragma warning disable CS0618, CS0672 // OnBackPressed: fine for this API range; one handler for every screen.
        public override void OnBackPressed()
        {
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

        private Ui NewScreen(string title, System.Action back = null)
        {
            _screen++;
            _worldOpen = false;
            _onLocation = null;
            _location.Stop();
            _back = back;
            _title.Text = title;
            _status.Text = string.Empty;
            _ui.Content.RemoveAllViews();
            _scroll.ScrollTo(0, 0);
            return _ui;
        }

        private void SetStatus(string text) => _status.Text = text;

        /// <summary>Runs a UI action, turning failures into a readable status line instead of a crash.</summary>
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

        // ---- account ------------------------------------------------------------------------------------------

        private void ShowLogin()
        {
            var ui = NewScreen("Jianghu Go");
            ui.Label("Świat wuxia na ulicach twojego miasta. Kultywuj Qi, zbieraj zioła, walcz i zakładaj sekty.", 15);
            ui.Action("☯ Graj offline (bez serwera)", () => Run(PlayOfflineAsync));
            ui.Label("Świat działa w telefonie, postęp zapisuje się w pamięci aplikacji.", 12, global::Android.Graphics.Color.Gray);
            ui.Divider();
            ui.Heading("Gra z serwerem");
            ui.Label("Adres serwera", 13);
            var server = ui.Field("http://192.168.1.10:8080", value: _prefs.GetString(ServerUrlKey, DefaultServer));
            var email = ui.Field("E-mail");
            var password = ui.Field("Hasło", password: true);

            bool Connect()
            {
                if (ConnectTo(server.Text)) return true;
                SetStatus("Podaj adres serwera, np. http://192.168.1.10:8080");
                return false;
            }

            ui.Action("Zaloguj", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.LoginAsync(email.Text.Trim(), password.Text, Ct);
                await ShowHomeAsync();
            }));
            ui.Action("Załóż konto", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.RegisterAsync(email.Text.Trim(), password.Text, Ct);
                await _api.LoginAsync(email.Text.Trim(), password.Text, Ct);
                await ShowHomeAsync();
            }), secondary: true);
            ui.Label("Hasło: co najmniej 10 znaków. Serwer uruchomisz na komputerze: API_BIND=0.0.0.0 docker compose up --build", 12, global::Android.Graphics.Color.Gray);
        }

        /// <summary>Starts the in-app server and signs in with the local account (created on first use).</summary>
        private async Task PlayOfflineAsync()
        {
            NewScreen("Jianghu Go");
            _ui.Label("Budzę świat Jianghu…", 16);
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
            else ShowCharacter(characters[0]);
        }

        private async Task ShowCreationAsync()
        {
            var flow = new CharacterCreationFlow(_api);
            await flow.StartAsync(Ct);
            var ui = NewScreen("Narodziny wojownika");
            ui.Label("Los rzuca cechy. Zablokuj te, które chcesz zachować, i losuj ponownie (kosztuje Żetony Losu).", 14);
            var traits = ui.Column();
            var stats = ui.Label(string.Empty, 15);
            var tokens = ui.Label(string.Empty, 15, Ui.Cinnabar);

            void Render()
            {
                var d = flow.Draft;
                traits.RemoveAllViews();
                foreach (var t in d.Traits)
                {
                    var trait = t;
                    var locked = flow.LockedCategories.Contains(trait.Category);
                    ui.Action((locked ? "🔒 " : "") + trait.NamePl + "  · " + trait.Category + " · " + trait.Rarity, () =>
                    {
                        if (!flow.ToggleLock(trait.Category)) SetStatus("Możesz zablokować najwyżej " + flow.Draft.MaxLockedTraits + " cechy.");
                        Render();
                    }, secondary: !locked, parent: traits);
                }

                stats.Text = string.Join("   ", d.Stats.Select(st => StatName(st.Id) + " " + st.Value));
                tokens.Text = "Żetony Losu: " + d.DestinyTokens + "   koszt losowania: " + flow.NextRerollCost + "   pozostało losowań: " + d.RerollsRemaining;
            }

            ui.Action("🎲 Losuj ponownie", () => Run(async () =>
            {
                if (!await flow.RerollAsync(Ct)) SetStatus("Szkic zmienił się na innym urządzeniu — odświeżono.");
                Render();
            }));
            ui.Divider();
            var name = ui.Field("Imię postaci (2–24 litery)");
            ui.Label("Wybierz ścieżkę kultywacji:", 15, bold: true);
            foreach (var path in new[] { ("qi", "Qi — równowaga i technika"), ("body", "Ciało — siła i wytrzymałość"), ("flame", "Płomień — moc miejsc Płomienia"), ("poison", "Trucizna — podstęp i osłabienie") })
            {
                var p = path;
                ui.Action(p.Item2, () => Run(async () => ShowCharacter(await flow.FinalizeAsync(name.Text.Trim(), p.Item1, Ct))));
            }

            Render();
        }

        // ---- character & cultivation --------------------------------------------------------------------------

        private void ShowCharacter(CharacterDto c)
        {
            var ui = NewScreen(c.Name);
            ui.Label("Ścieżka: " + PathName(c.Path) + "\n" + string.Join(" · ", c.Traits.Select(t => t.NamePl)), 14);
            ui.Label(string.Join("   ", c.Stats.Select(st => StatName(st.Id) + " " + st.Value)), 14);
            ui.Divider();
            var realm = ui.Label("…", 18, Ui.Ink, bold: true);
            var qi = ui.Meter("Qi", Ui.Jade);
            var details = ui.Label(string.Empty, 14);
            var flow = new CultivationFlow(_api, c.Id);
            var meditate = ui.Action("🧘 Medytuj", () => { });

            void Render()
            {
                var st = flow.State;
                if (st == null) return;
                realm.Text = st.RealmNamePl + " — etap " + st.Stage + "/" + st.StagesInRealm;
                Ui.SetMeter(qi, "Qi: " + st.Qi + " / " + st.QiRequired, st.Qi, st.QiRequired);
                var deviation = st.DeviationUntil.HasValue && st.DeviationUntil > DateTimeOffset.UtcNow
                    ? "\nOdchylenie Qi do " + st.DeviationUntil.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
                details.Text = "Szansa przełomu: " + Math.Round(st.BreakthroughChance * 100) + "%" +
                               (st.IsMeditating ? "\nMedytujesz od " + st.MeditationStartedAt.Value.ToLocalTime().ToString("HH:mm") : string.Empty) +
                               "\nDzisiaj z medytacji: " + st.DailyMeditationQi + " / " + st.DailyMeditationQiCap + deviation;
                meditate.Text = st.IsMeditating ? "🧘 Zakończ medytację" : "🧘 Medytuj";
            }

            meditate.Click += (_, __) => Run(async () =>
            {
                if (flow.State == null) return;
                if (flow.State.IsMeditating)
                {
                    var r = await flow.FinishMeditationAsync(Ct);
                    SetStatus("+" + r.QiGained + " Qi (" + r.MinutesCounted + " min)" + (r.DailyCapReached ? " — dzienny limit" : string.Empty));
                }
                else
                {
                    await flow.StartMeditationAsync(Ct);
                }

                Render();
            });
            ui.Action("⚡ Próba przełomu", () => Run(async () =>
            {
                var r = await flow.BreakthroughAsync(Ct);
                Render();
                SetStatus(r == null ? "Stan zmienił się na innym urządzeniu — odświeżono." : r.Success ? "Przełom udany!" : "Przełom nieudany — odchylenie Qi.");
            }));

            ui.Divider();
            ui.Action("🗺 Eksploruj świat", () => Run(async () =>
            {
                var exploration = new ExplorationFlow(_api, _location, c.Id);
                await exploration.StartAsync(Ct);
                ShowWorld(exploration, c);
            }));
            ui.Label("Trening walki", 15, bold: true);
            foreach (var npc in new[] { ("training_dummy", "Manekin treningowy"), ("grey_wolf", "Szary Wilk"), ("mountain_bandit", "Górski Bandyta") })
            {
                var n = npc;
                ui.Action("⚔ " + n.Item2, () => Run(async () =>
                {
                    var combat = new CombatFlow(_api);
                    await combat.StartPveAsync(c.Id, n.Item1, Ct);
                    ShowBattle(combat, c);
                }), secondary: true);
            }

            ui.Divider();
            ui.Action("🎒 Sakwa, kuźnia i targ", () => Run(async () =>
            {
                var economy = new EconomyFlow(_api, c.Id);
                await economy.LoadAsync(Ct);
                ShowBag(economy, c);
            }));
            ui.Action("☯ Laboratorium Dao", () => Run(async () =>
            {
                var lab = new DaoLabFlow(_api, c.Id);
                await lab.LoadAsync(Ct);
                var economy = new EconomyFlow(_api, c.Id);
                await economy.LoadAsync(Ct);
                ShowDaoLab(lab, economy, c);
            }));
            ui.Action("🏯 Sekta i terytoria", () => Run(async () =>
            {
                var sect = new SectFlow(_api, c.Id);
                await sect.LoadAsync(Ct);
                ShowSect(sect, c);
            }));
            var inbox = ui.Label(string.Empty, 14);
            ui.Action("✉ Powiadomienia", () => Run(async () =>
            {
                var page = await _api.GetNotificationsAsync(true, 10, null, Ct);
                inbox.Text = page.Items.Count == 0 ? "Brak nowych powiadomień." : string.Join("\n", page.Items.Select(n => "• " + Describe(n)));
                foreach (var n in page.Items) await _api.MarkNotificationReadAsync(n.Id, Ct);
            }), secondary: true);
            ui.Action("Wyloguj", () => Run(async () =>
            {
                await _api.LogoutAsync(Ct);
                using (var e = _prefs.Edit())
                {
                    e.PutString(ModeKey, "login"); // next start shows the menu instead of resuming
                    e.Apply();
                }

                ShowLogin();
            }), secondary: true);
            Run(async () =>
            {
                await flow.LoadAsync(Ct);
                Render();
            });
        }

        // ---- world ---------------------------------------------------------------------------------------------

        private void ShowWorld(ExplorationFlow exploration, CharacterDto c)
        {
            async Task Leave()
            {
                await exploration.StopAsync(Ct);
                ShowCharacter(c);
            }

            var ui = NewScreen("Jianghu", () => Run(Leave));
            var gen = _screen;
            _worldOpen = true;
            if (!_paused) _location.Start();

            var info = ui.Label("…", 14);
            var map = new WorldMapView(this);
            ui.Add(map, 340);
            ui.Label("Dotknij punktu w zasięgu (czerwony okrąg ~40 m), by go zebrać lub walczyć.", 12, global::Android.Graphics.Color.Gray);
            var list = ui.Column();

            void Interact(NodeDto node) => Run(async () =>
            {
                if (!node.InRange || node.Harvested)
                {
                    SetStatus(node.Harvested ? "Już to zebrałeś." : node.NamePl + ": " + node.DistanceM + " m — podejdź bliżej.");
                    return;
                }

                var r = await exploration.InteractAsync(node.Key, Ct);
                if (r.BattleId.HasValue)
                {
                    var combat = new CombatFlow(_api);
                    await combat.OpenAsync(r.BattleId.Value, Ct);
                    await exploration.StopAsync(Ct);
                    ShowBattle(combat, c);
                    return;
                }

                Render();
                SetStatus("+" + r.RewardQi + " Qi" + (r.Items.Count > 0 ? "  · " + string.Join(", ", r.Items.Select(i => i.ItemId + " ×" + i.Quantity)) : string.Empty));
            });

            void Render()
            {
                if (gen != _screen) return;
                var n = exploration.Nearby;
                info.Text = (exploration.Problem == null ? "📍 Pozycja potwierdzona" : "📍 " + DescribeProblem(exploration.Problem)) +
                            (exploration.Session != null ? "   · przebyto " + Math.Round(exploration.Session.DistanceM) + " m" : string.Empty) +
                            (n?.Event != null ? "\n✨ Wydarzenie: " + n.Event.NamePl : string.Empty) +
                            (n?.NearestFlameSite != null ? "\n🔥 Najbliższe miejsce Płomienia: " + n.NearestFlameSite.NamePl + " (" + (n.NearestFlameSite.DistanceM < 1000 ? n.NearestFlameSite.DistanceM + " m" : n.NearestFlameSite.DistanceM / 1000 + " km") + ")" : string.Empty);
                map.Player = _location.Last;
                map.Nearby = n;
                map.Invalidate();
                list.RemoveAllViews();
                if (n == null) return;
                foreach (var node in n.Nodes.OrderBy(x => x.DistanceM).Take(8))
                {
                    var target = node;
                    ui.Action(target.NamePl + " — " + target.DistanceM + " m" + (target.Harvested ? " (zebrane)" : target.InRange ? "  ✋ zbierz" : string.Empty),
                        () => Interact(target), secondary: !(target.InRange && !target.Harvested), parent: list);
                }
            }

            map.NodeTapped += Interact;
            _onLocation = () =>
            {
                map.Player = _location.Last;
                map.Invalidate();
            };

            ui.Action("🔥 Dostrój się do Płomienia", () => Run(async () =>
            {
                var site = exploration.Nearby?.NearestFlameSite;
                if (site == null || !site.InRange)
                {
                    SetStatus("Musisz stać przy jednym z 12 miejsc Płomienia.");
                    return;
                }

                var r = await exploration.AttuneAsync(site.Id, Ct);
                SetStatus("+" + r.RewardQi + " Qi z Płomienia");
            }), secondary: true);
            ui.Action("Zakończ eksplorację", () => Run(Leave), secondary: true);

            async void Loop()
            {
                while (gen == _screen && !_lifetime.IsCancellationRequested)
                {
                    if (!_paused)
                    {
                        try
                        {
                            await exploration.TickAsync(Ct);
                            Render();
                        }
                        catch (ApiException e)
                        {
                            if (gen == _screen) SetStatus(Describe(e));
                        }
                        catch (System.OperationCanceledException)
                        {
                            return;
                        }
                    }

                    try
                    {
                        await Task.Delay(_paused ? TimeSpan.FromSeconds(2) : exploration.RecommendedInterval, Ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            Render();
            Loop();
        }

        // ---- battle -------------------------------------------------------------------------------------------

        private void ShowBattle(CombatFlow combat, CharacterDto c)
        {
            var ui = NewScreen("Pojedynek", () => ShowCharacter(c));
            var me = ui.Meter("Ty", Ui.Jade);
            var myQi = ui.Meter("Qi", global::Android.Graphics.Color.Rgb(46, 104, 150));
            var foe = ui.Meter("Przeciwnik", Ui.Cinnabar);
            var header = ui.Label(string.Empty, 15, Ui.Ink, bold: true);
            var log = ui.Label(string.Empty, 14);
            var buttons = new List<Button>();
            var gen = _screen;

            void Render()
            {
                var b = combat.Battle;
                Ui.SetMeter(me, b.Me.Name + "  HP " + b.Me.Hp + "/" + b.Me.MaxHp, b.Me.Hp, b.Me.MaxHp);
                Ui.SetMeter(myQi, "Qi " + b.Me.Qi + "/" + b.Me.MaxQi, b.Me.Qi, b.Me.MaxQi);
                Ui.SetMeter(foe, b.Opponent.Name + "  HP " + b.Opponent.Hp + "/" + b.Opponent.MaxHp, b.Opponent.Hp, b.Opponent.MaxHp);
                header.Text = "Tura " + b.Turn + "   ·   dystans " + b.Distance +
                              (b.IsActive ? (b.MyActionSubmitted ? "\nCzekasz na ruch przeciwnika…" : string.Empty)
                                  : "\nWYNIK: " + ResultName(b) + (b.RewardQi > 0 ? "  (+" + b.RewardQi + " Qi)" : string.Empty));
                log.Text = string.Join("\n", b.LastTurnEvents.Select(e =>
                    (b.IsMine(e) ? "Ty" : "Wróg") + ": " + ActionName(e.Action) + (e.TechniqueId != null ? " (" + e.TechniqueId + ")" : string.Empty) +
                    (e.Hit == true ? " → " + e.Damage + " obrażeń" : e.Hit == false ? " → pudło" : string.Empty)));
                foreach (var button in buttons)
                {
                    button.Enabled = b.IsActive && !b.MyActionSubmitted;
                    button.Alpha = button.Enabled ? 1f : 0.45f;
                }
            }

            void Act(int? slot, string direction) => Run(async () =>
            {
                if (!await combat.ActAsync(slot, direction, Ct)) SetStatus("Tura już minęła — odświeżono.");
                if (gen == _screen) Render();
            });

            ui.Divider();
            foreach (var s in combat.Battle.MySlots.Where(x => x.TechniqueId != null))
            {
                var slot = s;
                var cost = slot.QiCost > 0 ? "  · " + slot.QiCost + " Qi" : string.Empty;
                if (slot.Kind == "movement")
                {
                    buttons.Add(ui.Action("➡ " + slot.NamePl + " (naprzód)" + cost, () => Act(slot.Slot, "advance")));
                    buttons.Add(ui.Action("⬅ " + slot.NamePl + " (odwrót)" + cost, () => Act(slot.Slot, "retreat")));
                }
                else
                {
                    buttons.Add(ui.Action(slot.NamePl + " [" + slot.Kind + "]" + cost, () => Act(slot.Slot, null)));
                }
            }

            buttons.Add(ui.Action("Odpoczynek (+Qi)", () => Act(null, null), secondary: true));
            ui.Action("Odśwież", () => Run(async () =>
            {
                await combat.RefreshAsync(Ct);
                Render();
            }), secondary: true);
            ui.Action("Wróć", () => ShowCharacter(c), secondary: true);
            Render();
        }

        // ---- economy ------------------------------------------------------------------------------------------

        private void ShowBag(EconomyFlow economy, CharacterDto c)
        {
            var ui = NewScreen("Sakwa", () => ShowCharacter(c));
            var summary = ui.Label("…", 15, Ui.Ink, bold: true);
            var list = ui.Column();

            void Render()
            {
                var inv = economy.Inventory;
                summary.Text = "💎 Kamienie Duchowe: " + inv.Balance + "   Miejsca: " + inv.SlotsUsed + "/" + inv.Capacity +
                               (inv.Bonuses.Count > 0 ? "\nBonusy: " + string.Join("  ", inv.Bonuses.Select(b => b.Key + " +" + b.Value)) : string.Empty);
                list.RemoveAllViews();
                if (inv.Stacks.Count == 0 && inv.Items.Count == 0) ui.Label("Sakwa jest pusta — zbieraj zioła i rudę w świecie.", 14, parent: list);
                foreach (var st in inv.Stacks)
                {
                    var stack = st;
                    if (stack.Type == "consumable")
                        ui.Action("Użyj: " + stack.NamePl + " ×" + stack.Quantity, () => Run(async () =>
                        {
                            var r = await economy.UseAsync(stack.ItemId, 1, Ct);
                            Render();
                            SetStatus("+" + r.QiGranted + " Qi (wkrótce na koncie kultywacji)");
                        }), parent: list);
                    else
                        ui.Label("• " + stack.NamePl + " ×" + stack.Quantity, 15, parent: list);
                }

                foreach (var it in inv.Items)
                {
                    var item = it;
                    var caption = (item.IsEquipped ? "[założone] " : item.State == "listed" ? "[na targu] " : string.Empty) +
                                  item.NamePl + (item.UpgradeLevel > 0 ? " +" + item.UpgradeLevel : string.Empty) +
                                  "  " + string.Join(" ", item.Stats.Select(x => x.Key + "+" + x.Value));
                    if (item.State == "listed")
                    {
                        ui.Label(caption, 15, parent: list);
                        continue;
                    }

                    ui.Action((item.IsEquipped ? "Zdejmij: " : "Załóż: ") + caption, () => Run(async () =>
                    {
                        var ok = await economy.ToggleEquipAsync(item, Ct);
                        Render();
                        if (!ok) SetStatus("Przedmiot zmienił się na innym urządzeniu — odświeżono.");
                    }), secondary: !item.IsEquipped, parent: list);
                    var next = economy.NextUpgrade(item);
                    if (next != null)
                        ui.Action("  ⚒ Ulepsz do +" + next.Level + " (" + next.Stones + " KD, " + next.SuccessPermille / 10 + "%)", () => Run(async () =>
                        {
                            var r = await economy.UpgradeAsync(item, Ct);
                            Render();
                            SetStatus(r == null ? "Odświeżono." : r.Success ? "Udane! Teraz +" + r.Level : "Nieudane — poziom +" + r.Level + " zachowany.");
                        }), secondary: true, parent: list);
                }
            }

            ui.Divider();
            ui.Action("⚒ Kuźnia i alchemia", () => ShowCrafting(economy, c));
            ui.Action("🏮 Targ", () => Run(() => ShowMarketAsync(economy, c)));
            ui.Action("Odśwież", () => Run(async () =>
            {
                await economy.ReloadAsync(Ct);
                Render();
            }), secondary: true);
            ui.Action("Wróć", () => ShowCharacter(c), secondary: true);
            Render();
        }

        private void ShowCrafting(EconomyFlow economy, CharacterDto c)
        {
            var ui = NewScreen("Kuźnia i alchemia", () => ShowBag(economy, c));
            var balance = ui.Label(string.Empty, 15, Ui.Ink, bold: true);
            void Render() => balance.Text = "💎 Kamienie Duchowe: " + economy.Inventory.Balance;
            Render();
            foreach (var r in economy.Recipes.Items)
            {
                var recipe = r;
                var inputs = string.Join(", ", recipe.Inputs.Select(i => i.Item + "×" + i.Quantity));
                ui.Action(recipe.Output.Item + "  ←  " + inputs + " + " + recipe.Stones + " KD" +
                          (recipe.Kind == "alchemy" ? " (" + recipe.SuccessPermille / 10 + "%+)" : string.Empty), () => Run(async () =>
                {
                    if (!economy.CanAfford(recipe, 1))
                    {
                        SetStatus("Brakuje składników lub kamieni.");
                        return;
                    }

                    var result = await economy.CraftAsync(recipe.Id, 1, Ct);
                    Render();
                    SetStatus(result.Successes > 0 ? "Wytworzono: " + result.OutputItem : "Alchemia nie powiodła się — składniki przepadły.");
                }), secondary: !economy.CanAfford(recipe, 1));
            }

            ui.Action("Wróć do sakwy", () => ShowBag(economy, c), secondary: true);
        }

        private async Task ShowMarketAsync(EconomyFlow economy, CharacterDto c)
        {
            var page = await economy.BrowseAsync(null, null, Ct);
            var mine = await economy.MyListingsAsync(Ct);
            var ui = NewScreen("Targ", () => ShowBag(economy, c));
            var rules = economy.MarketRules;
            ui.Label("Opłata za wystawienie " + rules.ListingFeePermille / 10.0 + "% (min. " + rules.MinListingFee + "), podatek od sprzedaży " +
                     rules.SaleTaxPermille / 10.0 + "%, oferta ważna " + rules.ListingHours + " h", 13);
            ui.Label("💎 Twoje Kamienie Duchowe: " + economy.Inventory.Balance, 15, Ui.Ink, bold: true);
            var offers = page.Items.Where(l => l.SellerCharacterId != c.Id).ToList();
            ui.Heading("Oferty");
            if (offers.Count == 0) ui.Label("Na targu nic nie ma.", 14);
            foreach (var l in offers)
            {
                var listing = l;
                ui.Action("Kup: " + listing.ItemNamePl + (listing.UpgradeLevel > 0 ? " +" + listing.UpgradeLevel : string.Empty) +
                          " ×" + listing.Quantity + " — " + listing.Price + " KD (" + listing.SellerName + ")", () => Run(async () =>
                {
                    var bought = await economy.BuyAsync(listing, Ct);
                    await ShowMarketAsync(economy, c);
                    SetStatus(bought == null ? "Ktoś był szybszy — oferta już nieaktualna." : "Kupiono za " + bought.Paid + " KD.");
                }));
            }

            ui.Heading("Twoje oferty");
            foreach (var m in mine.Where(x => x.Listing.Status == "active"))
            {
                var listing = m.Listing;
                ui.Action((listing.Expired ? "Odbierz (wygasła): " : "Wycofaj: ") + listing.ItemNamePl + " ×" + listing.Quantity + " — " + listing.Price + " KD", () => Run(async () =>
                {
                    await economy.CancelAsync(listing, Ct);
                    await ShowMarketAsync(economy, c);
                }), secondary: true);
            }

            ui.Heading("Wystaw na sprzedaż");
            var price = ui.Field("Cena w Kamieniach Duchowych", number: true);
            foreach (var it in economy.Inventory.Items.Where(i => i.InBag))
            {
                var item = it;
                ui.Action("Wystaw: " + item.NamePl + (item.UpgradeLevel > 0 ? " +" + item.UpgradeLevel : string.Empty), () => Run(async () =>
                {
                    if (!TryPrice(price.Text, rules, out var p)) return;
                    await economy.SellAsync(item, p, Ct);
                    await ShowMarketAsync(economy, c);
                }), secondary: true);
            }

            foreach (var st in economy.Inventory.Stacks)
            {
                var stack = st;
                ui.Action("Wystaw: " + stack.NamePl + " ×" + stack.Quantity, () => Run(async () =>
                {
                    if (!TryPrice(price.Text, rules, out var p)) return;
                    await economy.SellAsync(stack.ItemId, stack.Quantity, p, Ct);
                    await ShowMarketAsync(economy, c);
                }), secondary: true);
            }

            ui.Action("Wróć do sakwy", () => ShowBag(economy, c), secondary: true);
        }

        private bool TryPrice(string text, MarketRulesDto rules, out long price)
        {
            if (long.TryParse(text, out price) && price >= rules.MinPrice && price <= rules.MaxPrice) return true;
            SetStatus("Podaj cenę od " + rules.MinPrice + " do " + rules.MaxPrice + ".");
            return false;
        }

        // ---- Dao Lab ------------------------------------------------------------------------------------------

        private void ShowDaoLab(DaoLabFlow lab, EconomyFlow economy, CharacterDto c)
        {
            var ui = NewScreen("Laboratorium Dao", () => ShowCharacter(c));
            ui.Label("Ułóż własną technikę ataku. Serwer liczy jej moc i pilnuje budżetu twojego poziomu.", 14);
            var name = ui.Field("Nazwa techniki");
            var power = ui.Field("Moc (1–30)", number: true);
            var qi = ui.Field("Koszt Qi (0–20)", number: true);
            var cooldown = ui.Field("Odnowienie w turach (0–4)", number: true);
            var reach = ui.Field("Zasięg maks. (1–3)", number: true);
            var verdict = ui.Label("Ułóż technikę i sprawdź budżet.", 14, Ui.Ink, bold: true);

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
                (p.Problems.Count > 0 ? "\n" + string.Join("\n", p.Problems) : string.Empty);

            ui.Action("Sprawdź budżet", () => Run(async () => verdict.Text = DescribePreview(await lab.PreviewAsync(Compose(), Ct))), secondary: true);
            ui.Action("☯ Stwórz technikę (opłata w KD)", () => Run(async () =>
            {
                var created = await lab.CreateAsync(Compose(), Ct);
                if (created == null)
                {
                    verdict.Text = DescribePreview(lab.LastPreview);
                    return;
                }

                ShowDaoLab(lab, economy, c);
                SetStatus("Powstała technika: " + created.Technique.NamePl);
            }));

            ui.Heading("Znane techniki");
            if (lab.Known.Count == 0) ui.Label("Jeszcze żadnej.", 14);
            foreach (var k in lab.Known)
            {
                var known = k;
                ui.Action("Załóż: " + known.Technique.NamePl + " (" + known.Technique.Slot + ")", () => Run(async () =>
                {
                    var ok = await lab.EquipAsync(known.Technique.Id, known.Technique.Slot, Ct);
                    SetStatus(ok ? "Technika w zestawie walki." : "Zestaw zmienił się na innym urządzeniu — odświeżono.");
                }), secondary: true);
                if (known.Source == "designed")
                    ui.Action("  📜 Spisz zwój: " + known.Technique.NamePl, () => Run(async () =>
                    {
                        await lab.InscribeAsync(known.Technique.Id, Ct);
                        await economy.ReloadAsync(Ct);
                        SetStatus("Zwój trafił do sakwy — możesz go wystawić na targu.");
                    }), secondary: true);
            }

            foreach (var it in economy.Inventory.Items.Where(i => i.BoundRef != null && i.InBag))
            {
                var scroll = it;
                ui.Action("📜 Przeczytaj zwój (" + scroll.BoundRef + ")", () => Run(async () =>
                {
                    var learned = await lab.LearnAsync(scroll, Ct);
                    await economy.ReloadAsync(Ct);
                    ShowDaoLab(lab, economy, c);
                    SetStatus("Poznano technikę: " + learned.Technique.NamePl);
                }));
            }

            ui.Action("Wróć", () => ShowCharacter(c), secondary: true);
        }

        // ---- sects & territories ------------------------------------------------------------------------------

        private void ShowSect(SectFlow flow, CharacterDto c)
        {
            if (flow.Mine == null)
            {
                var ui0 = NewScreen("Sekty", () => ShowCharacter(c));
                ui0.Label("Nie należysz do żadnej sekty. Załóż własną lub poproś o przyjęcie.", 14);
                var name = ui0.Field("Nazwa nowej sekty");
                ui0.Action("🏯 Załóż sektę (opłata w KD)", () => Run(async () =>
                {
                    await flow.FoundAsync(name.Text.Trim(), Ct);
                    ShowSect(flow, c);
                }));
                var list = ui0.Column();
                ui0.Action("Pokaż sekty", () => Run(async () =>
                {
                    var page = await flow.BrowseAsync(Ct);
                    list.RemoveAllViews();
                    if (page.Items.Count == 0) ui0.Label("Nie ma jeszcze żadnej sekty.", 14, parent: list);
                    foreach (var item in page.Items)
                    {
                        var sect = item;
                        ui0.Action("Poproś o przyjęcie: " + sect.Name + " (" + sect.MemberCount + ")", () => Run(async () =>
                        {
                            await flow.ApplyAsync(sect.Id, "Proszę o przyjęcie.", Ct);
                            SetStatus("Wysłano prośbę do " + sect.Name + ".");
                        }), secondary: true, parent: list);
                    }
                }), secondary: true);
                ui0.Action("Wróć", () => ShowCharacter(c), secondary: true);
                return;
            }

            var mine = flow.Mine;
            var ui = NewScreen(mine.Sect.Name, () => ShowCharacter(c));
            ui.Label("Twoja ranga: " + RoleName(mine.MyRole) + "\nSkarbiec: " + mine.Treasury + " KD   ·   Członkowie: " + mine.Sect.MemberCount + "/" + mine.Sect.MaxMembers, 15, Ui.Ink, bold: true);
            ui.Label(string.Join("\n", mine.Sect.Members.Select(m => "• " + m.Name + " — " + RoleName(m.Role) + " (wkład " + m.Contribution + ")")), 14);
            if (flow.Territories != null)
                ui.Label("Terytoria: " + flow.Territories.Owned + "/" + flow.Territories.Max +
                         (flow.Territories.Wars.Count > 0 ? "\nWojny: " + string.Join(", ", flow.Territories.Wars.Select(w => w.TerritoryKey + " " + w.Phase + " " + w.AttackerScore + ":" + w.DefenderScore)) : string.Empty), 14);

            if (mine.PendingApplications.Count > 0) ui.Heading("Prośby o przyjęcie");
            foreach (var a in mine.PendingApplications)
            {
                var application = a;
                ui.Action("✔ Przyjmij: " + application.CharacterName, () => Run(async () =>
                {
                    await flow.DecideAsync(application, true, Ct);
                    ShowSect(flow, c);
                }));
                ui.Action("✘ Odrzuć: " + application.CharacterName, () => Run(async () =>
                {
                    await flow.DecideAsync(application, false, Ct);
                    ShowSect(flow, c);
                }), secondary: true);
            }

            ui.Divider();
            ui.Action("Wpłać 100 KD do skarbca", () => Run(async () =>
            {
                await flow.DonateAsync(100, Ct);
                ShowSect(flow, c);
            }), secondary: true);
            ui.Action("Terytorium tutaj", () => Run(async () =>
            {
                var here = await flow.HereAsync(Ct);
                SetStatus(here.Key + ": " + (here.OwnerName ?? "niczyje") + (here.War != null ? " — wojna (" + here.War.Phase + ")" : string.Empty));
            }), secondary: true);
            if (mine.CanManage)
            {
                ui.Action("🚩 Zajmij terytorium tutaj", () => Run(async () =>
                {
                    var t = await flow.ClaimHereAsync(Ct);
                    ShowSect(flow, c);
                    SetStatus("Zajęto " + t.Key + ".");
                }));
                ui.Action("⚔ Wypowiedz wojnę o terytorium tutaj", () => Run(async () =>
                {
                    var w = await flow.ChallengeHereAsync(Ct);
                    ShowSect(flow, c);
                    SetStatus("Wojna o " + w.TerritoryKey + " od " + w.StartsAt.ToLocalTime().ToString("dd.MM HH:mm") + ".");
                }));
                foreach (var t in flow.Territories?.Territories ?? new List<TerritoryDto>())
                {
                    var territory = t;
                    ui.Action("Danina z " + territory.Key, () => Run(async () =>
                    {
                        await flow.CollectTributeAsync(territory, Ct);
                        ShowSect(flow, c);
                    }), secondary: true);
                }
            }

            ui.Action("Opuść sektę", () => Run(async () =>
            {
                await flow.LeaveAsync(Ct);
                ShowSect(flow, c);
            }), secondary: true);
            ui.Action("Wróć", () => ShowCharacter(c), secondary: true);
        }

        // ---- texts --------------------------------------------------------------------------------------------

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
            "movement" => "ruch",
            "rest" => "odpoczynek",
            "timeout" => "zwłoka",
            _ => action,
        };

        private static string ResultName(BattleDto b) => b.Result switch
        {
            "draw" => "remis",
            null => "-",
            _ when b.Result == b.MySide + "_won" => "ZWYCIĘSTWO",
            _ when b.Result.EndsWith("_won", StringComparison.Ordinal) => "porażka",
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
            "war_declared" => "Wypowiedziano wojnę o terytorium twojej sekty!",
            "war_resolved" => "Wojna o terytorium rozstrzygnięta.",
            _ => n.Type,
        };

        private static string Describe(ApiException e) => e.Code switch
        {
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
