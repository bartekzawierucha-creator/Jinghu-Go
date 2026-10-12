using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using Npgsql;

namespace JianghuGo.Modules.Combat.Infrastructure;

internal static class CombatJson
{
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Idempotency.Json);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Idempotency.Json)!;
}

internal sealed class InMemoryCombatRepository : ICombatRepository
{
    private const string Loadouts = "combat.loadouts";
    private const string Mastery = "combat.mastery";
    private const string Battles = "combat.battles";
    private const string Intents = "combat.intents";
    private const string Turns = "combat.turns";
    private const string Custom = "combat.custom_techniques";
    private const string Known = "combat.known_techniques";

    public Task AddCustomTechniqueAsync(IUnitOfWork uow, CustomTechnique technique, CancellationToken ct)
    {
        U(uow).SetTable(Custom, U(uow).Table<string, CustomTechnique>(Custom).Add(technique.Id, technique));
        return Task.CompletedTask;
    }

    public Task<CustomTechnique?> FindCustomTechniqueAsync(IUnitOfWork uow, string id, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<string, CustomTechnique>(Custom).GetValueOrDefault(id));

    public Task<int> CountDesignsAsync(IUnitOfWork uow, CharacterId author, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<string, CustomTechnique>(Custom).Values.Count(t => t.AuthorCharacterId == author));

