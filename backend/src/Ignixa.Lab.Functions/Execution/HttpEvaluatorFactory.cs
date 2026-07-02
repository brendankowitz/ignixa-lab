using Ignixa.Lab.Functions.Configuration;
using Ignixa.TestScript.Client;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Default <see cref="IEvaluatorFactory"/> that builds a real HTTP-backed
/// request provider pointed at the target FHIR server. Uses
/// <see cref="IHttpClientFactory"/> so connection pooling and DNS refresh are
/// handled correctly.
/// </summary>
public sealed class HttpEvaluatorFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<IgnixaLabOptions> options) : IEvaluatorFactory
{
    public const string HttpClientName = "fhir-target";

    private readonly IgnixaLabOptions _options = options.Value;

    public RequestProviderScope CreateRequestProvider(Uri target)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = NormalizeBaseAddress(target);
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        var provider = new HttpTestRequestProvider(client);
        return new RequestProviderScope(provider, client);
    }

    /// <summary>
    /// Ensures a FHIR base URL ends with a trailing slash. Without it, resolving
    /// a relative request path such as "Patient" against a base like
    /// "https://host/baseR4" drops the last segment ("baseR4") per RFC 3986,
    /// sending every request to the wrong URL.
    /// </summary>
    public static Uri NormalizeBaseAddress(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.AbsolutePath.EndsWith('/'))
        {
            return target;
        }

        return new UriBuilder(target) { Path = target.AbsolutePath + "/" }.Uri;
    }
}
