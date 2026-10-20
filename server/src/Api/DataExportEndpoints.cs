using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Application;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Identity.Application;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Notifications;
using JianghuGo.Modules.Quests.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Api;

/// <summary>
/// Data portability (2.5, GDPR art. 20): everything the game holds about the signed-in account, as one JSON document —
/// the account, each character with its cultivation, bag, quests and letters. Location samples are not kept, so there are none.
/// </summary>
public static class DataExportEndpoints
{
    public static IEndpointRouteBuilder MapDataExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/auth/export", async (HttpContext http, IUnitOfWorkFactory db, IAccountRepository accounts, CharacterService chars, CultivationService cultivation,
            InventoryService inventory, QuestService quests, SocialService social, TimeProvider clock, CancellationToken ct) =>
        {
            var accountId = http.User.GetAccountId();
            var account = await db.RunAsync(uow => accounts.FindByIdAsync(uow, accountId, ct), ct);
            if (account is null) return Results.NotFound();
            var characters = new List<object>();
            foreach (var c in await chars.ListAsync(accountId, ct))
            {
                characters.Add(new
                {
                    character = c,
                    cultivation = (await cultivation.GetAsync(accountId, c.Id, ct)).Value,
                    inventory = (await inventory.GetAsync(accountId, c.Id, ct)).Value,
                    quests = (await quests.GetAsync(accountId, c.Id, ct)).Value,
                    friends = (await social.FriendsAsync(accountId, c.Id, ct)).Value,
                    mail = (await social.MailAsync(accountId, c.Id, ct)).Value,
                });
            }

            http.Response.Headers.ContentDisposition = "attachment; filename=\"jianghu-go-export.json\"";
            return Results.Ok(new { exportedAt = clock.GetUtcNow(), account = new { id = account.Id.Value, account.Email, account.CreatedAt }, characters });
        }).WithTags("Identity").RequireAuthorization().RequireRateLimiting(JianghuGo.Modules.Identity.Api.IdentityEndpoints.AuthRateLimitPolicy);
        return app;
    }
}
