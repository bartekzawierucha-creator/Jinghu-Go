using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Arts.Application;
using JianghuGo.Modules.Arts.Domain;
using JianghuGo.Modules.Arts.Infrastructure;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Crafting.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Arts;

public static class ArtsModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddArtsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:ArtsPath"] ?? "game-config/arts.json";
        if (!Path.IsPathRooted(path)) path = Path.Combine(AppContext.BaseDirectory, path);
        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<IArtsRepository, PostgresArtsRepository>();
            services.AddSingleton(new RetentionPolicy("arts.handled_events",
                "DELETE FROM arts.handled_events WHERE ctid IN (SELECT ctid FROM arts.handled_events WHERE handled_at < $1 - interval '30 days' LIMIT 1000)"));
        }
        else
            services.AddSingleton<IArtsRepository, InMemoryArtsRepository>();
        services.AddScoped<ArtsService>();
        services.AddSingleton<ArtsReader>();
        services.AddSingleton<ICombatPerkBonuses, ArtsCombatPerks>();
        services.AddSingleton<ICombatStatBonuses, ArtsCombatStats>();
        services.AddSingleton<ICraftingBonuses, ArtsCraftingBonuses>();
        services.AddSingleton<ICharacterDecorationProvider, ArtsDecorations>();
        services.AddSingleton<ITitleSource, ArtsTitles>();
        services.AddSingleton<IOutboxHandler, ArtsBattleHandler>();
        services.AddSingleton<IOutboxHandler, ArtsStepsHandler>();
        services.AddSingleton<IOutboxHandler, ArtsRealmHandler>();
        return services;
    }

    public static ArtsConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<ArtsConfig>(File.ReadAllText(path), StrictJson);
        var errors = ArtsConfigValidator.Validate(config);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid arts config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        return config!;
    }
}
