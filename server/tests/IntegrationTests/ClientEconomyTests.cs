using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Client")]
public class ClientEconomyTests : IAsyncLifetime
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
        return (api, await creation.FinalizeAsync(name, "qi", default));
    }

    private Task GiveAsync(Guid characterId, string itemId, long qty) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var r = await _host.Service<IInventory>().AddItemsAsync(uow, new CharacterId(characterId), itemId, qty, "test", Guid.NewGuid().ToString(), CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return r.Value;
        }, CancellationToken.None);

    [Fact]
    public async Task Craft_equip_upgrade_sell_and_buy_through_the_client()
    {
        var (smithApi, smith) = await PlayerAsync("kowal@test.dev", "Kowal Wu");
        var (buyerApi, buyer) = await PlayerAsync("kupiec@test.dev", "Kupiec Lin");
        await GiveAsync(smith.Id, "iron_ore", 5);
        await GiveAsync(smith.Id, "refining_stone", 1);
        await GiveAsync(smith.Id, "qi_pill", 2);

        var flow = new EconomyFlow(smithApi, smith.Id);
        await flow.LoadAsync(default);
        var recipe = flow.Recipes.Items.Single(r => r.Id == "forge_iron_sword");
        Assert.True(flow.CanAfford(recipe, 1));
        Assert.False(flow.CanAfford(recipe, 2));

        var crafted = await flow.CraftAsync(recipe.Id, 1, default);
        var sword = Assert.NotNull(flow.Inventory.Find(crafted.CreatedItems.Single()));
        Assert.True(await flow.ToggleEquipAsync(sword, default));
        Assert.Equal(2, flow.Inventory.Bonuses["strength"]);

        var stale = sword;                                            // version from before equipping
        Assert.False(await flow.ToggleEquipAsync(stale, default), "stale version reloads instead of failing");
        sword = flow.Inventory.Find(sword.Id)!;
        Assert.Equal(20, flow.NextUpgrade(sword)!.Stones);
        var up = Assert.NotNull(await flow.UpgradeAsync(sword, default));
        Assert.True(up.Success);
        sword = flow.Inventory.Find(sword.Id)!;
        Assert.Equal(1, sword.UpgradeLevel);

        var used = await flow.UseAsync("qi_pill", 2, default);
        Assert.Equal(120, used.QiGranted);

        Assert.True(await flow.ToggleEquipAsync(sword, default));    // unequip: only bag items can be sold
        var listing = await flow.SellAsync(flow.Inventory.Find(sword.Id)!, 50, default);
        Assert.Equal("listed", flow.Inventory.Find(sword.Id)!.State);

        var market = new EconomyFlow(buyerApi, buyer.Id);
        await market.LoadAsync(default);
        var offer = (await market.BrowseAsync("iron_sword", null, default)).Items.Single();
        Assert.Equal(1, offer.UpgradeLevel);
        var bought = Assert.NotNull(await market.BuyAsync(offer, default));
        Assert.Equal(50, bought.Paid);
        Assert.Equal(50, market.Inventory.Balance);
        Assert.Equal(sword.Id, market.Inventory.Items.Single().Id);
        Assert.Null(await market.BuyAsync(offer, default));            // already sold: a quiet "too late"

        await flow.ReloadAsync(default);
        Assert.Equal(100 - 20 - 20 - 1 + 48, flow.Inventory.Balance);  // craft, upgrade, fee, sale minus 5 % tax (2)
        var mine = await flow.MyListingsAsync(default);
        Assert.Equal(48L, mine.Single().Proceeds);
        Assert.Equal(listing.Id, mine.Single().Listing.Id);
    }
}
