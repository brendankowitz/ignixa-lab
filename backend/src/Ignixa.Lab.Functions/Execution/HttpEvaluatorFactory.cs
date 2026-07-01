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
        client.BaseAddress = target;
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        var provider = new HttpTestRequestProvider(client);
        return new RequestProviderScope(provider, client);
    }
}
