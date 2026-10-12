using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Client")]
public class ClientDaoLabTests : IAsyncLifetime
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

    private async Task<(ApiClient Api, CharacterDto C)> PlayerAsync(string email, string name)
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync(email, ApiHost.Password, default);
        await api.LoginAsync(email, ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        return (api, await creation.FinalizeAsync(name, "body", default));
    }

    private Task ArrangeAsync(Guid characterId, Func<IUnitOfWork, IInventory, CharacterId, Task> action) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            await action(uow, _host.Service<IInventory>(), new CharacterId(characterId));
            await uow.CommitAsync(CancellationToken.None);
            return 0;
        }, CancellationToken.None);

    [Fact]
    public async Task Design_equip_inscribe_sell_and_learn_through_the_client()
    {
        var (authorApi, author) = await PlayerAsync("autor@test.dev", "Autor Zhang");
        var (pupilApi, pupil) = await PlayerAsync("uczen@test.dev", "Uczeń Wei");
        await ArrangeAsync(author.Id, async (uow, inv, id) =>
        {
            await inv.CreditAsync(uow, id, 500, "test", "funds", CancellationToken.None);
            await inv.AddItemsAsync(uow, id, "jade", 1, "test", "jade", CancellationToken.None);
        });
        await ArrangeAsync(pupil.Id, (uow, inv, id) => inv.CreditAsync(uow, id, 500, "test", "funds", CancellationToken.None));

        var lab = new DaoLabFlow(authorApi, author.Id);
        await lab.LoadAsync(default);
        var tooStrong = DaoLabFlow.StarterStrike("Pięść Gromu");
        tooStrong.Attack!.Power = 30;
        tooStrong.QiCost = 0;
        Assert.Null(await lab.CreateAsync(tooStrong, default));
        Assert.False(lab.LastPreview!.Valid);
        Assert.True(lab.LastPreview.Points > lab.LastPreview.Allowance);

        var created = Assert.NotNull(await lab.CreateAsync(DaoLabFlow.StarterStrike("Pięść Gromu"), default));
        var techniqueId = created.Technique.Id;
        Assert.True(await lab.EquipAsync(techniqueId, "attack", default));
        Assert.Equal(techniqueId, lab.Loadout!.Slots[0].TechniqueId);

        var scroll = await lab.InscribeAsync(techniqueId, default);
        var economy = new EconomyFlow(authorApi, author.Id);
        await economy.LoadAsync(default);
        await economy.SellAsync(economy.Inventory.Find(scroll.InstanceId)!, 120, default);

        var market = new EconomyFlow(pupilApi, pupil.Id);
        await market.LoadAsync(default);
        var offer = (await market.BrowseAsync("technique_scroll", null, default)).Items.Single();
        var inspected = await pupilApi.GetDaoTechniqueAsync(offer.InstanceRef!, default);
        Assert.Equal("Pięść Gromu", inspected.NamePl);
        Assert.NotNull(await market.BuyAsync(offer, default));

        var pupilLab = new DaoLabFlow(pupilApi, pupil.Id);
        var learned = await pupilLab.LearnAsync(market.Inventory.Items.Single(i => i.BoundRef == techniqueId), default);
        Assert.Equal("scroll", learned.Source);
        Assert.True(await pupilLab.EquipAsync(techniqueId, "attack", default));
        Assert.Equal(techniqueId, pupilLab.Loadout!.Slots[0].TechniqueId);
        await market.ReloadAsync(default);
        Assert.Equal(0, market.Inventory.Items.Count);
    }
}
