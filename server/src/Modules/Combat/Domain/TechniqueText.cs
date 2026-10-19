namespace JianghuGo.Modules.Combat.Domain;

/// <summary>
/// Human descriptions generated from the numbers (1.7), so text never drifts from the rules: techniques, effects and the
/// damage a technique would deal right now.
/// </summary>
public static class TechniqueText
{
    public static string StatPl(string? stat) => stat switch
    {
        "strength" => "Siła",
        "spirit" => "Duch",
        "agility" => "Zręczność",
        "constitution" => "Kondycja",
        _ => stat ?? "—",
    };

    public static string ElementPl(string? element) => element switch
    {
        "wood" => "drewno",
        "fire" => "ogień",
        "earth" => "ziemia",
        "metal" => "metal",
        "water" => "woda",
        _ => "—",
    };

    private static string Pct(int permille) => (permille / 10.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

    public static string Effect(EffectSpec fx)
    {
        var chance = fx.ChancePermille >= 1000 ? string.Empty : Pct(fx.ChancePermille) + " na ";
        var turns = fx.Duration == 1 ? "1 turę" : fx.Duration + " tury";
        return chance + fx.Type switch
        {
            EffectTypes.Bleed => $"krwawienie ({fx.Magnitude}/turę przez {turns})",
            EffectTypes.Burn => $"podpalenie ({fx.Magnitude}/turę przez {turns})",
            EffectTypes.Poison => $"zatrucie ({fx.Magnitude}/turę przez {turns}, słabsze leczenie)",
            EffectTypes.Stun => "ogłuszenie (traci następny ruch)",
            EffectTypes.Weaken => $"osłabienie (−{Pct(fx.Magnitude)} obrażeń przez {turns})",
            EffectTypes.Slow => $"spowolnienie (−{fx.Magnitude} inicjatywy przez {turns})",
            EffectTypes.Frail => $"kruchość (+{Pct(fx.Magnitude)} otrzymywanych obrażeń przez {turns})",
            EffectTypes.Dispel => "rozproszenie (zdejmuje wzmocnienie wroga)",
            EffectTypes.Drain => $"wyssanie {fx.Magnitude} Qi",
            EffectTypes.Shield => $"tarczę ({Pct(fx.Magnitude)} maks. życia przez {turns})",
            EffectTypes.Empower => $"wzmocnienie (+{Pct(fx.Magnitude)} obrażeń przez {turns})",
            EffectTypes.Fortify => $"umocnienie (−{Pct(fx.Magnitude)} otrzymywanych obrażeń przez {turns})",
            EffectTypes.Haste => $"przyspieszenie (+{fx.Magnitude} inicjatywy przez {turns})",
            EffectTypes.Cleanse => "oczyszczenie (zdejmuje jedną dolegliwość)",
            _ => fx.Type,
        };
    }

    public static string KindPl(string kind) => kind switch
    {
        SlotKinds.Attack => "Atak",
        SlotKinds.Defense => "Obrona",
        SlotKinds.Special => "Specjał",
        SlotKinds.Counter => "Kontra",
        _ => kind,
    };

    /// <summary>One line describing what the technique does.</summary>
    public static string Describe(CombatConfig c, TechniqueDefinition t, EffectSpec? awakening = null)
    {
        var parts = new List<string> { KindPl(t.Kind) };
        if (t.Attack is { } a)
        {
            parts.Add($"moc {a.Power} ({StatPl(a.Scaling)})");
            if (a.AccuracyPermille != 0) parts.Add("celność " + (a.AccuracyPermille > 0 ? "+" : string.Empty) + Pct(a.AccuracyPermille));
            if (a.Effect is { } fx) parts.Add(Effect(fx));
        }

        if (t.Defense is { } d) parts.Add($"blok {Pct(d.BlockPermille)} obrażeń w tej turze");
        if (t.Counter is { } k) parts.Add($"odbija {Pct(k.ReflectPermille)} ciosu" + (k.QiSteal > 0 ? $" i kradnie {k.QiSteal} Qi" : string.Empty));
        if (t.Movement is { } m) parts.Add($"unik {Pct(m.EvasionPermille)}");
        if (t.Special is { } sp)
        {
            if (sp.Power > 0) parts.Add($"cios mocy {sp.Power} ({StatPl(sp.Scaling)})");
            if (sp.HealPermille > 0) parts.Add($"leczy {Pct(sp.HealPermille)} życia");
            if (sp.GuardPermille > 0) parts.Add($"garda {Pct(sp.GuardPermille)}");
            if (sp.EvasionPermille > 0) parts.Add($"unik {Pct(sp.EvasionPermille)}");
            if (sp.Effect is { } fx) parts.Add(Effect(fx));
            if (sp.SelfEffect is { } self) parts.Add("daje sobie " + Effect(self));
        }

        parts.Add(t.QiCost > 0 ? $"{t.QiCost} Qi" : "bez Qi");
        if (t.Cooldown > 0) parts.Add($"odnowienie {t.Cooldown}");
        if (c.Elements?.OfTechnique(t.Id) is { } el) parts.Add("żywioł: " + ElementPl(el));
        if (t.MinRealm > 0) parts.Add($"od {t.MinRealm + 1}. królestwa");
        if (awakening is not null) parts.Add("przebudzenie: " + Effect(awakening));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Damage of a strike against an equal opponent with no guard (min-max over the variance, before crits, defence and
    /// elements): what the player can expect from this technique at its current level with the current stats and perks.
    /// </summary>
    public static (int Min, int Max)? Preview(CombatConfig c, Combatant who, EquippedTechnique eq)
    {
        var t = c.Technique(eq);
        var (power, scaling) = t.Attack is { } a ? (a.Power, a.Scaling) : t.Special is { Power: > 0 } sp ? (sp.Power, sp.Scaling!) : (0, (string?)null);
        if (power <= 0 || scaling is null) return null;
        long dmg = power;
        dmg = dmg * (1000 + ((eq.Level - 1) * c.Damage.LevelScalingPermille)) / 1000;
        dmg = dmg * (1000 + (who.Stat(scaling) * c.Damage.StatScalingPermille)) / 1000;
        dmg = dmg * (1000 + (who.Perks?.DamageBonus(scaling) ?? 0)) / 1000;
        var v = c.Damage.VariancePermille;
        return ((int)Math.Max(1, dmg * (1000 - v) / 1000), (int)Math.Max(1, dmg * (1000 + v) / 1000));
    }
}
