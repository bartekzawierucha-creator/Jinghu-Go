using JianghuGo.Domain;
using JianghuGo.Modules.Arts;
using JianghuGo.Modules.Arts.Domain;
using JianghuGo.Modules.Estate;
using JianghuGo.Modules.Estate.Domain;
using JianghuGo.Modules.Inventory;
using JianghuGo.Modules.Quests;
using JianghuGo.Modules.Quests.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Sects;
using JianghuGo.Modules.Sects.Domain;
using JianghuGo.Modules.World;
using JianghuGo.Modules.World.Domain;
using JianghuGo.TestKit;
using Microsoft.Extensions.Configuration;

namespace JianghuGo.UnitTests;

/// <summary>MVP 1.5 rules: path arts, estate, secret/seasonal quests, regions, sect development.</summary>
public class GrowthRulesTests
{
    private static string Cfg(string name) => Path.Combine(AppContext.BaseDirectory, "game-config", name);

    private static readonly ArtsConfig Arts = ArtsModule.LoadConfig(Cfg("arts.json"));
    private static readonly EstateConfig Estate = EstateModule.LoadConfig(Cfg("estate.json"));
    private static readonly QuestsConfig Quests = QuestsModule.LoadConfig(Cfg("quests.json"));
    private static readonly WorldConfig World = WorldModule.LoadConfig(Cfg("world.json"));
    private static readonly SectsConfig Sects = SectsModule.LoadConfig(Cfg("sects.json"));
    private static readonly Modules.Combat.Domain.CombatConfig Combat = CombatModule.LoadConfig(Cfg("combat.json"));
    private static readonly Modules.Inventory.Contracts.IItemCatalog Items = InventoryModule.LoadCatalog(new ConfigurationBuilder().Build());

    private static readonly CharacterId Hero = new(Guid.Parse("0f3c1b6e-2222-4c4c-9a9a-123456789abc"));
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shipped_configs_are_valid_and_reference_real_items_and_npcs()
    {
        Assert.Equal(0, ArtsConfigValidator.Validate(Arts).Count);
        Assert.Equal(0, EstateConfigValidator.Validate(Estate).Count);
        Assert.Equal(26, Arts.Flames.Count);
        Assert.Equal(26, Arts.Flames.Select(f => f.Rank).Distinct().Count());
        // 1.6: no gap in the flame ladder (a new flame at least every ~6 realms up to realm 44, then the last two)
        var realms = Arts.Flames.Select(f => f.MinRealm).Distinct().Order().ToList();
        for (var i = 1; i < realms.Count; i++) Assert.True(realms[i] - realms[i - 1] <= 20, $"gap {realms[i - 1]}..{realms[i]}");
        Assert.Equal(2000, ArtsRules.FlameResonancePermille(Arts.Flame("soul_candle")!, 10));
        Assert.Equal(1000, ArtsRules.FlameResonancePermille(Arts.Flame("first_dawn")!, 10));
        foreach (var v in Arts.Venoms) Assert.True(Items.Find(v.Item) is not null, "venom item " + v.Item);
        foreach (var f in Arts.Flames)
        {
            Assert.True(Combat.Npc(f.Spirit) is not null, "flame spirit " + f.Spirit);
            foreach (var s in f.Discover.Where(s => s.Kind == FlameSourceKinds.Npc)) Assert.True(Combat.Npc(s.Ref!) is not null, "flame source npc " + s.Ref);
        }

        foreach (var item in new[] { Arts.FlameRules.FeedItem, Arts.FlameRules.TraceItem, Arts.Toxin.AntidoteItem, Arts.Gu.HerbItem, Arts.Tempering.PillItem, Arts.SwordIntent.EchoItem })
            Assert.True(Items.Find(item) is not null, "arts item " + item);
        foreach (var b in Arts.Tempering.Baths.SelectMany(b => b.Items)) Assert.True(Items.Find(b.Item) is not null, "bath item " + b.Item);
        foreach (var s in Arts.SwordIntent.Swordsmen) Assert.True(Combat.Npc(s) is not null, "swordsman " + s);
        foreach (var crop in Estate.Garden.Crops)
        {
            Assert.True(Items.Find(crop.Seed) is not null, "seed " + crop.Seed);
            foreach (var y in crop.Yields) Assert.True(Items.Find(y.Item) is not null, "yield " + y.Item);
        }

        foreach (var d in Estate.Expeditions.SelectMany(e => e.Drops)) Assert.True(Items.Find(d.Item) is not null, "expedition drop " + d.Item);
        foreach (var t in World.Regions!.Terrains.SelectMany(t => t.Bosses)) Assert.True(Combat.Npc(t) is not null, "region boss " + t);
        Assert.True(Combat.Npc(Sects.Development!.Boss.NpcId) is not null);
        foreach (var ch in Quests.StoryList.Where(c => c.Item is not null)) Assert.True(Items.Find(ch.Item) is not null, "story item " + ch.Item);
    }

