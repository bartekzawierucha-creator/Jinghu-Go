using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Inventory.Contracts;

namespace JianghuGo.Modules.Combat.Application;

public sealed record DaoTechniqueView(string Id, string NamePl, string Slot, int QiCost, int Cooldown, int Speed,
    AttackSpec? Attack, DefenseSpec? Defense, MovementSpec? Movement, CounterSpec? Counter,
    Guid AuthorCharacterId, string AuthorName, int Points, int Allowance, string RulesVersion, DateTimeOffset CreatedAt);

public sealed record KnownDaoTechniqueView(DaoTechniqueView Technique, string Source, DateTimeOffset LearnedAt);

public sealed record DaoPreview(bool Valid, int Points, int Allowance, IReadOnlyList<string> Problems);

public sealed record InscribeCommand(Guid CharacterId);

public sealed record ScrollView(Guid InstanceId, int Version, string TechniqueId, long StonesSpent, long Balance);

public sealed record LearnCommand(int ExpectedVersion);

/// <summary>
/// Dao Lab: players design techniques from approved components within a power budget (DaoLabRules), pay a design fee,
/// and may inscribe their designs onto tradable scrolls. Reading a scroll teaches the technique and consumes the scroll.
/// Designs are immutable: an equipped Dao technique is frozen into each battle snapshot.
/// </summary>
public sealed class DaoLabService(
    IUnitOfWorkFactory uowFactory,
    ICombatRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    DaoLabConfig config,
    CombatConfig combat,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error TechniqueNotFound = Error.NotFound("technique_not_found", "Technique not found.");

    public DaoPreview Preview(DaoDesign design)
    {
        var a = DaoLabRules.Assess(config, combat, design);
        return new DaoPreview(a.Valid, a.Points, a.Allowance, a.Problems);
    }

    public Task<Result<KnownDaoTechniqueView>> DesignAsync(AccountId accountId, Guid characterId, string? key, DaoDesign design, CancellationToken ct) =>
        uowFactory.RunAsync<Result<KnownDaoTechniqueView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "dao.design", key, new { characterId, design }, async Task<Result<KnownDaoTechniqueView>> () =>
            {
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var assessment = DaoLabRules.Assess(config, combat, design);
                if (!assessment.Valid) return Error.Validation("design_invalid", string.Join(" ", assessment.Problems));
                if (await repo.CountDesignsAsync(uow, c.Id, ct) >= config.MaxDesignsPerCharacter)
                    return Error.LimitReached("design_limit", $"A cultivator can author at most {config.MaxDesignsPerCharacter} techniques.");

                var now = clock.GetUtcNow();
                var id = DaoLabRules.NewTechniqueId(seeds.NextSeed());
                if (config.DesignFeeStones > 0)
                {
                    var paid = await inventory.DebitAsync(uow, c.Id, config.DesignFeeStones, "dao_design", id, ct);
                    if (!paid.IsSuccess) return paid.Error!;
                }

                var technique = new CustomTechnique(id, c.Id, accountId, c.Name, DaoLabRules.ToDefinition(id, design), assessment.Points, assessment.Allowance,
                    config.RulesVersion, now);
                await repo.AddCustomTechniqueAsync(uow, technique, ct);
                var known = new KnownTechnique(c.Id, id, KnownSources.Designed, now);
                await repo.TryAddKnownAsync(uow, known, ct);
                await AuditAsync(uow, accountId, "dao.designed", c.Id, new { techniqueId = id, technique.Definition.Slot, assessment.Points, assessment.Allowance, fee = config.DesignFeeStones }, now, ct);
                return new KnownDaoTechniqueView(View(technique), known.Source, known.LearnedAt);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<IReadOnlyList<KnownDaoTechniqueView>>> KnownAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<KnownDaoTechniqueView>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var list = await repo.ListKnownCustomAsync(uow, c.Id, ct);
            return Result<IReadOnlyList<KnownDaoTechniqueView>>.Success([.. list.Select(x => new KnownDaoTechniqueView(View(x.Technique), x.Known.Source, x.Known.LearnedAt))]);
        }, ct);

    /// <summary>Public: anyone may inspect a technique (e.g. before buying its scroll on the market).</summary>
    public Task<Result<DaoTechniqueView>> GetAsync(string techniqueId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<DaoTechniqueView>>(async uow =>
            DaoLabRules.IsDaoId(techniqueId) && await repo.FindCustomTechniqueAsync(uow, techniqueId, ct) is { } t ? View(t) : TechniqueNotFound, ct);

    /// <summary>Only the author inscribes scrolls of a design. Costs stones and a material; the scroll is a tradable unique item.</summary>
    public Task<Result<ScrollView>> InscribeAsync(AccountId accountId, string techniqueId, string? key, InscribeCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ScrollView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "dao.inscribe", key, new { techniqueId, cmd }, async Task<Result<ScrollView>> () =>
            {
                var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (c is null) return CharacterNotFound;
                var t = DaoLabRules.IsDaoId(techniqueId) ? await repo.FindCustomTechniqueAsync(uow, techniqueId, ct) : null;
                if (t is null) return TechniqueNotFound;
                if (t.AuthorCharacterId != c.Id) return Error.Conflict("not_author", "Only the author of a technique can inscribe its scrolls.");

                var now = clock.GetUtcNow();
                var reference = Uuid7.New().ToString();
                var s = config.Scroll;
                if (s.MaterialQuantity > 0)
                {
                    var taken = await inventory.RemoveItemsAsync(uow, c.Id, s.Material, s.MaterialQuantity, "dao_scroll", reference, ct);
                    if (!taken.IsSuccess) return taken.Error!;
                }

                long balance = await inventory.BalanceAsync(uow, c.Id, ct);
                if (s.Stones > 0)
                {
                    var paid = await inventory.DebitAsync(uow, c.Id, s.Stones, "dao_scroll", reference, ct);
                    if (!paid.IsSuccess) return paid.Error!;
                    balance = paid.Value;
                }

                var scroll = await inventory.CreateInstanceAsync(uow, c.Id, accountId, s.ItemId, "dao_scroll", reference, ct, boundRef: t.Id);
                if (!scroll.IsSuccess) return scroll.Error!;
                await AuditAsync(uow, accountId, "dao.scroll_inscribed", c.Id, new { techniqueId, instanceId = scroll.Value.Id }, now, ct);
                return new ScrollView(scroll.Value.Id, scroll.Value.Version, t.Id, s.Stones, balance);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Reads a scroll from the bag: the character learns its technique and the scroll is used up.</summary>
    public Task<Result<KnownDaoTechniqueView>> LearnAsync(AccountId accountId, Guid characterId, Guid instanceId, string? key, LearnCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<KnownDaoTechniqueView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "dao.learn", key, new { characterId, instanceId, cmd }, async Task<Result<KnownDaoTechniqueView>> () =>
            {
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var scroll = await inventory.FindInstanceAsync(uow, instanceId, ct);
                if (scroll is null || scroll.CharacterId != c.Id.Value || scroll.ItemId != config.Scroll.ItemId || scroll.BoundRef is null)
                    return Error.NotFound("scroll_not_found", "Scroll not found.");
                var t = await repo.FindCustomTechniqueAsync(uow, scroll.BoundRef, ct)
                    ?? throw new InvalidOperationException($"Scroll {instanceId} refers to missing technique {scroll.BoundRef}.");

                var now = clock.GetUtcNow();
                var known = new KnownTechnique(c.Id, t.Id, KnownSources.Scroll, now);
                if (!await repo.TryAddKnownAsync(uow, known, ct)) return Error.Conflict("technique_already_known", "You already know this technique; the scroll is kept.");
                var used = await inventory.ConsumeInstanceAsync(uow, instanceId, cmd.ExpectedVersion, "dao_learn", ct);
                if (!used.IsSuccess) return used.Error!;
                await AuditAsync(uow, accountId, "dao.learned", c.Id, new { techniqueId = t.Id, instanceId, author = t.AuthorCharacterId.Value }, now, ct);
                return new KnownDaoTechniqueView(View(t), known.Source, known.LearnedAt);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private static DaoTechniqueView View(CustomTechnique t)
    {
        var d = t.Definition;
        return new DaoTechniqueView(t.Id, d.NamePl, d.Slot, d.QiCost, d.Cooldown, d.Speed, d.Attack, d.Defense, d.Movement, d.Counter,
            t.AuthorCharacterId.Value, t.AuthorName, t.Points, t.Allowance, t.RulesVersion, t.CreatedAt);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, CharacterId characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
