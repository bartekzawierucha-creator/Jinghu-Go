using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class InfrastructureTests
{
    [Fact]
    public async Task Uncommitted_unit_of_work_rolls_back()
    {
        using var db = new InMemoryDatabase();
        await using (var uow = InMemoryUnitOfWork.From(await db.BeginAsync(default)))
        {
            uow.SetTable("t", uow.Table<int, string>("t").Add(1, "x"));
        }

        await using var check = InMemoryUnitOfWork.From(await db.BeginAsync(default));
        Assert.Equal(0, check.Table<int, string>("t").Count);
    }

    [Fact]
    public async Task Units_of_work_are_serializable_under_contention()
    {
        using var db = new InMemoryDatabase();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(async () =>
        {
            await using var uow = InMemoryUnitOfWork.From(await db.BeginAsync(default));
            var t = uow.Table<string, int>("counter");
            var v = t.GetValueOrDefault("n");
            await Task.Yield(); // widen the race window
            uow.SetTable("counter", t.SetItem("n", v + 1));
            await uow.CommitAsync(default);
        })));

        await using var check = InMemoryUnitOfWork.From(await db.BeginAsync(default));
        Assert.Equal(200, check.Table<string, int>("counter")["n"]);
    }

    [Fact]
    public void Idempotency_key_format_is_enforced()
    {
        Assert.Null(Idempotency.ValidateKey("abcd1234"));
        Assert.Null(Idempotency.ValidateKey(Guid.NewGuid().ToString()));
        foreach (var bad in new[] { null, "", "short", "has space here", new string('a', 129), "ąęśćżźół12" })
        {
            Assert.NotNull(Idempotency.ValidateKey(bad), bad ?? "null");
        }
    }

    [Fact]
    public async Task Idempotency_store_rejects_duplicate_key()
    {
        using var db = new InMemoryDatabase();
        var store = new InMemoryIdempotencyStore();
        var rec = new IdempotencyRecord(AccountId.New(), "op", "key-12345", "h", "{}", DateTimeOffset.UtcNow);
        await using var uow = await db.BeginAsync(default);
        await store.AddAsync(uow, rec, default);
        await Assert.ThrowsAsync<DuplicateKeyException>(() => store.AddAsync(uow, rec, default));
    }
}
