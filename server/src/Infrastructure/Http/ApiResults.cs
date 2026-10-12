using JianghuGo.Domain;
using Microsoft.AspNetCore.Http;

namespace JianghuGo.Infrastructure.Http;

/// <summary>Single mapping from domain errors to RFC 9457 problem responses (application/problem+json).</summary>
public static class ApiResults
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.InsufficientFunds => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.LimitReached => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static IResult Problem(Error error) =>
        Results.Problem(
            statusCode: StatusFor(error.Kind),
            title: error.Code,
            detail: error.Message,
            type: $"https://jianghu-go.dev/errors/{error.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static IResult From<T>(Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error!);

    /// <summary>Concurrent duplicate of an idempotent request that lost the race on the unique key.</summary>
    public static IResult IdempotencyRace() =>
        Problem(Error.Conflict("idempotency_in_progress", "A request with this Idempotency-Key is being processed; retry shortly."));
}
