using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    // Economy DTOs. Balances, quantities, chances, fees and taxes are always the server's numbers.

    public sealed class StackDto
    {
        public string ItemId { get; set; }
        public string NamePl { get; set; }
        public string Type { get; set; }
        public string Rarity { get; set; }
        public long Quantity { get; set; }
    }

    public sealed class ItemInstanceDto
    {
        public Guid Id { get; set; }
        public string ItemId { get; set; }
        public string NamePl { get; set; }
        /// <summary>Quality (1.8) and the special combat property of heavenly/divine equipment.</summary>
        public string Quality { get; set; }
        public string QualityPl { get; set; }
        public SpecialAffixDto Special { get; set; }
        public string Rarity { get; set; }
        public string Slot { get; set; }
        public int UpgradeLevel { get; set; }
        public string State { get; set; }
        public int Version { get; set; }
        public Dictionary<string, int> Stats { get; set; } = new Dictionary<string, int>();

        /// <summary>For scrolls: the Dao technique they teach.</summary>
        public string BoundRef { get; set; }

        /// <summary>Random properties fixed forever by the item (more on rarer items) and the set it belongs to.</summary>
        public List<AffixDto> Affixes { get; set; } = new List<AffixDto>();
        public string SetId { get; set; }
        public string SetNamePl { get; set; }

        public bool InBag => State == "bag";
        public bool IsEquipped => State == "equipped";
    }

    public sealed class AffixDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Stat { get; set; }
        public int Value { get; set; }
    }

    public sealed class SetBonusDto
    {
        public int Pieces { get; set; }
        public Dictionary<string, int> Stats { get; set; } = new Dictionary<string, int>();
    }

    public sealed class SetDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public int Worn { get; set; }
        public int Pieces { get; set; }
        public Dictionary<string, int> Active { get; set; } = new Dictionary<string, int>();
        public List<SetBonusDto> Bonuses { get; set; } = new List<SetBonusDto>();
    }

    public sealed class SpecialAffixDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Perk { get; set; }
        public int Value { get; set; }
    }

    /// <summary>The travelling merchant (1.6).</summary>
    public sealed class MerchantDto
    {
        public int SellPermille { get; set; }
        public List<MerchantOfferDto> Stock { get; set; } = new List<MerchantOfferDto>();
        public Dictionary<string, long> SellPrices { get; set; } = new Dictionary<string, long>();
        public long Balance { get; set; }
        public int MaxPerTrade { get; set; }
    }

    public sealed class MerchantOfferDto
    {
        public string ItemId { get; set; }
        public string NamePl { get; set; }
        public string Type { get; set; }
        public string Rarity { get; set; }
        public long Price { get; set; }
    }

    public sealed class MerchantTradeDto
    {
        public long Stones { get; set; }
        public long Balance { get; set; }
        public InventoryDto Inventory { get; set; }
    }

    public sealed class DismantleResultDto
    {
        public List<StackDto> Materials { get; set; } = new List<StackDto>();
        public InventoryDto Inventory { get; set; }
    }

    public sealed class InventoryDto
    {
        public Guid CharacterId { get; set; }
        public string RulesVersion { get; set; }
        public long Balance { get; set; }
        public int SlotsUsed { get; set; }
        public int Capacity { get; set; }
        public List<StackDto> Stacks { get; set; } = new List<StackDto>();
        public List<ItemInstanceDto> Items { get; set; } = new List<ItemInstanceDto>();
        public Dictionary<string, Guid> Equipped { get; set; } = new Dictionary<string, Guid>();
        public Dictionary<string, int> Bonuses { get; set; } = new Dictionary<string, int>();
        public List<SetDto> Sets { get; set; } = new List<SetDto>();

        public long Count(string itemId)
        {
            foreach (var s in Stacks) if (s.ItemId == itemId) return s.Quantity;
            return 0;
        }

        public ItemInstanceDto Find(Guid instanceId)
        {
            foreach (var i in Items) if (i.Id == instanceId) return i;
            return null;
        }
    }

    public sealed class UseResultDto
    {
        public string ItemId { get; set; }
        public int Quantity { get; set; }
        public long QiGranted { get; set; }
        public long Remaining { get; set; }
    }

    public sealed class WalletEntryDto
    {
        public Guid Id { get; set; }
        public long Delta { get; set; }
        public long BalanceAfter { get; set; }
        public string Reason { get; set; }
        public string ReferenceId { get; set; }
        public DateTimeOffset At { get; set; }
    }

    public sealed class WalletPageDto
    {
        public List<WalletEntryDto> Items { get; set; } = new List<WalletEntryDto>();
        public Guid? Next { get; set; }
    }

    public sealed class ItemAmountDto
    {
        public string Item { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class RecipeDto
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public List<ItemAmountDto> Inputs { get; set; } = new List<ItemAmountDto>();
        public long Stones { get; set; }
        public ItemAmountDto Output { get; set; }
        public int SuccessPermille { get; set; }
        public int MinLevel { get; set; } = 1;

        /// <summary>"alchemy" for alchemy recipes, otherwise "smithing".</summary>
        public string Profession => Kind == "alchemy" ? "alchemy" : "smithing";
    }

    public sealed class ProfessionDto
    {
        public string Id { get; set; }
        public int Level { get; set; }
        public long Xp { get; set; }
        public long XpForLevel { get; set; }
        public long? XpForNextLevel { get; set; }
        public int SuccessBonusPermille { get; set; }
        public int StoneDiscountPermille { get; set; }
        public int MaxLevel { get; set; }
    }

    public sealed class ProfessionsDto
    {
        public List<ProfessionDto> Items { get; set; } = new List<ProfessionDto>();
    }

    public sealed class UpgradeLevelDto
    {
        public int Level { get; set; }
        public int SuccessPermille { get; set; }
        public long Stones { get; set; }
    }

    public sealed class UpgradeRulesDto
    {
        public int MaxLevel { get; set; }
        public string Material { get; set; }
        public List<UpgradeLevelDto> Levels { get; set; } = new List<UpgradeLevelDto>();

        /// <summary>Stone price factor (‰) by item grade (1.6); missing grade = ×1.</summary>
        public Dictionary<string, int> RarityPermille { get; set; }
    }

    public sealed class RecipeBookDto
    {
        public string RulesVersion { get; set; }
        public int AlchemyInsightBonusPermille { get; set; }
        public int MaxTimesPerRequest { get; set; }
        public List<RecipeDto> Items { get; set; } = new List<RecipeDto>();
        public UpgradeRulesDto Upgrade { get; set; }

        /// <summary>Polish names of all items used or made by recipes.</summary>
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }

    public sealed class CraftResultDto
    {
        public Guid LogId { get; set; }
        public string RecipeId { get; set; }
        public int Attempts { get; set; }
        public int Successes { get; set; }
        public int ChancePermille { get; set; }
        public string OutputItem { get; set; }
        public int OutputQuantity { get; set; }
        public List<Guid> CreatedItems { get; set; } = new List<Guid>();
        public long StonesSpent { get; set; }
        public long Balance { get; set; }
    }

    public sealed class UpgradeResultDto
    {
        public Guid LogId { get; set; }
        public Guid InstanceId { get; set; }
        public bool Success { get; set; }
        public int ChancePermille { get; set; }
        public int FromLevel { get; set; }
        public int Level { get; set; }
        public int Version { get; set; }
        public long StonesSpent { get; set; }
        public long Balance { get; set; }
    }

    public sealed class ListingDto
    {
        public Guid Id { get; set; }
        public string ItemId { get; set; }
        public string ItemNamePl { get; set; }
        public string Rarity { get; set; }
        public long Quantity { get; set; }
        public Guid? InstanceId { get; set; }
        public int UpgradeLevel { get; set; }
        public long Price { get; set; }
        public Guid SellerCharacterId { get; set; }
        public string SellerName { get; set; }
        public string Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool Expired { get; set; }

        /// <summary>For scrolls: the Dao technique they teach (inspect it with GetDaoTechniqueAsync before buying).</summary>
        public string InstanceRef { get; set; }
    }

    public sealed class ListingPageDto
    {
        public List<ListingDto> Items { get; set; } = new List<ListingDto>();
        public Guid? Next { get; set; }
    }

    public sealed class MyListingDto
    {
        public ListingDto Listing { get; set; }
        public long Fee { get; set; }
        public long Tax { get; set; }
        public long? Proceeds { get; set; }
        public Guid? BuyerCharacterId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
    }

    public sealed class MyListingPageDto
    {
        public List<MyListingDto> Items { get; set; } = new List<MyListingDto>();
        public Guid? Next { get; set; }
    }

    public sealed class PurchaseResultDto
    {
        public ListingDto Listing { get; set; }
        public long Paid { get; set; }
        public long Balance { get; set; }
    }

    public sealed class MarketRulesDto
    {
        public string RulesVersion { get; set; }
        public int ListingFeePermille { get; set; }
        public long MinListingFee { get; set; }
        public int SaleTaxPermille { get; set; }
        public long MinPrice { get; set; }
        public long MaxPrice { get; set; }
        public int ListingHours { get; set; }
        public int MaxActiveListingsPerCharacter { get; set; }
    }

    /// <summary>Price history of an item (1.9): unit prices of the last 7 days and the cheapest offer now.</summary>
    public sealed class PriceDto
    {
        public string ItemId { get; set; }
        public string ItemNamePl { get; set; }
        public int Sales7d { get; set; }
        public long Volume7d { get; set; }
        public long? AverageUnitPrice7d { get; set; }
        public long? MinUnitPrice7d { get; set; }
        public long? MaxUnitPrice7d { get; set; }
        public long? LowestActiveUnitPrice { get; set; }
        public int ActiveListings { get; set; }
    }
}
