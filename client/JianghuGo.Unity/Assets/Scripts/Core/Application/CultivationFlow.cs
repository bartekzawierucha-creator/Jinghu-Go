using System;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// UI-independent cultivation screen state. Meditation time is measured by the SERVER: closing the app
    /// mid-session is fine, finishing later counts the real elapsed time (up to the server cap).
    /// </summary>
    public sealed class CultivationFlow
    {
        private readonly ApiClient _api;
        private readonly Guid _characterId;

        public CultivationFlow(ApiClient api, Guid characterId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _characterId = characterId;
        }

        public CultivationDto State { get; private set; }
        public event Action<CultivationDto> StateChanged;

        public async Task LoadAsync(CancellationToken ct) => Set(await _api.GetCultivationAsync(_characterId, ct).ConfigureAwait(false));

        public async Task StartMeditationAsync(CancellationToken ct) => Set(await _api.StartMeditationAsync(_characterId, ct).ConfigureAwait(false));

        public async Task<MeditationResultDto> FinishMeditationAsync(CancellationToken ct)
        {
            var result = await _api.FinishMeditationAsync(_characterId, ct).ConfigureAwait(false);
            Set(result.State);
            return result;
        }

        /// <summary>Returns null (after reloading) when the state changed elsewhere first.</summary>
        public async Task<BreakthroughResultDto> BreakthroughAsync(CancellationToken ct)
        {
            try
            {
                var result = await _api.BreakthroughAsync(State, ct).ConfigureAwait(false);
                Set(result.State);
                return result;
            }
            catch (ApiException e) when (e.Code == "version_conflict")
            {
                await LoadAsync(ct).ConfigureAwait(false);
                return null;
            }
        }

        public async Task<StepsResultDto> ReportStepsAsync(int steps, CancellationToken ct)
        {
            var result = await _api.ReportStepsAsync(_characterId, steps, ct).ConfigureAwait(false);
            Set(result.State);
            return result;
        }

        /// <summary>Applies a server call that returns the new cultivation state (retreat, vessel, method…).</summary>
        public async Task ApplyAsync(Func<Guid, CancellationToken, Task<CultivationDto>> call, CancellationToken ct) =>
            Set(await call(_characterId, ct).ConfigureAwait(false));

        public void Replace(CultivationDto state) => Set(state);

        private void Set(CultivationDto state)
        {
            if (state != null && state.ReceivedAt == null) state.ReceivedAt = DateTimeOffset.UtcNow;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
