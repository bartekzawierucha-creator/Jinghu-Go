using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Application;

internal sealed class ItemCatalog(ItemsConfig config) : IItemCatalog
{
    public int MaxStack => config.Bag.MaxStack;

    public IEnumerable<CatalogItem> All => config.Items.Select(d => new CatalogItem(d.Id, d.NamePl, d.Type, d.Rarity, d.Stackable, d.Tradable, d.Equipment?.Slot));

    public CatalogItem? Find(string? itemId) =>
        config.Find(itemId) is { } d ? new CatalogItem(d.Id, d.NamePl, d.Type, d.Rarity, d.Stackable, d.Tradable, d.Equipment?.Slot) : null;
}
