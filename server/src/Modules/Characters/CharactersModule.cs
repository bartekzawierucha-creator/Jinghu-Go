using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Characters.Application;
using JianghuGo.Modules.Characters.Domain;
using JianghuGo.Modules.Characters.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Characters;

public static class CharactersModule
{
    public static IServiceCollection AddCharactersModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:CharacterCreationPath"]
            ?? throw new InvalidOperationException("GameConfig:CharacterCreationPath is not configured.");
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<ICharacterRepository, PostgresCharacterRepository>();
            services.AddSingleton<IDestinyTokenLedger, PostgresDestinyTokenLedger>();
        }
        else
        {
            services.AddSingleton<ICharacterRepository, InMemoryCharacterRepository>();
            services.AddSingleton<IDestinyTokenLedger, InMemoryDestinyTokenLedger>();
        }
        services.AddScoped<CharacterService>();
        services.AddSingleton<Contracts.ICharacterDirectory, Contracts.CharacterDirectory>();
        services.AddSingleton<Contracts.IDestinyTokens, Contracts.DestinyTokens>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, Contracts.AccountDeletedRenamer>();
        return services;
    }

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Fails fast at startup: a server must never run with invalid game rules.</summary>
    public static CharacterCreationConfig LoadConfig(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<CharacterCreationConfig>(json, StrictJson);
        var errors = CharacterConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid character creation config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
