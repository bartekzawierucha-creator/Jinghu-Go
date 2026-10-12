using JianghuGo.Domain;
using JianghuGo.Modules.Sects;
using JianghuGo.Modules.Sects.Domain;
using JianghuGo.Modules.Territory;
using JianghuGo.Modules.Territory.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class SectTerritoryRulesTests
{
    private static string Path(string f) => System.IO.Path.Combine(AppContext.BaseDirectory, "game-config", f);

    private static readonly TerritoryConfig Territory = TerritoryModule.LoadConfig(Path("territory.json"));

    [Fact]
    public void Shipped_configs_are_valid()
    {
        Assert.Equal(0, SectsConfigValidator.Validate(SectsModule.LoadConfig(Path("sects.json"))).Count);
        Assert.Equal(0, TerritoryConfigValidator.Validate(Territory).Count);
    }

    [Fact]
    public void Permission_matrix()
    {
        foreach (var action in new[] { SectActions.DecideApplication, SectActions.Kick, SectActions.WageWar, SectActions.CollectTribute })
        {
            Assert.True(SectPolicy.Can(SectRoles.Leader, action), action);
            Assert.True(SectPolicy.Can(SectRoles.Elder, action), action);
            Assert.False(SectPolicy.Can(SectRoles.Member, action), action);
        }

        foreach (var action in new[] { SectActions.SetRole, SectActions.SetSeat })
        {
            Assert.True(SectPolicy.Can(SectRoles.Leader, action), action);
            Assert.False(SectPolicy.Can(SectRoles.Elder, action), action);
        }

        Assert.False(SectPolicy.Can("emperor", SectActions.Kick));
        Assert.False(SectPolicy.Can(SectRoles.Leader, "unknown_action"));
        Assert.True(SectPolicy.CanKick(SectRoles.Leader, SectRoles.Elder));
        Assert.True(SectPolicy.CanKick(SectRoles.Elder, SectRoles.Member));
        Assert.False(SectPolicy.CanKick(SectRoles.Elder, SectRoles.Elder));
        Assert.False(SectPolicy.CanKick(SectRoles.Elder, SectRoles.Leader));
        Assert.False(SectPolicy.CanKick(SectRoles.Member, SectRoles.Member));
    }

    [Fact]
    public void Sect_names_are_validated_and_normalised()
    {
        Assert.Equal("SEKTA ŻURAWIA", SectNames.Validate("  Sekta Żurawia ").Value.Normalized);
        Assert.Equal(SectNames.Validate("Wudang 2").Value.Normalized, SectNames.Validate("wudang 2").Value.Normalized);
        foreach (var bad in new[] { null, "ab", "1abc", "Sekta  Dwie", "<script>", new string('a', 25), "Sekta\nWudang" })
            Assert.False(SectNames.Validate(bad).IsSuccess, bad ?? "null");
    }

    [Fact]
    public void Territory_keys_limits_and_war_outcome()
    {
        Assert.Equal("398.1001", TerritoryRules.KeyOf(Territory, new GeoPoint(50.0614, 19.9372)));
        Assert.Equal("-1.-1", TerritoryRules.KeyOf(Territory, new GeoPoint(-0.01, -0.01)));
        Assert.True(TerritoryRules.IsKey("-1.-1"));
        foreach (var bad in new[] { null, "", "1", "1.2.3", "a.b", "123456789.1" }) Assert.False(TerritoryRules.IsKey(bad), bad ?? "null");
        var (lat, lon, size) = TerritoryRules.Bounds(Territory, "398.1001");
        Assert.True(Math.Abs(lat - 50.05) < 1e-9 && Math.Abs(lon - 19.9) < 1e-9 && Math.Abs(size - 0.05) < 1e-12);
        Assert.Equal(1, TerritoryRules.MaxTerritories(Territory, 4));
        Assert.Equal(2, TerritoryRules.MaxTerritories(Territory, 5));
        Assert.Equal(7, TerritoryRules.MaxTerritories(Territory, 30));

        var now = DateTimeOffset.UnixEpoch;
        var war = new War(Guid.NewGuid(), "1.1", Guid.NewGuid(), Guid.NewGuid(), WarStatuses.Declared, now, now.AddHours(12), now.AddHours(36), 3, 3, null, null, 1);
        Assert.Equal(war.DefenderSectId, TerritoryRules.Winner(war), "ties keep the territory with the defender");
        Assert.Equal(war.AttackerSectId, TerritoryRules.Winner(war with { AttackerScore = 4 }));
        Assert.False(war.InWindow(now.AddHours(11)));
        Assert.True(war.InWindow(now.AddHours(12)));
        Assert.False(war.InWindow(now.AddHours(36)));
        Assert.True(war.Due(now.AddHours(36)));
    }
}