    [Fact]
    public void Flames_level_by_feeding_and_slots_open_with_realms()
    {
        var f = new OwnedFlame("soul_candle", 1, 0, Now);
        var fed = ArtsRules.Feed(Arts, f, Arts.FlameRules.XpPerLevel);
        Assert.Equal(2, fed.Level);
        var maxed = ArtsRules.Feed(Arts, f, 1_000_000);
        Assert.Equal(Arts.FlameRules.MaxLevel, maxed.Level);
        Assert.Equal(0, maxed.Xp);
        Assert.Equal(1, ArtsRules.FlameSlots(Arts, 0));
        Assert.Equal(Arts.FlameRules.SlotRealms.Count, ArtsRules.FlameSlots(Arts, 65));
        var weak = Arts.Flame("soul_candle")!;
        var strong = Arts.Flame("first_dawn")!;
        Assert.True(ArtsRules.AbsorbChance(Arts, weak, 10) > ArtsRules.AbsorbChance(Arts, strong, 10));
        Assert.True(ArtsRules.SpiritPower(strong) < ArtsRules.SpiritPower(weak)); // 1.6: primal spirits are raised by their realm; power only sets the kind
        Assert.True(Arts.Flame("first_dawn")!.MinRealm > Arts.Flame("soul_candle")!.MinRealm);
    }

    [Fact]
    public void Absorption_settles_deterministically_into_a_flame_or_a_burn()
    {
        var p = ArtsProfile.New(Hero) with { Absorbing = new Absorption("soul_candle", Now, Now.AddHours(1), 1000) };
        Assert.True(ReferenceEquals(p.Absorbing, ArtsRules.Settle(Arts, p, Now.AddMinutes(30)).Absorbing));
        var done = ArtsRules.Settle(Arts, p, Now.AddHours(2));
        Assert.Null(done.Absorbing);
        Assert.Equal("soul_candle", done.Flames.Single().Id);

        var failing = p with { Absorbing = p.Absorbing! with { ChancePermille = 0 } };
        var burned = ArtsRules.Settle(Arts, failing, Now.AddHours(2));
        Assert.Equal(0, burned.Flames.Count);
        Assert.True(burned.BurnedUntil.HasValue);
    }

    [Fact]
    public void Slotted_flames_give_perks_stats_and_crafting_bonus_scaled_by_path()
    {
        var p = ArtsProfile.New(Hero) with { Flames = [new OwnedFlame("iron_red", 1, 0, Now)], Slots = ["iron_red"] };
        var flamePath = ArtsRules.Perks(Arts, p, "flame", 0);
        var qiPath = ArtsRules.Perks(Arts, p, "qi", 0);
        Assert.True(flamePath.DamagePermille > qiPath.DamagePermille);
        Assert.True(qiPath.DamagePermille > 0);
        Assert.True(ArtsRules.Stats(Arts, p, "flame", 0, Now)["strength"] > 0);
        Assert.True(ArtsRules.Crafting(Arts, p, "flame", 0).Forge > 0);
        var unslotted = p with { Slots = [] };
        Assert.Equal(0, ArtsRules.Perks(Arts, unslotted, "flame", 0).DamagePermille);
    }

