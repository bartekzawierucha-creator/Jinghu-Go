using JianghuGo.Modules.Market;
using JianghuGo.Modules.Market.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class MarketRulesTests
{
    private static readonly MarketConfig Config = MarketModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "market.json"));

    [Fact]
    public void Shipped_config_is_valid()
    {
        Assert.Equal(0, MarketConfigValidator.Validate(Config).Count);
        Assert.True(MarketConfigValidator.Validate(Config with { MinPrice = 0 }).Count > 0);
        Assert.True(MarketConfigValidator.Validate(Config with { SaleTaxPermille = 900 }).Count > 0);
    }

    [Fact]
    public void Fee_has_a_floor_and_tax_never_takes_the_whole_price()
    {
        Assert.Equal(1, MarketRules.ListingFee(Config, 1));
        Assert.Equal(1, MarketRules.ListingFee(Config, 199));
        Assert.Equal(10, MarketRules.ListingFee(Config, 1000));
        Assert.Equal(10_000_000, MarketRules.ListingFee(Config, 1_000_000_000));
        Assert.Equal(0, MarketRules.SaleTax(Config, 1));
        Assert.Equal(0, MarketRules.SaleTax(Config, 19));
        Assert.Equal(3, MarketRules.SaleTax(Config, 60));
        var greedy = Config with { SaleTaxPermille = 500 };
        Assert.Equal(1, MarketRules.SaleTax(greedy, 2));
        Assert.Equal(1, MarketRules.SaleTax(greedy with { SaleTaxPermille = 500 }, 3));
    }
}
