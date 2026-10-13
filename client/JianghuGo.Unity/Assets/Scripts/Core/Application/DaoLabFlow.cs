using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// UI-independent state of the Dao Lab screen: compose a design, preview it against the server's budget, create it,
    /// equip it in the loadout, inscribe scrolls and learn from scrolls. The client never decides validity or power.
    /// </summary>
    public sealed class DaoLabFlow
    {
        private static readonly string[] Layout = { "attack", "attack", "attack", "attack", "defense", "defense", "special", "counter" };
        private readonly ApiClient _api;
        private readonly Guid _characterId;

        public DaoLabFlow(ApiClient api, Guid characterId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _characterId = characterId;
        }

        public List<KnownDaoTechniqueDto> Known { get; private set; } = new List<KnownDaoTechniqueDto>();
        public LoadoutDto Loadout { get; private set; }
        public DaoPreviewDto LastPreview { get; private set; }

        public async Task LoadAsync(CancellationToken ct)
        {
            Known = (await _api.GetKnownDaoTechniquesAsync(_characterId, ct).ConfigureAwait(false)).Items;
            Loadout = await _api.GetLoadoutAsync(_characterId, ct).ConfigureAwait(false);
        }

        public async Task<DaoPreviewDto> PreviewAsync(DaoDesignDto design, CancellationToken ct) =>
            LastPreview = await _api.PreviewDesignAsync(design, ct).ConfigureAwait(false);

        /// <summary>Previews first and only submits a valid design (the fee is charged by the server on success).</summary>
        public async Task<KnownDaoTechniqueDto> CreateAsync(DaoDesignDto design, CancellationToken ct)
        {
            if (!(await PreviewAsync(design, ct).ConfigureAwait(false)).Valid) return null;
            var created = await _api.DesignAsync(_characterId, design, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
            return created;
        }

        /// <summary>Puts a technique into the first slot of its kind (replacing what is there). Reloads on a version conflict.</summary>
        public async Task<bool> EquipAsync(string techniqueId, string slotKind, CancellationToken ct)
        {
            var slots = new List<string>();
            foreach (var s in Loadout.Slots) slots.Add(s.TechniqueId == techniqueId ? null : s.TechniqueId);
            var target = Array.IndexOf(Layout, slotKind);
            if (target < 0) return false;
            slots[target] = techniqueId;
            try
            {
                Loadout = await _api.SetLoadoutAsync(_characterId, Loadout.Version, slots, ct).ConfigureAwait(false);
                return true;
            }
            catch (ApiException e) when (e.Code == "version_conflict")
            {
                Loadout = await _api.GetLoadoutAsync(_characterId, ct).ConfigureAwait(false);
                return false;
            }
        }

        public Task<ScrollDto> InscribeAsync(string techniqueId, CancellationToken ct) => _api.InscribeScrollAsync(_characterId, techniqueId, ct);

        public async Task<KnownDaoTechniqueDto> LearnAsync(ItemInstanceDto scroll, CancellationToken ct)
        {
            var learned = await _api.LearnScrollAsync(_characterId, scroll, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
            return learned;
        }

        /// <summary>A starting point for the editor: a modest single-target strike that fits the default budget.</summary>
        public static DaoDesignDto StarterStrike(string name) => new DaoDesignDto
        {
            Name = name,
            Slot = "attack",
            QiCost = 4,
            Cooldown = 1,
            Speed = 0,
            Attack = new AttackSpecDto { Power = 15, Scaling = "strength", RangeMin = 1, RangeMax = 1, AccuracyPermille = 0 },
        };
    }
}
