using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Outbox;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Crafting;
using JianghuGo.Modules.Cultivation;
using JianghuGo.Modules.Geolocation;
using JianghuGo.Modules.Inventory;
using JianghuGo.Modules.Market;
using JianghuGo.Modules.Sects;
using JianghuGo.Modules.Territory;
using JianghuGo.Modules.World;
using JianghuGo.Modules.Identity;
using JianghuGo.Modules.Notifications;
using JianghuGo.Modules.Quests;
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
        services.AddInventoryModule(configuration);
        services.AddCraftingModule(configuration);
        services.AddMarketModule(configuration);
        services.AddCultivationModule(configuration);
        services.AddCombatModule(configuration);
        services.AddGeolocationModule(configuration);
        services.AddWorldModule(configuration);
        services.AddSectsModule(configuration);
        services.AddTerritoryModule(configuration);
        services.AddNotificationsModule(configuration);
        services.AddQuestsModule(configuration);
        return services;
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        switch (StorageProvider.From(configuration))
        {
            case StorageKind.Postgres:
                var connectionString = configuration.GetConnectionString("Jianghu")
                    ?? throw new InvalidOperationException("ConnectionStrings:Jianghu is required for Storage:Provider=Postgres.");
                // SERIALIZABLE + sequential scans = relation-level predicate locks, so unrelated players' transactions
                // conflict on small tables (measured in the Stage 8 load test). Following the PostgreSQL SSI guidance,
                // every query here is index-backed and seq scans are discouraged for the application's sessions.
                // JIT compilation only adds latency to short OLTP queries (and is triggered by the seq-scan penalty).
                var csb = new NpgsqlConnectionStringBuilder(connectionString);
                csb.Options = $"{csb.Options} -c jit=off".Trim();
                if (configuration.GetValue("Storage:PreferIndexScans", true))
                {
                    csb.Options += " -c enable_seqscan=off";
                }

                services.AddSingleton(_ => NpgsqlDataSource.Create(csb.ConnectionString));
                services.AddSingleton<PostgresDatabase>();
                services.AddSingleton<IUnitOfWorkFactory>(sp => sp.GetRequiredService<PostgresDatabase>());
                services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
                services.AddSingleton<IAuditLog, PostgresAuditLog>();
                services.AddSingleton<IOutbox, PostgresOutbox>();
                services.AddSingleton(new JianghuGo.Application.RetentionPolicy("platform.idempotency_records", "DELETE FROM platform.idempotency_records WHERE ctid IN (SELECT ctid FROM platform.idempotency_records WHERE created_at < $1 - interval '7 days' LIMIT 1000)"));
                services.AddSingleton(new JianghuGo.Application.RetentionPolicy("platform.outbox_messages", "DELETE FROM platform.outbox_messages WHERE ctid IN (SELECT ctid FROM platform.outbox_messages WHERE status = 'processed' AND processed_at < $1 - interval '7 days' LIMIT 1000)"));
                services.AddSingleton<RetentionRunner>();
                break;
            case StorageKind.InMemory:
                if (environment.IsProduction())
                {
                    throw new InvalidOperationException("Storage:Provider=InMemory is not durable and is refused in Production.");
                }

                var snapshot = configuration["Storage:InMemorySnapshotPath"];
                services.AddSingleton(_ => string.IsNullOrEmpty(snapshot) ? new InMemoryDatabase() : new InMemoryDatabase(snapshot));
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
