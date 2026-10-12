using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// UI-independent state of the character creation screen. Holds the server's draft and the player's
    /// lock selection; all outcomes come from the server. Recovers from stale versions by reloading.
    /// </summary>
    public sealed class CharacterCreationFlow
    {
        private readonly ApiClient _api;
        private readonly HashSet<string> _locked = new HashSet<string>();

        public CharacterCreationFlow(ApiClient api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public DraftDto Draft { get; private set; }
        public IReadOnlyCollection<string> LockedCategories => _locked;
        public event Action<DraftDto> DraftChanged;

        public int NextRerollCost => Draft == null ? 0 : Draft.PreviewRerollCost(_locked.Count);
        public bool CanAffordReroll => Draft != null && Draft.RerollsRemaining > 0 && Draft.DestinyTokens >= NextRerollCost;

        public async Task StartAsync(CancellationToken ct)
        {
            SetDraft(await _api.StartOrResumeDraftAsync(ct).ConfigureAwait(false));
        }

        /// <summary>Toggles a lock. Returns false if the toggle would exceed the server's lock limit.</summary>
        public bool ToggleLock(string category)
        {
            if (_locked.Remove(category)) return true;
            if (Draft == null || _locked.Count >= Draft.MaxLockedTraits) return false;
            _locked.Add(category);
            return true;
        }

        /// <summary>Returns false (after reloading the draft) when another device changed it first.</summary>
        public async Task<bool> RerollAsync(CancellationToken ct)
        {
            try
            {
                SetDraft(await _api.RerollAsync(Draft, _locked.ToList(), ct).ConfigureAwait(false));
                return true;
            }
            catch (ApiException e) when (e.Code == "version_conflict")
            {
                SetDraft(await _api.GetCurrentDraftAsync(ct).ConfigureAwait(false));
                return false;
            }
        }

        public Task<CharacterDto> FinalizeAsync(string name, string path, CancellationToken ct) =>
            _api.FinalizeAsync(Draft, name, path, ct);

        private void SetDraft(DraftDto draft)
        {
            Draft = draft;
            DraftChanged?.Invoke(draft);
        }
    }
}
