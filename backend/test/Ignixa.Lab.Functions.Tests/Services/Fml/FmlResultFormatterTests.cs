using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.Fml;

public sealed class FmlResultFormatterTests
{
    private static FmlRequest MakeRequest(string map = "map \"http://x\" = \"x\"") => new()
    {
        Map = map,
        Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient"}""")
    };

    [Fact]
    public void FormatResult_TopLevelFailure_ReturnsOperationOutcome()
    {
        var formatter = new FmlResultFormatter();
        var result = new FmlResult
        {
            Request = MakeRequest(),
            Error = "Failed to parse FML map: unexpected token",
            ErrorDiagnostics = "bad map text"
        };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
        json["issue"]![0]!["diagnostics"]!.GetValue<string>().Should().Be("Failed to parse FML map: unexpected token");
    }

    [Fact]
    public void FormatResult_Success_IncludesParametersTraceResultAndOutcomeParts()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person","gender":"male"}""");
        var result = new FmlResult
        {
            Request = MakeRequest("map \"http://x\" = \"x\""),
            Output = output,
            LogLines = ["copied gender"],
            Errors = []
        };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        json["resourceType"]!.GetValue<string>().Should().Be("Parameters");
        var parts = json["parameter"]!.AsArray();

        var configPart = parts.First(p => p!["name"]!.GetValue<string>() == "parameters")!;
        var nested = configPart["part"]!.AsArray();
        nested.Should().Contain(p => p!["name"]!.GetValue<string>() == "evaluator");
        nested.Should().Contain(p => p!["name"]!.GetValue<string>() == "map");

        var tracePart = parts.First(p => p!["name"]!.GetValue<string>() == "trace")!;
        tracePart["valueString"]!.GetValue<string>().Should().Be("copied gender");

        var resultPart = parts.First(p => p!["name"]!.GetValue<string>() == "result")!;
        resultPart["valueString"]!.GetValue<string>().Should().Contain("\"resourceType\": \"Person\"");

        var outcomePart = parts.First(p => p!["name"]!.GetValue<string>() == "outcome")!;
        outcomePart["resource"]!["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
        outcomePart["resource"]!["issue"]![0]!["severity"]!.GetValue<string>().Should().Be("information");
    }

    [Fact]
    public void FormatResult_WithRuleErrors_AddsErrorIssuesToOutcome()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person"}""");
        var errors = new List<ExecutionError> { new("Element not found", ruleName: "copy_gender", groupName: "PatientToPerson") };
        var result = new FmlResult
        {
            Request = MakeRequest(),
            Output = output,
            LogLines = [],
            Errors = errors
        };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        var outcomePart = json["parameter"]!.AsArray().First(p => p!["name"]!.GetValue<string>() == "outcome")!;
        var issue = outcomePart["resource"]!["issue"]![0]!;
        issue["severity"]!.GetValue<string>().Should().Be("error");
        issue["diagnostics"]!.GetValue<string>().Should().Contain("Element not found");
    }

    [Fact]
    public void FormatResult_DebugFalse_OmitsRuleErrorsFromTrace()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person"}""");
        var errors = new List<ExecutionError> { new("Element not found", ruleName: "copy_gender", groupName: "PatientToPerson") };
        var result = new FmlResult { Request = MakeRequest(), Output = output, LogLines = [], Errors = errors };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        var traceParts = json["parameter"]!.AsArray().Where(p => p!["name"]!.GetValue<string>() == "trace").ToList();
        traceParts.Should().BeEmpty();
    }

    [Fact]
    public void FormatResult_DebugTrue_AddsRuleErrorsAsTraceParts()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person"}""");
        var errors = new List<ExecutionError> { new("Element not found", ruleName: "copy_gender", groupName: "PatientToPerson") };
        var result = new FmlResult { Request = MakeRequest(), Output = output, LogLines = [], Errors = errors };

        var formatted = formatter.FormatResult(result, debug: true);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        var traceParts = json["parameter"]!.AsArray().Where(p => p!["name"]!.GetValue<string>() == "trace").ToList();
        traceParts.Should().ContainSingle(p => p!["valueString"]!.GetValue<string>().Contains("Element not found"));
    }
}
