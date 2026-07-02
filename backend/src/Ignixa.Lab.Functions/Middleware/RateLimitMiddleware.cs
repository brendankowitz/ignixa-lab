using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// Enforces per-IP and global rate limits over the ASP.NET Core
/// <see cref="HttpContext"/> exposed by the HTTP-AspNetCore extension, mirroring
/// <see cref="CorsMiddleware"/>. Registered immediately after CORS so preflights
/// stay exempt and CORS headers are already present on 429 responses. See ADR-2608.
/// </summary>
public sealed class RateLimitMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RateLimitPolicy _policy;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(RateLimitPolicy policy, ILogger<RateLimitMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        _policy = policy;
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            await next(context);
            return;
        }

        var endpointClass = EndpointClassifier.Classify(context.FunctionDefinition.Name, httpContext.Request.Method);
        if (endpointClass == EndpointClass.Exempt)
        {
            await next(context);
            return;
        }

        var ipKey = ClientIpKeyExtractor.Extract(httpContext);

        using var decision = _policy.Acquire(endpointClass, ipKey);
        if (!decision.IsAllowed)
        {
            await RejectAsync(httpContext, endpointClass, ipKey, decision.RetryAfter);
            return;
        }

        await next(context);
    }

    private async Task RejectAsync(HttpContext httpContext, EndpointClass endpointClass, string ipKey, TimeSpan? retryAfter)
    {
        var retryAfterSeconds = retryAfter is { } value ? (int)Math.Ceiling(value.TotalSeconds) : 0;

        _logger.LogWarning(
            "Rate limit exceeded for {EndpointClass} from {IpKey}; retry after {RetryAfterSeconds}s.",
            endpointClass,
            ipKey,
            retryAfterSeconds);

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (retryAfterSeconds > 0)
        {
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        await httpContext.Response.WriteAsJsonAsync(
            new { error = $"Rate limit exceeded for this endpoint. Retry after {retryAfterSeconds} seconds." },
            SerializerOptions,
            httpContext.RequestAborted);
    }
}
