using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using JianghuGo.Api;
using JianghuGo.Domain;
using JianghuGo.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JianghuGo.IntegrationTests;

/// <summary>Starts the real API on a random loopback port with in-memory storage and a manual clock.</summary>
public sealed class ApiHost : IAsyncDisposable
{
    private ApiHost(WebApplication app, HttpClient client, ManualTimeProvider clock, string? database)
    {
        App = app;
        Client = client;
        Clock = clock;
        Database = database;
    }

    /// <summary>Name of the per-test PostgreSQL database, or null when running on the in-memory store.</summary>
    public string? Database { get; }

    public WebApplication App { get; }
    public HttpClient Client { get; }
    public ManualTimeProvider Clock { get; }

    /// <summary>When true the per-test database survives DisposeAsync (used to simulate a process restart).</summary>
    public bool KeepDatabase { get; set; }

    public static async Task<ApiHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        bool? postgres = null,
        IRandomSeedSource? seeds = null,
        string? existingDatabase = null,
        ManualTimeProvider? clock = null)
    {
        var usePostgres = existingDatabase is not null || (postgres ?? TestDatabase.SuiteUsesPostgres);
        var database = existingDatabase ?? (usePostgres ? await TestDatabase.CreateAsync() : null);
        clock ??= new ManualTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var app = JianghuApi.Create(["--urls", "http://127.0.0.1:0", "--environment", "Testing"], b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AuthPermitsPerMinute"] = "10000",
                ["RateLimiting:GlobalPermitsPerMinute"] = "100000",
                ["Storage:Provider"] = usePostgres ? "Postgres" : "InMemory",
                ["World:Seed"] = "1234567890123",
                ["Worker:RunInProcess"] = "false", // tests drain the outbox explicitly (OutboxDispatcher.DrainAsync)
                ["ConnectionStrings:Jianghu"] = usePostgres ? TestDatabase.ConnectionStringFor(database!) : null,
            });
            if (settings is not null)
            {
                b.Configuration.AddInMemoryCollection(settings);
            }

            b.Services.AddSingleton<TimeProvider>(clock);
            if (seeds is not null)
            {
                b.Services.AddSingleton(seeds);
            }
            b.Logging.SetMinimumLevel(LogLevel.Warning);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return new ApiHost(app, new HttpClient { BaseAddress = new Uri(address) }, clock, database);
    }

    public async ValueTask DisposeAsync()
    {
        if (Database is null && Environment.GetEnvironmentVariable("JIANGHU_TEST_SNAPSHOT") == "1")
        {
            // every table shape the suite produced must survive the offline-mode snapshot unchanged
            var tables = Service<JianghuGo.Infrastructure.InMemory.InMemoryDatabase>().Tables;
            var first = JianghuGo.Infrastructure.InMemory.InMemorySnapshot.Serialize(tables);
            var second = JianghuGo.Infrastructure.InMemory.InMemorySnapshot.Serialize(JianghuGo.Infrastructure.InMemory.InMemorySnapshot.Deserialize(first));
            Assert.Equal(first, second, "in-memory snapshot round trip");
        }

        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        if (Database is not null && !KeepDatabase)
        {
            await TestDatabase.DropAsync(Database);
        }
    }

    public HttpClient NewClient() => new() { BaseAddress = Client.BaseAddress };

    public T Service<T>() where T : notnull => App.Services.GetRequiredService<T>();

    /// <summary>Runs the outbox dispatcher until nothing is due (what the Worker does continuously).</summary>
    public Task<int> DrainOutboxAsync() => Service<JianghuGo.Infrastructure.Outbox.OutboxDispatcher>().DrainAsync(CancellationToken.None);

    // ---- helpers -------------------------------------------------------------------------------

    public const string Password = "correct-horse-battery";

    public async Task<Player> RegisterAndLoginAsync(string? email = null)
    {
        email ??= $"p{Guid.NewGuid():N}@test.dev";
        var reg = await Client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password });
        Assert.Equal(System.Net.HttpStatusCode.Created, reg.StatusCode, await reg.Content.ReadAsStringAsync());
        var tokens = await LoginAsync(email, Password);
        return new Player(this, email, tokens);
    }

    public async Task<JsonObject> LoginAsync(string email, string password)
    {
        var res = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await Json(res);
    }

    public static async Task<JsonObject> Json(HttpResponseMessage res) =>
        JsonNode.Parse(await res.Content.ReadAsStringAsync())!.AsObject();
}

public sealed class Player(ApiHost host, string email, JsonObject tokens)
{
    public string Email { get; } = email;
    public JsonObject Tokens { get; set; } = tokens;
    public string AccessToken => Tokens["accessToken"]!.GetValue<string>();
    public string RefreshToken => Tokens["refreshToken"]!.GetValue<string>();

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? idempotencyKey = null, string? token = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? AccessToken);
        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) req.Content = JsonContent.Create(body);
        return host.Client.SendAsync(req);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, string? key = null) => SendAsync(HttpMethod.Post, url, body, key);

    public async Task<JsonObject> CreateDraftAsync()
    {
        var res = await PostAsync("/api/v1/characters/drafts");
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    public async Task<JsonObject> CreateCharacterAsync(string name, string path = "qi")
    {
        var draft = await CreateDraftAsync();
        var res = await PostAsync($"/api/v1/characters/drafts/{draft["id"]}/finalize",
            new { expectedVersion = draft["version"]!.GetValue<int>(), name, path }, NewKey());
        Assert.Equal(System.Net.HttpStatusCode.Created, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>What a real client does after its 15-minute access token expires (tests advance the clock).</summary>
    public async Task RefreshAsync()
    {
        var res = await host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = RefreshToken });
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
        Tokens = await ApiHost.Json(res);
    }
}

internal static class JsonExt
{
    public static int Int(this JsonNode? node, string prop) => node![prop]!.GetValue<int>();
    public static string Str(this JsonNode? node, string prop) => node![prop]!.GetValue<string>();
    public static string Compact(this JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
