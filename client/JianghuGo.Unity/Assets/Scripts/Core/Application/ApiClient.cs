using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JianghuGo.Client.Infrastructure;

namespace JianghuGo.Client.Application
{
    /// <summary>Failure returned by the server (problem+json) or by the network (Status 0, Code "network_error").</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(int status, string code, string detail, string correlationId)
            : base(code + ": " + detail)
        {
            Status = status;
            Code = code;
            CorrelationId = correlationId;
        }

        public int Status { get; }
        public string Code { get; }
        public string CorrelationId { get; }
        public bool IsNetworkError => Status == 0;
        public bool IsUnauthorized => Status == 401;
    }

    /// <summary>
    /// Typed client for /api/v1. Responsibilities:
    /// - attaches the access token; on 401 refreshes ONCE (single-flight across concurrent calls) and retries;
    /// - retries network failures of state-changing calls with the SAME Idempotency-Key, so a retry can never
    ///   apply an effect twice (the server replays the stored response);
    /// - persists the rotating refresh token via <see cref="ITokenStore"/>.
    /// </summary>
    public sealed partial class ApiClient
    {
        private readonly IHttpTransport _transport;
        private readonly IJsonSerializer _json;
        private readonly ITokenStore _tokens;
        private readonly int _networkRetries;
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private string _accessToken;

        public ApiClient(IHttpTransport transport, IJsonSerializer json, ITokenStore tokens, int networkRetries = 2)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _networkRetries = networkRetries;
        }

        public bool HasSession => _accessToken != null || _tokens.LoadRefreshToken() != null;

        /// <summary>Raised when the session cannot be recovered (refresh rejected): UI should go to login.</summary>
        public event Action SessionExpired;

        // ---- identity --------------------------------------------------------------------------

        public Task RegisterAsync(string email, string password, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/auth/register", new { email, password }, false, null, ct);

        public async Task LoginAsync(string email, string password, CancellationToken ct)
        {
            var pair = await SendAsync<TokenPairDto>("POST", "/api/v1/auth/login", new { email, password }, false, null, ct).ConfigureAwait(false);
            Store(pair);
        }

        /// <summary>A one-time recovery code for resetting the password (2.0); shown once.</summary>
        public async Task<string> NewRecoveryCodeAsync(CancellationToken ct) =>
            (await SendAsync<RecoveryCodeDto>("POST", "/api/v1/auth/recovery-code", null, true, null, ct).ConfigureAwait(false))?.RecoveryCode;

        public Task ResetPasswordAsync(string email, string recoveryCode, string newPassword, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/auth/reset-password", new { email, recoveryCode, newPassword }, false, null, ct);

        public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/auth/change-password", new { currentPassword, newPassword }, true, null, ct);

        /// <summary>Restores a session from the stored refresh token. Returns false if there is none or it is dead.</summary>
        public async Task<bool> TryResumeAsync(CancellationToken ct)
        {
            return _tokens.LoadRefreshToken() != null && await RefreshAsync(null, ct).ConfigureAwait(false);
        }

        public async Task LogoutAsync(CancellationToken ct)
        {
            try
            {
                if (_accessToken != null)
                {
                    await SendAsync<object>("POST", "/api/v1/auth/logout", null, true, null, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _accessToken = null;
                _tokens.Clear();
            }
        }

        // ---- characters -------------------------------------------------------------------------

        public async Task<List<CharacterDto>> GetCharactersAsync(CancellationToken ct) =>
            (await SendAsync<CharacterListDto>("GET", "/api/v1/characters", null, true, null, ct).ConfigureAwait(false)).Items;

        public Task<CharacterDto> GetCharacterAsync(Guid id, CancellationToken ct) =>
            SendAsync<CharacterDto>("GET", "/api/v1/characters/" + id, null, true, null, ct);

        public Task<DraftDto> StartOrResumeDraftAsync(CancellationToken ct) =>
            SendAsync<DraftDto>("POST", "/api/v1/characters/drafts", null, true, null, ct);

        public Task<DraftDto> GetCurrentDraftAsync(CancellationToken ct) =>
            SendAsync<DraftDto>("GET", "/api/v1/characters/drafts/current", null, true, null, ct);

        public Task<DraftDto> RerollAsync(DraftDto draft, IList<string> lockedCategories, CancellationToken ct) =>
            SendAsync<DraftDto>("POST", "/api/v1/characters/drafts/" + draft.Id + "/reroll",
                new { expectedVersion = draft.Version, lockedCategories }, true, NewIdempotencyKey(), ct);

        public Task<CharacterDto> FinalizeAsync(DraftDto draft, string name, string path, CancellationToken ct) =>
            SendAsync<CharacterDto>("POST", "/api/v1/characters/drafts/" + draft.Id + "/finalize",
                new { expectedVersion = draft.Version, name, path }, true, NewIdempotencyKey(), ct);

        // ---- cultivation ------------------------------------------------------------------------

        private static string Cultivation(Guid characterId) => "/api/v1/characters/" + characterId + "/cultivation";

        public Task<CultivationDto> GetCultivationAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("GET", Cultivation(characterId), null, true, null, ct);

        public Task<CultivationDto> StartMeditationAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CultivationDto>("POST", Cultivation(characterId) + "/meditation/start", null, true, null, ct);

        public Task<MeditationResultDto> FinishMeditationAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<MeditationResultDto>("POST", Cultivation(characterId) + "/meditation/finish", null, true, NewIdempotencyKey(), ct);

        /// <summary>Body path. <paramref name="steps"/> = pedometer delta since the last successful report.</summary>
        public Task<StepsResultDto> ReportStepsAsync(Guid characterId, int steps, CancellationToken ct) =>
            SendAsync<StepsResultDto>("POST", Cultivation(characterId) + "/steps", new { steps }, true, NewIdempotencyKey(), ct);

        public Task<BreakthroughResultDto> BreakthroughAsync(CultivationDto state, CancellationToken ct) =>
            SendAsync<BreakthroughResultDto>("POST", Cultivation(state.CharacterId) + "/breakthrough",
                new { expectedVersion = state.Version }, true, NewIdempotencyKey(), ct);

        // ---- combat -----------------------------------------------------------------------------

        public Task<BattleDto> StartPveAsync(Guid characterId, string npcId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/pve", new { characterId, npcId }, true, NewIdempotencyKey(), ct);

        public Task<BattleDto> ChallengeAsync(Guid characterId, Guid opponentCharacterId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/pvp", new { characterId, opponentCharacterId }, true, NewIdempotencyKey(), ct);

        public Task<BattleDto> ForfeitAsync(Guid battleId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/" + battleId + "/forfeit", null, true, null, ct);

        public Task<BattleDto> AcceptChallengeAsync(Guid battleId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/" + battleId + "/accept", null, true, null, ct);

        /// <summary>Turns a challenge down (the challenger is told).</summary>
        public Task<BattleDto> DeclineChallengeAsync(Guid battleId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/" + battleId + "/decline", null, true, null, ct);

        public Task<BattleDto> GetBattleAsync(Guid battleId, CancellationToken ct) =>
            SendAsync<BattleDto>("GET", "/api/v1/combat/battles/" + battleId, null, true, null, ct);

        /// <summary>slot null = rest. Direction is obsolete (no distance since 1.3) and ignored by the server.</summary>
        public Task<BattleDto> ActAsync(BattleDto battle, int? slot, string direction, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/" + battle.Id + "/actions",
                new { turn = battle.Turn, slot, direction }, true, NewIdempotencyKey(), ct);

        // ---- exploration & world ------------------------------------------------------------------

        private static string Exploration(Guid characterId) => "/api/v1/characters/" + characterId + "/exploration";

        public Task<ExplorationSessionDto> StartExplorationAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ExplorationSessionDto>("POST", Exploration(characterId) + "/start", null, true, null, ct);

        public Task<ExplorationSessionDto> StopExplorationAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ExplorationSessionDto>("POST", Exploration(characterId) + "/stop", null, true, null, ct);

        /// <summary>Safe to retry as-is: the server drops samples it has already seen.</summary>
        public Task<SamplesResultDto> SendSamplesAsync(Guid characterId, IList<Infrastructure.LocationFix> fixes, CancellationToken ct)
        {
            var samples = new List<object>();
            foreach (var f in fixes) samples.Add(new { lat = f.Lat, lon = f.Lon, accuracyM = f.AccuracyM, capturedAt = f.CapturedAt, mock = f.Mock });
            return SendAsync<SamplesResultDto>("POST", Exploration(characterId) + "/samples", new { samples }, true, null, ct, retryPost: true);
        }

        public Task<FlameSitesDto> GetFlameSitesAsync(CancellationToken ct) =>
            SendAsync<FlameSitesDto>("GET", "/api/v1/world/flame-sites", null, true, null, ct);

        public Task<NearbyDto> GetNearbyAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<NearbyDto>("GET", "/api/v1/characters/" + characterId + "/world/nearby", null, true, null, ct);

        public Task<WorldInteractionDto> InteractAsync(Guid characterId, string nodeKey, CancellationToken ct) =>
            SendAsync<WorldInteractionDto>("POST", "/api/v1/world/interactions", new { characterId, nodeKey }, true, NewIdempotencyKey(), ct);

        public Task<WorldInteractionDto> AttuneAsync(Guid characterId, string siteId, CancellationToken ct) =>
            SendAsync<WorldInteractionDto>("POST", "/api/v1/world/flame-attunements", new { characterId, siteId }, true, NewIdempotencyKey(), ct);

        /// <summary>Cultivators exploring within 2 km (names and distance bands only).</summary>
        public Task<NearbyCultivatorsDto> GetNearbyCultivatorsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<NearbyCultivatorsDto>("GET", "/api/v1/characters/" + characterId + "/nearby", null, true, null, ct);

        // ---- progression: quests, meridians, rankings, spirit pet ---------------------------------

        private static string Quests(Guid characterId) => "/api/v1/characters/" + characterId + "/quests";

        public Task<QuestBoardDto> GetQuestsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<QuestBoardDto>("GET", Quests(characterId), null, true, null, ct);

        public Task<ClaimResultDto> ClaimQuestAsync(Guid characterId, string questId, CancellationToken ct) =>
            SendAsync<ClaimResultDto>("POST", Quests(characterId) + "/" + Uri.EscapeDataString(questId) + "/claim", null, true, null, ct);

        public Task<CheckinResultDto> CheckinAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CheckinResultDto>("POST", Quests(characterId) + "/checkin", null, true, null, ct);

        public Task<LeaderPageDto> GetLeadersAsync(Guid characterId, string metric, CancellationToken ct) =>
            SendAsync<LeaderPageDto>("GET", Quests(characterId) + "/leaders/" + Uri.EscapeDataString(metric), null, true, null, ct);

        public Task<MeridiansDto> GetMeridiansAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<MeridiansDto>("GET", Cultivation(characterId) + "/meridians", null, true, null, ct);

        public Task<MeridiansDto> OpenMeridianAsync(Guid characterId, string stat, int expectedSpent, CancellationToken ct) =>
            SendAsync<MeridiansDto>("POST", Cultivation(characterId) + "/meridians", new { stat, expectedSpent }, true, null, ct);

        public Task<MeridiansDto> ResetMeridiansAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<MeridiansDto>("POST", Cultivation(characterId) + "/meridians/reset", null, true, null, ct);

        public Task<RankingDto> GetRankingAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<RankingDto>("GET", Cultivation(characterId) + "/ranking?limit=50", null, true, null, ct);

        public Task<PetEnvelopeDto> GetPetAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<PetEnvelopeDto>("GET", "/api/v1/characters/" + characterId + "/combat/pet", null, true, null, ct);

        public Task<PetEnvelopeDto> ReleasePetAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<PetEnvelopeDto>("POST", "/api/v1/characters/" + characterId + "/combat/pet/release", null, true, null, ct);

        /// <summary>The character's unfinished battles (active or waiting for the opponent).</summary>
        public Task<OpenBattlesDto> GetOpenBattlesAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<OpenBattlesDto>("GET", "/api/v1/characters/" + characterId + "/combat/battles", null, true, null, ct);

        /// <summary>Auto-battle: the server plays this turn for the player with the NPC AI.</summary>
        public Task<BattleDto> AutoActAsync(Guid battleId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/combat/battles/" + battleId + "/auto", null, true, NewIdempotencyKey(), ct);

        public Task<TowerDto> GetTowerAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<TowerDto>("GET", "/api/v1/characters/" + characterId + "/combat/tower", null, true, null, ct);

        public Task<BattleDto> StartTowerAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/characters/" + characterId + "/combat/tower/start", null, true, NewIdempotencyKey(), ct);

        public Task<WorldBossDto> GetWorldBossAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<WorldBossDto>("GET", "/api/v1/characters/" + characterId + "/combat/world-boss", null, true, null, ct);

        public Task<BattleDto> StartWorldBossAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<BattleDto>("POST", "/api/v1/characters/" + characterId + "/combat/world-boss/start", null, true, NewIdempotencyKey(), ct);

        public Task<WorldBossDto> ClaimWorldBossAsync(Guid characterId, int? cycle, CancellationToken ct) =>
            SendAsync<WorldBossDto>("POST", "/api/v1/characters/" + characterId + "/combat/world-boss/claim", new { cycle }, true, null, ct);

        public Task<HuntEnvelopeDto> GetHuntAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<HuntEnvelopeDto>("GET", "/api/v1/characters/" + characterId + "/combat/hunt", null, true, null, ct);

        public Task<HuntEnvelopeDto> StartHuntAsync(Guid characterId, string npcId, CancellationToken ct) =>
            SendAsync<HuntEnvelopeDto>("POST", "/api/v1/characters/" + characterId + "/combat/hunt", new { npcId }, true, null, ct);

        public Task<HuntEnvelopeDto> ClaimHuntAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<HuntEnvelopeDto>("POST", "/api/v1/characters/" + characterId + "/combat/hunt/claim", null, true, null, ct);

        public Task<ProfessionsDto> GetProfessionsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<ProfessionsDto>("GET", "/api/v1/characters/" + characterId + "/crafting/professions", null, true, null, ct);

        public Task<CombatCatalogDto> GetCombatCatalogAsync(CancellationToken ct) =>
            SendAsync<CombatCatalogDto>("GET", "/api/v1/combat/catalog", null, true, null, ct);

        // ---- notifications ----------------------------------------------------------------------

        public Task<NotificationPageDto> GetNotificationsAsync(bool unreadOnly, int limit, Guid? before, CancellationToken ct) =>
            SendAsync<NotificationPageDto>("GET", "/api/v1/notifications?unreadOnly=" + (unreadOnly ? "true" : "false") + "&limit=" + limit
                + (before.HasValue ? "&before=" + before.Value : string.Empty), null, true, null, ct);

        public Task MarkNotificationReadAsync(Guid id, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/notifications/" + id + "/read", null, true, null, ct);

        // ---- inventory --------------------------------------------------------------------------

        private static string Inventory(Guid characterId) => "/api/v1/characters/" + characterId + "/inventory";

        public Task<InventoryDto> GetInventoryAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<InventoryDto>("GET", Inventory(characterId), null, true, null, ct);

        public Task<InventoryDto> EquipAsync(Guid characterId, ItemInstanceDto item, CancellationToken ct) =>
            SendAsync<InventoryDto>("POST", Inventory(characterId) + "/equip", new { instanceId = item.Id, expectedVersion = item.Version }, true, null, ct);

        public Task<InventoryDto> UnequipAsync(Guid characterId, ItemInstanceDto item, CancellationToken ct) =>
            SendAsync<InventoryDto>("POST", Inventory(characterId) + "/unequip", new { instanceId = item.Id, expectedVersion = item.Version }, true, null, ct);

        public Task<UseResultDto> UseItemAsync(Guid characterId, string itemId, int quantity, CancellationToken ct) =>
            SendAsync<UseResultDto>("POST", Inventory(characterId) + "/use", new { itemId, quantity }, true, NewIdempotencyKey(), ct);

        public Task<InventoryDto> DiscardStackAsync(Guid characterId, string itemId, int quantity, CancellationToken ct) =>
            SendAsync<InventoryDto>("POST", Inventory(characterId) + "/discard", new { itemId, quantity, instanceId = (Guid?)null }, true, NewIdempotencyKey(), ct);

        public Task<InventoryDto> DiscardItemAsync(Guid characterId, Guid instanceId, CancellationToken ct) =>
            SendAsync<InventoryDto>("POST", Inventory(characterId) + "/discard", new { itemId = (string)null, quantity = 1, instanceId }, true, NewIdempotencyKey(), ct);

        public Task<WalletPageDto> GetWalletAsync(Guid characterId, Guid? before, int limit, CancellationToken ct) =>
            SendAsync<WalletPageDto>("GET", Inventory(characterId) + "/wallet?limit=" + limit + (before.HasValue ? "&before=" + before.Value : string.Empty), null, true, null, ct);

        // ---- crafting ---------------------------------------------------------------------------

        public Task<RecipeBookDto> GetRecipesAsync(CancellationToken ct) =>
            SendAsync<RecipeBookDto>("GET", "/api/v1/crafting/recipes", null, true, null, ct);

        public Task<CraftResultDto> CraftAsync(Guid characterId, string recipeId, int times, CancellationToken ct) =>
            SendAsync<CraftResultDto>("POST", "/api/v1/characters/" + characterId + "/crafting/craft", new { recipeId, times }, true, NewIdempotencyKey(), ct);

        public Task<UpgradeResultDto> UpgradeAsync(Guid characterId, ItemInstanceDto item, CancellationToken ct) =>
            SendAsync<UpgradeResultDto>("POST", "/api/v1/characters/" + characterId + "/crafting/upgrade",
                new { instanceId = item.Id, expectedVersion = item.Version }, true, NewIdempotencyKey(), ct);

        // ---- market -----------------------------------------------------------------------------

        public Task<MarketRulesDto> GetMarketRulesAsync(CancellationToken ct) =>
            SendAsync<MarketRulesDto>("GET", "/api/v1/market/rules", null, true, null, ct);

        /// <summary>Search by name/type/rarity, cheapest per unit first (1.9).</summary>
        public Task<ListingPageDto> SearchMarketAsync(string q, string sort, int limit, CancellationToken ct) =>
            SendAsync<ListingPageDto>("GET", "/api/v1/market/search?limit=" + limit + "&sort=" + Uri.EscapeDataString(sort ?? "unit_price")
                + (!string.IsNullOrWhiteSpace(q) ? "&q=" + Uri.EscapeDataString(q.Trim()) : string.Empty), null, true, null, ct);

        public Task<PriceDto> GetPricesAsync(string itemId, CancellationToken ct) =>
            SendAsync<PriceDto>("GET", "/api/v1/market/prices/" + Uri.EscapeDataString(itemId), null, true, null, ct);

        public Task<ListingPageDto> BrowseMarketAsync(string itemId, Guid? before, int limit, CancellationToken ct) =>
            SendAsync<ListingPageDto>("GET", "/api/v1/market/listings?limit=" + limit
                + (itemId != null ? "&itemId=" + Uri.EscapeDataString(itemId) : string.Empty)
                + (before.HasValue ? "&before=" + before.Value : string.Empty), null, true, null, ct);

        public Task<ListingDto> ListStackAsync(Guid characterId, string itemId, long quantity, long price, CancellationToken ct) =>
            SendAsync<ListingDto>("POST", "/api/v1/market/listings",
                new { characterId, itemId, quantity, instanceId = (Guid?)null, expectedVersion = (int?)null, price }, true, NewIdempotencyKey(), ct);

        public Task<ListingDto> ListItemAsync(Guid characterId, ItemInstanceDto item, long price, CancellationToken ct) =>
            SendAsync<ListingDto>("POST", "/api/v1/market/listings",
                new { characterId, itemId = (string)null, quantity = 1, instanceId = item.Id, expectedVersion = item.Version, price }, true, NewIdempotencyKey(), ct);

        public Task<PurchaseResultDto> BuyAsync(Guid characterId, Guid listingId, CancellationToken ct) =>
            SendAsync<PurchaseResultDto>("POST", "/api/v1/market/listings/" + listingId + "/buy", new { characterId }, true, NewIdempotencyKey(), ct);

        public Task<ListingDto> CancelListingAsync(Guid characterId, Guid listingId, CancellationToken ct) =>
            SendAsync<ListingDto>("POST", "/api/v1/market/listings/" + listingId + "/cancel", new { characterId }, true, null, ct);

        public Task<MyListingPageDto> GetMyListingsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<MyListingPageDto>("GET", "/api/v1/characters/" + characterId + "/market/listings", null, true, null, ct);

        // ---- loadout & Dao Lab ------------------------------------------------------------------

        public Task<LoadoutDto> GetLoadoutAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<LoadoutDto>("GET", "/api/v1/characters/" + characterId + "/combat/loadout", null, true, null, ct);

        /// <summary>slots: 8 technique ids (null = empty), in slot order.</summary>
        public Task<LoadoutDto> SetLoadoutAsync(Guid characterId, int expectedVersion, IList<string> slots, CancellationToken ct) =>
            SendAsync<LoadoutDto>("PUT", "/api/v1/characters/" + characterId + "/combat/loadout", new { expectedVersion, slots }, true, null, ct);

        public Task<DaoPreviewDto> PreviewDesignAsync(DaoDesignDto design, CancellationToken ct) =>
            SendAsync<DaoPreviewDto>("POST", "/api/v1/dao-lab/preview", design, true, null, ct, retryPost: true);

        public Task<KnownDaoTechniqueDto> DesignAsync(Guid characterId, DaoDesignDto design, CancellationToken ct) =>
            SendAsync<KnownDaoTechniqueDto>("POST", "/api/v1/characters/" + characterId + "/dao-lab/designs", design, true, NewIdempotencyKey(), ct);

        public Task<KnownDaoPageDto> GetKnownDaoTechniquesAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<KnownDaoPageDto>("GET", "/api/v1/characters/" + characterId + "/dao-lab/techniques", null, true, null, ct);

        public Task<DaoTechniqueDto> GetDaoTechniqueAsync(string techniqueId, CancellationToken ct) =>
            SendAsync<DaoTechniqueDto>("GET", "/api/v1/dao-lab/techniques/" + Uri.EscapeDataString(techniqueId), null, true, null, ct);

        public Task<ScrollDto> InscribeScrollAsync(Guid characterId, string techniqueId, CancellationToken ct) =>
            SendAsync<ScrollDto>("POST", "/api/v1/dao-lab/techniques/" + Uri.EscapeDataString(techniqueId) + "/scrolls", new { characterId }, true, NewIdempotencyKey(), ct);

        public Task<KnownDaoTechniqueDto> LearnScrollAsync(Guid characterId, ItemInstanceDto scroll, CancellationToken ct) =>
            SendAsync<KnownDaoTechniqueDto>("POST", "/api/v1/characters/" + characterId + "/dao-lab/scrolls/" + scroll.Id + "/learn",
                new { expectedVersion = scroll.Version }, true, NewIdempotencyKey(), ct);

        // ---- sects & territories ----------------------------------------------------------------

        public Task<SectPageDto> ListSectsAsync(Guid? before, CancellationToken ct) =>
            SendAsync<SectPageDto>("GET", "/api/v1/sects?limit=30" + (before.HasValue ? "&before=" + before.Value : string.Empty), null, true, null, ct);

        public Task<ChatPageDto> GetSectChatAsync(Guid sectId, Guid characterId, Guid? after, CancellationToken ct) =>
            SendAsync<ChatPageDto>("GET", "/api/v1/sects/" + sectId + "/chat?characterId=" + characterId + (after.HasValue ? "&after=" + after.Value : string.Empty), null, true, null, ct);

        public Task<ChatMessageDto> PostSectChatAsync(Guid sectId, Guid characterId, string text, CancellationToken ct) =>
            SendAsync<ChatMessageDto>("POST", "/api/v1/sects/" + sectId + "/chat", new { characterId, text }, true, NewIdempotencyKey(), ct);

        public Task<SectDto> GetSectAsync(Guid sectId, CancellationToken ct) => SendAsync<SectDto>("GET", "/api/v1/sects/" + sectId, null, true, null, ct);

        public Task<MySectDto> GetMySectAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<MySectDto>("GET", "/api/v1/characters/" + characterId + "/sect", null, true, null, ct);

        public Task<MySectDto> FoundSectAsync(Guid characterId, string name, CancellationToken ct) =>
            SendAsync<MySectDto>("POST", "/api/v1/sects", new { characterId, name }, true, NewIdempotencyKey(), ct);

        public Task<SectApplicationDto> ApplyToSectAsync(Guid characterId, Guid sectId, string message, CancellationToken ct) =>
            SendAsync<SectApplicationDto>("POST", "/api/v1/sects/" + sectId + "/applications", new { characterId, message }, true, NewIdempotencyKey(), ct);

        public Task<SectApplicationDto> DecideApplicationAsync(Guid characterId, Guid applicationId, bool accept, CancellationToken ct) =>
            SendAsync<SectApplicationDto>("POST", "/api/v1/sects/applications/" + applicationId + "/decision", new { characterId, accept }, true, null, ct);

        public Task<SectApplicationDto> WithdrawApplicationAsync(Guid characterId, Guid applicationId, CancellationToken ct) =>
            SendAsync<SectApplicationDto>("POST", "/api/v1/sects/applications/" + applicationId + "/withdraw", new { characterId }, true, null, ct);

        public Task<SectApplicationPageDto> MyApplicationsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<SectApplicationPageDto>("GET", "/api/v1/characters/" + characterId + "/sect/applications", null, true, null, ct);

        public Task<SectDto> LeaveSectAsync(Guid characterId, Guid sectId, CancellationToken ct) =>
            SendAsync<SectDto>("POST", "/api/v1/sects/" + sectId + "/leave", new { characterId }, true, null, ct);

        public Task<SectDto> KickAsync(Guid characterId, Guid sectId, Guid targetCharacterId, CancellationToken ct) =>
            SendAsync<SectDto>("POST", "/api/v1/sects/" + sectId + "/kick", new { characterId, targetCharacterId }, true, null, ct);

        public Task<SectDto> SetRoleAsync(Guid characterId, Guid sectId, Guid targetCharacterId, string role, CancellationToken ct) =>
            SendAsync<SectDto>("POST", "/api/v1/sects/" + sectId + "/roles", new { characterId, targetCharacterId, role }, true, null, ct);

        public Task<MySectDto> DonateAsync(Guid characterId, Guid sectId, long amount, CancellationToken ct) =>
            SendAsync<MySectDto>("POST", "/api/v1/sects/" + sectId + "/donations", new { characterId, amount }, true, NewIdempotencyKey(), ct);

        public Task<MySectDto> SetSeatAsync(Guid characterId, Guid sectId, CancellationToken ct) =>
            SendAsync<MySectDto>("POST", "/api/v1/sects/" + sectId + "/seat", new { characterId }, true, NewIdempotencyKey(), ct);

        public Task<TerritoryDto> GetTerritoryHereAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<TerritoryDto>("GET", "/api/v1/characters/" + characterId + "/territory/here", null, true, null, ct);

        public Task<SectTerritoriesDto> GetSectTerritoriesAsync(Guid sectId, CancellationToken ct) =>
            SendAsync<SectTerritoriesDto>("GET", "/api/v1/sects/" + sectId + "/territories", null, true, null, ct);

        public Task<TerritoryDto> ClaimTerritoryAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<TerritoryDto>("POST", "/api/v1/territories/claim", new { characterId }, true, NewIdempotencyKey(), ct);

        public Task<WarDto> ChallengeTerritoryAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<WarDto>("POST", "/api/v1/territories/challenge", new { characterId }, true, NewIdempotencyKey(), ct);

        public Task<TerritoryDto> CollectTributeAsync(Guid characterId, string territoryKey, CancellationToken ct) =>
            SendAsync<TerritoryDto>("POST", "/api/v1/territories/" + Uri.EscapeDataString(territoryKey) + "/tribute", new { characterId }, true, NewIdempotencyKey(), ct);

        // ---- plumbing ---------------------------------------------------------------------------

        public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

        private async Task<T> SendAsync<T>(string method, string path, object body, bool authenticated, string idempotencyKey, CancellationToken ct, bool retryPost = false)
        {
            var json = body == null ? null : _json.Serialize(body);
            var refreshed = false;
            while (true)
            {
                var token = _accessToken;
                if (authenticated && token == null && !refreshed)
                {
                    refreshed = true;
                    if (!await RefreshAsync(null, ct).ConfigureAwait(false))
                    {
                        throw Unauthorized();
                    }

                    continue;
                }

                var result = await SendWithNetworkRetryAsync(method, path, json, authenticated ? token : null, idempotencyKey, ct, retryPost).ConfigureAwait(false);
                if (result.StatusCode == 401 && authenticated && !refreshed)
                {
                    refreshed = true;
                    if (await RefreshAsync(token, ct).ConfigureAwait(false))
                    {
                        continue; // retry once with the new token (same idempotency key)
                    }

                    throw Unauthorized();
                }

                if (!result.IsSuccess)
                {
                    throw ToException(result);
                }

                return string.IsNullOrEmpty(result.Body) ? default : _json.Deserialize<T>(result.Body);
            }
        }

        private async Task<HttpResult> SendWithNetworkRetryAsync(string method, string path, string json, string token, string idempotencyKey, CancellationToken ct, bool retryPost = false)
        {
            var headers = new Dictionary<string, string>();
            if (token != null) headers["Authorization"] = "Bearer " + token;
            if (idempotencyKey != null) headers["Idempotency-Key"] = idempotencyKey;

            // GETs are safe to retry; POSTs only when protected by an idempotency key.
            var attempts = method == "GET" || idempotencyKey != null || retryPost ? 1 + _networkRetries : 1;
            HttpResult result = null;
            for (var i = 0; i < attempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << (i - 1))), ct).ConfigureAwait(false);
                }

                result = await _transport.SendAsync(method, path, json, headers, ct).ConfigureAwait(false);
                if (!result.IsNetworkError && result.StatusCode != 503)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Single-flight refresh. <paramref name="staleAccessToken"/> lets a caller that saw a 401 skip the
        /// refresh if another call already replaced the token in the meantime (avoids burning refresh tokens,
        /// which the server would treat as reuse).
        /// </summary>
        private async Task<bool> RefreshAsync(string staleAccessToken, CancellationToken ct)
        {
            await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (staleAccessToken != null && _accessToken != null && _accessToken != staleAccessToken)
                {
                    return true;
                }

                var refreshToken = _tokens.LoadRefreshToken();
                if (refreshToken == null)
                {
                    return false;
                }

                var result = await SendWithNetworkRetryAsync("POST", "/api/v1/auth/refresh",
                    _json.Serialize(new { refreshToken }), null, null, ct).ConfigureAwait(false);
                if (result.IsNetworkError)
                {
                    throw ToException(result); // keep the refresh token: the session may still be alive
                }

                if (!result.IsSuccess)
                {
                    _accessToken = null;
                    _tokens.Clear();
                    SessionExpired?.Invoke();
                    return false;
                }

                Store(_json.Deserialize<TokenPairDto>(result.Body));
                return true;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private void Store(TokenPairDto pair)
        {
            _accessToken = pair.AccessToken;
            _tokens.SaveRefreshToken(pair.RefreshToken);
        }

        private static ApiException Unauthorized() => new ApiException(401, "unauthorized", "Session expired; please log in again.", null);

        private ApiException ToException(HttpResult result)
        {
            if (result.IsNetworkError)
            {
                return new ApiException(0, "network_error", "Server unreachable. Check your connection.", null);
            }

            ProblemDto problem = null;
            try
            {
                if (!string.IsNullOrEmpty(result.Body)) problem = _json.Deserialize<ProblemDto>(result.Body);
            }
            catch (Exception)
            {
                // non-JSON error body (e.g. proxy page): fall through to a generic error
            }

            return new ApiException(result.StatusCode, problem?.Code ?? "http_" + result.StatusCode, problem?.Detail ?? "Unexpected server response.", problem?.CorrelationId);
        }
    }
}
