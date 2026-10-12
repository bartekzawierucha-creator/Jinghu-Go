using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

// Usage: dotnet run -c Release -- --url http://localhost:8080 --users 50 --seconds 60 [--think-ms 0] [--max-error-rate 0.01] [--max-p95-ms 300]
// One-time onboarding calls (register, login, draft, finalize: deliberately slow password hashing) are reported but
// excluded from the p95 budget. Like the real client, a 503 busy_retry is retried (twice) with the same Idempotency-Key.
// The API must allow the burst of registrations (RateLimiting__AuthPermitsPerMinute, RateLimiting__GlobalPermitsPerMinute).
var opts = Options.Parse(args);
var retried = 0;
var stats = new ConcurrentDictionary<string, ConcurrentBag<(double Ms, bool Ok)>>();
using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 512, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    BaseAddress = new Uri(opts.Url),
    Timeout = TimeSpan.FromSeconds(30),
};

var deadline = DateTimeOffset.UtcNow.AddSeconds(opts.Seconds);
var run = Stopwatch.StartNew();
await Task.WhenAll(Enumerable.Range(0, opts.Users).Select(i => VirtualPlayerAsync(i)));
run.Stop();

var total = 0;
var errors = 0;
double worstP95 = 0;
string[] onboarding = ["register", "login", "draft", "finalize"];
Console.WriteLine($"{"operation",-22}{"count",8}{"errors",8}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}{"max ms",9}");
foreach (var (name, samples) in stats.OrderBy(s => s.Key, StringComparer.Ordinal))
{
    var ms = samples.Select(s => s.Ms).Order().ToArray();
    var err = samples.Count(s => !s.Ok);
    double P(double q) => ms[Math.Min(ms.Length - 1, (int)Math.Ceiling(q * ms.Length) - 1)];
    total += ms.Length;
    errors += err;
    if (!onboarding.Contains(name)) worstP95 = Math.Max(worstP95, P(0.95));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name,-22}{ms.Length,8}{err,8}{P(0.5),9:F1}{P(0.95),9:F1}{P(0.99),9:F1}{ms[^1],9:F1}"));
}

var rate = total == 0 ? 1 : errors / (double)total;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"\n{opts.Users} players, think {opts.ThinkMs} ms, {run.Elapsed.TotalSeconds:F0} s: {total} requests, {total / run.Elapsed.TotalSeconds:F0} req/s, error rate {rate:P2}, 503 retries {retried}, worst gameplay p95 {worstP95:F1} ms"));
var pass = rate <= opts.MaxErrorRate && worstP95 <= opts.MaxP95Ms;
Console.WriteLine(pass ? "RESULT: within budget" : $"RESULT: OVER BUDGET (max error rate {opts.MaxErrorRate:P2}, max p95 {opts.MaxP95Ms} ms)");
return pass ? 0 : 1;

