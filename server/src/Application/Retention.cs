namespace JianghuGo.Application;

/// <summary>
/// A data-retention rule owned by a module (the module knows its tables). <see cref="DeleteBatchSql"/> deletes at most
/// one batch of expired rows and takes the current time as $1; the runner repeats it until a batch comes back short.
/// The rules mirror docs/operations/data-retention.md.
/// </summary>
public sealed record RetentionPolicy(string Name, string DeleteBatchSql, int BatchSize = 1000);
