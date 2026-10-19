using System.Text.Json;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

/// <summary>
/// 1.6 content audit: every item can be obtained somewhere, every crafting level has recipes, and every era of the 66 realms has
/// techniques, flames, methods and expeditions to reach for. Reads the shipped JSON so a content edit cannot reopen a gap.
/// </summary>
public class ContentCoverageTests
{
    private static JsonElement Load(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-config", file))).RootElement;

    private static readonly int[] EraStarts = [0, 11, 22, 33, 44, 55];

    /// <summary>Every {"item": "x"} reachable from the element, skipping recipe inputs (those consume, not give).</summary>
    private static void Gives(JsonElement e, HashSet<string> acc)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "inputs") continue;
                    if (p.Name == "item" && p.Value.ValueKind == JsonValueKind.String) acc.Add(p.Value.GetString()!);
                    Gives(p.Value, acc);
                }

                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Gives(x, acc);
                break;
        }
    }

    [Fact]
    public void Every_item_has_a_source()
    {
        var items = Load("items.json");
        var all = items.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
        var sources = new HashSet<string>();
        Gives(items.GetProperty("lootTables"), sources);
        Gives(items.GetProperty("dismantle"), sources);
        var world = Load("world.json");
        Gives(world.GetProperty("nodeKinds"), sources);
        foreach (var r in Load("crafting.json").GetProperty("recipes").EnumerateArray()) sources.Add(r.GetProperty("output").GetProperty("item").GetString()!);
        var combat = Load("combat.json");
        Gives(combat.GetProperty("tower"), sources);
        Gives(combat.GetProperty("worldBoss"), sources);
        var estate = Load("estate.json");
        Gives(estate.GetProperty("expeditions"), sources);
        Gives(estate.GetProperty("garden"), sources);
        Gives(Load("quests.json"), sources);
        Gives(Load("sects.json"), sources);
        var orphans = all.Where(i => !sources.Contains(i)).ToList();
        Assert.Equal(0, orphans.Count, "items without a source: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Every_profession_level_has_recipes()
    {
        var recipes = Load("crafting.json").GetProperty("recipes").EnumerateArray().ToList();
        foreach (var kind in new[] { "alchemy", "forge" })
        {
            var levels = recipes.Where(r => r.GetProperty("kind").GetString() == kind)
                .Select(r => r.TryGetProperty("minLevel", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 1).ToHashSet();
            var missing = Enumerable.Range(1, 10).Where(l => !levels.Contains(l) && !levels.Contains(l - 1)).ToList(); // at most one level without a new recipe
            Assert.Equal(0, missing.Count, $"{kind}: no new recipe around level(s) {string.Join(",", missing)}");
            Assert.True(levels.Contains(10), kind + " has a master recipe");
        }
    }

    [Fact]
    public void Every_era_has_something_to_reach_for()
    {
        var techniques = Load("combat.json").GetProperty("techniques").EnumerateArray().ToList();
        var methods = Load("cultivation.json").GetProperty("methods").EnumerateArray().Select(m => m.GetProperty("minRealm").GetInt32()).ToList();
        var expeditions = Load("estate.json").GetProperty("expeditions").EnumerateArray().Select(x => x.GetProperty("minRealm").GetInt32()).ToList();
        var flames = Load("arts.json").GetProperty("flames").EnumerateArray().Select(f => f.GetProperty("minRealm").GetInt32()).ToList();
        foreach (var start in EraStarts)
        {
            var end = start + 11;
            Assert.True(techniques.Any(t => t.TryGetProperty("minRealm", out var r) ? r.GetInt32() >= start && r.GetInt32() < end : start == 0), $"technique in era from {start}");
            Assert.True(methods.Any(r => r >= start && r < end), $"method in era from {start}");
            Assert.True(expeditions.Any(r => r >= start && r < end), $"expedition in era from {start}");
            Assert.True(flames.Any(r => r >= start - 5 && r < end), $"flame around era from {start}");
        }
    }
}
