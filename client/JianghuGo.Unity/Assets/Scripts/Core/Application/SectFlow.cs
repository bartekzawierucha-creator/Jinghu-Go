using System;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// UI-independent state of the sect and territory screens for one character. "Here" is always the territory at the
    /// character's verified position, as decided by the server.
    /// </summary>
    public sealed class SectFlow
    {
        private readonly ApiClient _api;
        private readonly Guid _characterId;

        public SectFlow(ApiClient api, Guid characterId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _characterId = characterId;
        }

        /// <summary>Null while the character has no sect.</summary>
        public MySectDto Mine { get; private set; }
        public SectTerritoriesDto Territories { get; private set; }

        public async Task LoadAsync(CancellationToken ct)
        {
            try
            {
                Mine = await _api.GetMySectAsync(_characterId, ct).ConfigureAwait(false);
                Territories = await _api.GetSectTerritoriesAsync(Mine.Sect.Id, ct).ConfigureAwait(false);
            }
            catch (ApiException e) when (e.Code == "not_in_sect")
            {
                Mine = null;
                Territories = null;
            }
        }

        public Task<SectPageDto> BrowseAsync(CancellationToken ct) => _api.ListSectsAsync(null, ct);

        public async Task FoundAsync(string name, CancellationToken ct)
        {
            await _api.FoundSectAsync(_characterId, name, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
        }

        public Task<SectApplicationDto> ApplyAsync(Guid sectId, string message, CancellationToken ct) => _api.ApplyToSectAsync(_characterId, sectId, message, ct);

        public async Task DecideAsync(SectApplicationDto application, bool accept, CancellationToken ct)
        {
            await _api.DecideApplicationAsync(_characterId, application.Id, accept, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
        }

        public async Task DonateAsync(long amount, CancellationToken ct)
        {
            Mine = await _api.DonateAsync(_characterId, Mine.Sect.Id, amount, ct).ConfigureAwait(false);
        }

        public async Task LeaveAsync(CancellationToken ct)
        {
            await _api.LeaveSectAsync(_characterId, Mine.Sect.Id, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
        }

        public Task<TerritoryDto> HereAsync(CancellationToken ct) => _api.GetTerritoryHereAsync(_characterId, ct);

        public async Task<TerritoryDto> ClaimHereAsync(CancellationToken ct)
        {
            var t = await _api.ClaimTerritoryAsync(_characterId, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
            return t;
        }

        public async Task<WarDto> ChallengeHereAsync(CancellationToken ct)
        {
            var w = await _api.ChallengeTerritoryAsync(_characterId, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
            return w;
        }

        public async Task CollectTributeAsync(TerritoryDto territory, CancellationToken ct)
        {
            await _api.CollectTributeAsync(_characterId, territory.Key, ct).ConfigureAwait(false);
            await LoadAsync(ct).ConfigureAwait(false);
        }
    }
}
