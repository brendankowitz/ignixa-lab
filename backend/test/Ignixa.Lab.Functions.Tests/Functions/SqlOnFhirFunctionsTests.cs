using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class SqlOnFhirFunctionsTests
{
    private const string ValidViewDefinitionJson = """
        {
          "resourceType": "ViewDefinition",
          "status": "active",
          "resource": "Patient",
          "select": [ { "column": [ { "name": "id", "path": "id" }, { "name": "gender", "path": "gender" } ] } ]
        }
        """;

    [Fact]
    public async Task RunViewDefinition_ValidRequest_ReturnsJsonArrayOfRows()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""viewResource"",""resource"":" + ValidViewDefinitionJson + @"},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""id"":""p1"",""gender"":""male""}}
            ]}";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        content.ContentType.Should().Be("application/json");
        var json = JsonNode.Parse(content.Content!)!.AsArray();
        json.Should().ContainSingle();
        json[0]!["id"]!.GetValue<string>().Should().Be("p1");
    }

    [Fact]
    public async Task RunViewDefinition_MissingViewResource_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        const string body = """{"resourceType":"Parameters","parameter":[{"name":"resource","resource":{"resourceType":"Patient","id":"p1"}}]}""";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_NoResourceParameters_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[{""name"":""viewResource"",""resource"":" + ValidViewDefinitionJson + @"}]}";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_UnsupportedFormat_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""viewResource"",""resource"":" + ValidViewDefinitionJson + @"},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""id"":""p1""}},
                {""name"":""_format"",""valueCode"":""csv""}
            ]}";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_UnsupportedParameter_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""viewResource"",""resource"":" + ValidViewDefinitionJson + @"},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""id"":""p1""}},
                {""name"":""patient"",""valueString"":""Patient/1""}
            ]}";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_LimitParameter_TruncatesRows()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""viewResource"",""resource"":" + ValidViewDefinitionJson + @"},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""id"":""p1"",""gender"":""male""}},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""id"":""p2"",""gender"":""female""}},
                {""name"":""_limit"",""valueInteger"":1}
            ]}";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!.AsArray();
        json.Should().ContainSingle();
    }

    [Fact]
    public async Task RunViewDefinition_MalformedPostBody_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();

        var result = await function.RunViewDefinition(BuildPostRequest("{ this is not valid json"), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    private static SqlOnFhirFunctions CreateFunction()
    {
        var schemaFactory = new SchemaProviderFactory();
        var service = new SqlOnFhirService(schemaFactory);
        return new SqlOnFhirFunctions(NullLogger<SqlOnFhirFunctions>.Instance, service);
    }

    private static HttpRequest BuildPostRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context.Request;
    }
}
