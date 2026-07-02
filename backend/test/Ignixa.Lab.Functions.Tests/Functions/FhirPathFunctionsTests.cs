using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class FhirPathFunctionsTests
{
    [Fact]
    public void RunCapabilityStatement_ReturnsIgnixaCapabilityStatement()
    {
        var function = CreateFunction();

        var result = function.RunCapabilityStatement(BuildRequest());

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/fhir+json");
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("CapabilityStatement");
        json["title"]!.GetValue<string>().Should().Contain("Ignixa");
    }

    [Fact]
    public async Task RunFhirPathTestR4_GetWithResourceFreeExpression_ReturnsEvaluatedResult()
    {
        var function = CreateFunction();

        var result = await function.RunFhirPathTestR4(BuildRequest("expression=true"), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("Parameters");
        var resultParam = json["parameter"]!.AsArray()
            .FirstOrDefault(p => p?["name"]?.GetValue<string>() == "result");
        resultParam.Should().NotBeNull("evaluating a resource-free expression should still produce a result parameter");
    }

    [Fact]
    public async Task RunFhirPathTestR4_MissingExpression_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();

        var result = await function.RunFhirPathTestR4(BuildRequest(), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunFhirPathTestR4_MalformedPostBody_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();

        var result = await function.RunFhirPathTestR4(BuildPostRequest("{ this is not valid json"), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunFhirPathTestR4_PostWithValidExpression_ReturnsEvaluatedResult()
    {
        var function = CreateFunction();
        const string body = """{"resourceType":"Parameters","parameter":[{"name":"expression","valueString":"true"}]}""";

        var result = await function.RunFhirPathTestR4(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("Parameters");
        var resultParam = json["parameter"]!.AsArray()
            .FirstOrDefault(p => p?["name"]?.GetValue<string>() == "result");
        resultParam.Should().NotBeNull("evaluating a valid POST expression should produce a result parameter");
    }

    [Fact]
    public async Task RunFhirPathTestR4_RejectsPrivateResourceTarget_WithoutMakingAnHttpCall()
    {
        var function = CreateFunction();

        var result = await function.RunFhirPathTestR4(
            BuildRequest("resource=http://127.0.0.1/fhir/Patient/1&expression=true"),
            CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunFhirPathTestR4_MalformedEmbeddedResourceType_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();

        // "resourceType" must be a JSON string per the FHIR spec. A number
        // here throws deep inside the embedded resource's schema conversion
        // (ResourceJsonNode.ResourceType); this must be surfaced as a
        // structured 400 OperationOutcome instead of an unhandled exception.
        const string body = """
            {"resourceType":"Parameters","parameter":[
                {"name":"expression","valueString":"true"},
                {"name":"resource","resource":{"resourceType":123,"id":"example"}}
            ]}
            """;

        var result = await function.RunFhirPathTestR4(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    private static FhirPathFunctions CreateFunction()
    {
        var schemaFactory = new SchemaProviderFactory();
        var analyzer = new ExpressionAnalyzer(schemaFactory);
        var evaluator = new ExpressionEvaluator(schemaFactory);
        var formatter = new ResultFormatter();
        var options = Options.Create(new IgnixaLabOptions());
        var fhirPathService = new FhirPathService(analyzer, evaluator, formatter, new ThrowingHttpClientFactory(), options);

        return new FhirPathFunctions(NullLogger<FhirPathFunctions>.Instance, fhirPathService);
    }

    private static HttpRequest BuildRequest(string? queryString = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        if (queryString is not null)
        {
            context.Request.QueryString = new QueryString($"?{queryString}");
        }

        return context.Request;
    }

    private static HttpRequest BuildPostRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context.Request;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The HTTP client should not have been used for this test.");
        }
    }
}
