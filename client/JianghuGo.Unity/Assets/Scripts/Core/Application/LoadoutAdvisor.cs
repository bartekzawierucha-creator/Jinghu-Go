using System;
using System.Collections.Generic;
using System.Linq;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// "Arrange the best": fills every combat slot with the strongest unlocked technique of that slot's kind (usable level,
    /// then damage, then the later realm), never the same technique twice. Fights are automatic since 2.2, so this is
    /// the one-tap way to keep the loadout up to date; the server still validates the result.
    /// </summary>
    public static class LoadoutAdvisor
    {
        public static List<string> Best(LoadoutDto loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            var used = new HashSet<string>();
            var result = new List<string>();
            foreach (var slot in loadout.Slots.OrderBy(s => s.Slot))
            {
                var pick = loadout.Known
                    .Where(k => !k.Locked && k.Slot == slot.Kind && !used.Contains(k.Id))
                    .OrderByDescending(k => Math.Min(k.Level, k.LevelCap))
                    .ThenByDescending(k => k.DamageMax ?? 0)
                    .ThenByDescending(k => k.MinRealm)
                    .ThenBy(k => k.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                // keep what is there when nothing fits (a slot kind with no known technique), unless it is already used
                var id = pick?.Id ?? (slot.TechniqueId != null && !used.Contains(slot.TechniqueId) ? slot.TechniqueId : null);
                if (id != null) used.Add(id);
                result.Add(id);
            }

            return result;
        }
    }
}
