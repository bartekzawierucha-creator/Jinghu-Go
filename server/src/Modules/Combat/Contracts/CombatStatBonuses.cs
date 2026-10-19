using JianghuGo.Application;

namespace JianghuGo.Modules.Combat.Contracts;

/// <summary>
/// Extra combat stats a character has earned outside combat (e.g. cultivation realm and meridians). Every registered
/// provider is summed into the battle snapshot when a battle starts, so bonuses are frozen for the battle.
/// </summary>
public interface ICombatStatBonuses
{
    Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);

    /// <summary>
    /// Flat bonuses tuned for a fresh cultivator (titles) resonate with the realm like equipment and pets do: ×(10 + realm) / 10.
    /// Realm-aware providers (meridians, flames) keep their own numbers.
    /// </summary>
    bool ResonatesWithRealm => false;
}

/// <summary>
/// Combat perks a character earned outside combat (flames, toxin body, tempering, sword intent, path branches, pet,
/// sect buildings). Summed into the battle snapshot like stat bonuses; all values in ‰ unless noted.
/// </summary>
public sealed record PerkBonus(
    int DamagePermille = 0,
    int GuardPermille = 0,
    int EffectChancePermille = 0,
    int BleedPermille = 0,
    int HealPermille = 0,
    int QiRegen = 0,
    int QiPermille = 0,
    int RegenPermille = 0,
    int ReflectPermille = 0,
    int LifestealPermille = 0,
    int FirstStrikePermille = 0,
    int HpPermille = 0,
    int SurviveOnce = 0,
    int PetPower = 0,
    int PetEvery = 0)
{
    public static readonly PerkBonus None = new();

    public static PerkBonus operator +(PerkBonus a, PerkBonus b) => new(
        a.DamagePermille + b.DamagePermille, a.GuardPermille + b.GuardPermille, a.EffectChancePermille + b.EffectChancePermille, a.BleedPermille + b.BleedPermille,
        a.HealPermille + b.HealPermille, a.QiRegen + b.QiRegen, a.QiPermille + b.QiPermille, a.RegenPermille + b.RegenPermille, a.ReflectPermille + b.ReflectPermille,
        a.LifestealPermille + b.LifestealPermille, a.FirstStrikePermille + b.FirstStrikePermille, a.HpPermille + b.HpPermille, Math.Max(a.SurviveOnce, b.SurviveOnce),
        Math.Max(a.PetPower, b.PetPower), a.PetPower >= b.PetPower ? a.PetEvery : b.PetEvery);
}

public interface ICombatPerkBonuses
{
    Task<PerkBonus> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}

/// <summary>The cultivator's current realm index (0-65), so opponents and rewards can be raised to it.</summary>
public interface ICombatRealm
{
    Task<int> RealmAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);

    /// <summary>Qi the cultivator's current stage needs (rewards are a share of it, 1.7).</summary>
    Task<long> StageQiAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}
