using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JianghuGo.TestKit;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.IntegrationTests;

/// <summary>
/// Stage 8 security sweep over EVERY route: hostile input must produce a clean 4xx problem, never a 500 or a stack trace,
/// and every route must refuse callers without a token (except the public allowlist).
/// </summary>
[Category("Security")]
public partial class RobustnessTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [GeneratedRegex(@"\{(\w+)(:[^}]*)?\}")]
    private static partial Regex RouteParam();

    private List<(string Method, string Path)> Routes() =>
        [.. _host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m,
                "/" + RouteParam().Replace(e.RoutePattern.RawText!.TrimStart('/'), p => p.Groups[2].Value.Contains("guid", StringComparison.Ordinal) ? Guid.NewGuid().ToString() : "1.1"))))
            .Where(r => !r.Item2.StartsWith("/health", StringComparison.Ordinal))];

    private static readonly string[] HostileBodies =
    [
        "{",                                                    // malformed JSON
        "[]",                                                   // wrong JSON type
        "{\"characterId\":\"not-a-guid\",\"quantity\":\"many\"}", // wrong field types
        "{\"unexpected\":true}",                               // unknown field (rejected by the strict binder)
        "{\"quantity\":-99999999999999999999,\"price\":1e400}", // overflow
        "{\"name\":\"" + new string('x', 5000) + "\"}",        // oversized string
        "\"\\u0000\"",                                          // control characters
    ];

    [Fact]
    public async Task Hostile_bodies_never_cause_server_errors()
    {
        var player = await _host.RegisterAndLoginAsync();
        var failures = new List<string>();
        foreach (var (method, path) in Routes().Where(r => r.Method is "POST" or "PUT"))
        {
            foreach (var body in HostileBodies)
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", player.AccessToken);
                req.Headers.Add("Idempotency-Key", Player.NewKey());
                var res = await _host.Client.SendAsync(req);
                var text = await res.Content.ReadAsStringAsync();
                if ((int)res.StatusCode >= 500 || text.Contains("   at ", StringComparison.Ordinal) || text.Contains("Exception", StringComparison.Ordinal))
                    failures.Add($"{method} {path} <- {body[..Math.Min(40, body.Length)]}: {(int)res.StatusCode} {text[..Math.Min(200, text.Length)]}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Every_protected_route_rejects_missing_and_forged_tokens()
    {
        var anonymous = new[] { "/api/v1/auth/register", "/api/v1/auth/login", "/api/v1/auth/refresh" };
        foreach (var (method, path) in Routes().Where(r => !anonymous.Contains(r.Path)))
        {
            foreach (var token in new[] { null, "jga_forged-token-value", "Bearer-less" })
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), path);
                if (token is not null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                if (method is "POST" or "PUT") req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                var res = await _host.Client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode, $"{method} {path} with token '{token}'");
            }
        }
    }
}