    [Fact]
    public void Toxin_tempering_and_sword_levels_follow_their_thresholds()
    {
        Assert.Equal(0, ArtsRules.ToxinLevel(Arts, 0));
        Assert.Equal(1, ArtsRules.ToxinLevel(Arts, Arts.Toxin.Levels[0]));
        Assert.Equal(Arts.Toxin.Levels.Count, ArtsRules.ToxinLevel(Arts, int.MaxValue / 2));
        Assert.True(ArtsRules.TemperXpNeeded(Arts, 50) > ArtsRules.TemperXpNeeded(Arts, 10));
        Assert.True(ArtsRules.TemperChance(Arts, 90) < ArtsRules.TemperChance(Arts, 1));
        Assert.True(ArtsRules.TemperChance(Arts, 99) >= Arts.Tempering.ChanceMin);
        Assert.True(ArtsRules.TemperNeedsPill(Arts, 9));
        Assert.False(ArtsRules.TemperNeedsPill(Arts, 10));
        Assert.Equal(9, ArtsRules.SwordLevel(Arts, int.MaxValue / 2));
        var body = ArtsProfile.New(Hero) with { Temper = 40 };
        Assert.True(ArtsRules.Perks(Arts, body, "body", 0).HpPermille > ArtsRules.Perks(Arts, body, "qi", 0).HpPermille);
        Assert.Equal("Szpik", ArtsRules.TemperStageOf(Arts, 40)!.NamePl);
    }

    [Fact]
    public void Gu_grows_on_time_and_branches_scale_with_tiers()
    {
        var p = ArtsProfile.New(Hero) with { Gu = [new GuWorm(0, "warrior", 1, 1, Now, Now.AddHours(1))] };
        var grown = ArtsRules.Settle(Arts, p, Now.AddHours(2));
        Assert.Equal(2, grown.Gu.Single().Level);
        Assert.Null(grown.Gu.Single().GrowingUntil);
        Assert.True(ArtsRules.Perks(Arts, grown, "poison", 0).PetPower > 0);
        var branch = ArtsProfile.New(Hero) with { Branch = "sword" };
        Assert.Equal(0, ArtsRules.Perks(Arts, branch, "qi", 0).DamagePermille);
        Assert.True(ArtsRules.Perks(Arts, branch, "qi", 25).DamagePermille > ArtsRules.Perks(Arts, branch, "qi", 3).DamagePermille);
    }

    [Fact]
    public void Codex_titles_unlock_with_collections()
    {
        Assert.Equal(0, ArtsRules.Titles(Arts, ArtsProfile.New(Hero)).Count);
        var three = ArtsProfile.New(Hero) with { Flames = [.. Arts.Flames.Take(3).Select(f => new OwnedFlame(f.Id, 1, 0, Now))] };
        Assert.Equal("Zbieracz Iskier", ArtsRules.Titles(Arts, three).Single().NamePl);
    }

    [Fact]
    public void Estate_plots_disciples_and_expedition_loot()
    {
        Assert.Equal(Estate.Garden.PlotsBase, EstateRules.Plots(Estate, 0));
        Assert.Equal(Estate.Garden.MaxPlots, EstateRules.Plots(Estate, 65));
        Assert.Equal(Estate.Disciples.MaxSlots, EstateRules.DiscipleSlots(Estate, 65));
        Assert.Equal(Estate.Disciples.RecruitStones[^1], EstateRules.RecruitPrice(Estate, 100));
        var d = new Disciple(Guid.NewGuid(), "Lin", "herbs", 1, 0, null, null, Now);
        Assert.Equal(2, EstateRules.AddXp(Estate, d, Estate.Disciples.LevelXp).Level);
        var drops = Estate.Expeditions[0].Drops;
        var a = EstateRules.Roll(drops, new DeterministicRandom(7), 0, 0);
        var b = EstateRules.Roll(drops, new DeterministicRandom(7), 0, 0);
        Assert.Equal(string.Join(",", a), string.Join(",", b));
        var talented = EstateRules.Roll(drops, new DeterministicRandom(7), 0, 1000);
        Assert.True(talented.Sum(x => x.Quantity) >= a.Sum(x => x.Quantity));
    }

