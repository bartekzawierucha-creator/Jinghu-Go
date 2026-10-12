using Microsoft.Extensions.Configuration;

namespace JianghuGo.Infrastructure;

public enum StorageKind
{
    InMemory,
    Postgres,
}

public static class StorageProvider
{
    public static StorageKind From(IConfiguration configuration) => (configuration["Storage:Provider"] ?? "Postgres") switch
    {
        "InMemory" => StorageKind.InMemory,
        "Postgres" => StorageKind.Postgres,
        var other => throw new InvalidOperationException($"Unknown Storage:Provider '{other}'. Supported: Postgres, InMemory."),
    };
}
