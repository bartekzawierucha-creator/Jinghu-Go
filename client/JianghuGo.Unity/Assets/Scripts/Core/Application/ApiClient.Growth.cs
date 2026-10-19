using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    /// <summary>MVP 1.5 endpoints: cultivation-3, path arts, estate, arena, titles, crafting queue, sect development.</summary>
    public sealed partial class ApiClient
    {
        private static string Character(Guid characterId) => "/api/v1/characters/" + characterId;

        // ---- cultivation-3 ------------------------------------------------------------------------------------

        public Task<TribulationStartDto> StartTribulationAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<TribulationStartDto>("POST", Cultivation(characterId) + "/tribulation", null, true, null, ct);

        public Task<CultivationDto> StartRetreatAsync(Guid characterId, int hours, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/retreat", new { hours }, true, null, ct);

        public Task<CultivationDto> LeaveRetreatAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/retreat/leave", null, true, null, ct);

        public Task<CultivationDto> CompressAsync(Guid characterId, long qi, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/compress", new { qi }, true, null, ct);

        public Task<CultivationDto> UpgradeVesselAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/vessel", null, true, null, ct);

        public Task<CultivationDto> UseAcceleratorAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/accelerator", null, true, null, ct);

        public Task<CultivationDto> SetMethodAsync(Guid characterId, string methodId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/method", new { methodId }, true, null, ct);

        public Task<CultivationDto> LearnMethodAsync(Guid characterId, string methodId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/method/learn", new { methodId }, true, null, ct);

        public Task<CultivationDto> SetAutoBreakthroughAsync(Guid characterId, int? minChancePermille, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/auto-breakthrough", new { minChancePermille }, true, null, ct);

        public Task<CultivationDto> ReincarnateAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/reincarnate", null, true, null, ct);

        public Task<CultivationDto> AcknowledgeSummaryAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/summary/ack", null, true, null, ct);

        // ---- path arts ----------------------------------------------------------------------------------------

        private static string Arts(Guid characterId) => Character(characterId) + "/arts";

        public Task<ArtsDto> GetArtsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ArtsDto>("GET", Arts(characterId), null, true, null, ct);

        public Task<ArtsDto> DismissArtsNoticesAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/notices/dismiss", null, true, null, ct);

        public Task<TraceResultDto> FollowTracesAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<TraceResultDto>("POST", Arts(characterId) + "/flames/traces", null, true, null, ct);

        public Task<SpiritBattleDto> SubdueFlameAsync(Guid characterId, string flameId, CancellationToken ct) =>
            SendAsync<SpiritBattleDto>("POST", Arts(characterId) + "/flames/subdue", new { flameId }, true, null, ct);

        public Task<ArtsDto> AbsorbFlameAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/flames/absorb", null, true, null, ct);

        public Task<ArtsDto> FeedFlameAsync(Guid characterId, string flameId, int quantity, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/flames/feed", new { flameId, quantity }, true, null, ct);

        public Task<ArtsDto> SetFlameSlotsAsync(Guid characterId, IList<string> flameIds, CancellationToken ct) =>
            SendAsync<ArtsDto>("PUT", Arts(characterId) + "/flames/slots", new { flameIds }, true, null, ct);

        public Task<ArtsDto> FuseFlamesAsync(Guid characterId, string firstFlameId, string secondFlameId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/flames/fuse", new { firstFlameId, secondFlameId }, true, null, ct);

        public Task<VenomResultDto> AbsorbVenomAsync(Guid characterId, string item, int quantity, CancellationToken ct) =>
            SendAsync<VenomResultDto>("POST", Arts(characterId) + "/venoms/absorb", new { item, quantity }, true, null, ct);

        public Task<ArtsDto> CurePoisonAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/venoms/cure", null, true, null, ct);

        public Task<ArtsDto> ApplyCoatingAsync(Guid characterId, string coatingId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/coatings", new { coatingId }, true, null, ct);

        public Task<ArtsDto> HatchGuAsync(Guid characterId, string type, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/gu/hatch", new { type }, true, null, ct);

        public Task<ArtsDto> FeedGuAsync(Guid characterId, int slot, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/gu/feed", new { slot }, true, null, ct);

        public Task<ArtsDto> ReleaseGuAsync(Guid characterId, int slot, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/gu/release", new { slot }, true, null, ct);

        public Task<ArtsDto> StartBathAsync(Guid characterId, string bathId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/tempering/bath", new { bathId }, true, null, ct);

        public Task<TemperResultDto> AdvanceTemperAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<TemperResultDto>("POST", Arts(characterId) + "/tempering/advance", null, true, null, ct);

        public Task<ArtsDto> OfferEchoesAsync(Guid characterId, int quantity, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/sword/echoes", new { quantity }, true, null, ct);

        public Task<ArtsDto> ChooseBranchAsync(Guid characterId, string branchId, CancellationToken ct) =>
            SendAsync<ArtsDto>("POST", Arts(characterId) + "/branch", new { branchId }, true, null, ct);

        // ---- estate -------------------------------------------------------------------------------------------

        private static string Estate(Guid characterId) => Character(characterId) + "/estate";

        public Task<EstateDto> GetEstateAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<EstateDto>("GET", Estate(characterId), null, true, null, ct);

        public Task<EstateDto> PlantAsync(Guid characterId, int slot, string seed, CancellationToken ct) =>
            SendAsync<EstateDto>("POST", Estate(characterId) + "/garden/plant", new { slot, seed }, true, null, ct);

        public Task<EstateDto> HarvestAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<EstateDto>("POST", Estate(characterId) + "/garden/harvest", null, true, null, ct);

        public Task<EstateDto> RecruitDiscipleAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<EstateDto>("POST", Estate(characterId) + "/disciples/recruit", null, true, null, ct);

        public Task<EstateDto> DismissDiscipleAsync(Guid characterId, Guid discipleId, CancellationToken ct) =>
            SendAsync<EstateDto>("POST", Estate(characterId) + "/disciples/dismiss", new { discipleId }, true, null, ct);

        public Task<EstateDto> StartExpeditionAsync(Guid characterId, Guid discipleId, string expeditionId, CancellationToken ct) =>
            SendAsync<EstateDto>("POST", Estate(characterId) + "/expeditions/start", new { discipleId, expeditionId }, true, null, ct);

        public Task<EstateDto> CollectExpeditionsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<EstateDto>("POST", Estate(characterId) + "/expeditions/collect", null, true, null, ct);

        // ---- arena --------------------------------------------------------------------------------------------

        public Task<ArenaDto> GetArenaAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ArenaDto>("GET", Character(characterId) + "/combat/arena", null, true, null, ct);

        public Task<BattleDto> ChallengeArenaAsync(Guid characterId, Guid opponentCharacterId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", Character(characterId) + "/combat/arena/challenge", new { opponentCharacterId }, true, NewIdempotencyKey(), ct);

        public Task<ArenaDto> ClaimArenaWeekAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ArenaDto>("POST", Character(characterId) + "/combat/arena/claim", null, true, null, ct);

        // ---- titles -------------------------------------------------------------------------------------------

        public async Task<List<TitleDto>> GetTitlesAsync(Guid characterId, CancellationToken ct) =>
            (await SendAsync<TitleListDto>("GET", Character(characterId) + "/quests/titles", null, true, null, ct).ConfigureAwait(false)).Items;

        public async Task<List<TitleDto>> SelectTitleAsync(Guid characterId, string titleId, CancellationToken ct) =>
            (await SendAsync<TitleListDto>("PUT", Character(characterId) + "/quests/titles/selected", new { titleId }, true, null, ct).ConfigureAwait(false)).Items;

        // ---- crafting queue, dismantling ----------------------------------------------------------------------

        public Task<CraftQueueDto> GetCraftQueueAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CraftQueueDto>("GET", Character(characterId) + "/crafting/queue", null, true, null, ct);

        public Task<CraftQueueDto> QueueCraftAsync(Guid characterId, string recipeId, int times, CancellationToken ct) =>
            SendAsync<CraftQueueDto>("POST", Character(characterId) + "/crafting/queue", new { recipeId, times }, true, NewIdempotencyKey(), ct);

        public Task<CraftQueueDto> CollectCraftQueueAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CraftQueueDto>("POST", Character(characterId) + "/crafting/queue/collect", null, true, null, ct);

        public Task<DismantleResultDto> DismantleAsync(Guid characterId, ItemInstanceDto item, CancellationToken ct) =>
            SendAsync<DismantleResultDto>("POST", Character(characterId) + "/inventory/dismantle", new { instanceId = item.Id, expectedVersion = item.Version }, true, NewIdempotencyKey(), ct);

        // ---- sect development ---------------------------------------------------------------------------------

        public Task<SectDevelopmentDto> GetSectDevelopmentAsync(Guid sectId, Guid characterId, CancellationToken ct) =>
            SendAsync<SectDevelopmentDto>("GET", "/api/v1/sects/" + sectId + "/development?characterId=" + characterId, null, true, null, ct);

        public Task<SectDevelopmentDto> BuildAsync(Guid sectId, Guid characterId, string buildingId, CancellationToken ct) =>
            SendAsync<SectDevelopmentDto>("POST", "/api/v1/sects/" + sectId + "/buildings", new { characterId, buildingId }, true, null, ct);

        public Task<SectDevelopmentDto> ClaimSectMissionAsync(Guid sectId, Guid characterId, string missionId, CancellationToken ct) =>
            SendAsync<SectDevelopmentDto>("POST", "/api/v1/sects/" + sectId + "/missions/claim", new { characterId, missionId }, true, null, ct);

        public Task<SectBossBattleDto> FightSectBossAsync(Guid sectId, Guid characterId, CancellationToken ct) =>
            SendAsync<SectBossBattleDto>("POST", "/api/v1/sects/" + sectId + "/boss/fight", new { characterId }, true, null, ct);
    }
}
