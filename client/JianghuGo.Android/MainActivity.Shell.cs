using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>
    /// The game shell: hero HUD (portrait, realm, Qi, stones, quests / bag / news with badges) on top, five hubs in
    /// the dock at the bottom, a title row for sub-screens, and the screen host in between. Screens never scroll:
    /// each one is laid out with weights inside the host; long lists page sideways and details open in sheets.
    /// </summary>
    public sealed partial class MainActivity
    {
        public const string TabHome = "home";
        public const string TabWorld = "world";
        public const string TabCombat = "combat";
        public const string TabHero = "hero";
        public const string TabSect = "sect";

        private LinearLayout _hud;
        private LinearLayout _titleRow;
        private TextView _titleText;
        private TextView _titleSub;
        private TextView _titleBack;
        private FrameLayout _host;
        private LinearLayout _dock;
        private readonly Dictionary<string, View> _dockDots = new Dictionary<string, View>();
        private DateTime _alertsAt = DateTime.MinValue;

        /// <summary>The hero's combat power (2.2), shown on the cultivation and hero screens; 0 until known.</summary>
        private long _power;

        /// <summary>
        /// Red dots on the dock: something waits in that hub. Fetched at most every 45 s (more calls than the HUD).
        /// Home: a quest to claim or the vessel full for a breakthrough; Combat: a finished hunt or a boss reward;
        /// Hero: unspent meridian points.
        /// </summary>
        private async Task RefreshAlertsAsync(bool force)
        {
            if (_me == null || (!force && DateTime.UtcNow - _alertsAt < TimeSpan.FromSeconds(45))) return;
            if (DateTime.UtcNow - _alertsAt < TimeSpan.FromSeconds(8)) return; // even forced refreshes are spaced out
            _alertsAt = DateTime.UtcNow;
            var home = false;
            var combat = false;
            var hero = false;
            try
            {
                home = (_board != null && (_board.Daily.Any(q => q.Completed && !q.Claimed) || _board.Achievements.Any(q => q.Completed && !q.Claimed) || !_board.Checkin.CheckedInToday))
                       || (_cultivation != null && _cultivation.QiRequired > 0 && _cultivation.Qi >= _cultivation.QiRequired);
                var hunt = (await _api.GetHuntAsync(_me.Id, Ct)).Hunt;
                var boss = await _api.GetWorldBossAsync(_me.Id, Ct);
                combat = hunt?.Finished == true || boss.CanClaim || boss.UnclaimedCycle != null;
                hero = (await _api.GetMeridiansAsync(_me.Id, Ct)).Free > 0;
                _power = (await _api.GetCombatScoutAsync(_me.Id, Ct)).MyPower;
            }
            catch (ApiException)
            {
                // dots are a hint; keep the old ones
                return;
            }

            void Dot(string tab, bool on)
            {
                if (_dockDots.TryGetValue(tab, out var v)) v.Visibility = on ? ViewStates.Visible : ViewStates.Gone;
            }

            Dot(TabHome, home);
            Dot(TabCombat, combat);
            Dot(TabHero, hero);
        }

        private readonly Dictionary<string, (LinearLayout Item, TextView Label, ImageView Icon)> _dockItems = new Dictionary<string, (LinearLayout, TextView, ImageView)>();
        private string _tab = TabHome;
        private readonly List<(View View, System.Action Close)> _overlays = new List<(View, System.Action)>();

        // HUD widgets and the last known values
        private ImageView _hudPortrait;
        private TextView _hudName;
        private MeterView _hudQi;
        private TextView _hudStones;
        private TextView _hudQuestBadge;
        private TextView _hudNewsBadge;
        private View _hudQuestButton;
        private View _hudBagButton;
        private CultivationDto _cultivation;
        private long _balance;
        private QuestBoardDto _board;
        private int _unread;
        private DateTime _hudAt = DateTime.MinValue;

        private int Px(float dp) => (int)(dp * Resources.DisplayMetrics.Density + 0.5f);

        private QiMotesView _motes;

        private void BuildShell()
        {
            Window.SetStatusBarColor(Ui.Night);
            Window.SetNavigationBarColor(Ui.Night);
            // paper theme: dark status/navigation bar icons
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.LightStatusBar | SystemUiFlags.LightNavigationBar);

            var frame = _frame = new FrameLayout(this);
            frame.SetBackgroundColor(Ui.Night);
            _backdrop = new ImageView(this);
            _backdrop.SetScaleType(ImageView.ScaleType.CenterCrop);
            var mono = new ColorMatrix();
            mono.SetSaturation(0.12f); // every backdrop reads as an ink wash on paper
            _backdrop.SetColorFilter(new ColorMatrixColorFilter(mono));
            frame.AddView(_backdrop, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            var veil = new View(this);
            veil.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom,
                new int[] { Color.Argb(170, 244, 240, 231).ToArgb(), Color.Argb(120, 244, 240, 231).ToArgb(), Color.Argb(235, 244, 240, 231).ToArgb() });
            frame.AddView(veil, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _motes = new QiMotesView(this);
            frame.AddView(_motes, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetFitsSystemWindows(true);
            frame.AddView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            _hud = BuildHud();
            root.AddView(_hud, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(58)));

            _titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            _titleRow.SetGravity(GravityFlags.CenterVertical);
            _titleRow.SetPadding(Px(12), Px(4), Px(12), 0);
            _titleBack = new TextView(this) { Text = "‹", Gravity = GravityFlags.Center, Clickable = true };
            _titleBack.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 26);
            _titleBack.SetTextColor(Ui.Gold);
            _titleBack.Background = new FrameDrawable(Resources.DisplayMetrics.Density, Color.Argb(200, 244, 240, 231), Ui.GoldDim, Ui.Gold);
            _titleBack.Click += (_, __) =>
            {
                Sfx.Play(Sfx.Click, 0.5f);
                _back?.Invoke();
            };
            _titleRow.AddView(_titleBack, new LinearLayout.LayoutParams(Px(40), Px(40)) { RightMargin = Px(10) });
            var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _titleText = new TextView(this);
            _titleText.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 20);
            _titleText.SetTextColor(Ui.Gold);
            _titleText.SetTypeface(Ui.Display, TypefaceStyle.Bold);
            _titleText.SetShadowLayer(8, 0, 2, Color.Argb(170, 250, 247, 240));
            _titleText.SetMaxLines(1);
            _titleText.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            titles.AddView(_titleText);
            _titleSub = new TextView(this);
            _titleSub.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
            _titleSub.SetTextColor(Ui.Muted);
            _titleSub.SetTypeface(Ui.Body, TypefaceStyle.Italic);
            _titleSub.SetMaxLines(1);
            titles.AddView(_titleSub);
            _titleRow.AddView(titles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            root.AddView(_titleRow, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(48)));

            _host = new FrameLayout(this);
            _host.SetPadding(Px(12), Px(6), Px(12), Px(6));
            root.AddView(_host, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

            _dock = BuildDock();
            root.AddView(_dock, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(62)));

            // message ribbon floats over the screen instead of pushing it down
            _status = new TextView(this);
            _status.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            _status.SetTextColor(Ui.Ink);
            _status.SetTypeface(Ui.Body, TypefaceStyle.Normal);
            var statusBg = new GradientDrawable();
            statusBg.SetColor(Color.Argb(246, 248, 245, 238));
            statusBg.SetStroke(Px(2), Ui.Cinnabar);
            statusBg.SetCornerRadius(Px(10));
            _status.Background = statusBg;
            _status.SetPadding(Px(14), Px(10), Px(14), Px(10));
            _status.Elevation = Px(8);
            _status.Visibility = ViewStates.Gone;
            _status.Click += (_, __) => _status.Visibility = ViewStates.Gone;
            frame.AddView(_status, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top)
            {
                TopMargin = Px(96),
                LeftMargin = Px(16),
                RightMargin = Px(16),
            });

            SetContentView(frame);
            var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _host.AddView(content, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _ui = new Ui(this, content);
        }

        // ---- HUD ----------------------------------------------------------------------------------------------

        private LinearLayout BuildHud()
        {
            var hud = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            hud.SetGravity(GravityFlags.CenterVertical);
            hud.SetPadding(Px(10), Px(6), Px(8), Px(6));
            hud.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom,
                new int[] { Color.Argb(250, 244, 240, 231).ToArgb(), Color.Argb(235, 244, 240, 231).ToArgb() });

            _hudPortrait = new ImageView(this);
            _hudPortrait.SetScaleType(ImageView.ScaleType.CenterCrop);
            _hudPortrait.Background = new FrameDrawable(Resources.DisplayMetrics.Density, Ui.Night, Ui.Gold, Ui.Gold);
            _hudPortrait.SetPadding(Px(2), Px(2), Px(2), Px(2));
            _hudPortrait.Click += (_, __) => GoTab(TabHero);
            hud.AddView(_hudPortrait, new LinearLayout.LayoutParams(Px(44), Px(44)) { RightMargin = Px(8) });

            var mid = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _hudName = new TextView(this);
            _hudName.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12);
            _hudName.SetTypeface(Ui.Display, TypefaceStyle.Bold);
            _hudName.SetTextColor(Ui.Gold);
            _hudName.SetMaxLines(1);
            _hudName.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            mid.AddView(_hudName);
            _hudQi = new MeterView(this, Ui.QiBlue);
            mid.AddView(_hudQi, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(16)) { TopMargin = Px(3) });
            hud.AddView(mid, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            _hudStones = new TextView(this) { Gravity = GravityFlags.CenterVertical };
            _hudStones.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13);
            _hudStones.SetTypeface(Ui.Display, TypefaceStyle.Bold);
            _hudStones.SetTextColor(Ui.Ink);
            var stoneArt = new BitmapDrawable(Resources, Ui.Art(this, Resource.Drawable.icon_stones));
            stoneArt.SetBounds(0, 0, Px(18), Px(18));
            _hudStones.SetCompoundDrawables(stoneArt, null, null, null);
            _hudStones.CompoundDrawablePadding = Px(4);
            hud.AddView(_hudStones, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Px(8), RightMargin = Px(4) });

            (_hudQuestButton, _hudQuestBadge) = HudButton(hud, Resource.Drawable.ink_ico_quest, "Zadania", () => Run(ShowQuestsAsync));
            (_hudBagButton, _) = HudButton(hud, Resource.Drawable.ink_nav_bag, "Sakwa", () => Run(() => ShowBagAsync(0)));
            (_, _hudNewsBadge) = HudButton(hud, Resource.Drawable.ink_ico_event, "Wieści", () => Run(ShowInboxAsync));
            return hud;
        }

        private (View, TextView) HudButton(LinearLayout hud, int art, string description, System.Action onClick)
        {
            var f = new FrameLayout(this) { Clickable = true, ContentDescription = description };
            var img = new ImageView(this);
            img.SetImageBitmap(Ui.Art(this, art));
            img.SetScaleType(ImageView.ScaleType.FitCenter);
            f.AddView(img, new FrameLayout.LayoutParams(Px(36), Px(36), GravityFlags.Center));
            var badge = _ui == null ? new Ui(this, new LinearLayout(this)).Badge("") : _ui.Badge("");
            badge.Visibility = ViewStates.Gone;
            f.AddView(badge, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.End));
            f.Click += (_, __) =>
            {
                f.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                Sfx.Play(Sfx.Click, 0.5f);
                f.Animate().ScaleX(0.88f).ScaleY(0.88f).SetDuration(70).WithEndAction(new Java.Lang.Runnable(() => f.Animate().ScaleX(1f).ScaleY(1f).SetDuration(90).Start())).Start();
                onClick();
            };
            hud.AddView(f, new LinearLayout.LayoutParams(Px(44), Px(44)) { LeftMargin = Px(2) });
            return (f, badge);
        }

        private static void SetBadge(TextView badge, int count)
        {
            badge.Text = count > 9 ? "9+" : count.ToString();
            badge.Visibility = count > 0 ? ViewStates.Visible : ViewStates.Gone;
        }

        private void RenderHud()
        {
            if (_me == null) return;
            var portrait = _ui.ArtId("portrait_" + _me.Path);
            _hudPortrait.SetImageBitmap(Ui.Art(this, portrait != 0 ? portrait : Resource.Drawable.portrait));
            // the title the player chose to wear (not just the latest one earned)
            var chosen = _board?.AllTitles?.FirstOrDefault(t => t.Selected);
            var title = chosen != null ? "「" + chosen.NamePl + "」 " : string.Empty;
            _hudName.Text = title + _me.Name + (_cultivation != null ? " · " + _cultivation.RealmNamePl + " " + _cultivation.Stage + "/" + _cultivation.StagesInRealm : string.Empty);
            if (_cultivation != null) Ui.SetMeter(_hudQi, "Qi " + _cultivation.Qi + " / " + _cultivation.QiRequired, _cultivation.Qi, Math.Max(1, _cultivation.QiRequired));
            _hudStones.Text = _balance.ToString("N0");
            var claimable = _board == null ? 0 : _board.Daily.Count(q => q.Completed && !q.Claimed) + _board.Achievements.Count(q => q.Completed && !q.Claimed) + (_board.Checkin.CheckedInToday ? 0 : 1);
            SetBadge(_hudQuestBadge, claimable);
            SetBadge(_hudNewsBadge, _unread);
        }

        /// <summary>Reloads what the HUD shows (throttled unless forced). Failures leave the old values.</summary>
        private async Task RefreshHudAsync(bool force = false)
        {
            if (_me == null || (!force && DateTime.UtcNow - _hudAt < TimeSpan.FromSeconds(4))) return;
            _hudAt = DateTime.UtcNow;
            try
            {
                _cultivation = await _api.GetCultivationAsync(_me.Id, Ct);
                _balance = (await _api.GetInventoryAsync(_me.Id, Ct)).Balance;
                _board = await _api.GetQuestsAsync(_me.Id, Ct);
                _unread = (await _api.GetNotificationsAsync(true, 30, null, Ct)).Items.Count;
            }
            catch (ApiException)
            {
                // keep the previous values; the screen shows its own errors
            }

            await RefreshAlertsAsync(force);

            RenderHud();
        }

        private void RefreshHud(bool force = true) => Run(() => RefreshHudAsync(force));

        // ---- dock ---------------------------------------------------------------------------------------------

        private LinearLayout BuildDock()
        {
            var dock = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            dock.SetGravity(GravityFlags.Center);
            dock.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom,
                new int[] { Color.Argb(240, 244, 240, 231).ToArgb(), Color.Argb(255, 244, 240, 231).ToArgb() });
            dock.SetPadding(Px(4), Px(2), Px(4), Px(2));
            foreach (var (tab, label, art) in new[]
                     {
                         (TabHome, "Kultywacja", Resource.Drawable.ink_nav_home), (TabWorld, "Świat", Resource.Drawable.ink_nav_world),
                         (TabCombat, "Walka", Resource.Drawable.ink_nav_combat), (TabHero, "Bohater", Resource.Drawable.ink_nav_hero), (TabSect, "Sekta", Resource.Drawable.ink_nav_sect),
                     })
            {
                var t = tab;
                var item = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
                item.SetGravity(GravityFlags.Center);
                var icon = new ImageView(this);
                icon.SetImageBitmap(Ui.Art(this, art));
                icon.SetScaleType(ImageView.ScaleType.FitCenter);
                // a red seal dot when something waits there (reward to claim, point to spend, hunt finished)
                var iconFrame = new FrameLayout(this);
                iconFrame.AddView(icon, new FrameLayout.LayoutParams(Px(36), Px(36)));
                var dot = new View(this) { Visibility = ViewStates.Gone };
                var dotBg = new GradientDrawable();
                dotBg.SetShape(ShapeType.Oval);
                dotBg.SetColor(Ui.Cinnabar);
                dotBg.SetStroke(Px(1.5f), Color.Rgb(248, 245, 238));
                dot.Background = dotBg;
                iconFrame.AddView(dot, new FrameLayout.LayoutParams(Px(11), Px(11), GravityFlags.Top | GravityFlags.End));
                _dockDots[t] = dot;
                item.AddView(iconFrame, new LinearLayout.LayoutParams(Px(36), Px(36)));
                var text = new TextView(this) { Text = label, Gravity = GravityFlags.Center };
                text.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 10);
                text.SetTypeface(Ui.Display, TypefaceStyle.Bold);
                text.SetMaxLines(1);
                item.AddView(text, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
                item.Click += (_, __) =>
                {
                    item.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                    Sfx.Play(Sfx.Click, 0.6f);
                    GoTab(t);
                };
                dock.AddView(item, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
                _dockItems[t] = (item, text, icon);
            }

            return dock;
        }

        /// <summary>The old painted screen backgrounds swapped for the ink-wash set.</summary>
        private static int InkBackdrop(int art)
        {
            if (art == Resource.Drawable.bg_market) return Resource.Drawable.ink_bg_home;
            if (art == Resource.Drawable.bg_sect) return Resource.Drawable.ink_bg_sect;
            if (art == Resource.Drawable.bg_forge) return Resource.Drawable.ink_bg_market;
            if (art == Resource.Drawable.bg_hero || art == Resource.Drawable.bg_meditation) return Resource.Drawable.ink_bg_hero;
            return art;
        }

        private void HighlightDock(string tab)
        {
            foreach (var (key, (item, label, icon)) in _dockItems)
            {
                var on = key == tab;
                label.SetTextColor(on ? Ui.Gold : Ui.Muted);
                icon.Alpha = on ? 1f : 0.55f;
                icon.ScaleX = icon.ScaleY = on ? 1.12f : 1f;
                if (on)
                {
                    var glow = new GradientDrawable(GradientDrawable.Orientation.BottomTop,
                        new int[] { Color.Argb(70, 40, 38, 34).ToArgb(), Color.Argb(0, 40, 38, 34).ToArgb() });
                    glow.SetCornerRadius(Px(10));
                    item.Background = glow;
                }
                else
                {
                    item.Background = null;
                }
            }
        }

        /// <summary>Opens one of the five hubs.</summary>
        private void GoTab(string tab)
        {
            if (_me == null) return;
            if (tab == TabSect && _board != null && !Unlocked("battles_won"))
            {
                // progressive unlock: sects open after the first won fight (the screen stays where it was)
                SetStatus("🔒 Sekty otworzą się po pierwszej wygranej walce — zacznij od Treningu w zakładce Walka.");
                return;
            }

            _tab = tab;
            switch (tab)
            {
                case TabWorld:
                    Run(ShowWorldTabAsync);
                    break;
                case TabCombat:
                    Run(() => ShowCombatTabAsync(0)); // the dock always opens the combat hub
                    break;
                case TabHero:
                    Run(() => ShowHeroTabAsync(0));
                    break;
                case TabSect:
                    Run(() => ShowSectTabAsync(0));
                    break;
                default:
                    ShowHomeTab();
                    break;
            }
        }

        private void BackToTab() => GoTab(_tab);

        /// <summary>Enters the game with a character: HUD, dock, the cultivation hub; first time also the mentor.</summary>
        private void EnterGame(CharacterDto c)
        {
            _me = c;
            HeroArt.CurrentPath = c?.Path;
            CurrentCharacterId = c.Id;
            _tab = TabHome;
            ShowHomeTab();
            Run(async () =>
            {
                await RefreshHudAsync(true);
                MaybeShowTutorial(c);
                await SendPendingCrashAsync();
            });
        }

        // ---- screens ------------------------------------------------------------------------------------------

        /// <summary>
        /// Starts a screen. <paramref name="chrome"/>: HUD and dock visible (in-game hubs and their sub-screens).
        /// <paramref name="title"/> null hides the title row (hub roots). The returned Ui builds into a fixed page.
        /// </summary>
        /// <summary>
        /// Screen captions read as a quiet line, not a shouted label: a list of the tabs below ("A · B · C") or the date
        /// repeats what the screen already shows and is dropped; the rest is set in sentence case.
        /// </summary>
        private static string Caption(string subtitle)
        {
            if (string.IsNullOrWhiteSpace(subtitle)) return null;
            if (subtitle != subtitle.ToUpperInvariant()) return subtitle;
            if (subtitle.StartsWith("DZIEŃ ", StringComparison.Ordinal)) return null;
            if (subtitle.Contains(" · ") && !subtitle.StartsWith("KROK ", StringComparison.Ordinal)) return null;
            var s = subtitle.ToLowerInvariant();
            s = s.Replace("jianghu", "Jianghu");
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private Ui NewScreen(string title, System.Action back = null, int art = 0, string subtitle = null, bool chrome = true, string tab = null)
        {
            _screen++;
            CloseAllOverlays();
            _worldOpen = false;
            _onLocation = null;
            _location.Stop();
            _compass?.Stop();
            if (tab != TabWorld && _exploration != null)
            {
                var leaving = _exploration;
                _exploration = null;
                Run(async () =>
                {
                    try
                    {
                        await leaving.StopAsync(Ct);
                    }
                    catch (ApiException)
                    {
                        // the session expires on its own
                    }
                });
            }

            _back = back;
            var inGame = chrome && _me != null;
            _hud.Visibility = inGame ? ViewStates.Visible : ViewStates.Gone;
            _dock.Visibility = inGame ? ViewStates.Visible : ViewStates.Gone;
            _titleRow.Visibility = title == null ? ViewStates.Gone : ViewStates.Visible;
            _titleText.Text = title ?? string.Empty;
            subtitle = Caption(subtitle);
            _titleSub.Text = subtitle ?? string.Empty;
            _titleSub.Visibility = string.IsNullOrEmpty(subtitle) ? ViewStates.Gone : ViewStates.Visible;
            _titleBack.Visibility = back == null ? ViewStates.Invisible : ViewStates.Visible;
            if (inGame)
            {
                HighlightDock(tab ?? string.Empty);
                RenderHud();
            }

            art = InkBackdrop(art);
            if (art != 0)
            {
                _backdrop.SetImageBitmap(Ui.Art(this, art));
                _backdrop.Visibility = ViewStates.Visible;
                _backdrop.Alpha = 0.6f;
                _backdrop.Animate().Alpha(1f).SetDuration(300).Start();
            }
            else
            {
                _backdrop.Visibility = ViewStates.Invisible;
            }

            SetStatus(string.Empty);
            _host.RemoveAllViews();
            var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _host.AddView(page, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _ui.Content = page;
            page.Alpha = 0f;
            page.TranslationY = Px(12);
            page.Animate().Alpha(1f).TranslationY(0).SetDuration(220).Start();
            var name = title ?? tab ?? "screen";
            var gen = _screen;
            page.PostDelayed(() =>
            {
                if (gen == _screen) AuditLayout(page, name);
            }, 900);
            return _ui;
        }

        /// <summary>
        /// Self-check of the no-scroll rule: logs (tag JianghuLayout) every visible view that sticks out of the
        /// screen page or text that is cut vertically. The emulator smoke test fails on any such line.
        /// </summary>
        private void AuditLayout(ViewGroup page, string name)
        {
            var origin = new int[2];
            page.GetLocationOnScreen(origin);
            var bottom = origin[1] + page.Height;
            var right = origin[0] + page.Width;
            var problems = 0;

            void Visit(View v, string path)
            {
                if (v.Visibility != ViewStates.Visible || v.Width == 0 || v.Height == 0) return;
                var at = new int[2];
                v.GetLocationOnScreen(at);
                var label = v is TextView tv ? "'" + tv.Text + "'" : v.GetType().Name;
                if (at[1] + v.Height > bottom + Px(2) || at[0] + v.Width > right + Px(2) || at[0] < origin[0] - Px(2))
                {
                    problems++;
                    global::Android.Util.Log.Warn("JianghuLayout", "OVERFLOW screen=" + name + " view=" + label + " bottom=" + (at[1] + v.Height) + " limit=" + bottom);
                }
                else if (v is TextView t && !(v is EditText) && t.Layout != null)
                {
                    var lines = Math.Min(t.LineCount, t.MaxLines);
                    var needed = t.Layout.GetLineBottom(Math.Max(0, lines - 1)) + t.CompoundPaddingTop + t.CompoundPaddingBottom;
                    if (needed > t.Height + Px(3))
                    {
                        problems++;
                        global::Android.Util.Log.Warn("JianghuLayout", "CLIPPED screen=" + name + " text=" + label + " needs=" + needed + " has=" + t.Height);
                    }
                }

                if (v is ViewGroup g)
                    for (var i = 0; i < g.ChildCount; i++)
                        Visit(g.GetChildAt(i), path);
            }

            Visit(page, name);
            global::Android.Util.Log.Info("JianghuLayout", "AUDIT screen=" + name + " problems=" + problems);
        }

        /// <summary>A waiting screen (key art and one line).</summary>
        private void ShowSplash(string text)
        {
            var ui = NewScreen(null, art: Resource.Drawable.key_art, chrome: false);
            ui.Box(ui.Content, 1f);
            var t = ui.Text("江湖行", ui.Content, 40, Ui.Gold, bold: true, center: true);
            ui.Text(text, ui.Content, 15, Ui.Ink, center: true, maxLines: 2);
            ui.Box(ui.Content, 0.4f);
        }

        /// <summary>Shows a message ribbon; "…" (work in progress) keeps it hidden. Messages fade after a while.</summary>
        private void SetStatus(string text)
        {
            _status.Text = text;
            var visible = !string.IsNullOrEmpty(text) && text != "…";
            _status.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
            if (!visible) return;
            _status.Alpha = 0f;
            _status.TranslationY = -Px(10);
            _status.Animate().Alpha(1f).TranslationY(0).SetDuration(180).Start();
            var shown = text;
            _status.PostDelayed(() =>
            {
                if (_status.Text == shown) _status.Animate().Alpha(0f).SetDuration(250).WithEndAction(new Java.Lang.Runnable(() =>
                {
                    if (_status.Text == shown) _status.Visibility = ViewStates.Gone;
                })).Start();
            }, 5000);
        }

        /// <summary>A vertical cell sharing a row equally with its siblings.</summary>
        private LinearLayout Cell(LinearLayout row)
        {
            var cell = new LinearLayout(this) { Orientation = Orientation.Vertical };
            Ui.AddCell(row, cell, _ui.Dp(8));
            return cell;
        }

        // ---- sheets & overlays --------------------------------------------------------------------------------

        private void PushOverlay(View view, System.Action close) => _overlays.Add((view, close));

        private void PopOverlay(View view) => _overlays.RemoveAll(o => o.View == view);

        private bool CloseTopOverlay()
        {
            if (_overlays.Count == 0) return false;
            _overlays[_overlays.Count - 1].Close();
            return true;
        }

        private void CloseAllOverlays()
        {
            foreach (var o in _overlays.ToList()) o.Close();
            _overlays.Clear();
        }

        /// <summary>
        /// A sheet sliding up from the bottom, <paramref name="heightFraction"/> of the screen tall, with a title and
        /// a close button. The body is a fixed column (use weights / pages inside, never scrolling).
        /// </summary>
        private LinearLayout Sheet(string title, float heightFraction, out System.Action close, string icon = null)
        {
            var overlay = new FrameLayout(this) { Clickable = true };
            overlay.SetBackgroundColor(Color.Argb(150, 0, 0, 0));
            var panel = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
            var bg = new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Argb(252, 244, 240, 231).ToArgb(), Color.Argb(252, 244, 240, 231).ToArgb() });
            bg.SetCornerRadii(new float[] { Px(18), Px(18), Px(18), Px(18), 0, 0, 0, 0 });
            bg.SetStroke(Px(1), Ui.Gold);
            panel.Background = bg;
            panel.SetPadding(Px(14), Px(8), Px(14), Px(14));
            var height = (int)(Resources.DisplayMetrics.HeightPixels * heightFraction);
            overlay.AddView(panel, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, height, GravityFlags.Bottom));

            var grip = new View(this);
            var gripBg = new GradientDrawable();
            gripBg.SetColor(Ui.GoldDim);
            gripBg.SetCornerRadius(Px(2));
            grip.Background = gripBg;
            panel.AddView(grip, new LinearLayout.LayoutParams(Px(44), Px(4)) { Gravity = GravityFlags.CenterHorizontal, BottomMargin = Px(6) });
            var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            head.SetGravity(GravityFlags.CenterVertical);
            panel.AddView(head, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(40)));
            if (icon != null) _ui.Icon(head, icon, 30);
            var t = new TextView(this) { Text = title };
            t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 17);
            t.SetTypeface(Ui.Display, TypefaceStyle.Bold);
            t.SetTextColor(Ui.Gold);
            t.SetMaxLines(1);
            t.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            head.AddView(t, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8) });
            var x = new TextView(this) { Text = "✕", Gravity = GravityFlags.Center, Clickable = true };
            x.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 18);
            x.SetTextColor(Ui.Gold);
            head.AddView(x, new LinearLayout.LayoutParams(Px(40), Px(40)));
            var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
            panel.AddView(body, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

            var closed = false;
            System.Action doClose = null;
            doClose = () =>
            {
                if (closed) return;
                closed = true;
                PopOverlay(overlay);
                panel.Animate().TranslationY(height).SetDuration(180).Start();
                overlay.Animate().Alpha(0f).SetDuration(200).WithEndAction(new Java.Lang.Runnable(() => _frame.RemoveView(overlay))).Start();
            };
            close = doClose;
            x.Click += (_, __) => doClose();
            overlay.Click += (_, __) => doClose();
            _frame.AddView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            PushOverlay(overlay, doClose);
            overlay.Alpha = 0f;
            panel.TranslationY = height;
            overlay.Animate().Alpha(1f).SetDuration(160).Start();
            panel.Animate().TranslationY(0).SetDuration(240).SetInterpolator(new global::Android.Views.Animations.DecelerateInterpolator()).Start();
            Sfx.Play(Sfx.Click, 0.4f);
            return body;
        }
    }
}
