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
    // 1.9: friends, direct messages and letters with gifts. The server decides limits and fees; the screen only asks.
    public sealed partial class MainActivity
    {
        private async Task ShowFriendsAsync()
        {
            var c = _me;
            var f = await _api.GetFriendsAsync(c.Id, Ct);
            var ui = NewScreen("Przyjaciele", () => Run(ShowInboxAsync), Resource.Drawable.place_altar, "ZNAJOMI · ROZMOWY · POCZTA");
            var page = ui.Content;
            var row = ui.HRow(page, 46, 6);
            var name = ui.Field("Imię kultywatora…", parent: row);
            name.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
            ui.Btn("Dodaj", () => Run(async () =>
            {
                var n = name.Text?.Trim();
                if (string.IsNullOrEmpty(n)) return;
                await _api.AddFriendAsync(c.Id, n, Ct);
                await ShowFriendsAsync();
                SetStatus("Wysłano zaproszenie: " + n);
            }), row, sp: 13, heightDp: 46).LayoutParameters = new LinearLayout.LayoutParams(Px(92), ViewGroup.LayoutParams.MatchParent) { LeftMargin = Px(6) };

            ui.Btn(f.UnreadMail > 0 ? "✉ Poczta (" + f.UnreadMail + " z darami)" : "✉ Poczta", () => Run(ShowMailAsync), page, secondary: f.UnreadMail == 0, sp: 13, heightDp: 38);

            var entries = f.Incoming.Concat(f.Friends).Concat(f.Outgoing).ToList();
            ui.Pages(page, entries.Count, 1, 4, (i, cell) =>
            {
                var e = entries[i];
                var card = ui.Card(cell, 1f, 8, e.Incoming ? Ui.Gold : e.Unread > 0 ? Ui.Jade : (Color?)null, 0);
                var state = e.Incoming ? "zaprasza cię" : e.Status == "pending" ? "czeka na odpowiedź" : e.Unread > 0 ? e.Unread + " nowych wiadomości" : "ścieżka " + PathName(e.Path);
                ui.Text(e.Name, card, 15, e.Incoming ? Ui.Gold : Ui.Ink, bold: true);
                ui.Text(state, card, 11, Ui.Muted);
                var actions = ui.HRow(card, 34, 0);
                if (e.Incoming)
                {
                    ui.Btn("Przyjmij", () => Run(async () => { await _api.AcceptFriendAsync(c.Id, e.CharacterId, Ct); await ShowFriendsAsync(); }), actions, sp: 12, heightDp: 34);
                    ui.Btn("Odrzuć", () => Run(async () => { await _api.RemoveFriendAsync(c.Id, e.CharacterId, Ct); await ShowFriendsAsync(); }), actions, secondary: true, sp: 12, heightDp: 34);
                }
                else if (e.Status == "accepted")
                {
                    ui.Btn("Rozmowa", () => OpenConversation(e), actions, sp: 12, heightDp: 34);
                    ui.Btn("List / dar", () => Run(() => SendLetterAsync(e)), actions, secondary: true, sp: 12, heightDp: 34);
                    Button remove = null;
                    remove = ui.Btn("Usuń", () =>
                    {
                        // Second tap confirms.
                        if (remove.Text != "Na pewno?") { remove.Text = "Na pewno?"; return; }
                        Run(async () =>
                        {
                            await _api.RemoveFriendAsync(c.Id, e.CharacterId, Ct);
                            await ShowFriendsAsync();
                        });
                    }, actions, secondary: true, sp: 12, heightDp: 34);
                }
                else
                {
                    ui.Btn("Wycofaj", () => Run(async () => { await _api.RemoveFriendAsync(c.Id, e.CharacterId, Ct); await ShowFriendsAsync(); }), actions, secondary: true, sp: 12, heightDp: 34);
                }
            }, empty: "Nie masz jeszcze przyjaciół — wpisz imię kultywatora.");
        }

        /// <summary>Conversation with a friend in a sheet (newest at the bottom), polled while open.</summary>
        private void OpenConversation(FriendDto friend)
        {
            var c = _me;
            var body = Sheet(friend.Name, 0.72f, out var close, "menu_chat");
            var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
            list.SetGravity(GravityFlags.Bottom);
            body.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            var row = _ui.HRow(body, 46, 6);
            var field = _ui.Field("Napisz…", parent: row);
            field.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
            field.SetFilters(new global::Android.Text.IInputFilter[] { new global::Android.Text.InputFilterLengthFilter(300) });
            var lines = new List<DirectMessageDto>();
            const int MaxShown = 9;

            void Render()
            {
                list.RemoveAllViews();
                if (lines.Count == 0) _ui.Text("Napisz pierwszy!", list, 12, Ui.Muted, center: true);
                foreach (var m in lines.Skip(Math.Max(0, lines.Count - MaxShown)))
                {
                    var t = _ui.Text(m.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm") + "\n" + m.Text, list, 12, m.Mine ? Ui.Gold : Ui.Ink, maxLines: 3);
                    var bg = new global::Android.Graphics.Drawables.GradientDrawable();
                    bg.SetColor(m.Mine ? Color.Argb(60, 200, 160, 80) : Color.Argb(110, 20, 24, 28));
                    bg.SetCornerRadius(Px(10));
                    t.Background = bg;
                    t.Gravity = m.Mine ? GravityFlags.End : GravityFlags.Start;
                    t.SetPadding(Px(8), Px(4), Px(8), Px(4));
                    ((LinearLayout.LayoutParams)t.LayoutParameters).TopMargin = Px(4);
                }
            }

            async Task Pull()
            {
                var page = await _api.GetConversationAsync(c.Id, friend.CharacterId, Ct);
                lines.Clear();
                lines.AddRange(page.Items);
                Render();
            }

            _ui.Btn("Wyślij", () => Run(async () =>
            {
                var text = field.Text?.Trim();
                if (string.IsNullOrEmpty(text)) return;
                await _api.SendDirectMessageAsync(c.Id, friend.CharacterId, text, Ct);
                field.Text = string.Empty;
                Sfx.Play(Sfx.Click, 0.5f);
                await Pull();
            }), row, sp: 13, heightDp: 46).LayoutParameters = new LinearLayout.LayoutParams(Px(92), ViewGroup.LayoutParams.MatchParent) { LeftMargin = Px(6) };

            Render();
            async void Poll()
            {
                for (var wait = 0; wait < 20 && !body.IsAttachedToWindow; wait++) await Task.Delay(100);
                while (body.IsAttachedToWindow)
                {
                    try
                    {
                        if (!_paused) await Pull();
                        await Task.Delay(TimeSpan.FromSeconds(5), Ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                    catch (ApiException)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }
            }

            Poll();
        }

        /// <summary>A letter to a friend: text, stones (+5% courier fee) and optionally one kind of item from the bag.</summary>
        private async Task SendLetterAsync(FriendDto friend)
        {
            var c = _me;
            var inv = await _api.GetInventoryAsync(c.Id, Ct);
            var body = Sheet("List do: " + friend.Name, 0.8f, out var close, "menu_notifications");
            var text = _ui.Field("Treść (opcjonalnie)…", parent: body);
            text.SetFilters(new global::Android.Text.IInputFilter[] { new global::Android.Text.InputFilterLengthFilter(300) });
            var stones = _ui.Field("Kamienie duchowe (masz " + inv.Balance + ")", number: true, parent: body);
            _ui.Text("Kurier pobiera 5% od wysłanych kamieni.", body, 11, Ui.Muted);
            string chosen = null;
            var qty = _ui.Field("Ilość przedmiotu", number: true, parent: body);
            var picked = _ui.Text("Bez przedmiotu", body, 12, Ui.Ink);
            var stacks = inv.Stacks.Where(s => s.Quantity > 0).ToList();
            _ui.Pages(body, stacks.Count, 2, 2, (i, cell) =>
            {
                var s = stacks[i];
                _ui.Btn(s.NamePl + " ×" + s.Quantity, () =>
                {
                    chosen = chosen == s.ItemId ? null : s.ItemId;
                    picked.Text = chosen == null ? "Bez przedmiotu" : "Dar: " + s.NamePl;
                    if (chosen != null && string.IsNullOrEmpty(qty.Text)) qty.Text = "1";
                }, cell, secondary: true, sp: 11, heightDp: 36);
            }, empty: "Sakwa pusta.");
            _ui.Btn("Wyślij list", () => Run(async () =>
            {
                long.TryParse(stones.Text, out var st);
                long.TryParse(qty.Text, out var q);
                await _api.SendMailAsync(c.Id, friend.CharacterId, text.Text?.Trim() ?? string.Empty, Math.Max(0, st), chosen, chosen == null ? 0 : Math.Max(1, q), Ct);
                close();
                Sfx.Play(Sfx.Click, 0.6f);
                SetStatus("List wysłany do " + friend.Name + ".");
            }), body, sp: 14, heightDp: 44);
        }

        private async Task ShowMailAsync()
        {
            var c = _me;
            var mail = (await _api.GetMailAsync(c.Id, Ct)).Items;
            var ui = NewScreen("Poczta", () => Run(ShowFriendsAsync), Resource.Drawable.place_altar, "LISTY I DARY");
            ui.Pages(ui.Content, mail.Count, 1, 4, (i, cell) =>
            {
                var m = mail[i];
                var gift = m.Stones > 0 || m.ItemId != null;
                var card = ui.Card(cell, 1f, 8, gift && !m.Claimed ? Ui.Gold : (Color?)null, 0);
                ui.Text(m.FromName + " · " + m.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"), card, 13, Ui.Ink, bold: true);
                if (!string.IsNullOrEmpty(m.Text)) ui.Text(m.Text, card, 12, Ui.Ink, maxLines: 3);
                if (gift)
                {
                    var what = string.Join(" + ", new[] { m.Stones > 0 ? m.Stones + " KD" : null, m.ItemId != null ? m.Quantity + "× " + m.ItemNamePl : null }.Where(x => x != null));
                    ui.Text((m.Claimed ? "Odebrano: " : "Dar: ") + what, card, 12, m.Claimed ? Ui.Muted : Ui.Gold);
                    if (!m.Claimed)
                        ui.Btn("Odbierz", () => Run(async () =>
                        {
                            await _api.ClaimMailAsync(c.Id, m.Id, Ct);
                            Sfx.Play(Sfx.Breakthrough, 0.4f);
                            await ShowMailAsync();
                            SetStatus("Odebrano: " + what);
                        }), card, sp: 12, heightDp: 34);
                }
            }, empty: "Brak listów.");
        }
    }
}
