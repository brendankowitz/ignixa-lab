using System.Net;
using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class CapabilityStatementFetcherTests
{
    private static readonly Uri Target = new("https://fhir.example.org/base");

    [Fact]
    public async Task GivenSuccessfulResponse_WhenFetching_ThenReturnsBody()
    {
        const string body = """{"resourceType":"CapabilityStatement","status":"active"}""";
        var fetcher = new CapabilityStatementFetcher(new FixedResponseHttpClientFactory(HttpStatusCode.OK, body), Options.Create(new IgnixaLabOptions()));

        var result = await fetcher.FetchAsync(Target, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Json.Should().Be(body);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task GivenNonSuccessStatusCode_WhenFetching_ThenReturnsFailureWithStatusCodeInMessage()
    {
        var fetcher = new CapabilityStatementFetcher(new FixedResponseHttpClientFactory(HttpStatusCode.InternalServerError, ""), Options.Create(new IgnixaLabOptions()));

        var result = await fetcher.FetchAsync(Target, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(CapabilityStatementFetchFailureKind.NonSuccessStatus);
        result.Error.Should().Contain("500");
    }

    [Fact]
    public async Task GivenUnreachableTarget_WhenFetching_ThenReturnsUnreachableFailure()
    {
        var fetcher = new CapabilityStatementFetcher(new ThrowingHttpClientFactory(), Options.Create(new IgnixaLabOptions()));

        var result = await fetcher.FetchAsync(Target, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(CapabilityStatementFetchFailureKind.Unreachable);
        result.Error.Should().Contain(Target.ToString());
    }

    [Fact]
    public async Task GivenClientSideTimeout_WhenFetching_ThenReturnsTimeoutFailure()
    {
        var fetcher = new CapabilityStatementFetcher(new TimingOutHttpClientFactory(), Options.Create(new IgnixaLabOptions()));

        var result = await fetcher.FetchAsync(Target, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(CapabilityStatementFetchFailureKind.Timeout);
    }

    private sealed class FixedResponseHttpClientFactory(HttpStatusCode statusCode, string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FixedResponseHandler(statusCode, body));

        private sealed class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("simulated connection failure");
        }
    }

    /// <summary>Simulates HttpClient.Timeout firing: a TaskCanceledException while the caller's token is still live.</summary>
    private sealed class TimingOutHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new TimingOutHandler());

        private sealed class TimingOutHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new TaskCanceledException("simulated client timeout");
        }
    }
}
