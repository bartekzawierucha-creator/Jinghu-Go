using System.Net;
using System.Net.Http.Json;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class IdentityTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Register_login_and_access_protected_endpoint()
    {
        var p = await _host.RegisterAndLoginAsync();
        Assert.Contains("jga_", p.AccessToken);
        Assert.Contains("jgr_", p.RefreshToken);
        var res = await p.GetAsync("/api/v1/characters");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        await _host.RegisterAndLoginAsync("Mistrz@Wudang.dev");
        var res = await _host.Client.PostAsJsonAsync("/api/v1/auth/register", new { email = "mistrz@wudang.dev", password = ApiHost.Password });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("email_taken", (await ApiHost.Json(res)).Str("code"));
    }

    [Fact]
    public async Task Weak_password_and_bad_email_are_rejected()
    {
        var weak = await _host.Client.PostAsJsonAsync("/api/v1/auth/register", new { email = "a@b.dev", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("password_invalid", (await ApiHost.Json(weak)).Str("code"));
        var bad = await _host.Client.PostAsJsonAsync("/api/v1/auth/register", new { email = "not-an-email", password = ApiHost.Password });
        Assert.Equal("email_invalid", (await ApiHost.Json(bad)).Str("code"));
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_give_identical_error()
    {
        var p = await _host.RegisterAndLoginAsync();
        var wrong = await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = p.Email, password = "wrong-password-123" });
        var unknown = await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = "ghost@test.dev", password = "wrong-password-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await ApiHost.Json(wrong)).Str("code"), (await ApiHost.Json(unknown)).Str("code"));
    }

    // Acceptance #13: expired session gives no access.
    [Fact]
    public async Task Expired_access_token_is_rejected()
    {
        var p = await _host.RegisterAndLoginAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(16));
        var res = await p.GetAsync("/api/v1/characters");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("Bearer", res.Headers.WwwAuthenticate.ToString());
    }

    // Acceptance #13: revoked session gives no access.
    [Fact]
    public async Task Logout_revokes_access_and_refresh_tokens()
    {
        var p = await _host.RegisterAndLoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await p.PostAsync("/api/v1/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.GetAsync("/api/v1/characters")).StatusCode);
        var refresh = await _host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = p.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_kills_the_old_pair()
    {
        var p = await _host.RegisterAndLoginAsync();
        var oldAccess = p.AccessToken;
        var res = await _host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = p.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        p.Tokens = await ApiHost.Json(res);
        Assert.NotEqual(oldAccess, p.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.SendAsync(HttpMethod.Get, "/api/v1/characters", token: oldAccess)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await p.GetAsync("/api/v1/characters")).StatusCode);
    }

    [Fact]
    public async Task Refresh_token_reuse_revokes_whole_family()
    {
        var p = await _host.RegisterAndLoginAsync();
        var stolen = p.RefreshToken;
        var first = await _host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = stolen });
        p.Tokens = await ApiHost.Json(first);

        var replay = await _host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = stolen });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The legitimate holder's newest tokens die too: the attacker cannot keep a live branch.
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.GetAsync("/api/v1/characters")).StatusCode);
        var again = await _host.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = p.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_and_unlocks_after_timeout()
    {
        var p = await _host.RegisterAndLoginAsync();
        for (var i = 0; i < 5; i++)
        {
            await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = p.Email, password = "wrong-password-123" });
        }

        var locked = await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = p.Email, password = ApiHost.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode, "Correct password must not work during lockout");

        _host.Clock.Advance(TimeSpan.FromMinutes(16));
        var unlocked = await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = p.Email, password = ApiHost.Password });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    public async Task Garbage_bearer_tokens_are_rejected()
    {
        var p = await _host.RegisterAndLoginAsync();
        foreach (var token in new[] { "x", "jga_", "jga_" + new string('A', 43), p.RefreshToken, new string('a', 5000) })
        {
            var res = await p.SendAsync(HttpMethod.Get, "/api/v1/characters", token: token);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode, token[..Math.Min(10, token.Length)]);
        }
    }
}
