using System.Net;
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

    /// <summary>Total attempts (including the first) for a transient failure before giving up.</summary>
    private const int MaxAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IgnixaLabOptions _options = options.Value;

    /// <summary>
    /// Fetches the target's CapabilityStatement, retrying transient failures
    /// (connection errors, timeouts, HTTP 429/408, and 5xx responses) up to
    /// <see cref="MaxAttempts"/> times before returning the last failure. A
    /// single flaky <c>/metadata</c> call used to fail open for the entire run
    /// (every <c>requiresCapability</c>-gated test running ungated); retrying
    /// here means that only happens when the target is genuinely down or
    /// misconfigured, not on a one-off network blip. Non-transient failures
    /// (e.g. 404, 401) return immediately — retrying them cannot help.
    /// </summary>
    public async Task<CapabilityStatementFetchResult> FetchAsync(Uri target, CancellationToken cancellationToken)
    {
        var metadataUri = new Uri(target.ToString().TrimEnd('/') + "/metadata");

        using var client = httpClientFactory.CreateClient(HttpEvaluatorFactory.HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        (CapabilityStatementFetchResult Result, bool IsTransient) attempt;
        var attemptNumber = 0;
        do
        {
            attemptNumber++;
            attempt = await TryFetchOnceAsync(client, target, metadataUri, cancellationToken);
            if (attempt.Result.Success || !attempt.IsTransient || attemptNumber >= MaxAttempts)
            {
                break;
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }
        while (true);

        return attempt.Result;
    }

    private static async Task<(CapabilityStatementFetchResult Result, bool IsTransient)> TryFetchOnceAsync(
        HttpClient client, Uri target, Uri metadataUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, metadataUri);
        request.Headers.Accept.Add(FhirJsonAccept);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return (CapabilityStatementFetchResult.Failed(
                $"Could not reach {target} for capability metadata: {ex.Message}",
                CapabilityStatementFetchFailureKind.Unreachable), IsTransient: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (CapabilityStatementFetchResult.Failed(
                $"Request to {target} timed out while fetching capability metadata.",
                CapabilityStatementFetchFailureKind.Timeout), IsTransient: true);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return (CapabilityStatementFetchResult.Failed(
                    $"{target} returned HTTP {(int)response.StatusCode} for /metadata.",
                    CapabilityStatementFetchFailureKind.NonSuccessStatus), IsTransient: IsTransientStatus(response.StatusCode));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (CapabilityStatementFetchResult.Succeeded(body), IsTransient: false);
        }
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
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
