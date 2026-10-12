using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Client")]
public class ClientSectTests : IAsyncLifetime
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

    private async Task<(ApiClient Api, CharacterDto C)> PlayerAsync(string email, string name, long stones)
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync(email, ApiHost.Password, default);
        await api.LoginAsync(email, ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        var c = await creation.FinalizeAsync(name, "qi", default);
        await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            await _host.Service<IInventory>().CreditAsync(uow, new CharacterId(c.Id), stones, "test", "funds", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return 0;
        }, CancellationToken.None);
        return (api, c);
    }

    [Fact]
    public async Task Found_apply_accept_and_donate_through_the_client()
    {
        var (leaderApi, leader) = await PlayerAsync("mistrz@test.dev", "Mistrz Sekty", 1000);
        var (pupilApi, pupil) = await PlayerAsync("adept@test.dev", "Adept Sekty", 500);

        var lead = new SectFlow(leaderApi, leader.Id);
        await lead.LoadAsync(default);
        Assert.Null(lead.Mine);
        await lead.FoundAsync("Sekta Klienta", default);
        Assert.True(lead.Mine!.IsLeader);

        var join = new SectFlow(pupilApi, pupil.Id);
        await join.LoadAsync(default);
        var sect = (await join.BrowseAsync(default)).Items.Single(s => s.Name == "Sekta Klienta");
        await join.ApplyAsync(sect.Id, "Chcę się uczyć.", default);

        await lead.LoadAsync(default);
        await lead.DecideAsync(lead.Mine!.PendingApplications.Single(), true, default);
        Assert.Equal(2, lead.Mine!.Sect.MemberCount);

        await join.LoadAsync(default);
        Assert.Equal("member", join.Mine!.MyRole);
        Assert.False(join.Mine.CanManage);
        await join.DonateAsync(100, default);
        Assert.Equal(100, join.Mine!.Treasury);
        Assert.Equal(1, join.Territories!.Max);
    }
}
