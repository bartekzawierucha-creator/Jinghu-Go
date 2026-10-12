using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class CultivationTests : IAsyncLifetime
{
    private readonly QueuedSeedSource _seeds = new();
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync(seeds: _seeds);
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Base(JsonObject character) => $"/api/v1/characters/{character["id"]}/cultivation";

    private async Task<(Player Player, JsonObject Character)> NewCultivatorAsync(string path = "qi")
    {
        var p = await _host.RegisterAndLoginAsync();
        var c = await p.CreateCharacterAsync("Uczen " + Guid.NewGuid().ToString("N")[..6].Replace("0", "a").Replace("1", "b").Replace("2", "c")
            .Replace("3", "d").Replace("4", "e").Replace("5", "f").Replace("6", "g").Replace("7", "h").Replace("8", "i").Replace("9", "j"), path);
        return (p, c);
    }

    private static int Spirit(JsonObject character) =>
        character["stats"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == "spirit")!["value"]!.GetValue<int>();

    private async Task<JsonObject> MeditateAsync(Player p, JsonObject c, TimeSpan duration)
    {
        Assert.Equal(HttpStatusCode.OK, (await p.PostAsync(Base(c) + "/meditation/start")).StatusCode);
        _host.Clock.Advance(duration);
        await p.RefreshAsync();
        var res = await p.PostAsync(Base(c) + "/meditation/finish", null, Player.NewKey());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    private async Task<JsonObject> StateAsync(Player p, JsonObject c) => await ApiHost.Json(await p.GetAsync(Base(c)));

    [Fact]
    public async Task New_character_starts_at_first_stage_with_no_qi()
    {
        var (p, c) = await NewCultivatorAsync();
        var s = await StateAsync(p, c);
        Assert.Equal("qi_condensation", s.Str("realmId"));
        Assert.Equal(1, s.Int("stage"));
        Assert.Equal(0, s.Int("qi"));
        Assert.Equal(100, s.Int("qiRequired"));
        Assert.Equal(0, s.Int("version"));
    }

    [Fact]
    public async Task Cannot_see_or_cultivate_someone_elses_character()
    {
        var (_, c) = await NewCultivatorAsync();
        var thief = await _host.RegisterAndLoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await thief.GetAsync(Base(c))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thief.PostAsync(Base(c) + "/meditation/start")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thief.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = 0 }, Player.NewKey())).StatusCode);
    }

    [Fact]
    public async Task Meditation_yields_qi_from_server_time_and_path()
    {
        var (p, c) = await NewCultivatorAsync("qi");
        var result = await MeditateAsync(p, c, TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(59));
        // 30 whole minutes × 2 × (1 + spirit × 0.02) × 1.5 (Qi path)
        var expected = (long)Math.Floor(30m * 2m * (1 + (Spirit(c) * 0.02m)) * 1.5m);
        Assert.Equal(expected, result["qiGained"]!.GetValue<long>());
        Assert.Equal(30, result.Int("minutesCounted"));
        Assert.Equal(expected, (await StateAsync(p, c))["qi"]!.GetValue<long>());
    }

    // Acceptance #2: the same reward request twice does not grant twice.
    [Fact]
    public async Task Finishing_meditation_twice_with_same_key_grants_once()
    {
        var (p, c) = await NewCultivatorAsync();
        await p.PostAsync(Base(c) + "/meditation/start");
        _host.Clock.Advance(TimeSpan.FromMinutes(20));
        await p.RefreshAsync();
        var key = Player.NewKey();
        var first = await ApiHost.Json(await p.PostAsync(Base(c) + "/meditation/finish", null, key));
        var second = await ApiHost.Json(await p.PostAsync(Base(c) + "/meditation/finish", null, key));
        Assert.Equal(first.Compact(), second.Compact());
        Assert.Equal(first["qiGained"]!.GetValue<long>(), (await StateAsync(p, c))["qi"]!.GetValue<long>());

        var other = await p.PostAsync(Base(c) + "/meditation/finish", null, Player.NewKey());
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal("no_active_meditation", (await ApiHost.Json(other)).Str("code"));
    }

    [Fact]
    public async Task Concurrent_finish_requests_grant_once()
    {
        var (p, c) = await NewCultivatorAsync();
        await p.PostAsync(Base(c) + "/meditation/start");
        _host.Clock.Advance(TimeSpan.FromMinutes(60));
        await p.RefreshAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => p.PostAsync(Base(c) + "/meditation/finish", null, Player.NewKey())));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var gained = (await ApiHost.Json(responses.Single(r => r.StatusCode == HttpStatusCode.OK)))["qiGained"]!.GetValue<long>();
        Assert.Equal(gained, (await StateAsync(p, c))["qi"]!.GetValue<long>());
    }

    [Fact]
    public async Task Starting_twice_keeps_the_original_start_time()
    {
        var (p, c) = await NewCultivatorAsync();
        var a = await ApiHost.Json(await p.PostAsync(Base(c) + "/meditation/start"));
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        var b = await ApiHost.Json(await p.PostAsync(Base(c) + "/meditation/start"));
        Assert.Equal(a.Str("meditationStartedAt"), b.Str("meditationStartedAt"));
    }

    [Fact]
    public async Task Sessions_are_capped_and_daily_cap_resets_next_world_day()
    {
        var (p, c) = await NewCultivatorAsync("qi");
        var capped = await MeditateAsync(p, c, TimeSpan.FromHours(10));
        Assert.Equal(120, capped.Int("minutesCounted"));

        long total = capped["qiGained"]!.GetValue<long>();
        for (var i = 0; i < 6 && total < 1500; i++)
        {
            total += (await MeditateAsync(p, c, TimeSpan.FromMinutes(120)))["qiGained"]!.GetValue<long>();
        }

        Assert.Equal(1500, total, "daily cap");
        var blocked = await MeditateAsync(p, c, TimeSpan.FromMinutes(60));
        Assert.Equal(0, blocked["qiGained"]!.GetValue<long>());
        Assert.True(blocked["dailyCapReached"]!.GetValue<bool>());

        _host.Clock.Advance(TimeSpan.FromDays(1));
        await p.RefreshAsync();
        Assert.True((await MeditateAsync(p, c, TimeSpan.FromMinutes(10)))["qiGained"]!.GetValue<long>() > 0, "new day, new cap");
    }

    [Fact]
    public async Task Steps_only_for_body_path_and_implausible_counts_are_clipped()
    {
        var (qiPlayer, qiChar) = await NewCultivatorAsync("qi");
        var wrong = await qiPlayer.PostAsync(Base(qiChar) + "/steps", new { steps = 1000 }, Player.NewKey());
        Assert.Equal("path_mismatch", (await ApiHost.Json(wrong)).Str("code"));

        var (p, c) = await NewCultivatorAsync("body");
        var key = Player.NewKey();
        var first = await ApiHost.Json(await p.PostAsync(Base(c) + "/steps", new { steps = 50_000 }, key));
        Assert.Equal(60 * 220, first.Int("acceptedSteps"));     // first report: at most one hour of walking
        Assert.True(first["clipped"]!.GetValue<bool>());
        Assert.Equal(60 * 220 / 20, first.Int("qiGained"));
        var replay = await ApiHost.Json(await p.PostAsync(Base(c) + "/steps", new { steps = 50_000 }, key));
        Assert.Equal(first.Compact(), replay.Compact());

        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        var plausible = await ApiHost.Json(await p.PostAsync(Base(c) + "/steps", new { steps = 1_000 }, Player.NewKey()));
        Assert.Equal(1_000, plausible.Int("acceptedSteps"));
        Assert.False(plausible["clipped"]!.GetValue<bool>());

        var audit = await _host.Service<IUnitOfWorkFactory>().RunAsync(
            uow => _host.Service<IAuditLog>().ListForEntityAsync(uow, "character", c.Str("id"), default), default);
        Assert.Equal(1, audit.Count(a => a.Action == "cultivation.steps_clipped"));

        foreach (var bad in new[] { 0, -5, 100_001 })
        {
            var res = await p.PostAsync(Base(c) + "/steps", new { steps = bad }, Player.NewKey());
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode, bad.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task Breakthrough_requires_enough_qi_and_current_version()
    {
        var (p, c) = await NewCultivatorAsync();
        var poor = await p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = 0 }, Player.NewKey());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, poor.StatusCode);
        Assert.Equal("insufficient_qi", (await ApiHost.Json(poor)).Str("code"));

        await MeditateAsync(p, c, TimeSpan.FromMinutes(60));
        var state = await StateAsync(p, c);
        var stale = await p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = state.Int("version") - 1 }, Player.NewKey());
        Assert.Equal("version_conflict", (await ApiHost.Json(stale)).Str("code"));
    }

    [Fact]
    public async Task Successful_breakthrough_advances_stage_spends_qi_and_is_replayable()
    {
        var (p, c) = await NewCultivatorAsync();
        await MeditateAsync(p, c, TimeSpan.FromMinutes(60));
        var before = await StateAsync(p, c);
        var seed = QueuedSeedSource.Find(success: true, before["breakthroughChance"]!.GetValue<double>());
        _seeds.Enqueue(seed);

        var key = Player.NewKey();
        var res = await p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = before.Int("version") }, key);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
        var body = await ApiHost.Json(res);
        Assert.True(body["success"]!.GetValue<bool>());
        Assert.Equal(2, body["state"].Int("stage"));
        Assert.Equal(before["qi"]!.GetValue<long>() - 100, body["state"]!["qi"]!.GetValue<long>());

        // Same key: replayed, not re-rolled (a second roll could otherwise be "fished" for success).
        var again = await ApiHost.Json(await p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = before.Int("version") }, key));
        Assert.Equal(body.Compact(), again.Compact());

        var history = await ApiHost.Json(await p.GetAsync(Base(c) + "/breakthroughs"));
        Assert.Equal(1, history["items"]!.AsArray().Count);
        var stored = (await _host.Service<IUnitOfWorkFactory>().RunAsync(
            uow => _host.Service<JianghuGo.Modules.Cultivation.Application.ICultivationRepository>()
                .ListBreakthroughsAsync(uow, new Domain.CharacterId(Guid.Parse(c.Str("id"))), default), default)).Single();
        Assert.Equal(seed, stored.Seed);
        Assert.Equal(JianghuGo.Modules.Cultivation.Domain.CultivationRules.Roll(seed), stored.Roll, "roll is reproducible from the stored seed");
    }

    [Fact]
    public async Task Failed_breakthrough_causes_qi_deviation()
    {
        var (p, c) = await NewCultivatorAsync("qi");
        await MeditateAsync(p, c, TimeSpan.FromMinutes(60));
        var before = await StateAsync(p, c);
        _seeds.Enqueue(QueuedSeedSource.Find(success: false, before["breakthroughChance"]!.GetValue<double>()));

        var body = await ApiHost.Json(await p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = before.Int("version") }, Player.NewKey()));
        Assert.False(body["success"]!.GetValue<bool>());
        Assert.Equal(30, body["qiSpent"]!.GetValue<long>());            // 30% of 100 lost
        Assert.Equal(1, body["state"].Int("stage"));
        Assert.NotNull(body["state"]!["deviationUntil"]);

        var blocked = await p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = body["state"].Int("version") }, Player.NewKey());
        Assert.Equal("qi_deviation_active", (await ApiHost.Json(blocked)).Str("code"));

        // Meditating during deviation yields half.
        var during = await MeditateAsync(p, c, TimeSpan.FromMinutes(20));
        var normal = (long)Math.Floor(20m * 2m * (1 + (Spirit(c) * 0.02m)) * 1.5m);
        Assert.Equal((long)Math.Floor(normal / 2m), during["qiGained"]!.GetValue<long>());
    }

    [Fact]
    public async Task Concurrent_breakthroughs_resolve_exactly_once()
    {
        var (p, c) = await NewCultivatorAsync();
        await MeditateAsync(p, c, TimeSpan.FromMinutes(120));
        var s = await StateAsync(p, c);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            p.PostAsync(Base(c) + "/breakthrough", new { expectedVersion = s.Int("version") }, Player.NewKey())));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, (await ApiHost.Json(await p.GetAsync(Base(c) + "/breakthroughs")))["items"]!.AsArray().Count);
    }
}
