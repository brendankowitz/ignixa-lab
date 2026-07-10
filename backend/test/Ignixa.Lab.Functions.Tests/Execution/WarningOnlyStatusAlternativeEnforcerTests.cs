using FluentAssertions;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Execution;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class WarningOnlyStatusAlternativeEnforcerTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    public void Apply_WhenDeleteReturnsAllowedStatus_Passes(int deleteStatus)
    {
        var result = PassingResult("allowed delete", DeletedResourceLifecycleSteps(deleteStatus, 404));

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Fact]
    public void Apply_WhenDeleteReturns201_Fails()
    {
        var result = PassingResult("invalid delete", DeletedResourceLifecycleSteps(201, 404));

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
        updated.Single().Error!.Received.Should().Contain("200").And.Contain("202").And.Contain("204").And.Contain("201");
    }

    [Theory]
    [InlineData(202, 200, "pass")]
    [InlineData(200, 200, "fail")]
    [InlineData(204, 200, "fail")]
    [InlineData(200, 404, "pass")]
    [InlineData(204, 410, "pass")]
    [InlineData(202, 404, "pass")]
    [InlineData(202, 410, "pass")]
    [InlineData(202, 500, "fail")]
    public void Apply_CorrelatesReadbackWithPrecedingDelete(int deleteStatus, int readStatus, string expectedStatus)
    {
        var result = PassingResult("correlated lifecycle", DeletedResourceLifecycleSteps(deleteStatus, readStatus));

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void Apply_WhenOneDefinitionTestProducesMultipleMappedResults_UsesEachResultsOwnDeleteStatus()
    {
        var results = new[]
        {
            PassingResult("parameterized async", DeletedResourceLifecycleSteps(202, 200)),
            PassingResult("parameterized complete", DeletedResourceLifecycleSteps(204, 200)),
        };

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(results);

        updated[0].Status.Should().Be(ConformanceStatus.Pass);
        updated[1].Status.Should().Be(ConformanceStatus.Fail);
    }

    [Fact]
    public void Apply_WhenMappedStepsContainSynthesizedActions_FindsCorrelatedOperations()
    {
        var result = PassingResult(
            "synthesized actions",
            [
                AssertionStep("Engine-synthesized pre-delete warning", "The engine inserted this step."),
                .. DeleteSteps(202),
                AssertionStep("Engine-synthesized inter-operation warning", "The engine inserted this step."),
                .. ReadbackSteps(200),
            ]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Fact]
    public void Apply_WhenOnlyPriorDeleteTargetsDifferentResource_DoesNotCorrelateReadback200()
    {
        var result = PassingResult(
            "different delete target",
            [
                .. DeleteSteps(202, "https://example.test/Patient/other"),
                .. ReadbackSteps(200),
            ]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
    }

    private static ConformanceResult PassingResult(string id, IReadOnlyList<ConformanceStep> steps) =>
        new(
            Id: id,
            File: "deleted-resource-status.json",
            Suite: "deleted-resource-status.json",
            Category: "test",
            Status: ConformanceStatus.Pass,
            DurationMs: 0,
            Error: null,
            Steps: steps);

    private static ConformanceStep[] DeletedResourceLifecycleSteps(int deleteStatus, int readStatus) =>
    [
        .. DeleteSteps(deleteStatus),
        .. ReadbackSteps(readStatus),
    ];

    private static ConformanceStep[] DeleteSteps(
        int statusCode,
        string url = "https://example.test/Patient/deleted") =>
    [
        OperationStep("DELETE", statusCode, url),
        AssertionStep("Accepted DELETE response: 200 OK for completed deletion"),
        AssertionStep("Accepted DELETE response: 202 Accepted for asynchronous deletion"),
        AssertionStep("Accepted DELETE response: 204 No Content for completed deletion"),
    ];

    private static ConformanceStep[] ReadbackSteps(int statusCode) =>
    [
        OperationStep("GET", statusCode),
        AssertionStep("Accepted alternative: 200 OK while an asynchronous delete is still pending"),
        AssertionStep("Accepted alternative: 410 Gone when the server tracks the deleted resource"),
        AssertionStep("Accepted alternative: 404 Not Found when deleted resources are not tracked"),
    ];

    private static ConformanceStep OperationStep(
        string method,
        int statusCode,
        string url = "https://example.test/Patient/deleted") =>
        new(
            Phase: "test",
            Kind: "operation",
            Status: ConformanceStatus.Pass,
            DurationMs: 0,
            Label: $"{method} Patient/deleted",
            Description: null,
            Message: null,
            Request: new ConformanceHttpRequest(method, url, new Dictionary<string, string>(), null),
            Response: new ConformanceHttpResponse(statusCode, new Dictionary<string, string>(), null, null));

    private static ConformanceStep AssertionStep(string description, string? message = null) =>
        new(
            Phase: "test",
            Kind: "assertion",
            Status: ConformanceStatus.Pass,
            DurationMs: 0,
            Label: null,
            Description: description,
            Message: message,
            Request: null,
            Response: null);
}
