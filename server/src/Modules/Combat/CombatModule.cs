using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Combat.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Combat;

public static class CombatModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddCombatModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:CombatPath"] ?? "game-config/combat.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        var combat = LoadConfig(path);
        var catalog = Inventory.InventoryModule.LoadCatalog(configuration);
        var unknown = (combat.Tower?.Rewards.SelectMany(r => r.Items) ?? []).Concat(combat.WorldBoss?.KillItems ?? [])
            .Where(i => catalog.Find(i.Item) is null).Select(i => i.Item).Distinct().ToList();
        if (unknown.Count > 0) throw new InvalidOperationException($"Invalid combat config '{path}': unknown reward items {string.Join(", ", unknown)}.");
        services.AddSingleton(combat);
        var daoPath = configuration["GameConfig:DaoLabPath"] ?? "game-config/dao-lab.json";
        if (!Path.IsPathRooted(daoPath))
        {
            daoPath = Path.Combine(AppContext.BaseDirectory, daoPath);
        }

        services.AddSingleton(LoadDaoLabConfig(daoPath, catalog));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<ICombatRepository, PostgresCombatRepository>();
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("combat.intents", "DELETE FROM combat.intents WHERE ctid IN (SELECT ctid FROM combat.intents WHERE submitted_at < $1 - interval '7 days' AND battle_id IN (SELECT id FROM combat.battles WHERE status IN ('finished', 'declined', 'expired')) LIMIT 1000)"));
        }
        else
            services.AddSingleton<ICombatRepository, InMemoryCombatRepository>();
        services.AddScoped<CombatService>();
        services.AddScoped<Contracts.ICombatEncounters>(sp => sp.GetRequiredService<CombatService>());
        services.AddScoped<DaoLabService>();
        return services;
    }

    public static DaoLabConfig LoadDaoLabConfig(string path, Inventory.Contracts.IItemCatalog catalog)
    {
        var config = JsonSerializer.Deserialize<DaoLabConfig>(File.ReadAllText(path), StrictJson);
        var errors = DaoLabConfigValidator.Validate(config).ToList();
        if (config?.Scroll is { } s)
        {
            if (catalog.Find(s.ItemId) is not { Type: "scroll" }) errors.Add($"scroll.itemId '{s.ItemId}' must be an item of type scroll.");
            if (s.MaterialQuantity > 0 && catalog.Find(s.Material) is not { Stackable: true }) errors.Add($"scroll.material '{s.Material}' must be a stackable item.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid Dao Lab config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }

    public static CombatConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<CombatConfig>(File.ReadAllText(path), StrictJson);
        var errors = CombatConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid combat config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
