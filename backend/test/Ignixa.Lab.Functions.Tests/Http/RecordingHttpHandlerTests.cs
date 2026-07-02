using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Http;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Http;

public sealed class RecordingHttpHandlerTests
{
    [Fact]
    public async Task SendAsync_RecordsMethodUrlStatusAndBody_WhenScopeIsActive()
    {
        var scope = new HttpExchangeScope();
        using var stub = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"resourceType":"Patient"}""", Encoding.UTF8, "application/fhir+json"),
        });
        using var handler = CreateHandler(stub, scope);
        using var client = new HttpClient(handler);
        using var captureScope = scope.Begin(out var collector);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://fhir.example.org/Patient")
        {
            Content = new StringContent("""{"resourceType":"Patient","id":"1"}""", Encoding.UTF8, "application/fhir+json"),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exchange = collector.Exchanges.Should().ContainSingle().Subject;
        exchange.Method.Should().Be("POST");
        exchange.Url.Should().Be("https://fhir.example.org/Patient");
        exchange.StatusCode.Should().Be(200);
        exchange.RequestBody.Should().Be("""{"resourceType":"Patient","id":"1"}""");
        exchange.ResponseBody.Should().Be("""{"resourceType":"Patient"}""");
    }

    [Fact]
    public async Task SendAsync_RedactsAuthorizationHeaderValue()
    {
        var scope = new HttpExchangeScope();
        using var stub = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = CreateHandler(stub, scope);
        using var client = new HttpClient(handler);
        using var captureScope = scope.Begin(out var collector);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://fhir.example.org/metadata");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "super-secret-token");

        await client.SendAsync(request);

        var exchange = collector.Exchanges.Should().ContainSingle().Subject;
        exchange.RequestHeaders["Authorization"].Should().Be("***redacted***");
        exchange.RequestHeaders.Values.Should().NotContain(v => v.Contains("super-secret-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_TruncatesBodyOverConfiguredCap()
    {
        var scope = new HttpExchangeScope();
        var longBody = new string('a', 100);
        using var stub = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(longBody) });
        using var handler = CreateHandler(stub, scope, new IgnixaLabOptions { HttpCaptureMaxBodyBytes = 10 });
        using var client = new HttpClient(handler);
        using var captureScope = scope.Begin(out var collector);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://fhir.example.org/Patient/1"));

        var exchange = collector.Exchanges.Should().ContainSingle().Subject;
        exchange.ResponseBody.Should().StartWith(new string('a', 10));
        exchange.ResponseBody.Should().Contain("truncated 100 bytes");
        exchange.ResponseBody!.Length.Should().BeLessThan(longBody.Length);
    }

    [Fact]
    public async Task SendAsync_PreservesFullResponseBody_ForDownstreamReader_EvenWhenCaptureTruncates()
    {
        var scope = new HttpExchangeScope();
        var longBody = new string('b', 200);
        using var stub = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(longBody) });
        using var handler = CreateHandler(stub, scope, new IgnixaLabOptions { HttpCaptureMaxBodyBytes = 10 });
        using var client = new HttpClient(handler);
        using var captureScope = scope.Begin(out _);

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://fhir.example.org/Patient/1"));
        var downstreamBody = await response.Content.ReadAsStringAsync();

        downstreamBody.Should().Be(longBody);
    }

    [Fact]
    public async Task SendAsync_PassesThroughWithoutRecording_WhenNoScopeIsActive()
    {
        var scope = new HttpExchangeScope();
        using var stub = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var handler = CreateHandler(stub, scope);
        using var client = new HttpClient(handler);

        var act = () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://fhir.example.org/metadata"));

        (await act.Should().NotThrowAsync()).Which.StatusCode.Should().Be(HttpStatusCode.OK);
        scope.Current.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_DoesNotRecord_WhenCaptureIsDisabled()
    {
        var scope = new HttpExchangeScope();
        using var stub = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = CreateHandler(stub, scope, new IgnixaLabOptions { HttpCaptureEnabled = false });
        using var client = new HttpClient(handler);
        using var captureScope = scope.Begin(out var collector);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://fhir.example.org/metadata"));

        collector.Exchanges.Should().BeEmpty();
    }

    private static RecordingHttpHandler CreateHandler(HttpMessageHandler inner, HttpExchangeScope scope, IgnixaLabOptions? options = null) =>
        new(scope, Options.Create(options ?? new IgnixaLabOptions()))
        {
            InnerHandler = inner,
        };

    /// <summary>Canned-response inner handler standing in for the real target server.</summary>
    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