    [Fact]
    public void Quests_have_sixty_achievements_with_fifteen_secrets_and_seasons_wrap_the_year()
    {
        Assert.Equal(0, QuestsConfigValidator.Validate(Quests).Count);
        Assert.True(Quests.Achievements.Count >= 60);
        Assert.Equal(15, Quests.Achievements.Count(a => a.Secret));
        Assert.True(Quests.Achievements.Where(a => a.Secret).All(a => !string.IsNullOrWhiteSpace(a.RiddlePl)));
        var winter = Quests.SeasonalList.Single(s => s.Id == "e_winter");
        Assert.True(QuestRules.SeasonActive(winter, new DateOnly(2026, 12, 31)));
        Assert.True(QuestRules.SeasonActive(winter, new DateOnly(2027, 1, 2)));
        Assert.False(QuestRules.SeasonActive(winter, new DateOnly(2027, 2, 1)));
        Assert.Equal("S2026", QuestRules.SeasonPeriod(new DateOnly(2026, 12, 31)));
        Assert.Equal(Enumerable.Range(1, Quests.StoryList.Count).Sum(), Quests.StoryList.Sum(c => c.Number));
        Assert.True(QuestsConfigValidator.Validate(Quests with { Achievements = [Quests.Achievements[0] with { Secret = true, RiddlePl = null }] }).Count > 0);
    }

    [Fact]
    public void Regions_are_stable_named_with_terrain_weather_and_a_boss()
    {
        Assert.Equal(0, WorldConfigValidator.Validate(World).Count);
        var at = new GeoPoint(52.2297, 21.0122);
        var a = WorldGenerator.RegionAt(World, 424242, at, Now);
        var b = WorldGenerator.RegionAt(World, 424242, at, Now.AddDays(3));
        Assert.Equal(a.NamePl, b.NamePl);
        Assert.Equal(a.Terrain.Id, b.Terrain.Id);
        Assert.True(a.Terrain.Bosses.Contains(a.BossNpcId!));
        Assert.True(a.WeatherEndsAt > Now);
        Assert.True(WorldGenerator.IsNight(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero), 0));
        Assert.False(WorldGenerator.IsNight(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero), 0));
        Assert.True(WorldGenerator.IsNight(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero), 150));   // noon UTC is night in the Pacific
    }

    [Fact]
    public void Night_only_nodes_never_spawn_by_day()
    {
        var noonEpoch = WorldGenerator.NodeEpoch(World, new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
        var nodes = Enumerable.Range(0, 400).SelectMany(i => WorldGenerator.NodesInCell(World, 99, noonEpoch, 4200 + i, 10445)).ToList();
        Assert.True(nodes.Count > 0);
        var nightKinds = World.NodeKinds.Where(k => k.Time == "night").Select(k => k.Id).ToHashSet();
        Assert.False(nodes.Any(n => nightKinds.Contains(n.Kind)));
    }

    [Fact]
    public void Sect_missions_are_picked_per_sect_and_week()
    {
        Assert.Equal(0, SectsConfigValidator.Validate(Sects).Count);
        var dev = Sects.Development!;
        var sect = Guid.Parse("01a00000-0000-7000-8000-0000000000aa");
        var a = SectDevelopmentRules.MissionsFor(dev, sect, "2026-W42");
        Assert.Equal(dev.MissionsPerWeek, a.Count);
        Assert.Equal(a.Count, a.Select(m => m.Id).Distinct().Count());
        Assert.Equal(string.Join(",", a.Select(m => m.Id)), string.Join(",", SectDevelopmentRules.MissionsFor(dev, sect, "2026-W42").Select(m => m.Id)));
        Assert.Equal("2026-W42", SectDevelopmentRules.WeekOf(Now));
        var d = SectDevelopment.New(sect, "2026-W41") with { SealsBroken = 5, Buildings = new Dictionary<string, int> { ["training_hall"] = 2 } };
        var next = d.ForWeek("2026-W42");
        Assert.Equal(0, next.SealsBroken);
        Assert.Equal(2, next.Level("training_hall"));
        Assert.NotEqual(d.BossReference, next.BossReference);
    }
}
