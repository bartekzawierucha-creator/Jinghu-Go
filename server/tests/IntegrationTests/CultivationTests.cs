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

    private double MethodMeditation(JsonObject character)
    {
        var config = _host.Service<JianghuGo.Modules.Cultivation.Domain.CultivationConfig>();
        var method = config.Method(JianghuGo.Modules.Cultivation.Domain.CultivationProfile.StarterMethod(character.Str("path")));
        var flow = 1000.0 + (method?.FlowPermille ?? 0);
        return (flow + (method?.MeditationPermille ?? 0)) / flow;
    }


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
        Assert.Equal("body_tempering", s.Str("realmId"));
        Assert.Equal(66, s.Int("realmCount"));
        Assert.Equal(12, s.Int("stagesInRealm"));
        Assert.Equal("idle", s.Str("mode"));
        Assert.Equal(1, s.Int("stage"));
        Assert.Equal(0, s.Int("qi"));
        Assert.Equal(100, s.Int("qiRequired"));
        Assert.True(s.Int("version") >= 0);
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
        var state = await StateAsync(p, c);
        var perHour = state["meditationPerHour"]!.GetValue<double>();
        Assert.True(Math.Abs(perHour - (5 * state["flowPerHour"]!.GetValue<double>() * MethodMeditation(c))) < 0.01, "meditation flows ×5");
        Assert.True(perHour > 0);
        var result = await MeditateAsync(p, c, TimeSpan.FromMinutes(10));
        var expected = (long)Math.Floor(perHour * 10 / 60);
        Assert.InRange((int)result["qiGained"]!.GetValue<long>(), (int)expected - 1, (int)expected);
        Assert.Equal(10, result.Int("minutesCounted"));
        Assert.Equal(result["qiGained"]!.GetValue<long>(), (await StateAsync(p, c))["qi"]!.GetValue<long>());
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
    public async Task Qi_flows_while_away_up_to_the_vessel_and_reports_a_summary_once()
    {
        var (p, c) = await NewCultivatorAsync("qi");
        await _host.DrainOutboxAsync(); // the character starts cultivating when it is created
        _host.Clock.Advance(TimeSpan.FromMinutes(20));
        await p.RefreshAsync();
        var s = await StateAsync(p, c);
        Assert.True(s["qi"]!.GetValue<long>() > 0, "idle flow ×1");

        _host.Clock.Advance(TimeSpan.FromHours(30));
        await p.RefreshAsync();
        var full = await StateAsync(p, c);
        Assert.Equal(full["vessel"]!.GetValue<long>(), full["qi"]!.GetValue<long>());
        Assert.True(full["summary"]!["vesselFull"]!.GetValue<bool>());
        var acked = await ApiHost.Json(await p.PostAsync(Base(c) + "/summary/ack"));
        Assert.True(acked["summary"] is null);

        // a bigger vessel is bought with crystals (none yet)
        var vessel = await p.PostAsync(Base(c) + "/vessel");
        Assert.Equal("insufficient_crystals", (await ApiHost.Json(vessel)).Str("code"));
    }

    [Fact]
    public async Task Retreat_and_method_rules_are_enforced()
    {
        var (p, c) = await NewCultivatorAsync("qi");
        var early = await p.PostAsync(Base(c) + "/retreat", new { hours = 8 });
        Assert.Equal("realm_too_low", (await ApiHost.Json(early)).Str("code"));
        var unknown = await p.PostAsync(Base(c) + "/method", new { methodId = "primordial_breath" });
        Assert.True(unknown.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.BadRequest, await unknown.Content.ReadAsStringAsync());
        var accel = await ApiHost.Json(await p.PostAsync(Base(c) + "/accelerator"));
        Assert.False(accel["acceleratorAvailable"]!.GetValue<bool>());
        Assert.True(accel["qi"]!.GetValue<long>() > 0, "the accelerator pays hours of flow at once");
        var again = await p.PostAsync(Base(c) + "/accelerator");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
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
        var method = _host.Service<JianghuGo.Modules.Cultivation.Domain.CultivationConfig>().Method("iron_body_canon")!;
        Assert.Equal(60 * 220 / 20 * (1000 + method.StepsPermille) / 1000, first.Int("qiGained"));
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
        await MeditateAsync(p, c, TimeSpan.FromMinutes(10));
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
        var deviated = (await StateAsync(p, c))["meditationPerHour"]!.GetValue<double>();
        Assert.True(Math.Abs(deviated - (before["meditationPerHour"]!.GetValue<double>() / 2)) < 0.01, "deviation halves the flow");
        var during = await MeditateAsync(p, c, TimeSpan.FromMinutes(20));
        Assert.InRange((int)during["qiGained"]!.GetValue<long>(), (int)Math.Floor(deviated / 3) - 1, (int)Math.Floor(deviated / 3));
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
