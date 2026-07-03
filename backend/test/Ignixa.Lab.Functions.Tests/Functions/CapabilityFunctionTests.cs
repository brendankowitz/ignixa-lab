using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class CapabilityFunctionTests
{
    [Theory]
    [InlineData("http://127.0.0.1/fhir")]
    [InlineData("http://localhost:8080/fhir")]
    [InlineData("http://192.168.1.1/fhir")]
    public async Task Run_RejectsPrivateTarget_WithoutMakingAnHttpCall(string target)
    {
        var function = new CapabilityFunction(
            CreateFetcher(),
            Options.Create(new IgnixaLabOptions()),
            NullLogger<CapabilityFunction>.Instance);

        var result = await function.Run(BuildRequest(target), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "The target URL resolves to a private, loopback, or link-local address, which is not permitted." });
    }

    [Fact]
    public async Task Run_RejectsMissingTarget_WithoutMakingAnHttpCall()
    {
        var function = new CapabilityFunction(
            CreateFetcher(),
            Options.Create(new IgnixaLabOptions()),
            NullLogger<CapabilityFunction>.Instance);

        var result = await function.Run(BuildRequest(targetUrl: null), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static CapabilityStatementFetcher CreateFetcher() =>
        new(new ThrowingHttpClientFactory(), Options.Create(new IgnixaLabOptions()));

    private static HttpRequest BuildRequest(string? targetUrl)
    {
        var context = new DefaultHttpContext();
        if (targetUrl is not null)
        {
            context.Request.QueryString = new QueryString($"?target={Uri.EscapeDataString(targetUrl)}");
        }

        return context.Request;
    }

    /// <summary>
    /// An <see cref="IHttpClientFactory"/> whose clients throw if actually
    /// invoked, so tests can assert the SSRF guard short-circuits before any
    /// outbound call is attempted.
    /// </summary>
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The HTTP client should not have been used for a rejected target.");
        }
    }
}
