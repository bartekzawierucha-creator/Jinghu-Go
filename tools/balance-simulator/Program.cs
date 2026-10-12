using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Combat.Domain;

// Usage: dotnet run --project tools/balance-simulator -- [battlesPerPair=500] [path/to/combat.json]
var battles = args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 500;
var configPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "../../../../../content/game-config/combat.json");
var config = CombatModule.LoadConfig(Path.GetFullPath(configPath));

var average = new Dictionary<string, int>
{
    ["strength"] = 10, ["spirit"] = 10, ["agility"] = 10, ["constitution"] = 10, ["insight"] = 10,
    ["willpower"] = 10, ["focus"] = 10, ["elementalAffinity"] = 5, ["fate"] = 5,
};
var builds = config.StarterLoadouts.Select(kv => new Combatant($"{kv.Key} (start)", average,
        [.. kv.Value.Select(id => new EquippedTechnique(id, 1))], null, config.PathHpBonusPermille[kv.Key]))
    .Concat(config.Npcs.Select(n => new Combatant(n.Id, n.Stats, [.. n.Loadout.Select(s => s is null ? null : new EquippedTechnique(s.Id, s.Level))], n.HpOverride)))
    .ToList();

Console.WriteLine($"Rules {config.RulesVersion}, {battles} battles per ordered pair, AI vs AI. Row = side A win % (draws) [avg turns]");
Console.Write($"{"",-18}");
foreach (var b in builds) Console.Write($"{b.Name,-20}");
Console.WriteLine();
foreach (var a in builds)
{
    Console.Write($"{a.Name,-18}");
    foreach (var b in builds)
    {
        int wins = 0, draws = 0, turns = 0;
        for (var seed = 1UL; seed <= (ulong)battles; seed++)
        {
            var s = CombatEngine.Start(config, a, b);
            while (s.Outcome is null)
            {
                var rng = CombatEngine.TurnRng(seed, s.Turn);
                s = CombatEngine.ResolveTurn(config, a, b, s,
                    CombatAi.Choose(config, a, s.A, s.Distance, rng), CombatAi.Choose(config, b, s.B, s.Distance, rng), rng).State;
            }

            wins += s.Outcome == Outcomes.AWon ? 1 : 0;
            draws += s.Outcome == Outcomes.Draw ? 1 : 0;
            turns += s.Turn;
        }

        Console.Write($"{100.0 * wins / battles,5:0}% ({100.0 * draws / battles,2:0}) [{(double)turns / battles,4:0.0}]  ");
    }

    Console.WriteLine();
}
