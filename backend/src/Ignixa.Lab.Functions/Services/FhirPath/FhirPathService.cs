using System.Net;
using System.Text.Json;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Services.FhirPath;

/// <summary>
/// Main orchestrator service for FHIRPath evaluation.
/// Coordinates parsing, analysis, evaluation, and result formatting.
/// </summary>
public sealed class FhirPathService
{
    private readonly ExpressionAnalyzer _analyzer;
    private readonly ExpressionEvaluator _evaluator;
    private readonly ResultFormatter _formatter;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IgnixaLabOptions _options;

    /// <summary>
    /// Name of the dedicated <see cref="IHttpClientFactory"/> client used for
    /// <see cref="LoadResourceFromUrl"/>. Kept separate from
    /// <see cref="HttpEvaluatorFactory.HttpClientName"/> so this client's
    /// handler can have automatic redirect-following disabled without
    /// affecting the shared "fhir-target" client used elsewhere (e.g.
    /// <see cref="HttpEvaluatorFactory"/>, <c>CapabilityFunction</c>,
    /// <c>TestScriptRunner</c>). Redirects are instead followed manually in
    /// <see cref="LoadResourceFromUrl"/>, re-validating each hop through
    /// <see cref="TargetUrlValidator"/> so a permitted target cannot redirect
    /// the fetch to a private/loopback/link-local address (SSRF via redirect).
    /// </summary>
    public const string HttpClientName = "fhirpath-resource-fetch";

    /// <summary>Maximum number of redirects <see cref="LoadResourceFromUrl"/> will follow before giving up.</summary>
    private const int MaxRedirects = 5;

    public FhirPathService(
        ExpressionAnalyzer analyzer,
        ExpressionEvaluator evaluator,
        ResultFormatter formatter,
        IHttpClientFactory httpClientFactory,
        IOptions<IgnixaLabOptions> options)
    {
        _analyzer = analyzer;
        _evaluator = evaluator;
        _formatter = formatter;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <summary>
    /// Processes a FHIRPath evaluation request and returns the formatted result.
    /// </summary>
    /// <param name="request">The FHIRPath request containing expression and resource.</param>
    /// <returns>A FHIR Parameters resource containing the evaluation results.</returns>
    public ResourceJsonNode ProcessRequest(FhirPathRequest request)
    {
        var result = Evaluate(request);
        return _formatter.FormatResult(result);
    }

    /// <summary>
    /// Evaluates a FHIRPath request and returns the structured result.
    /// </summary>
    /// <param name="request">The FHIRPath request.</param>
    /// <returns>The evaluation result.</returns>
    public FhirPathResult Evaluate(FhirPathRequest request)
    {
        // Validate required input
        if (string.IsNullOrEmpty(request.Expression))
        {
            return new FhirPathResult
            {
                Request = request,
                Error = "Expression parameter is required"
            };
        }

        // Parse and analyze expressions
        var rootTypeName = request.Resource?.ResourceType;
        var (parsed, contextExpr, parseError) = _analyzer.ParseAndAnalyze(
            request.Expression,
            request.Context,
            rootTypeName,
            request.FhirVersion);

        if (parseError != null)
        {
            return new FhirPathResult
            {
                Request = request,
                Error = parseError,
                ErrorDiagnostics = request.Expression
            };
        }

        // Evaluate the expression
        var evaluationResults = _evaluator.Evaluate(
            parsed!,
            contextExpr,
            request.Resource,
            request.Variables,
            request.FhirVersion,
            request.DebugTrace);

        return new FhirPathResult
        {
            Request = request,
            ParsedExpression = parsed,
            ContextExpression = contextExpr,
            Results = evaluationResults
        };
    }

    /// <summary>
    /// Loads a resource from a remote URL, subject to the same SSRF guard
    /// (<see cref="TargetUrlValidator"/>) the TestScript run/capability
    /// endpoints already use. Redirect responses are followed manually (up to
    /// <see cref="MaxRedirects"/> hops), re-validating each redirect target so
    /// a permitted target cannot bounce the request to a private, loopback,
    /// or link-local address.
    /// </summary>
    /// <param name="url">The URL to fetch the resource from.</param>
    /// <param name="cancellationToken">Cancellation token for the outbound request.</param>
    /// <returns>The parsed resource, or an error if validation or the fetch failed.</returns>
    public async Task<(ResourceJsonNode? Resource, string? Error)> LoadResourceFromUrl(
        string url,
        CancellationToken cancellationToken)
    {
        if (!TargetUrlValidator.TryValidate(url, _options.AllowPrivateTargets, out var target, out var urlError))
        {
            return (null, urlError);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        var currentUri = target;
        var redirectsFollowed = 0;

        try
        {
            while (true)
            {
                using var response = await client.GetAsync(currentUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!IsRedirectStatusCode(response.StatusCode))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return (null, $"Unable to retrieve resource {url}: the server returned HTTP {(int)response.StatusCode}.");
                    }

                    var body = await response.Content.ReadAsStringAsync(cancellationToken);

                    try
                    {
                        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(body);
                        return (resource, null);
                    }
                    catch (JsonException ex)
                    {
                        return (null, $"Unable to retrieve resource {url}: the response was not valid FHIR JSON ({ex.Message}).");
                    }
                }

                redirectsFollowed++;
                if (redirectsFollowed > MaxRedirects)
                {
                    return (null, $"Unable to retrieve resource {url}: too many redirects.");
                }

                var location = response.Headers.Location;
                if (location is null)
                {
                    return (null, $"Unable to retrieve resource {url}: redirect response did not include a Location header.");
                }

                var redirectUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);

                if (!TargetUrlValidator.TryValidate(redirectUri.ToString(), _options.AllowPrivateTargets, out var validatedRedirectUri, out var redirectError))
                {
                    return (null, redirectError);
                }

                currentUri = validatedRedirectUri;
            }
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Unable to retrieve resource {url}: {ex.Message}");
        }
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MultipleChoices
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