    public Task<bool> TryAddKnownAsync(IUnitOfWork uow, KnownTechnique known, CancellationToken ct)
    {
        var t = U(uow).Table<(CharacterId, string), KnownTechnique>(Known);
        if (t.ContainsKey((known.CharacterId, known.TechniqueId))) return Task.FromResult(false);
        U(uow).SetTable(Known, t.Add((known.CharacterId, known.TechniqueId), known));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<(KnownTechnique Known, CustomTechnique Technique)>> ListKnownCustomAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        var custom = U(uow).Table<string, CustomTechnique>(Custom);
        IReadOnlyList<(KnownTechnique, CustomTechnique)> list = [.. U(uow).Table<(CharacterId, string), KnownTechnique>(Known).Values
            .Where(k => k.CharacterId == characterId).OrderBy(k => k.LearnedAt).ThenBy(k => k.TechniqueId, StringComparer.Ordinal)
            .Select(k => (k, custom[k.TechniqueId]))];
        return Task.FromResult(list);
    }

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task<Loadout?> FindLoadoutAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<CharacterId, Loadout>(Loadouts).GetValueOrDefault(characterId));

    public Task<bool> TrySaveLoadoutAsync(IUnitOfWork uow, Loadout loadout, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<CharacterId, Loadout>(Loadouts);
        if ((t.GetValueOrDefault(loadout.CharacterId)?.Version ?? 0) != expectedVersion) return Task.FromResult(false);
        U(uow).SetTable(Loadouts, t.SetItem(loadout.CharacterId, loadout));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<TechniqueMastery>> ListMasteryAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<TechniqueMastery> list = [.. U(uow).Table<(CharacterId, string), TechniqueMastery>(Mastery).Values.Where(m => m.CharacterId == characterId).OrderBy(m => m.TechniqueId, StringComparer.Ordinal)];
        return Task.FromResult(list);
    }

    public Task SaveMasteryAsync(IUnitOfWork uow, TechniqueMastery m, CancellationToken ct)
    {
        U(uow).SetTable(Mastery, U(uow).Table<(CharacterId, string), TechniqueMastery>(Mastery).SetItem((m.CharacterId, m.TechniqueId), m));
        return Task.CompletedTask;
    }

    public Task AddBattleAsync(IUnitOfWork uow, Battle battle, CancellationToken ct)
    {
        U(uow).SetTable(Battles, U(uow).Table<Guid, Battle>(Battles).Add(battle.Id, battle));
        return Task.CompletedTask;
    }

    public Task<Battle?> FindBattleAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Battle>(Battles).GetValueOrDefault(id));

    public Task<bool> TryUpdateBattleAsync(IUnitOfWork uow, Battle battle, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, Battle>(Battles);
        if (!t.TryGetValue(battle.Id, out var current) || current.Version != expectedVersion || battle.Version != expectedVersion + 1) return Task.FromResult(false);
        U(uow).SetTable(Battles, t.SetItem(battle.Id, battle));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Battle>> ListOpenBattlesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<Battle> list = [.. U(uow).Table<Guid, Battle>(Battles).Values
            .Where(b => (b.CharacterA == characterId || b.CharacterB == characterId) && b.Status is BattleStatuses.Active or BattleStatuses.PendingAcceptance)
            .OrderBy(b => b.Id)];
        return Task.FromResult(list);
    }

    public Task<int> CountRewardedPveWinsSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Battle>(Battles).Values.Count(b =>
            b.CharacterA == characterId && b.Kind == BattleKinds.Pve && b.RewardQi > 0 && b.FinishedAt >= since));

    public Task<IReadOnlyList<SubmittedIntent>> ListIntentsAsync(IUnitOfWork uow, Guid battleId, int turn, CancellationToken ct)
    {
        IReadOnlyList<SubmittedIntent> list = [.. U(uow).Table<(Guid, int, char), SubmittedIntent>(Intents).Values.Where(i => i.BattleId == battleId && i.Turn == turn).OrderBy(i => i.Side)];
        return Task.FromResult(list);
    }

    public Task AddIntentAsync(IUnitOfWork uow, SubmittedIntent intent, CancellationToken ct)
    {
        var t = U(uow).Table<(Guid, int, char), SubmittedIntent>(Intents);
        var key = (intent.BattleId, intent.Turn, intent.Side);
        if (t.ContainsKey(key)) throw new DuplicateKeyException("Intent already submitted for this turn.");
        U(uow).SetTable(Intents, t.Add(key, intent));
        return Task.CompletedTask;
    }

    public Task AddTurnAsync(IUnitOfWork uow, TurnRecord turn, CancellationToken ct)
    {
        var t = U(uow).Table<(Guid, int), TurnRecord>(Turns);
        if (t.ContainsKey((turn.BattleId, turn.Turn))) throw new DuplicateKeyException("Turn already resolved.");
        U(uow).SetTable(Turns, t.Add((turn.BattleId, turn.Turn), turn));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TurnRecord>> ListTurnsAsync(IUnitOfWork uow, Guid battleId, CancellationToken ct)
    {
        IReadOnlyList<TurnRecord> list = [.. U(uow).Table<(Guid, int), TurnRecord>(Turns).Values.Where(t => t.BattleId == battleId).OrderBy(t => t.Turn)];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresCombatRepository : ICombatRepository
{
    private const string CustomColumns = "id, author_character_id, author_account_id, author_name, definition::text, points, allowance, rules_version, created_at";

    private static CustomTechnique MapCustom(NpgsqlDataReader r, int o = 0) => new(
        r.GetString(o), new CharacterId(r.GetGuid(o + 1)), new AccountId(r.GetGuid(o + 2)), r.GetString(o + 3), CombatJson.Read<TechniqueDefinition>(r.GetString(o + 4)),
        r.GetInt32(o + 5), r.GetInt32(o + 6), r.GetString(o + 7), r.GetFieldValue<DateTimeOffset>(o + 8));

    public Task AddCustomTechniqueAsync(IUnitOfWork uow, CustomTechnique t, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO combat.custom_techniques (id, author_character_id, author_account_id, author_name, slot, definition, points, allowance, rules_version, created_at) " +
            "VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8, $9, $10)",
            ct, t.Id, t.AuthorCharacterId.Value, t.AuthorAccountId.Value, t.AuthorName, t.Definition.Slot, CombatJson.Write(t.Definition), t.Points, t.Allowance, t.RulesVersion, t.CreatedAt);

    public Task<CustomTechnique?> FindCustomTechniqueAsync(IUnitOfWork uow, string id, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {CustomColumns} FROM combat.custom_techniques WHERE id = $1", r => MapCustom(r), ct, id);

    public async Task<int> CountDesignsAsync(IUnitOfWork uow, CharacterId author, CancellationToken ct) =>
        (int)(await PostgresUnitOfWork.From(uow).QueryAsync("SELECT count(*) FROM combat.custom_techniques WHERE author_character_id = $1",
            r => r.GetInt64(0), ct, author.Value)).Single();

    public async Task<bool> TryAddKnownAsync(IUnitOfWork uow, KnownTechnique k, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO combat.known_techniques (character_id, technique_id, source, learned_at) VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING",
            ct, k.CharacterId.Value, k.TechniqueId, k.Source, k.LearnedAt) == 1;

    public async Task<IReadOnlyList<(KnownTechnique Known, CustomTechnique Technique)>> ListKnownCustomAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT k.source, k.learned_at, t.id, t.author_character_id, t.author_account_id, t.author_name, t.definition::text, t.points, t.allowance, t.rules_version, t.created_at " +
            "FROM combat.known_techniques k JOIN combat.custom_techniques t ON t.id = k.technique_id WHERE k.character_id = $1 ORDER BY k.learned_at, k.technique_id",
            r => (new KnownTechnique(characterId, r.GetString(2), r.GetString(0), r.GetFieldValue<DateTimeOffset>(1)), MapCustom(r, 2)), ct, characterId.Value);

    private const string BattleColumns =
        "id, kind, status, rules_version, seed, seed_hash, character_a, account_a, character_b, account_b, npc_id, combatant_a::text, combatant_b::text, state::text, " +
        "version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at, finished_at";

    private static PostgresUnitOfWork Pg(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static Battle MapBattle(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), (ulong)r.GetDecimal(4), r.GetString(5),
        new CharacterId(r.GetGuid(6)), new AccountId(r.GetGuid(7)),
        r.IsDBNull(8) ? null : new CharacterId(r.GetGuid(8)), r.IsDBNull(9) ? null : new AccountId(r.GetGuid(9)), r.IsDBNull(10) ? null : r.GetString(10),
        r.IsDBNull(11) ? null : CombatJson.Read<Combatant>(r.GetString(11)), r.IsDBNull(12) ? null : CombatJson.Read<Combatant>(r.GetString(12)),
        r.IsDBNull(13) ? null : CombatJson.Read<BattleState>(r.GetString(13)),
        r.GetInt32(14), r.GetFieldValue<DateTimeOffset>(15), r.GetInt32(16), r.GetInt32(17), r.GetInt64(18),
        r.GetFieldValue<DateTimeOffset>(19), r.GetFieldValue<DateTimeOffset>(20), r.IsDBNull(21) ? null : r.GetFieldValue<DateTimeOffset>(21));

    private static object?[] BattleArgs(Battle b) =>
    [
        b.Id, b.Kind, b.Status, b.RulesVersion, (decimal)b.Seed, b.SeedHash, b.CharacterA.Value, b.AccountA.Value, b.CharacterB?.Value, b.AccountB?.Value, b.NpcId,
        b.CombatantA is null ? null : CombatJson.Write(b.CombatantA), b.CombatantB is null ? null : CombatJson.Write(b.CombatantB),
        b.State is null ? null : CombatJson.Write(b.State), b.Version, b.Deadline, b.TimeoutsA, b.TimeoutsB, b.RewardQi, b.CreatedAt, b.UpdatedAt, b.FinishedAt,
    ];

    public Task<Loadout?> FindLoadoutAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync("SELECT account_id, slots::text, version FROM combat.loadouts WHERE character_id = $1",
            r => new Loadout(characterId, new AccountId(r.GetGuid(0)), CombatJson.Read<List<string?>>(r.GetString(1)), r.GetInt32(2)), ct, characterId.Value);

    public async Task<bool> TrySaveLoadoutAsync(IUnitOfWork uow, Loadout l, int expectedVersion, CancellationToken ct)
    {
        var json = CombatJson.Write(l.Slots);
        return expectedVersion == 0
            ? await Pg(uow).ExecuteAsync("INSERT INTO combat.loadouts (character_id, account_id, slots, version) VALUES ($1, $2, $3::jsonb, $4) ON CONFLICT (character_id) DO NOTHING",
                ct, l.CharacterId.Value, l.AccountId.Value, json, l.Version) == 1
            : await Pg(uow).ExecuteAsync("UPDATE combat.loadouts SET slots = $3::jsonb, version = $4 WHERE character_id = $1 AND account_id = $2 AND version = $5",
                ct, l.CharacterId.Value, l.AccountId.Value, json, l.Version, expectedVersion) == 1;
    }

    public async Task<IReadOnlyList<TechniqueMastery>> ListMasteryAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await Pg(uow).QueryAsync("SELECT technique_id, level, xp FROM combat.technique_mastery WHERE character_id = $1 ORDER BY technique_id",
            r => new TechniqueMastery(characterId, r.GetString(0), r.GetInt32(1), r.GetInt32(2)), ct, characterId.Value);

    public Task SaveMasteryAsync(IUnitOfWork uow, TechniqueMastery m, CancellationToken ct) =>
        Pg(uow).ExecuteAsync(
            "INSERT INTO combat.technique_mastery (character_id, technique_id, level, xp) VALUES ($1, $2, $3, $4) " +
            "ON CONFLICT (character_id, technique_id) DO UPDATE SET level = EXCLUDED.level, xp = EXCLUDED.xp",
            ct, m.CharacterId.Value, m.TechniqueId, m.Level, m.Xp);

    public Task AddBattleAsync(IUnitOfWork uow, Battle b, CancellationToken ct) =>
        Pg(uow).ExecuteAsync(
            "INSERT INTO combat.battles (id, kind, status, rules_version, seed, seed_hash, character_a, account_a, character_b, account_b, npc_id, combatant_a, combatant_b, state, " +
            "version, deadline, timeouts_a, timeouts_b, reward_qi, created_at, updated_at, finished_at) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12::jsonb, $13::jsonb, $14::jsonb, $15, $16, $17, $18, $19, $20, $21, $22)",
            ct, BattleArgs(b));

    public Task<Battle?> FindBattleAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {BattleColumns} FROM combat.battles WHERE id = $1", MapBattle, ct, id);

    public async Task<bool> TryUpdateBattleAsync(IUnitOfWork uow, Battle b, int expectedVersion, CancellationToken ct)
    {
        if (b.Version != expectedVersion + 1) return false;
        return await Pg(uow).ExecuteAsync(
            "UPDATE combat.battles SET status = $2, combatant_a = $3::jsonb, combatant_b = $4::jsonb, state = $5::jsonb, version = $6, deadline = $7, " +
            "timeouts_a = $8, timeouts_b = $9, reward_qi = $10, updated_at = $11, finished_at = $12 WHERE id = $1 AND version = $13",
            ct, b.Id, b.Status,
            b.CombatantA is null ? null : CombatJson.Write(b.CombatantA), b.CombatantB is null ? null : CombatJson.Write(b.CombatantB),
            b.State is null ? null : CombatJson.Write(b.State), b.Version, b.Deadline, b.TimeoutsA, b.TimeoutsB, b.RewardQi, b.UpdatedAt, b.FinishedAt,
            expectedVersion) == 1;
    }

    public async Task<IReadOnlyList<Battle>> ListOpenBattlesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            $"SELECT {BattleColumns} FROM combat.battles WHERE (character_a = $1 OR character_b = $1) AND status IN ('active', 'pending_acceptance') ORDER BY id",
            MapBattle, ct, characterId.Value);

    public async Task<int> CountRewardedPveWinsSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct) =>
        (int)(await Pg(uow).QueryAsync(
            "SELECT count(*) FROM combat.battles WHERE character_a = $1 AND kind = 'pve' AND reward_qi > 0 AND finished_at >= $2",
            r => r.GetInt64(0), ct, characterId.Value, since)).Single();

    public async Task<IReadOnlyList<SubmittedIntent>> ListIntentsAsync(IUnitOfWork uow, Guid battleId, int turn, CancellationToken ct) =>
        await Pg(uow).QueryAsync("SELECT side, intent::text, submitted_at FROM combat.intents WHERE battle_id = $1 AND turn = $2 ORDER BY side",
            r => new SubmittedIntent(battleId, turn, r.GetString(0)[0], CombatJson.Read<Intent>(r.GetString(1)), r.GetFieldValue<DateTimeOffset>(2)), ct, battleId, turn);

    public Task AddIntentAsync(IUnitOfWork uow, SubmittedIntent i, CancellationToken ct) =>
        Pg(uow).ExecuteAsync("INSERT INTO combat.intents (battle_id, turn, side, intent, submitted_at) VALUES ($1, $2, $3, $4::jsonb, $5)",
            ct, i.BattleId, i.Turn, i.Side.ToString(), CombatJson.Write(i.Intent), i.SubmittedAt);

    public Task AddTurnAsync(IUnitOfWork uow, TurnRecord t, CancellationToken ct) =>
        Pg(uow).ExecuteAsync(
            "INSERT INTO combat.turns (battle_id, turn, intent_a, intent_b, forced_a, forced_b, events, state_after, resolved_at) " +
            "VALUES ($1, $2, $3::jsonb, $4::jsonb, $5, $6, $7::jsonb, $8::jsonb, $9)",
            ct, t.BattleId, t.Turn, CombatJson.Write(t.IntentA), CombatJson.Write(t.IntentB), t.ForcedA, t.ForcedB,
            CombatJson.Write(t.Events), CombatJson.Write(t.StateAfter), t.ResolvedAt);

    public async Task<IReadOnlyList<TurnRecord>> ListTurnsAsync(IUnitOfWork uow, Guid battleId, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            "SELECT turn, intent_a::text, intent_b::text, forced_a, forced_b, events::text, state_after::text, resolved_at FROM combat.turns WHERE battle_id = $1 ORDER BY turn",
            r => new TurnRecord(battleId, r.GetInt32(0), CombatJson.Read<Intent>(r.GetString(1)), CombatJson.Read<Intent>(r.GetString(2)), r.GetBoolean(3), r.GetBoolean(4),
                CombatJson.Read<List<TurnEvent>>(r.GetString(5)), CombatJson.Read<BattleState>(r.GetString(6)), r.GetFieldValue<DateTimeOffset>(7)),
            ct, battleId);
}
