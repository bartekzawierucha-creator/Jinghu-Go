using System.Net;
using System.Net.Http.Json;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>Offline single-player build: in-memory storage persisted to a snapshot file survives an app restart.</summary>
[Category("Integration")]
public class OfflineSnapshotTests
{
    [Fact]
    public async Task Progress_survives_a_restart_with_a_snapshot_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jianghu-snapshot-" + Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?> { ["Storage:InMemorySnapshotPath"] = Path.Combine(dir, "world.json") };
        try
        {
            string email, refresh, characterId;
            var first = await ApiHost.StartAsync(settings, postgres: false);
            await using (first)
            {
                var c = new Cultivators(first);
                var (p, character) = await c.NewAsync(stones: 250);
                await Cultivators.Ok(await p.PostAsync($"/api/v1/characters/{character.Str("id")}/cultivation/meditation/start"));
                email = p.Email;
                refresh = p.RefreshToken;
                characterId = character.Str("id");
            }

            Assert.True(File.Exists(Path.Combine(dir, "world.json")), "snapshot written");
            await using var second = await ApiHost.StartAsync(settings, postgres: false);
            var res = await second.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
            var tokens = await ApiHost.Json(res);
            var again = new Player(second, email, tokens);
            var list = await Cultivators.Ok(await again.GetAsync("/api/v1/characters"));
            Assert.Equal(characterId, list["items"]![0]!.Str("id"));
            var cultivation = await Cultivators.Ok(await again.GetAsync($"/api/v1/characters/{characterId}/cultivation"));
            Assert.True(cultivation["meditationStartedAt"] is not null, "meditation state kept");
            var bag = await Cultivators.Ok(await again.GetAsync($"/api/v1/characters/{characterId}/inventory"));
            Assert.Equal(250, bag.Int("balance"));
            await second.LoginAsync(email, ApiHost.Password);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
