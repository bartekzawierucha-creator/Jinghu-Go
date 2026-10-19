namespace JianghuGo.Modules.Arts.Contracts;

/// <summary>Integration events published by Arts (Quests counts milestones from them).</summary>
public static class ArtsEvents
{
    public const string Progress = "arts.progress";

    /// <summary>Milestones after a change: flames owned, toxin body level, body tempering (+N), swords of intent (⚔N).</summary>
    public sealed record ProgressV1(Guid AccountId, Guid CharacterId, int FlamesOwned, int ToxinLevel, int Temper, int Swords);
}
