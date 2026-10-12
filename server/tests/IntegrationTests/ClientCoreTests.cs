using System.Text.Json;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>Engine-independent Unity client code (client/.../Scripts/Core) exercised against the real server.</summary>
[Category("Client")]
public class ClientCoreTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed class StjSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    }

    /// <summary>Delivers the request to the server but "loses" the first N responses (simulates a dropped connection).</summary>
    private sealed class LossyTransport(IHttpTransport inner, int responsesToLose) : IHttpTransport
    {
        private int _remaining = responsesToLose;
        public int Calls;

        public async Task<HttpResult> SendAsync(string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var result = await inner.SendAsync(method, path, jsonBody, headers, ct);
            return method == "POST" && path.EndsWith("/reroll", StringComparison.Ordinal) && Interlocked.Decrement(ref _remaining) >= 0
                ? new HttpResult(0, string.Empty)
                : result;
        }
    }

    private (ApiClient Api, ITokenStore Store) NewClient(IHttpTransport? transport = null)
    {
        var store = new InMemoryTokenStore();
        return (new ApiClient(transport ?? new HttpClientTransport(_host.NewClient()), new StjSerializer(), store), store);
    }

    [Fact]
    public async Task Full_flow_register_login_roll_lock_reroll_finalize()
    {
        var (api, _) = NewClient();
        await api.RegisterAsync("klient@test.dev", ApiHost.Password, default);
        await api.LoginAsync("klient@test.dev", ApiHost.Password, default);
        Assert.Equal(0, (await api.GetCharactersAsync(default)).Count);

        var flow = new CharacterCreationFlow(api);
        await flow.StartAsync(default);
        var bloodline = flow.Draft!.Traits.Single(t => t.Category == "bloodline").Id;
        Assert.True(flow.ToggleLock("bloodline"));
        Assert.Equal(3, flow.NextRerollCost);
        Assert.True(await flow.RerollAsync(default));
        Assert.Equal(bloodline, flow.Draft.Traits.Single(t => t.Category == "bloodline").Id);
        Assert.Equal(7, flow.Draft.DestinyTokens);

        var character = await flow.FinalizeAsync("Klient Testowy", "poison", default);
        Assert.Equal("poison", character.Path);
        Assert.Equal(1, (await api.GetCharactersAsync(default)).Count);
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_transparently()
    {
        var (api, store) = NewClient();
        await api.RegisterAsync("refresh@test.dev", ApiHost.Password, default);
        await api.LoginAsync("refresh@test.dev", ApiHost.Password, default);
        var firstRefresh = store.LoadRefreshToken();

        _host.Clock.Advance(TimeSpan.FromMinutes(20));
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.GetCharactersAsync(default))); // concurrent 401s -> one refresh
        Assert.NotEqual(firstRefresh, store.LoadRefreshToken());
        Assert.Equal(0, (await api.GetCharactersAsync(default)).Count, "session still healthy (no reuse-triggered revocation)");
    }

    [Fact]
    public async Task Lost_response_retry_reuses_idempotency_key_and_charges_once()
    {
        var lossy = new LossyTransport(new HttpClientTransport(_host.NewClient()), responsesToLose: 2);
        var (api, _) = NewClient(lossy);
        await api.RegisterAsync("lossy@test.dev", ApiHost.Password, default);
        await api.LoginAsync("lossy@test.dev", ApiHost.Password, default);
        var flow = new CharacterCreationFlow(api);
        await flow.StartAsync(default);

        Assert.True(await flow.RerollAsync(default));
        Assert.Equal(9, flow.Draft!.DestinyTokens);
        Assert.Equal(2, flow.Draft.Version);
    }

    [Fact]
    public async Task Stale_version_reloads_draft_instead_of_failing()
    {
        var (api1, store1) = NewClient();
        await api1.RegisterAsync("dwa@test.dev", ApiHost.Password, default);
        await api1.LoginAsync("dwa@test.dev", ApiHost.Password, default);
        var (api2, _) = NewClient();
        await api2.LoginAsync("dwa@test.dev", ApiHost.Password, default); // second device

        var phone = new CharacterCreationFlow(api1);
        var tablet = new CharacterCreationFlow(api2);
        await phone.StartAsync(default);
        await tablet.StartAsync(default);
        Assert.True(await tablet.RerollAsync(default));
        Assert.False(await phone.RerollAsync(default), "stale reroll must be rejected");
        Assert.Equal(2, phone.Draft!.Version);
        Assert.Equal(9, phone.Draft.DestinyTokens);
        Assert.NotNull(store1.LoadRefreshToken());
    }

    [Fact]
    public async Task Rejected_refresh_raises_session_expired_and_clears_tokens()
    {
        var (api, store) = NewClient();
        await api.RegisterAsync("expire@test.dev", ApiHost.Password, default);
        await api.LoginAsync("expire@test.dev", ApiHost.Password, default);
        var expired = false;
        api.SessionExpired += () => expired = true;

        _host.Clock.Advance(TimeSpan.FromDays(31));
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetCharactersAsync(default));
        Assert.True(ex.IsUnauthorized);
        Assert.True(expired);
        Assert.Null(store.LoadRefreshToken());
    }

    [Fact]
    public async Task Server_errors_surface_stable_codes()
    {
        var (api, _) = NewClient();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.RegisterAsync("bad", "short", default));
        Assert.Equal(400, ex.Status);
        Assert.Equal("email_invalid", ex.Code);
        Assert.NotNull(ex.CorrelationId);
    }
}
