using JianghuGo.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;

// Balance simulator: a typical player of each path at realm R (stage 12, all meridian points spent sensibly,
// techniques levelled with the realm) against the realm's tribulation and against every NPC. Prints win rates.
var cfg = CombatModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "combat.json"));
string[] stats = ["strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate"];
var mainOf = new Dictionary<string, string> { ["qi"] = "spirit", ["body"] = "strength", ["flame"] = "spirit", ["poison"] = "agility" };

Combatant Player(string path, int realm)
{
    var points = (realm * 12) + 12;
    var main = mainOf[path];
    var s = stats.ToDictionary(x => x, x => (x is "elementalAffinity" or "fate" ? 5 : 10) + (2 * realm));
    s[main] += points * 35 / 100;
    s["constitution"] += points * 25 / 100;
    s["agility"] += points * 20 / 100;
    s["focus"] += points * 10 / 100;
    s["willpower"] += points * 10 / 100;
    var gear = Math.Min(40, 4 + realm);
    s[main] += gear; s["constitution"] += gear / 2;
    var lvl = Math.Min(cfg.Progression.LevelCap(realm), Math.Clamp(1 + realm, 1, 10));
    var loadout = cfg.StarterLoadouts[path].Select(id => (EquippedTechnique?)new EquippedTechnique(id, lvl)).ToList();
    return new Combatant("P", s, loadout, null, cfg.PathHpBonusPermille[path], cfg.Perks(path), cfg.Elements?.Paths.GetValueOrDefault(path));
}

Combatant Npc(NpcDefinition n) => new(n.NamePl, n.Stats, [.. n.Loadout.Select(x => x is null ? null : new EquippedTechnique(x.Id, x.Level))], n.HpOverride,
    Element: cfg.Elements?.Npcs.GetValueOrDefault(n.Id));

double WinRate(Combatant a, Combatant b, int runs = 60)
{
    var wins = 0;
    for (var seed = 1UL; seed <= (ulong)runs; seed++)
    {
        var s = CombatEngine.Start(cfg, a, b);
        while (s.Outcome is null)
        {
            var rng = CombatEngine.TurnRng(seed * 7919, s.Turn);
            s = CombatEngine.ResolveTurn(cfg, a, b, s, CombatAi.Choose(cfg, a, s.A, s.Distance, rng), CombatAi.Choose(cfg, b, s.B, s.Distance, rng), rng).State;
        }
        if (s.Outcome == "a_won") wins++;
    }
    return wins * 100.0 / runs;
}

var mode = args.FirstOrDefault() ?? "trib";
var trib = cfg.Npc("heavenly_tribulation")!;
if (mode == "trib")
{
    Console.WriteLine("realm  qi  body flame poison  (tribulation win %)");
    foreach (var r in new[] { 0, 1, 2, 3, 5, 8, 10, 15, 20, 30, 40, 50, 64 })
    {
        var t = Npc(CombatService.TribulationNpc(cfg, trib, r, 1000 + (r * 3), "t"));
        Console.WriteLine($"{r,5} " + string.Join(" ", mainOf.Keys.Select(p => $"{WinRate(Player(p, r), t),5:0}")));
    }
}
else if (mode == "spirit")
{
    Console.WriteLine("flame spirits (power 900-1665) and the sect boss avatar (blood_demon_lord, realm+2, 600‰): win % per path qi/body/flame/poison");
    foreach (var (id, power) in new[] { ("flame_spirit_lesser", 900), ("flame_spirit_greater", 780), ("flame_spirit_primal", 660), ("flame_spirit_primal", 720), ("blood_demon_lord", 750) })
        foreach (var r in new[] { 0, 2, 5, 10, 30, 64 })
        {
            var rr = id == "blood_demon_lord" ? r + 2 : r;
            var foe = Npc(CombatService.TribulationNpc(cfg, cfg.Npc(id)!, rr, power, "x"));
            Console.WriteLine($"{id,-22} {power,5} r{r,-3} " + string.Join(" ", mainOf.Keys.Select(p => $"{WinRate(Player(p, r), foe, 30),5:0}")));
        }
}
else
{
    var npcs = cfg.Npcs.Where(n => n.Id is not ("heavenly_tribulation" or "training_dummy")).ToList();
    Console.WriteLine("realm  " + string.Join(" ", npcs.Select(n => n.Id[..Math.Min(6, n.Id.Length)].PadLeft(6))));
    foreach (var r in new[] { 0, 1, 2, 3, 5, 8, 10, 15, 20, 30, 45, 64 })
        Console.WriteLine($"{r,5}  " + string.Join(" ", npcs.Select(n => $"{WinRate(Player(args.ElementAtOrDefault(1) ?? "qi", r), Npc(cfg.Scaling!.ForFighter(n, r)), 30),6:0}")));
}
