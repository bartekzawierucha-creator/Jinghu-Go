using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Market.Application;
using JianghuGo.Modules.Market.Domain;
using Npgsql;

namespace JianghuGo.Modules.Market.Infrastructure;

internal sealed class InMemoryMarketRepository : IMarketRepository
{
    private const string Listings = "market.listings";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task AddAsync(IUnitOfWork uow, Listing listing, CancellationToken ct)
    {
        U(uow).SetTable(Listings, U(uow).Table<Guid, Listing>(Listings).Add(listing.Id, listing));
        return Task.CompletedTask;
    }

    public Task<Listing?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Listing>(Listings).GetValueOrDefault(id));

    public Task<bool> TryUpdateAsync(IUnitOfWork uow, Listing listing, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, Listing>(Listings);
        if (!t.TryGetValue(listing.Id, out var cur) || cur.Version != expectedVersion || listing.Version != expectedVersion + 1) return Task.FromResult(false);
        U(uow).SetTable(Listings, t.SetItem(listing.Id, listing));
        return Task.FromResult(true);
    }

    public Task<int> CountActiveAsync(IUnitOfWork uow, CharacterId seller, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Listing>(Listings).Values.Count(l => l.SellerCharacterId == seller && l.Status == ListingStatuses.Active));

    public Task<IReadOnlyList<Listing>> BrowseAsync(IUnitOfWork uow, string? itemId, Guid? before, int limit, DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<Listing> list = [.. U(uow).Table<Guid, Listing>(Listings).Values
            .Where(l => l.IsBuyable(now) && (itemId is null || l.ItemId == itemId) && (before is null || l.Id.CompareTo(before.Value) < 0))
            .OrderByDescending(l => l.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<Listing>> ListBySellerAsync(IUnitOfWork uow, CharacterId seller, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<Listing> list = [.. U(uow).Table<Guid, Listing>(Listings).Values
            .Where(l => l.SellerCharacterId == seller && (before is null || l.Id.CompareTo(before.Value) < 0)).OrderByDescending(l => l.Id).Take(limit)];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresMarketRepository : IMarketRepository
{
    private const string Columns =
        "id, seller_character_id, seller_account_id, seller_name, item_id, quantity, instance_id, upgrade_level, price, fee, status, " +
        "buyer_character_id, buyer_account_id, tax, version, created_at, expires_at, closed_at, instance_ref";

    private static PostgresUnitOfWork Pg(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static Listing Map(NpgsqlDataReader r) => new(
        r.GetGuid(0), new CharacterId(r.GetGuid(1)), new AccountId(r.GetGuid(2)), r.GetString(3), r.GetString(4), r.GetInt64(5),
        r.IsDBNull(6) ? null : r.GetGuid(6), r.GetInt32(7), r.GetInt64(8), r.GetInt64(9), r.GetString(10),
        r.IsDBNull(11) ? null : new CharacterId(r.GetGuid(11)), r.IsDBNull(12) ? null : new AccountId(r.GetGuid(12)), r.GetInt64(13), r.GetInt32(14),
        r.GetFieldValue<DateTimeOffset>(15), r.GetFieldValue<DateTimeOffset>(16), r.IsDBNull(17) ? null : r.GetFieldValue<DateTimeOffset>(17),
        r.IsDBNull(18) ? null : r.GetString(18));

    public Task AddAsync(IUnitOfWork uow, Listing l, CancellationToken ct) =>
        Pg(uow).ExecuteAsync($"INSERT INTO market.listings ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19)",
            ct, l.Id, l.SellerCharacterId.Value, l.SellerAccountId.Value, l.SellerName, l.ItemId, l.Quantity, l.InstanceId, l.UpgradeLevel, l.Price, l.Fee,
            l.Status, l.BuyerCharacterId?.Value, l.BuyerAccountId?.Value, l.Tax, l.Version, l.CreatedAt, l.ExpiresAt, l.ClosedAt, l.InstanceRef);

    public Task<Listing?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM market.listings WHERE id = $1", Map, ct, id);

    public async Task<bool> TryUpdateAsync(IUnitOfWork uow, Listing l, int expectedVersion, CancellationToken ct) =>
        l.Version == expectedVersion + 1 && await Pg(uow).ExecuteAsync(
            "UPDATE market.listings SET status = $2, buyer_character_id = $3, buyer_account_id = $4, tax = $5, version = $6, closed_at = $7 " +
            "WHERE id = $1 AND version = $8",
            ct, l.Id, l.Status, l.BuyerCharacterId?.Value, l.BuyerAccountId?.Value, l.Tax, l.Version, l.ClosedAt, expectedVersion) == 1;

    public async Task<int> CountActiveAsync(IUnitOfWork uow, CharacterId seller, CancellationToken ct) =>
        (int)(await Pg(uow).QueryAsync("SELECT count(*) FROM market.listings WHERE seller_character_id = $1 AND status = 'active'",
            r => r.GetInt64(0), ct, seller.Value)).Single();

    public async Task<IReadOnlyList<Listing>> BrowseAsync(IUnitOfWork uow, string? itemId, Guid? before, int limit, DateTimeOffset now, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            $"SELECT {Columns} FROM market.listings WHERE status = 'active' AND expires_at > $1 AND ($2::text IS NULL OR item_id = $2::text) " +
            "AND ($3::uuid IS NULL OR id < $3::uuid) ORDER BY id DESC LIMIT $4", Map, ct, now, itemId, before, limit);

    public async Task<IReadOnlyList<Listing>> ListBySellerAsync(IUnitOfWork uow, CharacterId seller, Guid? before, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            $"SELECT {Columns} FROM market.listings WHERE seller_character_id = $1 AND ($2::uuid IS NULL OR id < $2::uuid) ORDER BY id DESC LIMIT $3",
            Map, ct, seller.Value, before, limit);
}
