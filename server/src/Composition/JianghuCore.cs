using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Outbox;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters;
using JianghuGo.Modules.Cultivation;
using JianghuGo.Modules.Identity;
using JianghuGo.Modules.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace JianghuGo.Composition;

public static class JianghuCore
{
    /// <summary>Everything both hosts need. Host-specific concerns (HTTP, hosted services) stay in the hosts.</summary>
    public static IServiceCollection AddJianghuCore(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRandomSeedSource, CsprngSeedSource>();
        services.TryAddSingleton<ICorrelationContext, NoCorrelation>();
        AddStorage(services, configuration, environment);

        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.Section));
        services.AddSingleton<OutboxDispatcher>();

        services.AddIdentityModule(configuration);
        services.AddCharactersModule(configuration);
        services.AddCultivationModule(configuration);
        services.AddNotificationsModule(configuration);
        return services;
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        switch (StorageProvider.From(configuration))
        {
            case StorageKind.Postgres:
                var connectionString = configuration.GetConnectionString("Jianghu")
                    ?? throw new InvalidOperationException("ConnectionStrings:Jianghu is required for Storage:Provider=Postgres.");
                services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
                services.AddSingleton<PostgresDatabase>();
                services.AddSingleton<IUnitOfWorkFactory>(sp => sp.GetRequiredService<PostgresDatabase>());
                services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
                services.AddSingleton<IAuditLog, PostgresAuditLog>();
                services.AddSingleton<IOutbox, PostgresOutbox>();
                break;
            case StorageKind.InMemory:
                if (environment.IsProduction())
                {
                    throw new InvalidOperationException("Storage:Provider=InMemory is not durable and is refused in Production.");
                }

                services.AddSingleton<InMemoryDatabase>();
                services.AddSingleton<IUnitOfWorkFactory>(sp => sp.GetRequiredService<InMemoryDatabase>());
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<IAuditLog, InMemoryAuditLog>();
                services.AddSingleton<IOutbox, InMemoryOutbox>();
                break;
        }
    }

    private sealed class NoCorrelation : ICorrelationContext
    {
        public string? CorrelationId => null;
    }
}
