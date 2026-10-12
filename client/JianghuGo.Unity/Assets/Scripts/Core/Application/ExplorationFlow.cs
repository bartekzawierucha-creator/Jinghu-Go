using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JianghuGo.Client.Infrastructure;

namespace JianghuGo.Client.Application
{
    /// <summary>
    /// Drives exploration: forwards device fixes to the server in small batches, refreshes nearby nodes, and performs
    /// interactions. The UI calls <see cref="TickAsync"/> on a timer (see <see cref="RecommendedInterval"/>) and stops
    /// calling it when the app is paused, so GPS and radio can sleep (battery).
    /// </summary>
    public sealed class ExplorationFlow
    {
        public const int MaxBatch = 20;

        private readonly ApiClient _api;
        private readonly ILocationSource _location;
        private readonly Guid _characterId;
        private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
        private int _ticks;

        public ExplorationFlow(ApiClient api, ILocationSource location, Guid characterId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _location = location ?? throw new ArgumentNullException(nameof(location));
            _characterId = characterId;
        }

        public ExplorationSessionDto Session { get; private set; }
        public NearbyDto Nearby { get; private set; }

        /// <summary>"location_denied", "location_unavailable", "location_starting", or the server's position problem; null when all is well.</summary>
        public string Problem { get; private set; }

        public event Action Changed;

        /// <summary>Faster updates only when something is within reach; otherwise save battery.</summary>
        public TimeSpan RecommendedInterval =>
            Nearby != null && Nearby.Nodes.Any(n => n.DistanceM <= 80 && !n.Harvested) ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(15);

        public async Task StartAsync(CancellationToken ct)
        {
            Session = await _api.StartExplorationAsync(_characterId, ct).ConfigureAwait(false);
            await TickAsync(ct).ConfigureAwait(false);
        }

        public async Task StopAsync(CancellationToken ct)
        {
            Session = await _api.StopExplorationAsync(_characterId, ct).ConfigureAwait(false);
            Nearby = null;
            Changed?.Invoke();
        }

        public async Task TickAsync(CancellationToken ct)
        {
            _ticks++;
            switch (_location.Status)
            {
                case LocationStatus.Denied:
                    Problem = "location_denied";
                    Changed?.Invoke();
                    return;
                case LocationStatus.Unavailable:
                    Problem = "location_unavailable";
                    Changed?.Invoke();
                    return;
                case LocationStatus.Initializing:
                    Problem = "location_starting";
                    Changed?.Invoke();
                    return;
            }

            var fixes = _location.FixesSince(_lastSent).OrderBy(f => f.CapturedAt).ToList();
            if (fixes.Count > MaxBatch) fixes = fixes.Skip(fixes.Count - MaxBatch).ToList(); // newest matter most
            if (fixes.Count > 0)
            {
                var result = await _api.SendSamplesAsync(_characterId, fixes, ct).ConfigureAwait(false);
                _lastSent = fixes[fixes.Count - 1].CapturedAt;
                Session = result.Session;
            }

            if (Nearby == null || fixes.Count > 0 || _ticks % 4 == 0)
            {
                Nearby = await _api.GetNearbyAsync(_characterId, ct).ConfigureAwait(false);
            }

            Problem = Session?.PositionProblem;
            Changed?.Invoke();
        }

        public async Task<WorldInteractionDto> InteractAsync(string nodeKey, CancellationToken ct)
        {
            var result = await _api.InteractAsync(_characterId, nodeKey, ct).ConfigureAwait(false);
            Nearby = await _api.GetNearbyAsync(_characterId, ct).ConfigureAwait(false);
            Changed?.Invoke();
            return result;
        }

        public Task<WorldInteractionDto> AttuneAsync(string siteId, CancellationToken ct) => _api.AttuneAsync(_characterId, siteId, ct);
    }
}
