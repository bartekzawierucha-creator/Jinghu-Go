using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JianghuGo.Domain;

namespace JianghuGo.Application;

public sealed record IdempotencyRecord(
    AccountId AccountId,
    string Operation,
    string Key,
    string RequestHash,
    string ResponseJson,
    DateTimeOffset CreatedAt);

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(IUnitOfWork uow, AccountId accountId, string operation, string key, CancellationToken ct);

    /// <summary>Must fail (throw <see cref="DuplicateKeyException"/>) if the (account, operation, key) triple exists.</summary>
    Task AddAsync(IUnitOfWork uow, IdempotencyRecord record, CancellationToken ct);
}

public sealed class DuplicateKeyException(string message) : Exception(message);

/// <summary>
/// Runs a side-effecting operation at most once per (account, operation, key).
/// The stored response is written in the SAME unit of work as the operation's effects, so either both
/// persist or neither does. Only successes are stored: a failed operation commits nothing, so retrying
/// it can never duplicate an effect.
/// </summary>
public static partial class Idempotency
{
    [GeneratedRegex("^[A-Za-z0-9_-]{8,128}$")]
    private static partial Regex KeyFormat();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Error? ValidateKey(string? key) =>
        key is not null && KeyFormat().IsMatch(key)
            ? null
            : Error.Validation("idempotency_key_invalid", "Header Idempotency-Key is required: 8-128 characters [A-Za-z0-9_-].");

    public static string HashRequest<TRequest>(TRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))));

    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(
        IIdempotencyStore store,
        IUnitOfWork uow,
        TimeProvider clock,
        AccountId accountId,
        string operation,
        string? key,
        TRequest request,
        Func<Task<Result<TResponse>>> action,
        CancellationToken ct)
    {
        if (ValidateKey(key) is { } keyError)
        {
            return keyError;
        }

        var requestHash = HashRequest(request);
        var existing = await store.FindAsync(uow, accountId, operation, key!, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return Error.Conflict("idempotency_key_reused", "This Idempotency-Key was already used with a different request body.");
            }

            return JsonSerializer.Deserialize<TResponse>(existing.ResponseJson, Json)!;
        }

        var result = await action();
        if (result.IsSuccess)
        {
            await store.AddAsync(
                uow,
                new IdempotencyRecord(accountId, operation, key!, requestHash, JsonSerializer.Serialize(result.Value, Json), clock.GetUtcNow()),
                ct);
        }

        return result;
    }
}
