using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Fml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class FmlFunctionsTests
{
    private const string ValidMap = """
        map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'

        uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
        uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target

        group PatientToPerson(source src : Patient, target tgt : Person) {
          src.gender as vG -> tgt.gender = vG 'copy_gender';
        }
        """;

    [Fact]
    public async Task RunTransform_PostWithValidMapAndEmbeddedResource_ReturnsSuccessParameters()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""map"",""valueString"":" + JsonValue.Create(ValidMap).ToJsonString() + @"},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""gender"":""male""}}
            ]}";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("Parameters");
    }

    [Fact]
    public async Task RunTransform_MissingMapParameter_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        const string body = """{"resourceType":"Parameters","parameter":[{"name":"resource","resource":{"resourceType":"Patient"}}]}""";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunTransform_MalformedMap_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        const string body = """
            {"resourceType":"Parameters","parameter":[
                {"name":"map","valueString":"this is not valid FML"},
                {"name":"resource","resource":{"resourceType":"Patient"}}
            ]}
            """;

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunTransform_RejectsPrivateResourceUrlTarget_WithoutMakingAnHttpCall()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""map"",""valueString"":" + JsonValue.Create(ValidMap).ToJsonString() + @"},
                {""name"":""resource"",""valueString"":""http://127.0.0.1/fhir/Patient/1""}
            ]}";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunTransform_ResourceAsRawJsonString_IsParsedAndTransformed()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""map"",""valueString"":" + JsonValue.Create(ValidMap).ToJsonString() + @"},
                {""name"":""resource"",""valueString"":""{\""resourceType\"":\""Patient\"",\""gender\"":\""female\""}""}
            ]}";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!;
        var resultPart = json["parameter"]!.AsArray().First(p => p!["name"]!.GetValue<string>() == "result")!;
        resultPart["valueString"]!.GetValue<string>().Should().Contain("\"gender\": \"female\"");
    }

    private static FmlFunctions CreateFunction()
    {
        var schemaFactory = new SchemaProviderFactory();
        var fmlService = new FmlService(schemaFactory, NullLogger<FmlService>.Instance);
        var resultFormatter = new FmlResultFormatter();

        var analyzer = new ExpressionAnalyzer(schemaFactory);
        var evaluator = new ExpressionEvaluator(schemaFactory);
        var fhirPathFormatter = new ResultFormatter();
        var options = Options.Create(new IgnixaLabOptions());
        var fhirPathService = new FhirPathService(analyzer, evaluator, fhirPathFormatter, new ThrowingHttpClientFactory(), options);

        return new FmlFunctions(NullLogger<FmlFunctions>.Instance, fmlService, resultFormatter, fhirPathService);
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
