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
    [Activity(Label = "Jianghu Go", MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar",
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
        private TextView _subtitle;
        private TextView _backButton;
        private ImageView _backdrop;
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

            Window.SetStatusBarColor(Ui.Night);
            Window.SetNavigationBarColor(Ui.Night);
            var d = Resources.DisplayMetrics.Density;
            int Px(float dp) => (int)(dp * d + 0.5f);

            // backdrop art + darkening veil behind every screen
            var frame = new FrameLayout(this);
            frame.SetBackgroundColor(Ui.Night);
            _backdrop = new ImageView(this);
            _backdrop.SetScaleType(ImageView.ScaleType.CenterCrop);
            frame.AddView(_backdrop, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            var veil = new View(this);
            veil.Background = new global::Android.Graphics.Drawables.GradientDrawable(global::Android.Graphics.Drawables.GradientDrawable.Orientation.TopBottom,
                new int[] { global::Android.Graphics.Color.Argb(150, 6, 8, 10).ToArgb(), global::Android.Graphics.Color.Argb(205, 8, 10, 12).ToArgb(), global::Android.Graphics.Color.Argb(250, 8, 10, 12).ToArgb() });
            frame.AddView(veil, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetFitsSystemWindows(true); // Android 15+ draws edge-to-edge
            root.SetPadding(Px(16), Px(10), Px(16), 0);

            // header: back seal, title in gold, subtitle
            var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            header.SetGravity(GravityFlags.CenterVertical);
            _backButton = new TextView(this) { Text = "‹" };
            _backButton.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 30);
            _backButton.SetTextColor(Ui.Gold);
            _backButton.Gravity = GravityFlags.Center;
            _backButton.Background = new FrameDrawable(d, global::Android.Graphics.Color.Argb(200, 14, 18, 22), Ui.GoldDim, Ui.Gold);
            _backButton.Click += (_, __) => _back?.Invoke();
            var backLp = new LinearLayout.LayoutParams(Px(44), Px(44)) { RightMargin = Px(12) };
            header.AddView(_backButton, backLp);
            var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _title = new TextView(this);
            _title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 26);
            _title.SetTextColor(Ui.Gold);
            _title.SetTypeface(Ui.Display, global::Android.Graphics.TypefaceStyle.Bold);
            _title.SetShadowLayer(8, 0, 2, global::Android.Graphics.Color.Black);
            _title.LetterSpacing = 0.04f;
            titles.AddView(_title);
            _subtitle = new TextView(this);
            _subtitle.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
            _subtitle.SetTextColor(Ui.Muted);
            _subtitle.SetTypeface(Ui.Body, global::Android.Graphics.TypefaceStyle.Normal);
            _subtitle.LetterSpacing = 0.12f;
            titles.AddView(_subtitle);
            header.AddView(titles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            var seal = new TextView(this) { Text = "江湖" };
            seal.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 16);
            seal.SetTextColor(Ui.Ink);
            seal.SetTypeface(Ui.Display, global::Android.Graphics.TypefaceStyle.Bold);
            seal.Gravity = GravityFlags.Center;
            var sealBg = new global::Android.Graphics.Drawables.GradientDrawable();
            sealBg.SetColor(Ui.CinnabarDeep);
            sealBg.SetStroke(Px(1), Ui.Gold);
            sealBg.SetCornerRadius(Px(4));
            seal.Background = sealBg;
            seal.SetPadding(Px(6), Px(4), Px(6), Px(4));
            header.AddView(seal);
            root.AddView(header);

            // status: a lacquer ribbon that appears only when there is something to say
            _status = new TextView(this);
            _status.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            _status.SetTextColor(Ui.Ink);
            _status.SetTypeface(Ui.Body, global::Android.Graphics.TypefaceStyle.Normal);
            var statusBg = new global::Android.Graphics.Drawables.GradientDrawable();
            statusBg.SetColor(global::Android.Graphics.Color.Argb(235, 60, 16, 12));
            statusBg.SetStroke(Px(1), Ui.Gold);
            statusBg.SetCornerRadius(Px(6));
            _status.Background = statusBg;
            _status.SetPadding(Px(12), Px(8), Px(12), Px(8));
            _status.Visibility = ViewStates.Gone;
            var statusLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Px(8) };
            root.AddView(_status, statusLp);

            _scroll = new ScrollView(this) { VerticalScrollBarEnabled = false };
            var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
            content.SetPadding(0, Px(4), 0, Px(28));
            _scroll.AddView(content);
            root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            frame.AddView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
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

                NewScreen("Jianghu Go", art: Resource.Drawable.bg_hero);
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

        private Ui NewScreen(string title, System.Action back = null, int art = 0, string subtitle = null)
        {
            _screen++;
            _worldOpen = false;
            _onLocation = null;
            _location.Stop();
            _back = back;
            _title.Text = title;
            _subtitle.Text = subtitle ?? string.Empty;
            _subtitle.Visibility = string.IsNullOrEmpty(subtitle) ? ViewStates.Gone : ViewStates.Visible;
            _backButton.Visibility = back == null ? ViewStates.Gone : ViewStates.Visible;
            if (art != 0)
            {
                _backdrop.SetImageBitmap(Ui.Art(this, art));
                _backdrop.Visibility = ViewStates.Visible;
            }
            else
            {
                _backdrop.Visibility = ViewStates.Invisible;
            }

            SetStatus(string.Empty);
            _ui.Content.RemoveAllViews();
            _scroll.ScrollTo(0, 0);
            _ui.Content.Alpha = 0f;
            _ui.Content.TranslationY = _ui.Dp(14);
            _ui.Content.Animate().Alpha(1f).TranslationY(0).SetDuration(260).Start();
            return _ui;
        }

        /// <summary>Shows a message ribbon; "…" (work in progress) keeps it hidden. Messages fade after a while.</summary>
        private void SetStatus(string text)
        {
            _status.Text = text;
            var visible = !string.IsNullOrEmpty(text) && text != "…";
            _status.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
            if (!visible) return;
            _status.Alpha = 0f;
            _status.Animate().Alpha(1f).SetDuration(180).Start();
            var shown = text;
            _status.PostDelayed(() =>
            {
                if (_status.Text == shown) _status.Visibility = ViewStates.Gone;
            }, 6000);
        }

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
            var ui = NewScreen("Jianghu Go", art: Resource.Drawable.bg_hero, subtitle: "ŚWIAT WUXIA NA TWOICH ULICACH");
            var spacer = new View(this);
            ui.Add(spacer, 200);
            var hero = ui.Panel();
            ui.Label("江湖行", 34, Ui.Gold, bold: true, parent: hero).Gravity = GravityFlags.Center;
            ui.Label("Kultywuj Qi, zbieraj duchowe zioła na ulicach swojego miasta, walcz z bestiami i zakładaj sekty.", 15, parent: hero).Gravity = GravityFlags.Center;
            ui.Action("☯  Graj offline (bez serwera)", () => Run(PlayOfflineAsync), parent: hero);
            ui.Label("Świat działa w telefonie, postęp zapisuje się w pamięci aplikacji.", 12, Ui.Muted, parent: hero).Gravity = GravityFlags.Center;

            ui.Heading("Gra z serwerem");
            var online = ui.Panel();
            ui.Label("Adres serwera", 12, Ui.Muted, parent: online);
            var server = ui.Field("http://192.168.1.10:8080", value: _prefs.GetString(ServerUrlKey, DefaultServer), parent: online);
            var email = ui.Field("E-mail", parent: online);
            var password = ui.Field("Hasło", password: true, parent: online);

            bool Connect()
            {
                if (ConnectTo(server.Text)) return true;
                SetStatus("Podaj adres serwera, np. http://192.168.1.10:8080");
                return false;
            }

            var buttons = ui.Columns(online);
            ui.Action("Zaloguj", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.LoginAsync(email.Text.Trim(), password.Text, Ct);
                await ShowHomeAsync();
            }), parent: Cell(buttons));
            ui.Action("Załóż konto", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.RegisterAsync(email.Text.Trim(), password.Text, Ct);
                await _api.LoginAsync(email.Text.Trim(), password.Text, Ct);
                await ShowHomeAsync();
            }), secondary: true, parent: Cell(buttons));
            ui.Label("Hasło: co najmniej 10 znaków. Serwer: API_BIND=0.0.0.0 docker compose up --build", 11, Ui.Muted, parent: online);
        }

        /// <summary>A vertical cell sharing a row equally with its siblings.</summary>
        private LinearLayout Cell(LinearLayout row)
        {
            var cell = new LinearLayout(this) { Orientation = Orientation.Vertical };
            Ui.AddCell(row, cell, _ui.Dp(8));
            return cell;
        }

        /// <summary>Starts the in-app server and signs in with the local account (created on first use).</summary>
        private async Task PlayOfflineAsync()
        {
            NewScreen("Jianghu Go", art: Resource.Drawable.bg_hero, subtitle: "BUDZĘ ŚWIAT JIANGHU…");
            _ui.Label("Budzę świat Jianghu…", 16, Ui.Gold);
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
            var ui = NewScreen("Narodziny wojownika", art: Resource.Drawable.bg_hero, subtitle: "LOS RZUCA KOŚCI TWOJEGO PRZEZNACZENIA");
            ui.Label("Zablokuj cechy, które chcesz zachować, i losuj ponownie. Każde losowanie kosztuje Żetony Losu.", 14, Ui.Muted);
            var traits = ui.Column();
            ui.Heading("Atrybuty");
            var statsPanel = ui.Panel();
            var stats = ui.Flow(statsPanel);
            var tokens = ui.Label(string.Empty, 14, Ui.Gold, bold: true);

            void Render()
            {
                var d = flow.Draft;
                traits.RemoveAllViews();
                foreach (var t in d.Traits)
                {
                    var trait = t;
                    var locked = flow.LockedCategories.Contains(trait.Category);
                    var rarity = Ui.RarityOf(trait.Rarity);
                    var card = new LinearLayout(this) { Orientation = Orientation.Horizontal, Clickable = true };
                    card.SetGravity(GravityFlags.CenterVertical);
                    card.Background = new FrameDrawable(Resources.DisplayMetrics.Density, global::Android.Graphics.Color.Argb(230, 14, 18, 22), rarity.Color, rarity.Color);
                    card.SetPadding(_ui.Dp(14), _ui.Dp(10), _ui.Dp(14), _ui.Dp(10));
                    var text = new LinearLayout(this) { Orientation = Orientation.Vertical };
                    ui.Label(trait.NamePl, 17, Ui.Ink, bold: true, parent: text);
                    ui.Label(CategoryName(trait.Category) + " · " + rarity.Name, 12, rarity.Color, parent: text);
                    card.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
                    var lockIcon = new TextView(this) { Text = locked ? "🔒" : "🔓" };
                    lockIcon.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 20);
                    lockIcon.Alpha = locked ? 1f : 0.45f;
                    card.AddView(lockIcon);
                    card.Click += (_, __) =>
                    {
                        if (!flow.ToggleLock(trait.Category)) SetStatus("Możesz zablokować najwyżej " + flow.Draft.MaxLockedTraits + " cechy.");
                        Render();
                    };
                    var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = _ui.Dp(8) };
                    traits.AddView(card, lp);
                }

                stats.RemoveAllViews();
                foreach (var st in d.Stats) ui.Chip(StatName(st.Id) + "  " + st.Value, Ui.Ink, stats);
                tokens.Text = "Żetony Losu: " + d.DestinyTokens + "   ·   koszt losowania: " + flow.NextRerollCost + "   ·   pozostało: " + d.RerollsRemaining;
            }

            ui.Action("🎲  Losuj ponownie", () => Run(async () =>
            {
                if (!await flow.RerollAsync(Ct)) SetStatus("Szkic zmienił się na innym urządzeniu — odświeżono.");
                Render();
            }), secondary: true);
            ui.Heading("Imię");
            var name = ui.Field("Imię postaci (2–24 litery)");
            ui.Heading("Ścieżka kultywacji");
            var paths = new[]
            {
                ("qi", "Qi — równowaga i technika", Resource.Drawable.place_qiarray, Ui.QiBlue),
                ("body", "Ciało — siła i wytrzymałość", Resource.Drawable.loc_fortress, global::Android.Graphics.Color.Rgb(196, 140, 80)),
                ("flame", "Płomień — moc miejsc Płomienia", Resource.Drawable.place_brazier, Ui.Violet),
                ("poison", "Trucizna — podstęp i osłabienie", Resource.Drawable.place_rift, Ui.Jade),
            };
            for (var i = 0; i < paths.Length; i += 2)
            {
                var row = ui.Columns();
                for (var j = i; j < i + 2; j++)
                {
                    var p = paths[j];
                    ui.Tile(p.Item2, string.Empty, p.Item3, () => Run(async () => ShowCharacter(await flow.FinalizeAsync(name.Text.Trim(), p.Item1, Ct))),
                        parent: row, heightDp: 118, accent: p.Item4);
                }
            }

            Render();
        }

        // ---- character & cultivation --------------------------------------------------------------------------

        private void ShowCharacter(CharacterDto c)
        {
            var ui = NewScreen(c.Name, art: Resource.Drawable.bg_town, subtitle: "ŚCIEŻKA " + PathName(c.Path).ToUpperInvariant());

            // hero card: portrait, realm, Qi, cultivation actions
            var card = ui.Panel();
            var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            card.AddView(top);
            var portrait = new ImageView(this);
            var portraitArt = new global::Android.Graphics.Drawables.BitmapDrawable(Resources, Ui.Art(this, Resource.Drawable.portrait)) { FilterBitmap = false };
            portrait.SetImageDrawable(portraitArt);
            portrait.SetScaleType(ImageView.ScaleType.CenterCrop);
            portrait.Background = new FrameDrawable(Resources.DisplayMetrics.Density, Ui.Night, Ui.Gold, Ui.Gold);
            portrait.SetPadding(_ui.Dp(3), _ui.Dp(3), _ui.Dp(3), _ui.Dp(3));
            top.AddView(portrait, new LinearLayout.LayoutParams(_ui.Dp(92), _ui.Dp(100)) { RightMargin = _ui.Dp(14) });
            var info = new LinearLayout(this) { Orientation = Orientation.Vertical };
            top.AddView(info, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            var realm = ui.Label("…", 18, Ui.Gold, bold: true, parent: info);
            var traitsText = ui.Label(string.Join(" · ", c.Traits.Select(t => t.NamePl)), 12, Ui.Muted, parent: info);
            var qi = ui.Meter("Qi", Ui.QiBlue, info, 20);
            var details = ui.Label(string.Empty, 13, parent: card);
            var flow = new CultivationFlow(_api, c.Id);
            var actions = ui.Columns(card);
            var meditate = ui.Action("🧘 Medytuj", () => { }, parent: Cell(actions));
            var breakthrough = ui.Action("⚡ Przełom", () => { }, secondary: true, parent: Cell(actions));

            var statsRow = ui.Flow(card);
            foreach (var st in c.Stats) ui.Chip(StatName(st.Id) + " " + st.Value, Ui.Ink, statsRow);

            void Render()
            {
                var st = flow.State;
                if (st == null) return;
                realm.Text = st.RealmNamePl + " · etap " + st.Stage + "/" + st.StagesInRealm;
                Ui.SetMeter(qi, "Qi  " + st.Qi + " / " + st.QiRequired, st.Qi, st.QiRequired);
                var deviation = st.DeviationUntil.HasValue && st.DeviationUntil > DateTimeOffset.UtcNow
                    ? "\nOdchylenie Qi do " + st.DeviationUntil.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
                details.Text = "Szansa przełomu: " + Math.Round(st.BreakthroughChance * 100) + "%" +
                               (st.IsMeditating ? "   ·   Medytujesz od " + st.MeditationStartedAt.Value.ToLocalTime().ToString("HH:mm") : string.Empty) +
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
            breakthrough.Click += (_, __) => Run(async () =>
            {
                var r = await flow.BreakthroughAsync(Ct);
                Render();
                SetStatus(r == null ? "Stan zmienił się na innym urządzeniu — odświeżono." : r.Success ? "Przełom udany!" : "Przełom nieudany — odchylenie Qi.");
            });

            ui.Heading("Wyprawa");
            ui.Tile("🗺  Eksploruj świat", "Zioła, źródła Qi i bestie wokół ciebie", Resource.Drawable.loc_mountains, () => Run(async () =>
            {
                var exploration = new ExplorationFlow(_api, _location, c.Id);
                await exploration.StartAsync(Ct);
                ShowWorld(exploration, c);
            }), heightDp: 130);

            ui.Heading("Trening walki");
            var fights = ui.Columns();
            foreach (var npc in new[] { ("training_dummy", "Manekin treningowy", Resource.Drawable.sprite_dummy), ("grey_wolf", "Szary Wilk", Resource.Drawable.sprite_wolf), ("mountain_bandit", "Górski Bandyta", Resource.Drawable.sprite_bandit) })
            {
                var n = npc;
                ui.Tile(n.Item2, string.Empty, n.Item3, () => Run(async () =>
                {
                    var combat = new CombatFlow(_api);
                    await combat.StartPveAsync(c.Id, n.Item1, Ct);
                    ShowBattle(combat, c);
                }), parent: fights, heightDp: 140, pixelArt: true, accent: Ui.Cinnabar);
            }

            ui.Heading("Ścieżka mistrza");
            var row1 = ui.Columns();
            ui.Tile("🎒 Sakwa, kuźnia i targ", "Ekwipunek i handel", Resource.Drawable.loc_village, () => Run(async () =>
            {
                var economy = new EconomyFlow(_api, c.Id);
                await economy.LoadAsync(Ct);
                ShowBag(economy, c);
            }), parent: row1, heightDp: 120);
            ui.Tile("☯ Laboratorium Dao", "Własne techniki", Resource.Drawable.place_qiarray, () => Run(async () =>
            {
                var lab = new DaoLabFlow(_api, c.Id);
                await lab.LoadAsync(Ct);
                var economy = new EconomyFlow(_api, c.Id);
                await economy.LoadAsync(Ct);
                ShowDaoLab(lab, economy, c);
            }), parent: row1, heightDp: 120);
            var row2 = ui.Columns();
            ui.Tile("🏯 Sekta i terytoria", "Bractwo i wojny", Resource.Drawable.loc_temple, () => Run(async () =>
            {
                var sect = new SectFlow(_api, c.Id);
                await sect.LoadAsync(Ct);
                ShowSect(sect, c);
            }), parent: row2, heightDp: 120);
            var inboxTile = ui.Tile("✉ Powiadomienia", "Wieści z Jianghu", Resource.Drawable.place_altar, () => { }, parent: row2, heightDp: 120);
            var inbox = ui.Label(string.Empty, 14);
            inboxTile.Click += (_, __) => Run(async () =>
            {
                var page = await _api.GetNotificationsAsync(true, 10, null, Ct);
                inbox.Text = page.Items.Count == 0 ? "Brak nowych powiadomień." : string.Join("\n", page.Items.Select(n => "• " + Describe(n)));
                foreach (var n in page.Items) await _api.MarkNotificationReadAsync(n.Id, Ct);
            });

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

            var ui = NewScreen("Jianghu", () => Run(Leave), subtitle: "EKSPLORACJA");
            var gen = _screen;
            _worldOpen = true;
            if (!_paused) _location.Start();

            var info = ui.Label("…", 13, Ui.Ink);
            var map = new WorldMapView(this);
            ui.Add(map, 380);
            ui.Label("Dotknij miejsca w czerwonym kręgu (~40 m), by je zebrać lub stoczyć walkę.", 12, Ui.Muted);
            ui.Heading("W pobliżu");
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
                            (exploration.Session != null ? "   ·   przebyto " + Math.Round(exploration.Session.DistanceM) + " m" : string.Empty) +
                            (n?.Event != null ? "\n✨ Wydarzenie: " + n.Event.NamePl : string.Empty) +
                            (n?.NearestFlameSite != null ? "\n🔥 Płomień: " + n.NearestFlameSite.NamePl + " (" + (n.NearestFlameSite.DistanceM < 1000 ? n.NearestFlameSite.DistanceM + " m" : n.NearestFlameSite.DistanceM / 1000 + " km") + ")" : string.Empty);
                map.Player = _location.Last;
                map.Nearby = n;
                map.Invalidate();
                list.RemoveAllViews();
                if (n == null) return;
                foreach (var node in n.Nodes.OrderBy(x => x.DistanceM).Take(8))
                {
                    var target = node;
                    var ready = target.InRange && !target.Harvested;
                    ui.Action(NodeGlyph(target.Kind) + "  " + target.NamePl + " — " + target.DistanceM + " m" + (target.Harvested ? "  ✓" : ready ? "  ✋ zbierz" : string.Empty),
                        () => Interact(target), secondary: !ready, parent: list);
                }
            }

            map.NodeTapped += Interact;
            _onLocation = () =>
            {
                map.Player = _location.Last;
                map.Invalidate();
            };

            var buttons = ui.Columns();
            ui.Action("🔥 Dostrój się", () => Run(async () =>
            {
                var site = exploration.Nearby?.NearestFlameSite;
                if (site == null || !site.InRange)
                {
                    SetStatus("Musisz stać przy jednym z 12 miejsc Płomienia.");
                    return;
                }

                var r = await exploration.AttuneAsync(site.Id, Ct);
                SetStatus("+" + r.RewardQi + " Qi z Płomienia");
            }), secondary: true, parent: Cell(buttons));
            ui.Action("Zakończ eksplorację", () => Run(Leave), secondary: true, parent: Cell(buttons));

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

        private static string NodeGlyph(string kind) => kind switch
        {
            "qi_spring" => "💧",
            "spirit_herb" => "🌿",
            "spirit_beast" => "🐺",
            _ => "✦",
        };

        // ---- battle -------------------------------------------------------------------------------------------

        private void ShowBattle(CombatFlow combat, CharacterDto c)
        {
            var ui = NewScreen("Pojedynek", () => ShowCharacter(c), subtitle: combat.Battle.Opponent.Name.ToUpperInvariant());
            var stage = new BattleStageView(this, combat.Battle.NpcId);
            ui.Add(stage, 300);
            var header = ui.Label(string.Empty, 14, Ui.Gold, bold: true);
            var logPanel = ui.Panel(padDp: 10);
            var log = ui.Label(string.Empty, 13, parent: logPanel);
            var buttons = new List<Button>();
            var gen = _screen;

            void Render()
            {
                var b = combat.Battle;
                stage.Show(b);
                header.Text = "Tura " + b.Turn + "   ·   dystans " + b.Distance + "   ·   Qi " + b.Me.Qi + "/" + b.Me.MaxQi +
                              (b.IsActive ? (b.MyActionSubmitted ? "\nCzekasz na ruch przeciwnika…" : string.Empty)
                                  : "\nWYNIK: " + ResultName(b) + (b.RewardQi > 0 ? "  (+" + b.RewardQi + " Qi)" : string.Empty));
                log.Text = b.LastTurnEvents.Count == 0 ? "Przeciwnicy mierzą się wzrokiem…" : string.Join("\n", b.LastTurnEvents.Select(e =>
                    (b.IsMine(e) ? "Ty" : "Wróg") + ": " + ActionName(e.Action) + (e.TechniqueId != null ? " (" + e.TechniqueId + ")" : string.Empty) +
                    (e.Hit == true ? " → " + e.Damage + " obrażeń" : e.Hit == false ? " → pudło" : string.Empty)));
                foreach (var button in buttons)
                {
                    button.Enabled = b.IsActive && !b.MyActionSubmitted;
                    button.Alpha = button.Enabled ? 1f : 0.4f;
                }
            }

            void Act(int? slot, string direction) => Run(async () =>
            {
                if (!await combat.ActAsync(slot, direction, Ct)) SetStatus("Tura już minęła — odświeżono.");
                if (gen == _screen) Render();
            });

            ui.Heading("Techniki");
            LinearLayout row = null;
            var count = 0;
            void AddSkill(string caption, System.Action onClick, bool secondary)
            {
                if (count % 2 == 0) row = ui.Columns();
                buttons.Add(ui.Action(caption, onClick, secondary, Cell(row)));
                count++;
            }

            foreach (var s in combat.Battle.MySlots.Where(x => x.TechniqueId != null))
            {
                var slot = s;
                var cost = slot.QiCost > 0 ? "\n" + slot.QiCost + " Qi" : string.Empty;
                if (slot.Kind == "movement")
                {
                    AddSkill("➡ " + slot.NamePl + cost, () => Act(slot.Slot, "advance"), true);
                    AddSkill("⬅ " + slot.NamePl + cost, () => Act(slot.Slot, "retreat"), true);
                }
                else
                {
                    AddSkill(KindGlyph(slot.Kind) + " " + slot.NamePl + cost, () => Act(slot.Slot, null), slot.Kind != "attack");
                }
            }

            AddSkill("Odpoczynek\n+Qi", () => Act(null, null), true);
            var bottom = ui.Columns();
            ui.Action("Odśwież", () => Run(async () =>
            {
                await combat.RefreshAsync(Ct);
                Render();
            }), secondary: true, parent: Cell(bottom));
            ui.Action("Wróć", () => ShowCharacter(c), secondary: true, parent: Cell(bottom));
            Render();
        }

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

        // ---- economy ------------------------------------------------------------------------------------------

        private void ShowBag(EconomyFlow economy, CharacterDto c)
        {
            var ui = NewScreen("Sakwa", () => ShowCharacter(c), Resource.Drawable.loc_village, "EKWIPUNEK");
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
            var ui = NewScreen("Kuźnia i alchemia", () => ShowBag(economy, c), Resource.Drawable.loc_fortress, "RZEMIOSŁO");
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
            var ui = NewScreen("Targ", () => ShowBag(economy, c), Resource.Drawable.loc_village, "HANDEL");
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
            var ui = NewScreen("Laboratorium Dao", () => ShowCharacter(c), Resource.Drawable.place_qiarray, "TWORZENIE TECHNIK");
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
                var ui0 = NewScreen("Sekty", () => ShowCharacter(c), Resource.Drawable.loc_temple, "BRACTWA JIANGHU");
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
            var ui = NewScreen(mine.Sect.Name, () => ShowCharacter(c), Resource.Drawable.loc_fortress, "TWOJA SEKTA");
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
