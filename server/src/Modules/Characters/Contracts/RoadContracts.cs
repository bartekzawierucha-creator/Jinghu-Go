namespace JianghuGo.Modules.Characters.Contracts;

/// <summary>A realm on the Road of 66 Realms (1.9), provided by Cultivation.</summary>
public sealed record RoadRealm(int Index, string NamePl, string Hanzi, int Era, string EraPl, string TribulationPl, string UnlockPl);

/// <summary>The realm ladder, provided by the module that owns it.</summary>
public interface IRoadRealms
{
    IReadOnlyList<RoadRealm> Realms { get; }
}

/// <summary>What a module opens at each realm for a path (techniques, flames, methods, expeditions…), for the Road screen.</summary>
public interface IRoadContributor
{
    IEnumerable<(int Realm, string TextPl)> Unlocks(string path);
}
