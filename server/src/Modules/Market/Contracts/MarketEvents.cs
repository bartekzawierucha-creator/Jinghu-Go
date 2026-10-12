namespace JianghuGo.Modules.Market.Contracts;

public static class MarketEvents
{
    /// <summary>A listing was bought. Consumed by Notifications (seller is told what sold and for how much).</summary>
    public const string ListingSold = "market.listing_sold";

    public sealed record ListingSoldV1(Guid SellerAccountId, Guid SellerCharacterId, Guid ListingId, string ItemId, long Quantity, int UpgradeLevel, long Price, long Tax, long Proceeds);
}
