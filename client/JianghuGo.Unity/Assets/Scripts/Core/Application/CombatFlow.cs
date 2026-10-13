using System;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// UI-independent battle screen state. The client only ever sends the chosen slot for the CURRENT turn;
    /// damage, hits and the outcome come from the server's timeline (LastTurnEvents) to be animated.
    /// </summary>
    public sealed class CombatFlow
    {
        private readonly ApiClient _api;

        public CombatFlow(ApiClient api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public BattleDto Battle { get; private set; }
        public event Action<BattleDto> BattleChanged;

        public async Task StartPveAsync(Guid characterId, string npcId, CancellationToken ct) =>
            Set(await _api.StartPveAsync(characterId, npcId, ct).ConfigureAwait(false));

        public async Task OpenAsync(Guid battleId, CancellationToken ct) => Set(await _api.GetBattleAsync(battleId, ct).ConfigureAwait(false));

        public Task RefreshAsync(CancellationToken ct) => OpenAsync(Battle.Id, ct);

        /// <summary>Gives up the battle (the opponent wins).</summary>
        public async Task ForfeitAsync(CancellationToken ct) => Set(await _api.ForfeitAsync(Battle.Id, ct).ConfigureAwait(false));

        /// <summary>
        /// Returns false (after refreshing) when the turn moved on before the action arrived — e.g. the deadline passed
        /// or the action was already submitted from another device.
        /// </summary>
        /// <summary>Auto-battle step: the server chooses and plays this turn for the player. False when the turn moved on.</summary>
        public async Task<bool> AutoAsync(CancellationToken ct)
        {
            try
            {
                Set(await _api.AutoActAsync(Battle.Id, ct).ConfigureAwait(false));
                return true;
            }
            catch (ApiException e) when (e.Code == "turn_mismatch" || e.Code == "action_already_submitted" || e.Code == "battle_not_active")
            {
                await RefreshAsync(ct).ConfigureAwait(false);
                return false;
            }
        }

        public async Task<bool> ActAsync(int? slot, string direction, CancellationToken ct)
        {
            try
            {
                Set(await _api.ActAsync(Battle, slot, direction, ct).ConfigureAwait(false));
                return true;
            }
            catch (ApiException e) when (e.Code == "turn_mismatch" || e.Code == "action_already_submitted" || e.Code == "battle_not_active")
            {
                await RefreshAsync(ct).ConfigureAwait(false);
                return false;
            }
        }

        private void Set(BattleDto battle)
        {
            Battle = battle;
            BattleChanged?.Invoke(battle);
        }
    }
}
