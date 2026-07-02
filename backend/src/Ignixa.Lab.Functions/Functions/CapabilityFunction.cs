using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Fetches and normalizes a target FHIR server's declared capabilities
/// (<c>GET {target}/metadata</c>) so the frontend can render a capability
/// coverage map. Invalid or rejected targets return HTTP 400, matching
/// <see cref="RunFunction"/>; upstream failures (unreachable server,
/// non-2xx response, unparseable body) return HTTP 502/504 so the frontend
/// can fall back to observed-only rendering.
/// </summary>
public sealed class CapabilityFunction(
    CapabilityStatementFetcher capabilityFetcher,
    IOptions<IgnixaLabOptions> options,
    ILogger<CapabilityFunction> logger)
{
    private readonly IgnixaLabOptions _options = options.Value;

    [Function("Capability")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "capability")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetUrl = request.Query["target"].ToString();
        if (!TargetUrlValidator.TryValidate(targetUrl, _options.AllowPrivateTargets, out var target, out var urlError))
        {
            return new BadRequestObjectResult(new { error = urlError });
        }

        var fhirVersion = request.Query["fhirVersion"].ToString();
        var resolvedFhirVersion = string.IsNullOrWhiteSpace(fhirVersion) ? _options.DefaultFhirVersion : fhirVersion;

        var fetchResult = await capabilityFetcher.FetchAsync(target, cancellationToken);
        if (!fetchResult.Success)
        {
            logger.LogWarning("Capability fetch failed for {Target}: {Error}", target, fetchResult.Error);
            var statusCode = fetchResult.FailureKind == CapabilityStatementFetchFailureKind.Timeout
                ? StatusCodes.Status504GatewayTimeout
                : StatusCodes.Status502BadGateway;
            return UpstreamError(fetchResult.Error!, statusCode);
        }

        if (!CapabilityStatementParser.TryParse(fetchResult.Json, out var resources, out var parseError))
        {
            logger.LogWarning("Capability fetch for {Target} returned an unparseable CapabilityStatement: {Error}", target, parseError);
            return UpstreamError(parseError);
        }

        return new OkObjectResult(new CapabilityResponse(target.ToString(), resolvedFhirVersion, resources));
    }

    private static IActionResult UpstreamError(string message, int statusCode = StatusCodes.Status502BadGateway) =>
        new ObjectResult(new { error = message }) { StatusCode = statusCode };
}
