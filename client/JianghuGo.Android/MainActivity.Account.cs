using System;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>Title screen (offline play / server sign-in in a sheet) and the three-step character creation.</summary>
    public sealed partial class MainActivity
    {
        private void ShowLogin()
        {
            _me = null;
            var ui = NewScreen(null, art: Resource.Drawable.key_art, chrome: false);
            var page = ui.Content;
            ui.Box(page, 1f);
            ui.Text("江湖行", page, 44, Ui.Gold, bold: true, center: true);
            ui.Text("JIANGHU GO", page, 22, Ui.Ink, bold: true, center: true);
            ui.Text("Świat wuxia na twoich ulicach — kultywuj Qi, zbieraj zioła, walcz z bestiami, zakładaj sekty.", page, 13, Ui.Ink, center: true, maxLines: 3);
            ui.Box(page, 0.3f);
            ui.Btn("☯  Graj offline", () => Run(PlayOfflineAsync), page, sp: 18, heightDp: 56);
            ui.Text("Świat działa w telefonie, postęp zapisuje się w aplikacji.", page, 11, Ui.Muted, center: true);
            ui.Btn("Gra z serwerem", () => ServerSheet(), page, secondary: true, sp: 14, heightDp: 46);
        }

        private void ServerSheet()
        {
            var ui = _ui;
            var body = Sheet("Gra z serwerem", 0.62f, out var close, "menu_settings");
            ui.Text("Adres serwera", body, 11, Ui.Muted);
            var server = ui.Field("http://192.168.1.10:8080", value: _prefs.GetString(ServerUrlKey, DefaultServer), parent: body);
            var email = ui.Field("E-mail", parent: body);
            var password = ui.Field("Hasło (co najmniej 10 znaków)", password: true, parent: body);
            ui.Box(body, 1f, marginDp: 0);

            bool Connect()
            {
                if (ConnectTo(server.Text)) return true;
                SetStatus("Podaj adres serwera, np. http://192.168.1.10:8080");
                return false;
            }

            var row = ui.HRow(body, 48);
            ui.Btn("Zaloguj", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.LoginAsync(email.Text.Trim(), password.Text, Ct);
                close();
                await ShowHomeAsync();
            }), row, sp: 15, heightDp: 48);
            ui.Btn("Załóż konto", () => Run(async () =>
            {
                if (!Connect()) return;
                await _api.RegisterAsync(email.Text.Trim(), password.Text, Ct);
                await _api.LoginAsync(email.Text.Trim(), password.Text, Ct);
                close();
                await ShowHomeAsync();
            }), row, secondary: true, sp: 15, heightDp: 48);
        }

        // ---- creation: 1 fate, 2 path, 3 name ------------------------------------------------------------------

        private async Task ShowCreationAsync()
        {
            var flow = new CharacterCreationFlow(_api);
            await flow.StartAsync(Ct);
            CreationStep(flow, 0, null, null);
        }

        private void CreationStep(CharacterCreationFlow flow, int step, string path, string name)
        {
            var ui = NewScreen("Narodziny wojownika", step == 0 ? null : (System.Action)(() => CreationStep(flow, step - 1, path, name)),
                Resource.Drawable.key_art, "KROK " + (step + 1) + " Z 3 · " + (step == 0 ? "LOS" : step == 1 ? "ŚCIEŻKA" : "IMIĘ"), chrome: false);
            var page = ui.Content;

            if (step == 0)
            {
                ui.Text("Zablokuj cechy, które chcesz zachować, i losuj ponownie. Każde losowanie kosztuje Żetony Losu.", page, 12, Ui.Muted, maxLines: 2);
                var traits = ui.Box(page, 1.3f);
                var stats = ui.Card(page, 1f, 8);
                var tokens = ui.Text(string.Empty, page, 12, Ui.Gold, bold: true, maxLines: 2);
                var row = ui.HRow(page, 50);
                ui.Btn("🎲 Losuj ponownie", () => Run(async () =>
                {
                    if (!await flow.RerollAsync(Ct)) SetStatus("Szkic zmienił się na innym urządzeniu — odświeżono.");
                    Sfx.Play(Sfx.Click);
                    Render();
                }), row, secondary: true, sp: 14, heightDp: 50);
                ui.Btn("Dalej ›", () => CreationStep(flow, 1, path, name), row, sp: 16, heightDp: 50);

                void Render()
                {
                    var d = flow.Draft;
                    traits.RemoveAllViews();
                    foreach (var t in d.Traits)
                    {
                        var trait = t;
                        var locked = flow.LockedCategories.Contains(trait.Category);
                        var rarity = Ui.RarityOf(trait.Rarity);
                        var card = ui.Card(traits, 1f, 8, rarity.Color);
                        card.Clickable = true;
                        var r = ui.HRow(card, 40, 0);
                        var col = ui.Box(r, 1f, marginDp: 0);
                        ui.Text(trait.NamePl, col, 15, Ui.Ink, bold: true);
                        ui.Text(CategoryName(trait.Category) + " · " + rarity.Name, col, 11, rarity.Color);
                        var lockIcon = ui.Text(locked ? "🔒" : "🔓", r, 20);
                        lockIcon.LayoutParameters = new LinearLayout.LayoutParams(Px(36), ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.CenterVertical };
                        lockIcon.Alpha = locked ? 1f : 0.45f;
                        card.Click += (_, __) =>
                        {
                            if (!flow.ToggleLock(trait.Category)) SetStatus("Możesz zablokować najwyżej " + flow.Draft.MaxLockedTraits + " cechy.");
                            Sfx.Play(Sfx.Click, 0.5f);
                            Render();
                        };
                    }

                    stats.RemoveAllViews();
                    LinearLayout line = null;
                    var k = 0;
                    foreach (var st in d.Stats)
                    {
                        if (k++ % 3 == 0) line = ui.Box(stats, 1f, horizontal: true, marginDp: 2);
                        var cell = ui.Box(line, 1f, horizontal: true, marginDp: 4);
                        ui.Icon(cell, "icon_" + st.Id, 18);
                        var n = StatName(st.Id);
                        ui.Text((n.Length > 9 ? n.Substring(0, 8) + "." : n) + " " + st.Value, cell, 12, Ui.Ink, bold: true)
                            .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(4), Gravity = GravityFlags.CenterVertical };
                    }

                    tokens.Text = "Żetony Losu: " + d.DestinyTokens + "   ·   koszt: " + flow.NextRerollCost + "   ·   pozostało: " + d.RerollsRemaining;
                }

                Render();
            }
            else if (step == 1)
            {
                ui.Text("Wybierz ścieżkę kultywacji. Zdecyduje, jak rośnie twoja moc.", page, 12, Ui.Muted, maxLines: 2);
                var paths = new[]
                {
                    ("qi", "Qi", "równowaga i technika", Resource.Drawable.place_qiarray, Ui.QiBlue),
                    ("body", "Ciało", "siła i kroki z telefonu", Resource.Drawable.loc_fortress, Color.Rgb(196, 140, 80)),
                    ("flame", "Płomień", "moc 12 miejsc Płomienia", Resource.Drawable.place_brazier, Ui.Violet),
                    ("poison", "Trucizna", "podstęp, nocą silniejsza", Resource.Drawable.place_rift, Ui.Jade),
                };
                for (var i = 0; i < 4; i += 2)
                {
                    var row = ui.Box(page, 1f, horizontal: true);
                    for (var j = i; j < i + 2; j++)
                    {
                        var p = paths[j];
                        var cell = ui.Box(row, 1f, marginDp: 8);
                        ui.TileIn(cell, p.Item2, p.Item3 + (path == p.Item1 ? "  ✓" : string.Empty), p.Item4, () => CreationStep(flow, 2, p.Item1, name), accent: path == p.Item1 ? Ui.Gold : p.Item5, titleSp: 18);
                    }
                }
            }
            else
            {
                var summary = ui.Card(page, 1f, 12, Ui.Gold);
                ui.Text("Ścieżka: " + PathName(path), summary, 16, Ui.Gold, bold: true);
                ui.Text(string.Join(" · ", flow.Draft.Traits.Select(t => t.NamePl)), summary, 13, Ui.Ink, maxLines: 3);
                ui.Text(string.Join("  ", flow.Draft.Stats.Select(s => StatName(s.Id) + " " + s.Value)), summary, 11, Ui.Muted, maxLines: 4);
                ui.Text("Imię", page, 12, Ui.Muted);
                var field = ui.Field("Imię postaci (2–24 litery)", value: name, parent: page);
                ui.Btn("Rozpocznij drogę", () => Run(async () =>
                {
                    var c = await flow.FinalizeAsync(field.Text.Trim(), path, Ct);
                    Sfx.Play(Sfx.Breakthrough);
                    EnterGame(c);
                }), page, sp: 18, heightDp: 56);
            }
        }
    }
}
