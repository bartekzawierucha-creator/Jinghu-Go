using JianghuGo.Domain;

namespace JianghuGo.Modules.Market.Domain;

public sealed record MarketConfig(
    string RulesVersion,
    int ListingFeePermille,
    long MinListingFee,
    int SaleTaxPermille,
    long MinPrice,
    long MaxPrice,
    int ListingHours,
    int MaxActiveListingsPerCharacter);

public static class ListingStatuses
{
    public const string Active = "active";
    public const string Sold = "sold";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// A whole lot for sale: either a stack (ItemId × Quantity) or one unique item (InstanceId). The goods sit in escrow
/// from listing until sale or cancellation; an expired listing simply stops being buyable and is reclaimed by cancelling.
/// </summary>
public sealed record Listing(
    Guid Id,
    CharacterId SellerCharacterId,
    AccountId SellerAccountId,
    string SellerName,
    string ItemId,
    long Quantity,
    Guid? InstanceId,
    int UpgradeLevel,
    long Price,
    long Fee,
    string Status,
    CharacterId? BuyerCharacterId,
    AccountId? BuyerAccountId,
    long Tax,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt,
    string? InstanceRef = null) // copied from the unique item (a scroll's technique id) so buyers see what they buy
{
    public bool IsBuyable(DateTimeOffset now) => Status == ListingStatuses.Active && ExpiresAt > now;
}

public static class MarketRules
{
    /// <summary>Non-refundable listing fee: max(min fee, price × fee‰), rounded down. A sink against listing spam.</summary>
    public static long ListingFee(MarketConfig c, long price) => Math.Max(c.MinListingFee, checked(price * c.ListingFeePermille) / 1000);

    /// <summary>Sale tax (a Spirit Stone sink), rounded down; the seller always keeps at least 1 stone.</summary>
    public static long SaleTax(MarketConfig c, long price) => Math.Min(price - 1, checked(price * c.SaleTaxPermille) / 1000);
}

public static class MarketConfigValidator
{
    public static IReadOnlyList<string> Validate(MarketConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.ListingFeePermille is < 0 or > 200 || c.MinListingFee < 0) e.Add("listing fee invalid.");
        if (c.SaleTaxPermille is < 0 or > 500) e.Add("saleTaxPermille must be 0-500.");
        if (c.MinPrice < 1 || c.MaxPrice < c.MinPrice || c.MaxPrice > 1_000_000_000_000) e.Add("price range invalid.");
        if (c.ListingHours is < 1 or > 720) e.Add("listingHours must be 1-720.");
        if (c.MaxActiveListingsPerCharacter is < 1 or > 500) e.Add("maxActiveListingsPerCharacter must be 1-500.");
        return e;
    }
}
