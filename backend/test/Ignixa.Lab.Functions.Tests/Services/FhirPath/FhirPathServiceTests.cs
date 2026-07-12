using System.Net;
using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Services.FhirPath;

public sealed class FhirPathServiceTests
{
    [Fact]
    public void Evaluate_MalformedResourceType_ReturnsStructuredErrorInsteadOfThrowing()
    {
        var service = CreateService(new ThrowingHttpClientFactory(), allowPrivateTargets: true);

        // "resourceType" must be a JSON string per the FHIR spec. A resource
        // JSON payload with a non-string resourceType actually fails even
        // earlier (during Parameters body parsing), so to reach the
        // FhirPathService.Evaluate orchestration boundary specifically, mutate
        // an already-parsed resource's underlying node directly (via the
        // public MutableNode) to the same malformed shape. Accessing
        // ResourceType - used both for analysis and for the ToElement schema
        // conversion - then throws InvalidOperationException, which
        // previously propagated out of FhirPathService.Evaluate unhandled
        // instead of becoming a structured error result.
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"example"}""");
        ((IMutableJsonNode)resource).MutableNode["resourceType"] = System.Text.Json.Nodes.JsonValue.Create(123);

        var request = new FhirPathRequest
        {
            Resource = resource,
            Expression = "true",
            FhirVersion = "R4"
        };

        var act = () => service.Evaluate(request);

        act.Should().NotThrow();

        var result = service.Evaluate(request);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("http://127.0.0.1/fhir/Patient/1")]
    [InlineData("http://localhost:8080/fhir/Patient/1")]
    [InlineData("http://192.168.1.1/fhir/Patient/1")]
    public async Task LoadResourceFromUrl_RejectsPrivateTarget_WithoutMakingAnHttpCall(string url)
    {
        var service = CreateService(new ThrowingHttpClientFactory(), allowPrivateTargets: false);

        var (resource, error) = await service.LoadResourceFromUrl(url, CancellationToken.None);

        resource.Should().BeNull();
        error.Should().Be("The target URL resolves to a private, loopback, or link-local address, which is not permitted.");
    }

    [Fact]
    public async Task LoadResourceFromUrl_AllowedTarget_ReturnsParsedResource()
    {
        const string patientJson = """{"resourceType":"Patient","id":"example"}""";
        var service = CreateService(new StubHttpClientFactory(patientJson), allowPrivateTargets: true);

        var (resource, error) = await service.LoadResourceFromUrl("http://127.0.0.1/fhir/Patient/example", CancellationToken.None);

        error.Should().BeNull();
        resource.Should().NotBeNull();
        resource!.ResourceType.Should().Be("Patient");
    }

    [Fact]
    public async Task LoadResourceFromUrl_RedirectToPrivateTarget_IsRejectedWithoutFollowing()
    {
        // 8.8.8.8 is a public IP literal, so it passes initial validation even
        // with private targets disallowed; the redirect then points at a
        // loopback address, which must be rejected without a second request.
        var factory = new RedirectThenThrowHttpClientFactory("http://127.0.0.1/fhir/Patient/1");
        var service = CreateService(factory, allowPrivateTargets: false);

        var (resource, error) = await service.LoadResourceFromUrl("http://8.8.8.8/fhir/Patient/1", CancellationToken.None);

        resource.Should().BeNull();
        error.Should().Be("The target URL resolves to a private, loopback, or link-local address, which is not permitted.");
        factory.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadResourceFromUrl_RedirectToAllowedTarget_IsFollowedAndSucceeds()
    {
        const string patientJson = """{"resourceType":"Patient","id":"example"}""";

        // Both 8.8.8.8 and 8.8.4.4 are public IP literals, so the redirect
        // target passes re-validation and the second request is made.
        var factory = new RedirectThenSucceedHttpClientFactory("http://8.8.4.4/fhir/Patient/example", patientJson);
        var service = CreateService(factory, allowPrivateTargets: false);

        var (resource, error) = await service.LoadResourceFromUrl("http://8.8.8.8/fhir/Patient/example", CancellationToken.None);

        error.Should().BeNull();
        resource.Should().NotBeNull();
        resource!.ResourceType.Should().Be("Patient");
        factory.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task LoadResourceFromUrl_MalformedResponseBody_ReturnsErrorInsteadOfThrowing()
    {
        var service = CreateService(new StubHttpClientFactory("<html>not json</html>"), allowPrivateTargets: true);

        var act = async () => await service.LoadResourceFromUrl("http://127.0.0.1/fhir/Patient/1", CancellationToken.None);

        await act.Should().NotThrowAsync();

        var (resource, error) = await service.LoadResourceFromUrl("http://127.0.0.1/fhir/Patient/1", CancellationToken.None);
        resource.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadResourceFromUrl_SetsConfiguredHttpTimeout_OnCreatedClient()
    {
        const string patientJson = """{"resourceType":"Patient","id":"example"}""";
        var factory = new CapturingHttpClientFactory(patientJson);
        var service = CreateService(factory, allowPrivateTargets: true, httpTimeoutSeconds: 42);

        await service.LoadResourceFromUrl("http://127.0.0.1/fhir/Patient/example", CancellationToken.None);

        factory.CapturedClient.Should().NotBeNull();
        factory.CapturedClient!.Timeout.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task LoadResourceFromUrl_RequestTimesOut_ReturnsErrorInsteadOfThrowing()
    {
        // The handler delays well past the configured 1-second client timeout,
        // so HttpClient itself cancels the request (TaskCanceledException) even
        // though the caller's own CancellationToken.None was never triggered.
        var factory = new DelayingHttpClientFactory(TimeSpan.FromSeconds(5));
        var service = CreateService(factory, allowPrivateTargets: true, httpTimeoutSeconds: 1);

        var act = async () => await service.LoadResourceFromUrl("http://127.0.0.1/fhir/Patient/1", CancellationToken.None);

        await act.Should().NotThrowAsync();

        var (resource, error) = await service.LoadResourceFromUrl("http://127.0.0.1/fhir/Patient/1", CancellationToken.None);
        resource.Should().BeNull();
        error.Should().NotBeNull();
    }

    private static FhirPathService CreateService(IHttpClientFactory httpClientFactory, bool allowPrivateTargets, int httpTimeoutSeconds = 100)
    {
        var schemaFactory = new SchemaProviderFactory();
        var analyzer = new ExpressionAnalyzer(schemaFactory);
        var evaluator = new ExpressionEvaluator(schemaFactory);
        var formatter = new ResultFormatter();
        var options = Options.Create(new IgnixaLabOptions
        {
            AllowPrivateTargets = allowPrivateTargets,
            HttpTimeoutSeconds = httpTimeoutSeconds,
        });

        return new FhirPathService(analyzer, evaluator, formatter, httpClientFactory, options);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The HTTP client should not have been used for a rejected target.");
        }
    }

    private sealed class StubHttpClientFactory(string responseBody) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responseBody));

        private sealed class StubHandler(string responseBody) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody)
                });
        }
    }

    /// <summary>
    /// Returns a 302 redirect to <paramref name="redirectLocation"/> for the
    /// first request, then throws if a second request is ever dispatched -
    /// proving the redirect target was rejected rather than followed.
    /// </summary>
    private sealed class RedirectThenThrowHttpClientFactory(string redirectLocation) : IHttpClientFactory
    {
        public int RequestCount { get; private set; }

        public HttpClient CreateClient(string name) => new(new RedirectHandler(this, redirectLocation));

        private sealed class RedirectHandler(RedirectThenThrowHttpClientFactory owner, string redirectLocation) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.RequestCount++;
                if (owner.RequestCount > 1)
                {
                    throw new InvalidOperationException("The HTTP client should not have followed a redirect to a rejected target.");
                }

                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(redirectLocation);
                return Task.FromResult(response);
            }
        }
    }

    /// <summary>
    /// Returns a 302 redirect to <paramref name="redirectLocation"/> for the
    /// first request, then <paramref name="responseBody"/> with a 200 for the
    /// second - proving a safe redirect is followed to completion.
    /// </summary>
    private sealed class RedirectThenSucceedHttpClientFactory(string redirectLocation, string responseBody) : IHttpClientFactory
    {
        public int RequestCount { get; private set; }

        public HttpClient CreateClient(string name) => new(new RedirectHandler(this, redirectLocation, responseBody));

        private sealed class RedirectHandler(RedirectThenSucceedHttpClientFactory owner, string redirectLocation, string responseBody) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.RequestCount++;

                if (owner.RequestCount == 1)
                {
                    var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
                    redirectResponse.Headers.Location = new Uri(redirectLocation);
                    return Task.FromResult(redirectResponse);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody)
                });
            }
        }
    }

    /// <summary>
    /// Behaves like <see cref="StubHttpClientFactory"/>, but exposes the
    /// <see cref="HttpClient"/> it creates so tests can assert configuration
    /// (e.g. <see cref="HttpClient.Timeout"/>) applied by the service.
    /// </summary>
    private sealed class CapturingHttpClientFactory(string responseBody) : IHttpClientFactory
    {
        public HttpClient? CapturedClient { get; private set; }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new StubHandler(responseBody));
            CapturedClient = client;
            return client;
        }

        private sealed class StubHandler(string responseBody) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody)
                });
        }
    }

    /// <summary>
    /// Simulates an HTTP timeout: delays past the client's configured
    /// <see cref="HttpClient.Timeout"/> so <see cref="HttpClient.GetAsync"/>
    /// itself throws <see cref="TaskCanceledException"/>, proving
    /// <see cref="FhirPathService.LoadResourceFromUrl"/> handles that
    /// distinctly from the caller's own <see cref="CancellationToken"/> being
    /// cancelled.
    /// </summary>
    private sealed class DelayingHttpClientFactory(TimeSpan delay) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new DelayingHandler(delay));

        private sealed class DelayingHandler(TimeSpan delay) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(delay, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"resourceType":"Patient","id":"example"}""")
                };
            }
        }
    }
}
