using JianghuGo.Modules.Crafting;
using JianghuGo.Modules.Crafting.Domain;
using JianghuGo.Modules.Inventory;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;
using Microsoft.Extensions.Configuration;

namespace JianghuGo.UnitTests;

public class CraftingRulesTests
{
    private static readonly IItemCatalog Catalog = InventoryModule.LoadCatalog(new ConfigurationBuilder().Build());

    private static readonly CraftingConfig Config =
        CraftingModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "crafting.json"), Catalog);

    [Fact]
    public void Shipped_config_is_valid_and_cross_checked_against_items()
    {
        Assert.Equal(0, CraftingConfigValidator.Validate(Config, id => Catalog.Find(id)?.Stackable).Count);
        var unknownInput = Config with { Recipes = [Config.Recipes[0] with { Inputs = [new ItemAmount("unobtainium", 1)] }] };
        Assert.True(CraftingConfigValidator.Validate(unknownInput, id => Catalog.Find(id)?.Stackable).Count > 0);
        var failingForge = Config with { Recipes = [Config.Recipes[0] with { SuccessPermille = 500 }] };
        Assert.True(CraftingConfigValidator.Validate(failingForge, id => Catalog.Find(id)?.Stackable).Count > 0);
        var risingChance = Config with { Upgrade = Config.Upgrade with { SuccessPermilleByTargetLevel = [.. Config.Upgrade.SuccessPermilleByTargetLevel.Reverse()] } };
        Assert.True(CraftingConfigValidator.Validate(risingChance, id => Catalog.Find(id)?.Stackable).Count > 0);
    }

    [Fact]
    public void Insight_helps_alchemy_only_and_never_beyond_certainty()
    {
        var pill = Config.Find("brew_qi_pill")!;
        var sword = Config.Find("forge_iron_sword")!;
        Assert.Equal(850, CraftingRules.ChancePermille(Config, pill, 0));
        Assert.Equal(920, CraftingRules.ChancePermille(Config, pill, 7));
        Assert.Equal(1000, CraftingRules.ChancePermille(Config, pill, 40));
        Assert.Equal(1000, CraftingRules.ChancePermille(Config, sword, 0));
        Assert.Equal(850, CraftingRules.ChancePermille(Config, pill, -5));
    }

    [Fact]
    public void Upgrade_costs_grow_with_level_and_rolls_are_reproducible()
    {
        Assert.Equal(20, CraftingRules.UpgradeStones(Config, 1));
        Assert.Equal(300, CraftingRules.UpgradeStones(Config, 15));
        Assert.Equal(1000, CraftingRules.UpgradeChancePermille(Config, 1));
        Assert.Equal(200, CraftingRules.UpgradeChancePermille(Config, 15));
        Assert.SequenceEqual(CraftingRules.Rolls(77, 10), CraftingRules.Rolls(77, 10));
        Assert.True(CraftingRules.Rolls(77, 10).All(r => r is >= 0 and < 1000));
    }
}
