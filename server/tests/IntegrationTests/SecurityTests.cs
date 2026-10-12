using System.Net;
using System.Net.Http.Json;
using JianghuGo.TestKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.IntegrationTests;

[Category("Security")]
public class SecurityTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    // Acceptance #1: a user cannot read or modify someone else's character (or draft).
    [Fact]
    public async Task Cannot_read_or_modify_another_accounts_character_or_draft()
    {
        var owner = await _host.RegisterAndLoginAsync();
        var character = await owner.CreateCharacterAsync("Wlasciciel");
        var ownerDraft = await owner.CreateDraftAsync();

        var thief = await _host.RegisterAndLoginAsync();
        var read = await thief.GetAsync($"/api/v1/characters/{character["id"]}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode, "404, not 403: existence must not leak");

        var reroll = await thief.PostAsync($"/api/v1/characters/drafts/{ownerDraft["id"]}/reroll", new { expectedVersion = 1 }, Player.NewKey());
        Assert.Equal(HttpStatusCode.NotFound, reroll.StatusCode);
        var finalize = await thief.PostAsync($"/api/v1/characters/drafts/{ownerDraft["id"]}/finalize",
            new { expectedVersion = 1, name = "Zlodziej", path = "qi" }, Player.NewKey());
        Assert.Equal(HttpStatusCode.NotFound, finalize.StatusCode);

        var list = await ApiHost.Json(await thief.GetAsync("/api/v1/characters"));
        Assert.Equal(0, list["items"]!.AsArray().Count);
        var ownerView = await ApiHost.Json(await owner.GetAsync("/api/v1/characters/drafts/current"));
        Assert.Equal(1, ownerView.Int("version"));
        Assert.Equal(10, ownerView.Int("destinyTokens"));
    }

    [Fact]
    public void Every_endpoint_requires_authorization_unless_explicitly_public()
    {
        string[] publicRoutes =
        [
            "GET /health/live", "GET /health/ready",
            "POST /api/v1/auth/register", "POST /api/v1/auth/login", "POST /api/v1/auth/refresh",
        ];
        var endpoints = _host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.True(endpoints.Count > 5);
        foreach (var e in endpoints)
        {
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
            var route = $"{string.Join(",", methods)} /{e.RoutePattern.RawText!.TrimStart('/')}";
            var requiresAuth = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            Assert.True(requiresAuth || publicRoutes.Contains(route), $"Endpoint '{route}' is anonymous but not on the public allowlist");
            if (publicRoutes.Contains(route))
            {
                Assert.False(requiresAuth, $"Allowlisted '{route}' unexpectedly requires auth (update the allowlist)");
            }
        }
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_requests()
    {
        foreach (var url in new[] { "/api/v1/characters", "/api/v1/characters/drafts/current", "/api/v1/game-config/character-creation" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.GetAsync(new Uri(url, UriKind.Relative))).StatusCode, url);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.PostAsync(new Uri("/api/v1/characters/drafts", UriKind.Relative), null)).StatusCode);
    }

    [Fact]
    public async Task Oversized_request_body_is_rejected()
    {
        var huge = new string('a', 100 * 1024);
        var res = await _host.Client.PostAsJsonAsync("/api/v1/auth/register", new { email = "big@test.dev", password = huge });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited_per_client()
    {
        await using var host = await ApiHost.StartAsync(new() { ["RateLimiting:AuthPermitsPerMinute"] = "3" });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@test.dev", password = "whatever-123" })).StatusCode);
        }

        Assert.SequenceEqual([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Malformed_json_is_rejected_with_400()
    {
        using var content = new StringContent("{\"email\": ", System.Text.Encoding.UTF8, "application/json");
        var res = await _host.Client.PostAsync(new Uri("/api/v1/auth/register", UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
