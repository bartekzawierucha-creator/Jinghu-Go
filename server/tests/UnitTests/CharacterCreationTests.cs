using JianghuGo.Modules.Characters;
using JianghuGo.Modules.Characters.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class CharacterCreationTests
{
    private static readonly CharacterCreationConfig Config =
        CharactersModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "character-creation.json"));

    private static readonly HashSet<string> NoLocks = [];

    [Fact]
    public void Shipped_config_is_valid()
    {
        Assert.Equal(0, CharacterConfigValidator.Validate(Config).Count);
        Assert.Equal(9, Config.Stats.Count);
    }

    [Fact]
    public void Validator_reports_missing_rarity_slot()
    {
        var broken = Config with { Traits = [.. Config.Traits.Where(t => !(t.Category == "bloodline" && t.Rarity == "mythic"))] };
        var errors = CharacterConfigValidator.Validate(broken);
        Assert.True(errors.Any(e => e.Contains("bloodline", StringComparison.Ordinal) && e.Contains("mythic", StringComparison.Ordinal)), string.Join("; ", errors));
    }

    [Fact]
    public void Validator_rejects_free_rerolls_unknown_stats_and_locking_everything()
    {
        var broken = Config with
        {
            Reroll = new RerollRules(0, 2, 3),
            Traits = [.. Config.Traits.Select((t, i) => i == 0 ? t with { Modifiers = new Dictionary<string, int> { ["luck"] = 1 } } : t)],
        };
        var errors = CharacterConfigValidator.Validate(broken);
        Assert.True(errors.Any(e => e.Contains("baseCost", StringComparison.Ordinal)));
        Assert.True(errors.Any(e => e.Contains("maxLockedTraits", StringComparison.Ordinal)));
        Assert.True(errors.Any(e => e.Contains("unknown stat 'luck'", StringComparison.Ordinal)));
    }

    [Fact]
    public void Roll_is_deterministic_for_seed()
    {
        var a = CharacterRoller.Roll(Config, 123456789, null, NoLocks);
        var b = CharacterRoller.Roll(Config, 123456789, null, NoLocks);
        Assert.Equal(a.Traits, b.Traits);
        Assert.SequenceEqual(a.Stats, b.Stats);
    }

    [Fact]
    public void Locked_traits_survive_reroll()
    {
        var first = CharacterRoller.Roll(Config, 1, null, NoLocks);
        for (ulong seed = 2; seed < 500; seed++)
        {
            var next = CharacterRoller.Roll(Config, seed, first.Traits, new HashSet<string> { "bloodline", "constitution" });
            Assert.Equal(first.Traits.Bloodline, next.Traits.Bloodline);
            Assert.Equal(first.Traits.Constitution, next.Traits.Constitution);
        }
    }

    [Fact]
    public void Stats_always_within_configured_bounds_and_traits_resolve()
    {
        for (ulong seed = 0; seed < 5_000; seed++)
        {
            var roll = CharacterRoller.Roll(Config, seed * 7919, null, NoLocks);
            Assert.SequenceEqual(StatIds.All, roll.Stats.Select(s => s.Key));
            foreach (var (stat, def) in roll.Stats.Zip(Config.Stats))
            {
                Assert.InRange(stat.Value, def.Min, def.Max, stat.Key);
            }

            foreach (var category in TraitCategories.All)
            {
                Assert.Equal(category, Config.Trait(roll.Traits.Get(category)).Category);
            }
        }
    }

    [Fact]
    public void Rarity_distribution_follows_configured_weights()
    {
        const int n = 40_000;
        var counts = Rarities.All.ToDictionary(r => r, _ => 0);
        for (ulong seed = 0; seed < n; seed++)
        {
            counts[Config.Trait(CharacterRoller.Roll(Config, seed, null, NoLocks).Traits.Background).Rarity]++;
        }

        var total = Config.RarityWeights.Values.Sum();
        foreach (var rarity in Rarities.All)
        {
            var expected = (double)n * Config.RarityWeights[rarity] / total;
            var tolerance = 5 * Math.Sqrt(expected) + 5; // ~5 sigma
            Assert.True(Math.Abs(counts[rarity] - expected) <= tolerance, $"{rarity}: got {counts[rarity]}, expected ~{expected:0}");
        }
    }

    [Fact]
    public void Reroll_cost_follows_config()
    {
        Assert.Equal(1, Config.RerollCost(0));
        Assert.Equal(5, Config.RerollCost(2));
    }

    [Fact]
    public void Names_accept_wuxia_and_polish_names()
    {
        foreach (var ok in new[] { "Li Mu Bai", "Zhāng Wěi", "Łucja", "Xiao-Long", "O'Shen", "张三丰" })
        {
            Assert.True(CharacterNames.Validate(ok, Config.Name).IsSuccess, ok);
        }

        Assert.Equal("ŁUCJA", CharacterNames.Validate("  Łucja ", Config.Name).Value.Normalized);
    }

    [Fact]
    public void Names_reject_garbage()
    {
        foreach (var bad in new[] { null, "", " ", "x", "Ab  c", "<script>", "1abc", "-abc", "abc\u0000", new string('a', 25), "Li\tMu" })
        {
            Assert.False(CharacterNames.Validate(bad, Config.Name).IsSuccess, bad ?? "null");
        }
    }
}
