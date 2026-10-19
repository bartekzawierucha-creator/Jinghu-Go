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

public sealed record ZodiacView(string Id, string NamePl, string Hanzi, string Stat, int Bonus, string DescriptionPl, string TodayId, string TodayNamePl, bool LuckyToday,
    string? PhysiqueId = null, string? PhysiqueNamePl = null, string? PhysiqueDescriptionPl = null, string? PhysiqueRarity = null);

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

/// <summary>
/// Special physiques (2.2): about one cultivator in ten is born with one, fixed forever by the character's id. Each gives
/// a modest combat perk; the rarest are stronger.
/// </summary>
public static class Physiques
{
    public sealed record Physique(string Id, string NamePl, string DescriptionPl, string Rarity, JianghuGo.Modules.Combat.Contracts.PerkBonus Bonus);

    public static readonly Physique[] All =
    [
        new("phoenix_body", "Ciało Feniksa", "Rany goją się same: +2% regeneracji życia na turę.", "epic", new(RegenPermille: 20)),
        new("iron_bone", "Kości z Żelaza", "+6% gardy.", "rare", new(GuardPermille: 60)),
        new("sword_heart", "Serce Miecza", "+5% obrażeń.", "rare", new(DamagePermille: 50)),
        new("myriad_poison", "Ciało Tysiąca Trucizn", "+8% szansy na efekty.", "rare", new(EffectChancePermille: 80)),
        new("void_meridians", "Meridiany Pustki", "+1 Qi na turę i +5% Qi.", "epic", new(QiRegen: 1, QiPermille: 50)),
        new("blood_moon", "Krew Krwawego Księżyca", "+4% kradzieży życia.", "epic", new(LifestealPermille: 40)),
        new("heaven_chosen", "Wybraniec Niebios", "Raz na walkę przeżywa śmiertelny cios i +5% życia.", "legendary", new(HpPermille: 50, SurviveOnce: 1)),
    ];

    /// <summary>Roughly 1 in 10; within physiques the legendary one is the rarest.</summary>
    public static Physique? Of(Guid characterId)
    {
        var b = characterId.ToByteArray();
        var h = 2166136261u;
        foreach (var x in b) h = (h ^ x) * 16777619u;
        var roll = (int)(h % 1000);
        if (roll >= 100) return null;
        return roll < 4 ? All[^1] : All[roll % (All.Length - 1)];
    }
}

internal sealed class PhysiqueCombatPerks : JianghuGo.Modules.Combat.Contracts.ICombatPerkBonuses
{
    public string SourcePl => "Szczególne ciało";

    public Task<JianghuGo.Modules.Combat.Contracts.PerkBonus> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        Task.FromResult(Physiques.Of(characterId)?.Bonus ?? JianghuGo.Modules.Combat.Contracts.PerkBonus.None);
}
