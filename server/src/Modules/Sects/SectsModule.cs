using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Sects.Application;
using JianghuGo.Modules.Sects.Contracts;
using JianghuGo.Modules.Sects.Domain;
using JianghuGo.Modules.Sects.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Sects;

public static class SectsModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddSectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:SectsPath"] ?? "game-config/sects.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<ISectRepository, PostgresSectRepository>();
            services.AddSingleton<ISectDevelopmentRepository, PostgresSectDevelopmentRepository>();
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("sects.handled_events",
                "DELETE FROM sects.handled_events WHERE ctid IN (SELECT ctid FROM sects.handled_events WHERE handled_at < $1 - interval '30 days' LIMIT 1000)"));
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("sects.chat_messages",
                "DELETE FROM sects.chat_messages WHERE ctid IN (SELECT ctid FROM sects.chat_messages WHERE created_at < $1 - interval '30 days' LIMIT 1000)"));
        }
        else
        {
            services.AddSingleton<ISectRepository, InMemorySectRepository>();
            services.AddSingleton<ISectDevelopmentRepository, InMemorySectDevelopmentRepository>();
        }
        services.AddSingleton<ISectDirectory, SectDirectory>();
        services.AddScoped<SectService>();
        services.AddScoped<SectDevelopmentService>();
        services.AddSingleton<JianghuGo.Modules.Combat.Contracts.ICombatPerkBonuses, SectCombatPerks>();
        services.AddSingleton<JianghuGo.Modules.Crafting.Contracts.ICraftingBonuses, SectCraftingBonuses>();
        foreach (var type in new[] { JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinished, JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGathered,
                     JianghuGo.Modules.Crafting.Contracts.CraftingEvents.Crafted })
            services.AddSingleton<JianghuGo.Application.IOutboxHandler>(sp => new SectProgressHandler(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<SectsConfig>(), type));
        return services;
    }

    public static SectsConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<SectsConfig>(File.ReadAllText(path), StrictJson);
        var errors = SectsConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid sects config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
