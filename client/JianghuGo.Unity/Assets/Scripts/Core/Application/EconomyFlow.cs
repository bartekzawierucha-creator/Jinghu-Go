using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// UI-independent state for the bag, crafting and market screens of one character. Every action reloads the
    /// inventory from the server; the client never predicts balances, outcomes or fees. Version conflicts
    /// (another device changed the item) reload instead of failing.
    /// </summary>
    public sealed class EconomyFlow
    {
        private readonly ApiClient _api;
        private readonly Guid _characterId;

        public EconomyFlow(ApiClient api, Guid characterId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _characterId = characterId;
        }

        public InventoryDto Inventory { get; private set; }
        public RecipeBookDto Recipes { get; private set; }
        public MarketRulesDto MarketRules { get; private set; }
        public event Action<InventoryDto> InventoryChanged;

        public async Task LoadAsync(CancellationToken ct)
        {
            Recipes = await _api.GetRecipesAsync(ct).ConfigureAwait(false);
            MarketRules = await _api.GetMarketRulesAsync(ct).ConfigureAwait(false);
            await ReloadAsync(ct).ConfigureAwait(false);
        }

        public async Task ReloadAsync(CancellationToken ct)
        {
            Set(await _api.GetInventoryAsync(_characterId, ct).ConfigureAwait(false));
            Professions = (await _api.GetProfessionsAsync(_characterId, ct).ConfigureAwait(false)).Items;
        }

        /// <summary>Alchemy and Smithing levels (reloaded with the inventory).</summary>
        public List<ProfessionDto> Professions { get; private set; } = new List<ProfessionDto>();

        public ProfessionDto Profession(string id)
        {
            foreach (var p in Professions) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Whether the character's profession level allows this recipe.</summary>
        public bool LevelAllows(RecipeDto recipe) => recipe.MinLevel <= (Profession(recipe.Profession)?.Level ?? 1);

        /// <summary>Stone cost after the profession discount (the server applies the same rule).</summary>
        public long StonesFor(RecipeDto recipe)
        {
            var p = Profession(recipe.Profession);
            return p == null ? recipe.Stones : recipe.Stones * (1000 - p.StoneDiscountPermille) / 1000;
        }

        /// <summary>Equips, or unequips when already equipped. Returns false (after reloading) on a version conflict.</summary>
        public async Task<bool> ToggleEquipAsync(ItemInstanceDto item, CancellationToken ct)
        {
            try
            {
                Set(item.IsEquipped
                    ? await _api.UnequipAsync(_characterId, item, ct).ConfigureAwait(false)
                    : await _api.EquipAsync(_characterId, item, ct).ConfigureAwait(false));
                return true;
            }
            catch (ApiException e) when (e.Code == "version_conflict" || e.Code == "item_not_found")
            {
                await ReloadAsync(ct).ConfigureAwait(false);
                return false;
            }
        }

        public async Task<UseResultDto> UseAsync(string itemId, int quantity, CancellationToken ct)
        {
            var result = await _api.UseItemAsync(_characterId, itemId, quantity, ct).ConfigureAwait(false);
            await ReloadAsync(ct).ConfigureAwait(false);
            return result;
        }

        public async Task DiscardAsync(string itemId, int quantity, CancellationToken ct) =>
            Set(await _api.DiscardStackAsync(_characterId, itemId, quantity, ct).ConfigureAwait(false));

        public async Task DiscardAsync(ItemInstanceDto item, CancellationToken ct) =>
            Set(await _api.DiscardItemAsync(_characterId, item.Id, ct).ConfigureAwait(false));

        /// <summary>Whether the bag currently holds the inputs and stones for <paramref name="times"/> crafts (display hint only).</summary>
        public bool CanAfford(RecipeDto recipe, int times)
        {
            if (Inventory == null || recipe == null) return false;
            foreach (var input in recipe.Inputs)
            {
                if (Inventory.Count(input.Item) < (long)input.Quantity * times) return false;
            }

            return LevelAllows(recipe) && Inventory.Balance >= StonesFor(recipe) * times;
        }

        public async Task<CraftResultDto> CraftAsync(string recipeId, int times, CancellationToken ct)
        {
            var result = await _api.CraftAsync(_characterId, recipeId, times, ct).ConfigureAwait(false);
            await ReloadAsync(ct).ConfigureAwait(false);
            return result;
        }

        /// <summary>Null when the item changed meanwhile (inventory reloaded; the player may try again).</summary>
        public async Task<UpgradeResultDto> UpgradeAsync(ItemInstanceDto item, CancellationToken ct)
        {
            try
            {
                var result = await _api.UpgradeAsync(_characterId, item, ct).ConfigureAwait(false);
                await ReloadAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch (ApiException e) when (e.Code == "version_conflict")
            {
                await ReloadAsync(ct).ConfigureAwait(false);
                return null;
            }
        }

        public UpgradeLevelDto NextUpgrade(ItemInstanceDto item)
        {
            if (Recipes?.Upgrade == null || item.UpgradeLevel >= Recipes.Upgrade.MaxLevel) return null;
            return Recipes.Upgrade.Levels[item.UpgradeLevel];
        }

        // ---- market ---------------------------------------------------------------------------

        public Task<ListingPageDto> BrowseAsync(string itemId, Guid? before, CancellationToken ct) => _api.BrowseMarketAsync(itemId, before, 30, ct);

        public async Task<ListingDto> SellAsync(string itemId, long quantity, long price, CancellationToken ct)
        {
            var listing = await _api.ListStackAsync(_characterId, itemId, quantity, price, ct).ConfigureAwait(false);
            await ReloadAsync(ct).ConfigureAwait(false);
            return listing;
        }

        public async Task<ListingDto> SellAsync(ItemInstanceDto item, long price, CancellationToken ct)
        {
            var listing = await _api.ListItemAsync(_characterId, item, price, ct).ConfigureAwait(false);
            await ReloadAsync(ct).ConfigureAwait(false);
            return listing;
        }

        /// <summary>Null when somebody else was faster (listing sold, cancelled or expired).</summary>
        public async Task<PurchaseResultDto> BuyAsync(ListingDto listing, CancellationToken ct)
        {
            try
            {
                var result = await _api.BuyAsync(_characterId, listing.Id, ct).ConfigureAwait(false);
                await ReloadAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch (ApiException e) when (e.Code == "listing_unavailable")
            {
                return null;
            }
        }

        public async Task CancelAsync(ListingDto listing, CancellationToken ct)
        {
            await _api.CancelListingAsync(_characterId, listing.Id, ct).ConfigureAwait(false);
            await ReloadAsync(ct).ConfigureAwait(false);
        }

        public async Task<List<MyListingDto>> MyListingsAsync(CancellationToken ct) =>
            (await _api.GetMyListingsAsync(_characterId, ct).ConfigureAwait(false)).Items;

        private void Set(InventoryDto inventory)
        {
            Inventory = inventory;
            InventoryChanged?.Invoke(inventory);
        }
    }
}
