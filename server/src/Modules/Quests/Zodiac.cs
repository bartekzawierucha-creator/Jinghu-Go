using JianghuGo.Application;
using JianghuGo.Modules.Quests.Domain;

namespace JianghuGo.Modules.Quests.Application;

/// <summary>
/// The cultivator's birth sign (2.2): one of twelve animals, fixed by the day the character was born (from its
/// time-ordered id). Each sign gives a small stat bonus, doubled on the sign's own day of the twelve-day cycle.
/// </summary>
public static class Zodiac
{
    public sealed record Sign(string Id, string NamePl, string Hanzi, string Stat, string DescriptionPl);

    public static readonly Sign[] Signs =
    [
        new("rat", "Szczur", "鼠", "agility", "Spryt i szybkość."),
        new("ox", "Bawół", "牛", "constitution", "Wytrwałość ciała."),
        new("tiger", "Tygrys", "虎", "strength", "Siła ciosu."),
        new("rabbit", "Królik", "兔", "fate", "Szczęście w łupach."),
        new("dragon", "Smok", "龍", "spirit", "Moc ducha."),
        new("snake", "Wąż", "蛇", "insight", "Przenikliwość."),
        new("horse", "Koń", "馬", "agility", "Pęd w walce."),
        new("goat", "Koza", "羊", "willpower", "Spokój serca."),
        new("monkey", "Małpa", "猴", "focus", "Skupienie."),
        new("rooster", "Kogut", "雞", "elementalAffinity", "Więź z żywiołami."),
        new("dog", "Pies", "狗", "constitution", "Wierność i hart."),
        new("pig", "Świnia", "豬", "fate", "Dostatek."),
    ];

    public const int Bonus = 3;

    /// <summary>The sign from the creation day encoded in a UUIDv7 (first 48 bits = Unix ms); other ids hash the same way.</summary>
    public static Sign Of(Guid characterId)
    {
        var b = characterId.ToByteArray(true);
        long ms = 0;
        for (var i = 0; i < 6; i++) ms = (ms << 8) | b[i];
        var day = ms / 86_400_000L;
        return Signs[(int)(((day % 12) + 12) % 12)];
    }

    /// <summary>Whose day it is in the twelve-day cycle (world date).</summary>
    public static Sign OfDay(DateOnly day) => Signs[((day.DayNumber % 12) + 12) % 12];
}

public sealed record ZodiacView(string Id, string NamePl, string Hanzi, string Stat, int Bonus, string DescriptionPl, string TodayId, string TodayNamePl, bool LuckyToday);

internal sealed class ZodiacCombatBonuses(QuestsConfig config, TimeProvider clock) : JianghuGo.Modules.Combat.Contracts.ICombatStatBonuses
{
    public bool ResonatesWithRealm => true;

    public string SourcePl => "Znak urodzenia";

    public Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var sign = Zodiac.Of(characterId);
        var lucky = Zodiac.OfDay(QuestRules.WorldDate(config, clock.GetUtcNow())).Id == sign.Id;
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { [sign.Stat] = lucky ? Zodiac.Bonus * 2 : Zodiac.Bonus };
        return Task.FromResult(d);
    }
}
