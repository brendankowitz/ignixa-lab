using System.Net.Http.Headers;
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
    IHttpClientFactory httpClientFactory,
    IOptions<IgnixaLabOptions> options,
    ILogger<CapabilityFunction> logger)
{
    private static readonly MediaTypeWithQualityHeaderValue FhirJsonAccept = new("application/fhir+json");

    private readonly IgnixaLabOptions _options = options.Value;

    [Function("Capability")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "capability")] HttpRequest request,
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

        var metadataUri = new Uri(target.ToString().TrimEnd('/') + "/metadata");

        using var client = httpClientFactory.CreateClient(HttpEvaluatorFactory.HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, metadataUri);
        metadataRequest.Headers.Accept.Add(FhirJsonAccept);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(metadataRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Capability fetch failed for {Target}.", target);
            return UpstreamError($"Could not reach {target} for capability metadata: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Capability fetch timed out for {Target}.", target);
            return UpstreamError($"Request to {target} timed out while fetching capability metadata.", StatusCodes.Status504GatewayTimeout);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Capability fetch for {Target} returned {StatusCode}.", target, (int)response.StatusCode);
                return UpstreamError($"{target} returned HTTP {(int)response.StatusCode} for /metadata.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!CapabilityStatementParser.TryParse(body, out var resources, out var parseError))
            {
                logger.LogWarning("Capability fetch for {Target} returned an unparseable CapabilityStatement: {Error}", target, parseError);
                return UpstreamError(parseError);
            }

            return new OkObjectResult(new CapabilityResponse(target.ToString(), resolvedFhirVersion, resources));
        }
    }

    private static IActionResult UpstreamError(string message, int statusCode = StatusCodes.Status502BadGateway) =>
        new ObjectResult(new { error = message }) { StatusCode = statusCode };
}
