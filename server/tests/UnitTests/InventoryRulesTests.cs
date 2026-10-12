using JianghuGo.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Inventory;
using JianghuGo.Modules.Inventory.Domain;
using JianghuGo.Modules.World;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class InventoryRulesTests
{
    private static string ConfigPath(string file) => Path.Combine(AppContext.BaseDirectory, "game-config", file);

    private static readonly ItemsConfig Items = InventoryModule.LoadConfig(ConfigPath("items.json"));

    private static ItemInstance Equipped(string itemId, int level = 0) =>
        new(Guid.NewGuid(), new CharacterId(Guid.NewGuid()), new AccountId(Guid.NewGuid()), itemId, level, InstanceStates.Equipped, 1, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Shipped_items_config_is_valid_and_rejects_bad_specs()
    {
        Assert.Equal(0, ItemsConfigValidator.Validate(Items).Count);
        var bad = Items with { Items = [.. Items.Items, Items.Item("iron_sword") with { Id = "broken", Equipment = null }] };
        Assert.True(ItemsConfigValidator.Validate(bad).Count > 0);
        var badLoot = Items with { LootTables = new Dictionary<string, LootTable> { ["x"] = new(5, [new LootDrop("nope", 500, 1, 1)]) } };
        Assert.True(ItemsConfigValidator.Validate(badLoot).Any(e => e.Contains("unknown item", StringComparison.Ordinal)));
    }

    /// <summary>The configs are separate files owned by different modules; their references must still line up.</summary>
    [Fact]
    public void Loot_tables_and_world_yields_reference_existing_npcs_and_items()
    {
        var combat = CombatModule.LoadConfig(ConfigPath("combat.json"));
        foreach (var npc in Items.LootTables.Keys)
            Assert.True(combat.Npc(npc) is not null, $"loot table for unknown NPC '{npc}'");
        var world = WorldModule.LoadConfig(ConfigPath("world.json"));
        foreach (var y in world.NodeKinds.SelectMany(k => k.Yields ?? []))
            Assert.True(Items.Find(y.Item) is { Stackable: true }, $"world yield '{y.Item}' is not a stackable item");
    }

    [Fact]
    public void Upgrade_levels_scale_equipment_stats_down_rounded()
    {
        Assert.Equal(2, InventoryRules.ScaledStat(Items, 2, 0));
        Assert.Equal(2, InventoryRules.ScaledStat(Items, 2, 4));      // 2 × 1.4 = 2.8 → 2
        Assert.Equal(3, InventoryRules.ScaledStat(Items, 2, 5));      // 2 × 1.5 = 3
        Assert.Equal(5, InventoryRules.ScaledStat(Items, 2, 15));     // 2 × 2.5 = 5
        var bonuses = InventoryRules.Bonuses(Items, [Equipped("wolf_fang_dagger", 10), Equipped("jade_talisman")]);
        Assert.Equal(6, bonuses["agility"]);
        Assert.Equal(2, bonuses["strength"]);
        Assert.Equal(3, bonuses["spirit"]);
        Assert.Equal(2, bonuses["insight"]);
    }

    [Fact]
    public void Capacity_grows_only_with_an_equipped_ring_and_slots_ignore_equipped_items()
    {
        Assert.Equal(60, InventoryRules.Capacity(Items, []));
        Assert.Equal(80, InventoryRules.Capacity(Items, [Equipped("spatial_ring")]));
        var bag = Equipped("iron_sword") with { State = InstanceStates.Bag };
        Assert.Equal(4, InventoryRules.SlotsUsed(3, [bag, Equipped("spatial_ring"), bag with { State = InstanceStates.Listed }]));
    }

    [Fact]
    public void Loot_is_deterministic_per_seed_and_matches_configured_chances()
    {
        var wolf = Items.LootTables["grey_wolf"];
        Assert.Equal(InventoryRules.RollLoot(wolf, 99).Items.Count, InventoryRules.RollLoot(wolf, 99).Items.Count);
        const int n = 20_000;
        var fangs = 0;
        long total = 0;
        for (ulong s = 1; s <= n; s++)
        {
            var (stones, items) = InventoryRules.RollLoot(wolf, InventoryRules.SeedFrom(Guid.Empty, new Guid((int)s, 0, 0, new byte[8])));
            Assert.Equal(15, stones);
            if (items.Count == 1)
            {
                fangs++;
                total += items[0].Quantity;
                Assert.True(items[0].Quantity is 1 or 2);
            }
        }

        Assert.True(Math.Abs(fangs / (double)n - 0.6) < 0.02, $"drop rate {fangs / (double)n}");
        Assert.True(Math.Abs(total / (double)fangs - 1.5) < 0.05, $"mean quantity {total / (double)fangs}");
        Assert.NotEqual(InventoryRules.SeedFrom(Guid.NewGuid(), Guid.Empty), InventoryRules.SeedFrom(Guid.NewGuid(), Guid.Empty));
    }
}
