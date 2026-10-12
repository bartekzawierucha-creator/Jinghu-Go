using JianghuGo.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JianghuGo.Infrastructure.Http;

/// <summary>
/// A transaction that keeps losing serialization races (all retries in UnitOfWorkFactoryExtensions.RunAsync used up)
/// is overload, not a bug: answer 503 with Retry-After so the client repeats the request with the same Idempotency-Key
/// (nothing was committed). Every other unhandled exception stays a 500.
/// </summary>
public sealed class ConflictExceptionHandler(ILogger<ConflictExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not TransientConflictException) return false;
        logger.LogWarning("Serialization retries exhausted for {Path}; answering 503", httpContext.Request.Path.Value);
        httpContext.Response.Headers.RetryAfter = "1";
        await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "busy_retry", detail: "The server is busy; retry the same request shortly.",
            type: "https://jianghu-go.dev/errors/busy_retry", extensions: new Dictionary<string, object?> { ["code"] = "busy_retry" }).ExecuteAsync(httpContext);
        return true;
    }
}
