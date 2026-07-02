using Ignixa.Lab.Functions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// Adds CORS response headers for origins configured via
/// <see cref="IgnixaLabOptions.CorsAllowedOrigins"/>, so the hosted SPA (and
/// local dev servers) can call the API cross-origin. The isolated worker has
/// no Kestrel pipeline to attach <c>app.UseCors()</c> to, so this is applied
/// as worker middleware over the ASP.NET Core <see cref="HttpContext"/>
/// exposed by the HTTP-AspNetCore extension instead.
/// </summary>
public sealed class CorsMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IReadOnlySet<string> _allowedOrigins;

    public CorsMiddleware(IOptions<IgnixaLabOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _allowedOrigins = (options.Value.CorsAllowedOrigins ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

        var origin = httpContext.Request.Headers.Origin.ToString();
        var isAllowedOrigin = !string.IsNullOrEmpty(origin) && _allowedOrigins.Contains(origin);

        if (isAllowedOrigin)
        {
            httpContext.Response.Headers.AccessControlAllowOrigin = origin;
            httpContext.Response.Headers.Vary = "Origin";
        }

        if (HttpMethods.IsOptions(httpContext.Request.Method))
        {
            if (isAllowedOrigin)
            {
                httpContext.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
                httpContext.Response.Headers.AccessControlAllowHeaders = "Content-Type";
                httpContext.Response.Headers.AccessControlMaxAge = "3600";
            }

            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await next(context);
    }
}
