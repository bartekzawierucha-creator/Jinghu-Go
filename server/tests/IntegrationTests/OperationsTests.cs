using System.Net;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class OperationsTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        Assert.Equal(HttpStatusCode.OK, (await _host.Client.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode);
        var ready = await _host.Client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correlation_id_is_echoed_or_generated()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        req.Headers.Add("X-Correlation-Id", "test-corr-123");
        var echoed = await _host.Client.SendAsync(req);
        Assert.Equal("test-corr-123", echoed.Headers.GetValues("X-Correlation-Id").Single());

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        bad.Headers.Add("X-Correlation-Id", "<script>alert(1)</script>");
        var replaced = await _host.Client.SendAsync(bad);
        Assert.NotEqual("<script>alert(1)</script>", replaced.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("nosniff", replaced.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Errors_use_problem_json_with_stable_code_and_correlation_id()
    {
        var p = await _host.RegisterAndLoginAsync();
        var res = await p.GetAsync($"/api/v1/characters/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var body = await ApiHost.Json(res);
        Assert.Equal("character_not_found", body.Str("code"));
        Assert.Equal(res.Headers.GetValues("X-Correlation-Id").Single(), body.Str("correlationId"));
    }

    [Fact]
    public async Task Unknown_route_returns_404_problem()
    {
        var res = await _host.Client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var post = await _host.Client.PostAsync(new Uri("/health/live", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }
}