async Task VirtualPlayerAsync(int n)
{
    var email = $"load{Guid.NewGuid():N}@load.test";
    var password = "load-test-password-" + n;
    if (await CallAsync("register", HttpMethod.Post, "/api/v1/auth/register", new { email, password }) is null) return;
    var tokens = await CallAsync("login", HttpMethod.Post, "/api/v1/auth/login", new { email, password });
    var token = tokens?["accessToken"]?.GetValue<string>();
    if (token is null) return;
    var draft = await CallAsync("draft", HttpMethod.Post, "/api/v1/characters/drafts", null, token);
    if (draft is null) return;
    var name = "Gracz " + new string([.. Guid.NewGuid().ToString("N")[..10].Select(ch => (char)('a' + (ch % 26)))]);
    var character = await CallAsync("finalize", HttpMethod.Post, $"/api/v1/characters/drafts/{draft["id"]}/finalize",
        new { expectedVersion = draft["version"]!.GetValue<int>(), name, path = "qi" }, token, idempotent: true);
    var id = character?["id"]?.GetValue<string>();
    if (id is null) return;

    while (DateTimeOffset.UtcNow < deadline)
    {
        if (opts.ThinkMs > 0) await Task.Delay(Random.Shared.Next(opts.ThinkMs / 2, opts.ThinkMs * 3 / 2 + 1));
        await CallAsync("get_character", HttpMethod.Get, $"/api/v1/characters/{id}", null, token);
        await CallAsync("get_inventory", HttpMethod.Get, $"/api/v1/characters/{id}/inventory", null, token);
        await CallAsync("get_cultivation", HttpMethod.Get, $"/api/v1/characters/{id}/cultivation", null, token);
        await CallAsync("meditation_start", HttpMethod.Post, $"/api/v1/characters/{id}/cultivation/meditation/start", null, token);
        await CallAsync("meditation_finish", HttpMethod.Post, $"/api/v1/characters/{id}/cultivation/meditation/finish", null, token, idempotent: true);
        await CallAsync("market_browse", HttpMethod.Get, "/api/v1/market/listings?limit=20", null, token);
        await CallAsync("notifications", HttpMethod.Get, "/api/v1/notifications?limit=10", null, token);
        var battle = await CallAsync("pve_start", HttpMethod.Post, "/api/v1/combat/battles/pve", new { characterId = id, npcId = "training_dummy" }, token, idempotent: true);
        for (var turn = 0; battle is not null && battle["status"]?.GetValue<string>() == "active" && turn < 30; turn++)
        {
            var slot = battle["mySlots"]!.AsArray().FirstOrDefault(s => s!["unavailable"] is null && s["kind"]!.GetValue<string>() == "attack")?["slot"]?.GetValue<int>();
            battle = await CallAsync("pve_action", HttpMethod.Post, $"/api/v1/combat/battles/{battle["id"]}/actions",
                new { turn = battle["turn"]!.GetValue<int>(), slot, direction = (string?)null }, token, idempotent: true);
        }
    }
}

async Task<JsonObject?> CallAsync(string op, HttpMethod method, string path, object? body, string? token = null, bool idempotent = false)
{
    var key = idempotent ? Guid.NewGuid().ToString("N") : null;
    var sw = Stopwatch.StartNew();
    try
    {
        HttpResponseMessage res;
        var attempt = 0;
        while (true)
        {
            using var req = new HttpRequestMessage(method, path);
            if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (key is not null) req.Headers.Add("Idempotency-Key", key);
            if (body is not null) req.Content = JsonContent.Create(body);
            res = await http.SendAsync(req);
            if (res.StatusCode != HttpStatusCode.ServiceUnavailable || attempt++ == 2 || (method != HttpMethod.Get && key is null)) break;
            Interlocked.Increment(ref retried);
            res.Dispose();
            await Task.Delay(250 * attempt);
        }

        using var _ = res;
        var text = await res.Content.ReadAsStringAsync();
        sw.Stop();
        // 409/422 are valid game answers under load (e.g. meditation already running); only 5xx, 429 and transport errors count as failures.
        var ok = (int)res.StatusCode < 500 && res.StatusCode != HttpStatusCode.TooManyRequests;
        stats.GetOrAdd(op, _ => []).Add((sw.Elapsed.TotalMilliseconds, ok));
        return res.IsSuccessStatusCode && text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text)!.AsObject() : null;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        stats.GetOrAdd(op, _ => []).Add((sw.Elapsed.TotalMilliseconds, false));
        return null;
    }
}

internal sealed record Options(string Url, int Users, int Seconds, int ThinkMs, double MaxErrorRate, double MaxP95Ms)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var i = Array.IndexOf(args, "--" + name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        var inv = CultureInfo.InvariantCulture;
        return new Options(Get("url", "http://localhost:8080"), int.Parse(Get("users", "20"), inv), int.Parse(Get("seconds", "30"), inv), int.Parse(Get("think-ms", "0"), inv),
            double.Parse(Get("max-error-rate", "0.01"), inv), double.Parse(Get("max-p95-ms", "500"), inv));
    }
}
