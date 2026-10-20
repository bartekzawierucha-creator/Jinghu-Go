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
    /// <summary>Bag (paged item grid with an action sheet), forge and alchemy (recipe cards), market (paged offers).</summary>
    public sealed partial class MainActivity
    {
        private sealed class BagEntry
        {
            public StackDto Stack;
            public ItemInstanceDto Item;
            public string ItemId => Stack?.ItemId ?? Item.ItemId;
            public string Name => Stack?.NamePl ?? Item.NamePl;
        }

        private async Task ShowBagAsync(int tab)
        {
            var c = _me;
            var economy = new EconomyFlow(_api, c.Id);
            await economy.LoadAsync(Ct);
            if (tab == 0) await economy.LoadMerchantAsync(Ct);
            var page1 = tab == 2 ? await economy.BrowseAsync(null, null, Ct) : null;
            var mine = tab == 2 ? await economy.MyListingsAsync(Ct) : null;
            ShowBag(economy, tab, page1, mine, 0);
        }

        private void ShowBag(EconomyFlow economy, int tab, ListingPageDto offers, List<MyListingDto> mine, int startPage, bool marketMine = false)
        {
            var c = _me;
            var ui = NewScreen("Sakwa", BackToTab, Resource.Drawable.loc_village, "EKWIPUNEK · KUŹNIA · TARG");
            var page = ui.Content;
            var inv = economy.Inventory;
            _balance = inv.Balance;
            RenderHud();
            ui.Tabs(page, new[] { "Sakwa", "Kuźnia", "Targ" }, tab, i => Run(() => ShowBagAsync(i)));
            var head = ui.HRow(page, 26, 4);
            ui.Icon(head, "icon_stones", 22);
            ui.Text(inv.Balance.ToString("N0") + " Kamieni Duchowych", head, 13, Ui.Gold, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(6), Gravity = GravityFlags.CenterVertical };
            ui.Text("Miejsca " + inv.SlotsUsed + "/" + inv.Capacity, head, 12, Ui.Muted).Gravity = GravityFlags.End;
            if (tab == 0 && inv.Talisman != null)
                ui.Text("🔥 " + inv.Talisman.NamePl + " płonie do " + inv.Talisman.Until.ToLocalTime().ToString("HH:mm"), page, 11, Ui.Jade, center: true);

            if (tab == 0 && economy.Merchant != null)
                ui.Btn("🏮 Wędrowny kupiec", () => MerchantSheet(economy), page, secondary: true, sp: 13, heightDp: 40);
            if (tab == 0) BagGrid(economy, ui, page, startPage);
            else if (tab == 1) Forge(economy, ui, page, startPage);
            else Market(economy, ui, page, offers, mine, marketMine);
        }

        // ---- Sakwa --------------------------------------------------------------------------------------------

        private void BagGrid(EconomyFlow economy, Ui ui, LinearLayout page, int startPage)
        {
            var inv = economy.Inventory;
            var entries = inv.Items.Where(i => i.IsEquipped).Select(i => new BagEntry { Item = i })
                .Concat(inv.Stacks.Select(s => new BagEntry { Stack = s }))
                .Concat(inv.Items.Where(i => !i.IsEquipped).Select(i => new BagEntry { Item = i })).ToList();
            Pager pager = null;
            pager = ui.Pages(page, entries.Count, 4, 4, (i, cell) =>
            {
                var e = entries[i];
                var rarity = Ui.RarityOf(e.Stack?.Rarity ?? e.Item.Rarity).Color;
                var box = new FrameLayout(this) { Clickable = true };
                box.Background = new FrameDrawable(Resources.DisplayMetrics.Density, Color.Argb(235, 14, 18, 22), e.Item?.IsEquipped == true ? Ui.Gold : rarity, rarity);
                var img = new ImageView(this);
                var art = ui.ArtId("item_" + e.ItemId);
                img.SetImageBitmap(Ui.Art(this, art != 0 ? art : Resource.Drawable.item_slot_empty));
                img.SetScaleType(ImageView.ScaleType.FitCenter);
                box.AddView(img, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) { LeftMargin = Px(8), RightMargin = Px(8), TopMargin = Px(6), BottomMargin = Px(16) });
                var label = new TextView(this) { Text = e.Name, Gravity = GravityFlags.Center };
                label.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 9);
                label.SetTextColor(Ui.Ink);
                label.SetMaxLines(1);
                label.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
                label.SetShadowLayer(3, 0, 1, Color.Black);
                box.AddView(label, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom) { BottomMargin = Px(3) });
                var tag = e.Stack != null ? "×" + e.Stack.Quantity : e.Item.IsEquipped ? "✓" : e.Item.State == "listed" ? "targ" : e.Item.UpgradeLevel > 0 ? "+" + e.Item.UpgradeLevel : null;
                if (tag != null)
                {
                    var b = ui.Badge(tag);
                    if (e.Item?.IsEquipped == true) ((GradientDrawable)b.Background).SetColor(Ui.Jade);
                    box.AddView(b, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.End) { TopMargin = Px(3), RightMargin = Px(3) });
                }

                box.Click += (_, __) =>
                {
                    Sfx.Play(Sfx.Click, 0.5f);
                    ItemSheet(economy, e, pager.Page);
                };
                cell.AddView(box, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }, empty: "Sakwa jest pusta — zbieraj zioła i rudę w świecie.", startPage: startPage);
            if (inv.Bonuses.Count > 0)
                ui.Text("Premie z ekwipunku: " + string.Join("  ", inv.Bonuses.Select(b => StatName(b.Key) + " +" + b.Value)), page, 11, Ui.Gold, maxLines: 2);
        }

        private void ItemSheet(EconomyFlow economy, BagEntry e, int returnPage)
        {
            var ui = _ui;
            var art = ui.ArtId("item_" + e.ItemId) != 0 ? "item_" + e.ItemId : "item_slot_empty";
            var body = Sheet(e.Name + (e.Item?.UpgradeLevel > 0 ? " +" + e.Item.UpgradeLevel : string.Empty), 0.56f, out var close, art);
            var rarity = Ui.RarityOf(e.Stack?.Rarity ?? e.Item.Rarity);
            ui.Text(rarity.Name + (e.Stack != null ? "  ·  w sakwie: " + e.Stack.Quantity : e.Item.IsEquipped ? "  ·  założony" : e.Item.State == "listed" ? "  ·  na targu" : string.Empty), body, 12, rarity.Color, bold: true);
            if (e.Item != null && e.Item.Stats.Count > 0)
                ui.Text((e.Item.QualityPl != null ? "Jakość: " + e.Item.QualityPl + "  ·  " : string.Empty) + string.Join("  ·  ", e.Item.Stats.Select(x => StatName(x.Key) + " +" + x.Value)), body, 13, Ui.Ink, maxLines: 2);
            if (e.Item?.Special != null)
                ui.Text("Właściwość szczególna „" + e.Item.Special.NamePl + "”: " + PerksText(new Dictionary<string, int> { [e.Item.Special.Perk] = e.Item.Special.Value }), body, 12, Ui.Gold, maxLines: 2);
            if (e.Item != null && e.Item.Affixes != null && e.Item.Affixes.Count > 0)
                ui.Text("Właściwości: " + string.Join("  ·  ", e.Item.Affixes.Select(a => a.NamePl + " (" + StatName(a.Stat) + " +" + a.Value + ")")), body, 12, Ui.Violet, maxLines: 2);
            if (e.Item?.SetNamePl != null)
            {
                var set = economy.Inventory.Sets?.FirstOrDefault(s => s.Id == e.Item.SetId);
                ui.Text("Zestaw: " + e.Item.SetNamePl + (set != null ? " (" + set.Worn + "/" + set.Pieces + ")  " + string.Join("  ", set.Bonuses.Select(b => b.Pieces + " cz.: " + StatsText(b.Stats, ", "))) : string.Empty),
                    body, 11, Ui.Jade, maxLines: 2);
            }

            if (e.Item?.BoundRef != null)
            {
                var scrollText = ui.Text("Zwój techniki (przeczytaj w Bohater → Dao)", body, 12, Ui.Muted, maxLines: 4);
                var boundRef = e.Item.BoundRef;
                Run(async () =>
                {
                    var info = await _api.GetScrollInfoAsync(boundRef, Ct);
                    scrollText.Text = info.NamePl + " · " + info.RarityPl + "\n" + info.DescriptionPl + "\n(przeczytaj w Bohater → Dao albo rozbierz na Esencję Dao)";
                });
            }
            ui.Box(body, 1f, marginDp: 0);

            async Task Done(string message)
            {
                close();
                ShowBag(economy, 0, null, null, returnPage);
                if (message != null) SetStatus(message);
                RefreshHud();
            }

            var r1 = ui.HRow(body, 46);
            if (e.Stack != null)
            {
                if (e.Stack.Type == "consumable")
                    ui.Btn("Użyj", () => Run(async () =>
                    {
                        var r = await economy.UseAsync(e.Stack.ItemId, 1, Ct);
                        Sfx.Play(Sfx.Chime);
                        await Done(e.Stack.ItemId.StartsWith("talisman_") || e.Stack.ItemId.StartsWith("formation_")
                            ? e.Stack.NamePl + " płonie — premia w walkach przez kilka godzin (nowy talizman zastępuje poprzedni)."
                            : "Pigułka zażyta — Qi wpływa do naczynia (każda kolejna dziś działa o 25% słabiej).");
                    }), r1, sp: 14, heightDp: 46);
                ui.Btn("Wyrzuć 1", () => Run(async () =>
                {
                    await economy.DiscardAsync(e.Stack.ItemId, 1, Ct);
                    await Done(null);
                }), r1, secondary: true, sp: 13, heightDp: 46);
            }
            else if (e.Item.State != "listed")
            {
                if (e.Item.Slot != null)
                    ui.Btn(e.Item.IsEquipped ? "Zdejmij" : "Załóż", () => Run(async () =>
                    {
                        var ok = await economy.ToggleEquipAsync(e.Item, Ct);
                        Sfx.Play(Sfx.Chime, 0.6f);
                        await Done(ok ? null : "Przedmiot zmienił się na innym urządzeniu — odświeżono.");
                    }), r1, sp: 14, heightDp: 46);
                var next = economy.NextUpgrade(e.Item);
                if (next != null)
                    ui.Btn("⚒ +" + next.Level + " · " + next.Stones + " KD · " + next.SuccessPermille / 10 + "%", () => Run(async () =>
                    {
                        var r = await economy.UpgradeAsync(e.Item, Ct);
                        Sfx.Play(r != null && r.Success ? Sfx.Breakthrough : Sfx.Defeat, 0.6f);
                        await Done(r == null ? "Odświeżono." : r.Success ? "Udane! Teraz +" + r.Level : "Nieudane — poziom +" + r.Level + " zachowany.");
                    }), r1, secondary: true, sp: 12, heightDp: 46);
                if (!e.Item.IsEquipped)
                    ui.Btn(e.Item.Slot != null || e.Item.BoundRef != null ? "Rozbierz" : "Wyrzuć", () => Run(async () =>
                    {
                        if (e.Item.Slot != null || e.Item.BoundRef != null)
                        {
                            var r = await _api.DismantleAsync(_me.Id, e.Item, Ct);
                            await economy.LoadAsync(Ct);
                            Sfx.Play(Sfx.Coin, 0.6f);
                            await Done(r.Materials.Count == 0 ? "Rozebrano — nic się nie ostało." : "Odzyskano: " + string.Join(", ", r.Materials.Select(m => m.Quantity + "× " + m.NamePl)));
                            return;
                        }

                        await economy.DiscardAsync(e.Item, Ct);
                        await Done(null);
                    }), r1, secondary: true, sp: 13, heightDp: 46);
            }

            // sell to the travelling merchant (fixed price, instant)
            long merchantPrice = 0;
            if (economy.Merchant != null && (e.Stack != null || e.Item.InBag) && economy.Merchant.SellPrices.TryGetValue(e.ItemId, out merchantPrice))
            {
                var price = e.Item != null ? merchantPrice * (10 + e.Item.UpgradeLevel) / 10 : merchantPrice;
                var rm = ui.HRow(body, 46);
                ui.Btn("Kupcowi: 1 za " + price + " KD", () => Run(async () =>
                {
                    var r = e.Stack != null ? await _api.SellToMerchantAsync(_me.Id, e.Stack.ItemId, 1, Ct) : await _api.SellToMerchantAsync(_me.Id, e.Item, Ct);
                    await economy.ReloadAsync(Ct);
                    Sfx.Play(Sfx.Coin, 0.7f);
                    await Done("Sprzedano kupcowi: +" + r.Stones + " KD");
                }), rm, secondary: true, sp: 12, heightDp: 46);
                if (e.Stack != null && e.Stack.Quantity > 1)
                {
                    var all = (int)Math.Min(e.Stack.Quantity, economy.Merchant.MaxPerTrade);
                    ui.Btn("Wszystko (" + all + ") za " + (price * all) + " KD", () => Run(async () =>
                    {
                        var r = await _api.SellToMerchantAsync(_me.Id, e.Stack.ItemId, all, Ct);
                        await economy.ReloadAsync(Ct);
                        Sfx.Play(Sfx.Coin, 0.7f);
                        await Done("Sprzedano kupcowi: +" + r.Stones + " KD");
                    }), rm, secondary: true, sp: 12, heightDp: 46);
                }
            }

            // sell on the market (tradable stacks and items in the bag)
            if (e.Stack != null || e.Item.InBag)
            {
                var r2 = ui.HRow(body, 56);
                var priceBox = ui.Box(r2, 1.3f, marginDp: 0);
                var price = ui.Field("Cena (KD)", number: true, parent: priceBox);
                ui.Btn("Wystaw na targ", () => Run(async () =>
                {
                    var rules = economy.MarketRules ?? await _api.GetMarketRulesAsync(Ct);
                    if (!TryPrice(price.Text, rules, out var p)) return;
                    if (e.Stack != null) await economy.SellAsync(e.Stack.ItemId, e.Stack.Quantity, p, Ct);
                    else await economy.SellAsync(e.Item, p, Ct);
                    Sfx.Play(Sfx.Coin);
                    await Done("Wystawiono na targu.");
                }), r2, secondary: true, sp: 12, heightDp: 46);
            }
        }

        // ---- Wędrowny kupiec (1.6) ----------------------------------------------------------------------------

        private void MerchantSheet(EconomyFlow economy)
        {
            var ui = _ui;
            var m = economy.Merchant;
            var body = Sheet("Wędrowny kupiec", 0.7f, out var close, "icon_stones");
            ui.Text("Skupuje przedmioty za " + m.SellPermille / 10 + "% ich wartości (stuknij przedmiot w sakwie). Sprzedaje podstawowe towary:", body, 12, Ui.Muted, maxLines: 3);
            ui.Pages(body, m.Stock.Count, 1, 4, (i, cell) =>
            {
                var o = m.Stock[i];
                var row = ui.HRow(cell, 44, 0);
                ui.Icon(row, ui.ArtId("item_" + o.ItemId) != 0 ? "item_" + o.ItemId : "item_slot_empty", 36);
                var col = ui.Box(row, 1f, marginDp: 6);
                ui.Text(o.NamePl, col, 13, Ui.RarityOf(o.Rarity).Color, bold: true);
                ui.Text(o.Price + " KD / szt.", col, 11, Ui.Gold);
                foreach (var n in new[] { 1, 10 })
                {
                    var qty = n;
                    ui.Btn("×" + qty, () => Run(async () =>
                    {
                        var r = await _api.BuyFromMerchantAsync(_me.Id, o.ItemId, qty, Ct);
                        _balance = r.Balance;
                        await economy.ReloadAsync(Ct);
                        await economy.LoadMerchantAsync(Ct);
                        Sfx.Play(Sfx.Coin, 0.6f);
                        RefreshHud();
                        SetStatus("Kupiono " + qty + "× " + o.NamePl + " (" + (-r.Stones) + " KD)");
                    }), row, secondary: true, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(60), Px(38)) { Gravity = GravityFlags.CenterVertical, LeftMargin = Px(4) };
                }
            });
            ui.Btn("Zamknij", () => { close(); ShowBag(economy, 0, null, null, 0); }, body, secondary: true, sp: 13, heightDp: 42);
        }

        // ---- Kuźnia i alchemia --------------------------------------------------------------------------------

        private void Forge(EconomyFlow economy, Ui ui, LinearLayout page, int startPage)
        {
            var inv = economy.Inventory;
            var names = new Dictionary<string, string>(economy.Recipes.Names ?? new Dictionary<string, string>());
            foreach (var st in inv.Stacks) names[st.ItemId] = st.NamePl;
            foreach (var it in inv.Items) names[it.ItemId] = it.NamePl;
            string N(string id) => names.TryGetValue(id, out var n) ? n : Pretty(id);
            var recipes = economy.Recipes.Items.OrderByDescending(r => economy.CanAfford(r, 1)).ThenByDescending(r => economy.LevelAllows(r)).ThenBy(r => r.Stones).ToList();
            var profRow = ui.HRow(page, 30, 2);
            foreach (var (id, label, icon) in new[] { ("alchemy", "Alchemia", "menu_daolab"), ("smithing", "Kowalstwo", "menu_forge") })
            {
                var p = economy.Profession(id);
                var cell = ui.Box(profRow, 1f, horizontal: true, marginDp: 4);
                ui.Icon(cell, icon, 24);
                var next = p?.XpForNextLevel is long n ? " · " + p.Xp + "/" + n + " XP" : " · max";
                ui.Text(label + " " + (p?.Level ?? 1) + (p == null ? string.Empty : next), cell, 12, Ui.Gold, bold: true)
                    .LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(4), Gravity = GravityFlags.CenterVertical };
            }

            CraftQueueRow(economy, ui, page);
            Pager pager = null;
            pager = ui.Pages(page, recipes.Count, 1, 3, (i, cell) =>
            {
                var recipe = recipes[i];
                var can = economy.CanAfford(recipe, 1);
                var card = ui.Card(cell, 1f, 8, can ? Ui.Gold : (Color?)null, 0);
                card.SetGravity(GravityFlags.CenterVertical);
                var row = ui.HRow(card, 44, 0);
                ui.Icon(row, ui.ArtId("item_" + recipe.Output.Item) != 0 ? "item_" + recipe.Output.Item : "item_slot_empty", 40);
                var col = ui.Box(row, 1f, marginDp: 8);
                ui.Text(N(recipe.Output.Item) + (recipe.Output.Quantity > 1 ? " ×" + recipe.Output.Quantity : string.Empty), col, 13, can ? Ui.Gold : Ui.Ink, bold: true);
                var allowed = economy.LevelAllows(recipe);
                var bonus = economy.Profession(recipe.Profession)?.SuccessBonusPermille ?? 0;
                ui.Text((recipe.Kind == "alchemy" ? "Alchemia · " + Math.Min(100, (recipe.SuccessPermille + bonus) / 10) + "%+" : "Kuźnia") + " · " + economy.StonesFor(recipe) + " KD"
                        + (allowed ? string.Empty : " · wym. poz. " + recipe.MinLevel), col, 11, allowed ? Ui.Muted : Ui.Cinnabar);
                ui.Btn(can ? "Wytwórz" : allowed ? "Brak" : "🔒 " + recipe.MinLevel, () => Run(async () =>
                {
                    if (!allowed)
                    {
                        SetStatus((recipe.Profession == "alchemy" ? "Alchemia" : "Kowalstwo") + " musi mieć poziom " + recipe.MinLevel + " — wytwarzaj prostsze rzeczy, by zdobyć doświadczenie.");
                        return;
                    }

                    if (!economy.CanAfford(recipe, 1))
                    {
                        SetStatus("Brakuje składników lub kamieni.");
                        return;
                    }

                    var result = await economy.CraftAsync(recipe.Id, 1, Ct);
                    ShowBag(economy, 1, null, null, pager.Page);
                    if (result.Successes > 0)
                    {
                        Sfx.Play(Sfx.Chime);
                        ShowLoot("Wytworzono", 0, new List<(string, long)> { (result.OutputItem, recipe.Output.Quantity) }, null);
                    }
                    else
                    {
                        Sfx.Play(Sfx.Defeat, 0.6f);
                        SetStatus("Alchemia nie powiodła się — składniki przepadły.");
                    }

                    RefreshHud();
                }), row, secondary: !can, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(86), Px(38)) { Gravity = GravityFlags.CenterVertical };
                var batch = can && allowed && (recipe.Kind == "alchemy" || recipe.Output.Item == "refining_stone") ? QueueTimes(economy, recipe) : 0;
                if (batch >= 2)
                    ui.Btn("⏳×" + batch, () => Run(async () =>
                    {
                        await _api.QueueCraftAsync(_me.Id, recipe.Id, batch, Ct);
                        await economy.LoadAsync(Ct);
                        ShowBag(economy, 1, null, null, pager.Page);
                        Sfx.Play(Sfx.Chime, 0.6f);
                        SetStatus(batch + "× " + N(recipe.Output.Item) + " w kolejce — odbierz, gdy będzie gotowe.");
                        RefreshHud();
                    }), row, secondary: true, sp: 11, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(58), Px(38)) { Gravity = GravityFlags.CenterVertical, LeftMargin = Px(4) };
                var inputs = string.Join("   ", recipe.Inputs.Select(x => N(x.Item) + " " + inv.Count(x.Item) + "/" + x.Quantity));
                ui.Text(inputs, card, 11, can ? Ui.Jade : Ui.Cinnabar, maxLines: 1);
            }, empty: "Brak receptur.", startPage: startPage);
        }

        // ---- Targ ---------------------------------------------------------------------------------------------

        private string _marketQuery;

        /// <summary>Craft orders (2.3): ask for items with an escrowed reward, or deliver someone else's order.</summary>
        private async Task CraftOrdersSheetAsync(EconomyFlow economy)
        {
            var c = _me;
            var orders = (await _api.GetCraftOrdersAsync(c.Id, Ct)).Items;
            var body = Sheet("Zamówienia rzemieślnicze", 0.85f, out var close, "menu_market");
            var row = _ui.HRow(body, 42, 4);
            var item = _ui.Field("Przedmiot (nazwa)", parent: row);
            item.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.6f);
            var qty = _ui.Field("Ile", number: true, parent: row);
            qty.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 0.7f) { LeftMargin = Px(4) };
            var reward = _ui.Field("KD", number: true, parent: row);
            reward.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 0.9f) { LeftMargin = Px(4) };
            _ui.Btn("Złóż zamówienie", () => Run(async () =>
            {
                var term = item.Text?.Trim() ?? string.Empty;
                var found = await _api.SearchMarketAsync(term, "newest", 1, Ct);
                var id = found.Items.FirstOrDefault()?.ItemId ?? economy.Inventory.Stacks.FirstOrDefault(s => s.NamePl.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)?.ItemId
                    ?? term.ToLowerInvariant().Replace(' ', '_');
                long.TryParse(qty.Text, out var q);
                long.TryParse(reward.Text, out var r);
                await _api.PlaceCraftOrderAsync(c.Id, id, Math.Max(1, q), r, Ct);
                close();
                RefreshHud();
                SetStatus("Zamówienie złożone — nagroda czeka w depozycie.");
            }), body, sp: 13, heightDp: 38);
            _ui.Pages(body, orders.Count, 1, 4, (i, cell) =>
            {
                var o = orders[i];
                var card = _ui.Card(cell, 1f, 6, o.Mine ? Ui.Gold : (Color?)null, 0);
                var r2 = _ui.HRow(card, 40, 0);
                var col = _ui.Box(r2, 1f, marginDp: 4);
                _ui.Text(o.Quantity + "× " + o.ItemNamePl + " → " + o.Reward + " KD", col, 13, Ui.Ink, bold: true);
                _ui.Text(o.OrdererName + (o.Expired ? " · wygasło" : " · do " + o.ExpiresAt.ToLocalTime().ToString("dd.MM")), col, 10, Ui.Muted);
                var have = economy.Inventory.Count(o.ItemId);
                if (o.Mine)
                    _ui.Btn("Anuluj", () => Run(async () => { await _api.CancelCraftOrderAsync(c.Id, o.Id, Ct); close(); RefreshHud(); SetStatus("Zamówienie anulowane, nagroda wróciła."); }),
                        r2, secondary: true, sp: 11, heightDp: 34).LayoutParameters = new LinearLayout.LayoutParams(Px(80), Px(34));
                else if (!o.Expired && have >= o.Quantity)
                    _ui.Btn("Dostarcz", () => Run(async () => { await _api.FulfilCraftOrderAsync(c.Id, o.Id, Ct); close(); Sfx.Play(Sfx.Coin); RefreshHud(); SetStatus("Dostarczono — nagroda w sakwie."); }),
                        r2, sp: 11, heightDp: 34).LayoutParameters = new LinearLayout.LayoutParams(Px(80), Px(34));
            }, empty: "Brak zamówień.");
        }

        private void Market(EconomyFlow economy, Ui ui, LinearLayout page, ListingPageDto offersPage, List<MyListingDto> mine, bool showMine)
        {
            var c = _me;
            var rules = economy.MarketRules;
            if (rules != null)
                ui.Text("Opłata " + rules.ListingFeePermille / 10.0 + "% (min. " + rules.MinListingFee + ") · podatek " + rules.SaleTaxPermille / 10.0 + "% · " + rules.ListingHours + " h", page, 11, Ui.Muted);
            ui.Tabs(page, new[] { "Oferty", "Moje oferty" }, showMine ? 1 : 0, i => ShowBag(economy, 2, offersPage, mine, 0, i == 1), 32);

            async Task Reload(string message)
            {
                var o = await economy.BrowseAsync(null, null, Ct);
                var m = await economy.MyListingsAsync(Ct);
                await economy.ReloadAsync(Ct);
                ShowBag(economy, 2, o, m, 0, showMine);
                if (message != null) SetStatus(message);
                RefreshHud();
            }

            if (!showMine)
            {
                var search = ui.HRow(page, 40, 4);
                var term = ui.Field("Szukaj na targu…", parent: search, value: _marketQuery);
                term.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
                ui.Btn("Szukaj", () => Run(async () =>
                {
                    _marketQuery = term.Text?.Trim();
                    var found = await _api.SearchMarketAsync(_marketQuery, "unit_price", 50, Ct);
                    ShowBag(economy, 2, found, mine, 0, false);
                    SetStatus(string.IsNullOrEmpty(_marketQuery) ? "Najtańsze oferty (za sztukę)." : "Wyniki: " + _marketQuery + " — najtańsze za sztukę.");
                }), search, sp: 12, heightDp: 40).LayoutParameters = new LinearLayout.LayoutParams(Px(84), ViewGroup.LayoutParams.MatchParent) { LeftMargin = Px(6) };
                var offers = (offersPage?.Items ?? new List<ListingDto>()).Where(l => l.SellerCharacterId != c.Id).ToList();
                ui.Pages(page, offers.Count, 1, 4, (i, cell) =>
                {
                    var l = offers[i];
                    var card = ui.Card(cell, 1f, 6, Ui.RarityOf(l.Rarity).Color, 0);
                    var row = ui.HRow(card, 44, 0);
                    ui.Icon(row, ui.ArtId("item_" + l.ItemId) != 0 ? "item_" + l.ItemId : "item_slot_empty", 38);
                    var col = ui.Box(row, 1f, marginDp: 8);
                    ui.Text(l.ItemNamePl + (l.UpgradeLevel > 0 ? " +" + l.UpgradeLevel : string.Empty) + " ×" + l.Quantity, col, 13, Ui.Ink, bold: true);
                    ui.Text(l.SellerName + " · " + l.Price + " KD" + (l.Quantity > 1 ? " (" + l.Price / l.Quantity + "/szt.)" : string.Empty), col, 11, Ui.Gold);
                    col.Clickable = true;
                    col.Click += (_, __) => Run(async () =>
                    {
                        var pr = await _api.GetPricesAsync(l.ItemId, Ct);
                        SetStatus(pr.ItemNamePl + ": " + (pr.AverageUnitPrice7d.HasValue ? "średnio " + pr.AverageUnitPrice7d + " KD/szt. (" + pr.Sales7d + " sprzedaży w 7 dni, "
                            + pr.MinUnitPrice7d + "–" + pr.MaxUnitPrice7d + ")" : "brak sprzedaży w 7 dni")
                            + (pr.LowestActiveUnitPrice.HasValue ? " · najtaniej teraz " + pr.LowestActiveUnitPrice + " KD/szt." : string.Empty));
                    });
                    ui.Btn("Kup", () => Run(async () =>
                    {
                        var bought = await economy.BuyAsync(l, Ct);
                        Sfx.Play(Sfx.Coin);
                        await Reload(bought == null ? "Ktoś był szybszy — oferta już nieaktualna." : "Kupiono za " + bought.Paid + " KD.");
                    }), row, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(70), Px(38)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Na targu nic nie ma.");
                ui.Btn("📋 Zamówienia rzemieślnicze", () => Run(() => CraftOrdersSheetAsync(economy)), page, secondary: true, sp: 12, heightDp: 34);
            }
            else
            {
                var active = (mine ?? new List<MyListingDto>()).Where(x => x.Listing.Status == "active").Select(x => x.Listing).ToList();
                ui.Pages(page, active.Count, 1, 4, (i, cell) =>
                {
                    var l = active[i];
                    var card = ui.Card(cell, 1f, 6, null, 0);
                    var row = ui.HRow(card, 44, 0);
                    ui.Icon(row, ui.ArtId("item_" + l.ItemId) != 0 ? "item_" + l.ItemId : "item_slot_empty", 38);
                    var col = ui.Box(row, 1f, marginDp: 8);
                    ui.Text(l.ItemNamePl + " ×" + l.Quantity + " — " + l.Price + " KD", col, 13, Ui.Ink, bold: true);
                    ui.Text(l.Expired ? "wygasła" : "do " + l.ExpiresAt.ToLocalTime().ToString("dd.MM HH:mm"), col, 11, Ui.Muted);
                    ui.Btn(l.Expired ? "Odbierz" : "Wycofaj", () => Run(async () =>
                    {
                        await economy.CancelAsync(l, Ct);
                        await Reload(null);
                    }), row, secondary: true, sp: 12, heightDp: 38).LayoutParameters = new LinearLayout.LayoutParams(Px(84), Px(38)) { Gravity = GravityFlags.CenterVertical };
                }, empty: "Nie masz aktywnych ofert.");
            }
        }
    }
}
