using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Estate.Application;
using JianghuGo.Modules.Estate.Domain;
using JianghuGo.Modules.Estate.Infrastructure;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Estate;

public static class EstateModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddEstateModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:EstatePath"] ?? "game-config/estate.json";
        if (!Path.IsPathRooted(path)) path = Path.Combine(AppContext.BaseDirectory, path);
        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
            services.AddSingleton<IEstateRepository, PostgresEstateRepository>();
        else
            services.AddSingleton<IEstateRepository, InMemoryEstateRepository>();
        services.AddScoped<EstateService>();
        return services;
    }

    public static EstateConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<EstateConfig>(File.ReadAllText(path), StrictJson);
        var errors = EstateConfigValidator.Validate(config);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid estate config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        return config!;
    }

    public static IEndpointRouteBuilder MapEstateEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/estate").WithTags("Estate").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/garden/plant", async (Guid characterId, PlantCommand cmd, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.PlantAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/garden/harvest", async (Guid characterId, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HarvestAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/disciples/recruit", async (Guid characterId, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.RecruitAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/disciples/dismiss", async (Guid characterId, DiscipleCommand cmd, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.DismissAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/expeditions/start", async (Guid characterId, ExpeditionCommand cmd, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartExpeditionAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/expeditions/collect", async (Guid characterId, HttpContext http, EstateService svc, CancellationToken ct) =>
            ApiResults.From(await svc.CollectExpeditionsAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        return app;
    }
}
