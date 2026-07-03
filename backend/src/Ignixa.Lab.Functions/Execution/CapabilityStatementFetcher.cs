using System.Net.Http.Headers;
using Ignixa.Lab.Functions.Configuration;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Fetches a target FHIR server's declared <c>CapabilityStatement</c>
/// (<c>GET {target}/metadata</c>). Shared by <c>CapabilityFunction</c> (frontend
/// capability coverage map) and <see cref="TestScriptRunner"/> (evaluating
/// <c>requiresCapability</c>-gated tests), so the fetch/timeout/error handling
/// lives in exactly one place.
/// </summary>
public sealed class CapabilityStatementFetcher(
    IHttpClientFactory httpClientFactory,
    IOptions<IgnixaLabOptions> options)
{
    private static readonly MediaTypeWithQualityHeaderValue FhirJsonAccept = new("application/fhir+json");

    private readonly IgnixaLabOptions _options = options.Value;

    public async Task<CapabilityStatementFetchResult> FetchAsync(Uri target, CancellationToken cancellationToken)
    {
        var metadataUri = new Uri(target.ToString().TrimEnd('/') + "/metadata");

        using var client = httpClientFactory.CreateClient(HttpEvaluatorFactory.HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Get, metadataUri);
        request.Headers.Accept.Add(FhirJsonAccept);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return CapabilityStatementFetchResult.Failed(
                $"Could not reach {target} for capability metadata: {ex.Message}",
                CapabilityStatementFetchFailureKind.Unreachable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CapabilityStatementFetchResult.Failed(
                $"Request to {target} timed out while fetching capability metadata.",
                CapabilityStatementFetchFailureKind.Timeout);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return CapabilityStatementFetchResult.Failed(
                    $"{target} returned HTTP {(int)response.StatusCode} for /metadata.",
                    CapabilityStatementFetchFailureKind.NonSuccessStatus);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return CapabilityStatementFetchResult.Succeeded(body);
        }
    }
}

public enum CapabilityStatementFetchFailureKind
{
    None,
    Unreachable,
    Timeout,
    NonSuccessStatus
}

/// <summary>Outcome of a <see cref="CapabilityStatementFetcher.FetchAsync"/> call.</summary>
public sealed record CapabilityStatementFetchResult(
    bool Success,
    string? Json,
    string? Error,
    CapabilityStatementFetchFailureKind FailureKind = CapabilityStatementFetchFailureKind.None)
{
    public static CapabilityStatementFetchResult Succeeded(string json) => new(true, json, null);

    public static CapabilityStatementFetchResult Failed(string error, CapabilityStatementFetchFailureKind kind) =>
        new(false, null, error, kind);
}
