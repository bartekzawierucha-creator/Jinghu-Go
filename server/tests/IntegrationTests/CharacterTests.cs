using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Modules.Characters.Application;
using JianghuGo.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class CharacterTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Reroll(JsonObject draft) => $"/api/v1/characters/drafts/{draft["id"]}/reroll";
    private static string Finalize(JsonObject draft) => $"/api/v1/characters/drafts/{draft["id"]}/finalize";

    [Fact]
    public async Task Draft_has_three_traits_nine_stats_and_initial_tokens()
    {
        var p = await _host.RegisterAndLoginAsync();
        var first = await p.PostAsync("/api/v1/characters/drafts");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var draft = await ApiHost.Json(first);
        Assert.Equal(3, draft["traits"]!.AsArray().Count);
        Assert.Equal(9, draft["stats"]!.AsArray().Count);
        Assert.Equal(10, draft.Int("destinyTokens"));

        // Repeating the call returns the same active draft and grants nothing extra.
        var again = await p.PostAsync("/api/v1/characters/drafts");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var draft2 = await ApiHost.Json(again);
        Assert.Equal(draft.Str("id"), draft2.Str("id"));
        Assert.Equal(10, draft2.Int("destinyTokens"));
    }

    // Acceptance #2 (rewards/charges are applied once per request): same key -> one effect, same response.
    [Fact]
    public async Task Reroll_with_same_idempotency_key_charges_once()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var key = Player.NewKey();
        var body = new { expectedVersion = 1, lockedCategories = new[] { "bloodline" } };

        var r1 = await p.PostAsync(Reroll(draft), body, key);
        var r2 = await p.PostAsync(Reroll(draft), body, key);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode, await r1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var j1 = await ApiHost.Json(r1);
        Assert.Equal(j1.Compact(), (await ApiHost.Json(r2)).Compact());
        Assert.Equal(10 - 3, j1.Int("destinyTokens"));

        var current = await ApiHost.Json(await p.GetAsync("/api/v1/characters/drafts/current"));
        Assert.Equal(2, current.Int("version"));
        Assert.Equal(1, current.Int("rerollCount"));
        Assert.Equal(7, current.Int("destinyTokens"));
    }

    [Fact]
    public async Task Same_key_with_different_body_is_rejected()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var key = Player.NewKey();
        await p.PostAsync(Reroll(draft), new { expectedVersion = 1, lockedCategories = Array.Empty<string>() }, key);
        var res = await p.PostAsync(Reroll(draft), new { expectedVersion = 2, lockedCategories = Array.Empty<string>() }, key);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("idempotency_key_reused", (await ApiHost.Json(res)).Str("code"));
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected_without_charge()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var res = await p.PostAsync(Reroll(draft), new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("idempotency_key_invalid", (await ApiHost.Json(res)).Str("code"));
        Assert.Equal(10, (await ApiHost.Json(await p.GetAsync("/api/v1/characters/drafts/current"))).Int("destinyTokens"));
    }

    [Fact]
    public async Task Concurrent_rerolls_on_same_version_succeed_exactly_once()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            p.PostAsync(Reroll(draft), new { expectedVersion = 1 }, Player.NewKey())));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var current = await ApiHost.Json(await p.GetAsync("/api/v1/characters/drafts/current"));
        Assert.Equal(9, current.Int("destinyTokens"));
        Assert.Equal(2, current.Int("version"));
    }

    [Fact]
    public async Task Concurrent_duplicates_with_same_key_apply_once()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var key = Player.NewKey();
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            p.PostAsync(Reroll(draft), new { expectedVersion = 1 }, key)));

        Assert.True(responses.All(r => r.StatusCode == HttpStatusCode.OK), "All duplicates replay the stored response");
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        Assert.Equal(1, bodies.Distinct().Count());
        Assert.Equal(9, (await ApiHost.Json(await p.GetAsync("/api/v1/characters/drafts/current"))).Int("destinyTokens"));
    }

    // Acceptance #5 analogue: a failed operation leaves no partial charge.
    [Fact]
    public async Task Insufficient_tokens_fail_without_partial_charge()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var version = 1;
        for (var i = 0; i < 2; i++) // 2 x 5 tokens (two locks) = 10
        {
            var ok = await p.PostAsync(Reroll(draft), new { expectedVersion = version, lockedCategories = new[] { "background", "bloodline" } }, Player.NewKey());
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode, await ok.Content.ReadAsStringAsync());
            version++;
        }

        var broke = await p.PostAsync(Reroll(draft), new { expectedVersion = version }, Player.NewKey());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, broke.StatusCode);
        Assert.Equal("insufficient_destiny_tokens", (await ApiHost.Json(broke)).Str("code"));
        var current = await ApiHost.Json(await p.GetAsync("/api/v1/characters/drafts/current"));
        Assert.Equal(0, current.Int("destinyTokens"));
        Assert.Equal(version, current.Int("version"));
    }

    [Fact]
    public async Task Invalid_locks_are_rejected()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        foreach (var locks in new[] { new[] { "luck" }, ["bloodline", "bloodline"], ["background", "bloodline", "constitution"] })
        {
            var res = await p.PostAsync(Reroll(draft), new { expectedVersion = 1, lockedCategories = locks }, Player.NewKey());
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode, string.Join(",", locks));
        }
    }

    [Fact]
    public async Task Finalize_creates_character_with_server_rolled_stats()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var res = await p.PostAsync(Finalize(draft), new { expectedVersion = 1, name = "Li Mu Bai", path = "flame" }, Player.NewKey());
        Assert.Equal(HttpStatusCode.Created, res.StatusCode, await res.Content.ReadAsStringAsync());
        var character = await ApiHost.Json(res);
        Assert.Equal("Li Mu Bai", character.Str("name"));
        Assert.Equal("flame", character.Str("path"));
        Assert.Equal(draft["stats"]!.Compact(), character["stats"]!.Compact());
        Assert.Equal(draft["traits"]!.Compact(), character["traits"]!.Compact());

        var list = await ApiHost.Json(await p.GetAsync("/api/v1/characters"));
        Assert.Equal(1, list["items"]!.AsArray().Count);
        Assert.Equal(HttpStatusCode.NotFound, (await p.GetAsync("/api/v1/characters/drafts/current")).StatusCode);

        var got = await p.GetAsync($"/api/v1/characters/{character["id"]}");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode);
    }

    // Acceptance #6 analogue: the client cannot inject stats or results.
    [Fact]
    public async Task Client_supplied_stats_are_rejected()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        var res = await p.PostAsync(Finalize(draft), new { expectedVersion = 1, name = "Cheater", path = "qi", stats = new { strength = 99 } }, Player.NewKey());
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, (await ApiHost.Json(await p.GetAsync("/api/v1/characters")))["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Finalize_validates_name_path_and_uniqueness()
    {
        var a = await _host.RegisterAndLoginAsync();
        await a.CreateCharacterAsync("Xiao Long");

        var b = await _host.RegisterAndLoginAsync();
        var draft = await b.CreateDraftAsync();
        var taken = await b.PostAsync(Finalize(draft), new { expectedVersion = 1, name = "xiao long", path = "qi" }, Player.NewKey());
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("name_taken", (await ApiHost.Json(taken)).Str("code"));

        var badPath = await b.PostAsync(Finalize(draft), new { expectedVersion = 1, name = "Valid Name", path = "darkness" }, Player.NewKey());
        Assert.Equal("path_invalid", (await ApiHost.Json(badPath)).Str("code"));

        var badName = await b.PostAsync(Finalize(draft), new { expectedVersion = 1, name = "<b>", path = "qi" }, Player.NewKey());
        Assert.Equal("name_invalid", (await ApiHost.Json(badName)).Str("code"));
    }

    [Fact]
    public async Task Character_limit_is_enforced()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Pierwszy");
        await p.CreateCharacterAsync("Drugi");
        await p.CreateCharacterAsync("Trzeci");
        var res = await p.PostAsync("/api/v1/characters/drafts");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("character_limit_reached", (await ApiHost.Json(res)).Str("code"));
    }

    [Fact]
    public async Task Ledger_balance_matches_entries_and_rerolls_are_audited()
    {
        var p = await _host.RegisterAndLoginAsync();
        var draft = await p.CreateDraftAsync();
        await p.PostAsync(Reroll(draft), new { expectedVersion = 1 }, Player.NewKey());
        await p.PostAsync(Reroll(draft), new { expectedVersion = 2, lockedCategories = new[] { "background" } }, Player.NewKey());

        var services = _host.App.Services;
        var db = services.GetRequiredService<IUnitOfWorkFactory>();
        var ledger = services.GetRequiredService<IDestinyTokenLedger>();
        var audit = services.GetRequiredService<IAuditLog>();
        await using var uow = await db.BeginAsync(default);
        var accountId = (await audit.ListForEntityAsync(uow, "character_draft", draft.Str("id"), default))[0].AccountId!.Value;
        var entries = await ledger.ListEntriesAsync(uow, accountId, default);
        Assert.SequenceEqual([10, -1, -3], entries.Select(e => e.Delta));
        Assert.Equal(entries.Sum(e => e.Delta), await ledger.GetBalanceAsync(uow, accountId, default));
        Assert.Equal(entries[^1].BalanceAfter, await ledger.GetBalanceAsync(uow, accountId, default));

        var trail = await audit.ListForEntityAsync(uow, "character_draft", draft.Str("id"), default);
        Assert.SequenceEqual(["character.draft_created", "character.draft_rerolled", "character.draft_rerolled"], trail.Select(e => e.Action));
        Assert.True(trail.All(e => !string.IsNullOrEmpty(e.CorrelationId)), "audit entries carry correlation ids");
    }
}
