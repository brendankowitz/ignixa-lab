using FluentAssertions;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Execution;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class WarningOnlyStatusAlternativeEnforcerTests
{
    [Fact]
    public void Apply_WhenOneDefinitionTestProducesMultipleMappedResults_EnforcesEveryResult()
    {
        var results = new[]
        {
            PassingResult("parameterized case 1", DeletedResourceStepsWithStatus(200)),
            PassingResult("parameterized case 2", DeletedResourceStepsWithStatus(200)),
        };

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(results);

        updated.Should().OnlyContain(result => result.Status == ConformanceStatus.Fail);
        updated.Select(result => result.Error!.Received).Should().OnlyContain(message =>
            message!.Contains("410", StringComparison.Ordinal)
            && message.Contains("404", StringComparison.Ordinal)
            && message.Contains("200", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_WhenMappedStepsContainSynthesizedActionBeforeOperation_FindsActualPrecedingOperation()
    {
        var result = PassingResult(
            "synthesized action",
            [
                AssertionStep("Engine-synthesized URL encoding warning", "The engine inserted this step before the operation."),
                .. DeletedResourceStepsWithStatus(200),
            ]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
        updated.Single().Error!.Received.Should().Contain("200");
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

    private static ConformanceStep[] DeletedResourceStepsWithStatus(int statusCode) =>
    [
        OperationStep(statusCode),
        AssertionStep("Preferred: 410 Gone for a deleted resource"),
        AssertionStep("Alternative: 404 Not Found is accepted when deleted resources are not tracked"),
    ];

    private static ConformanceStep OperationStep(int statusCode) =>
        new(
            Phase: "test",
            Kind: "operation",
            Status: ConformanceStatus.Pass,
            DurationMs: 0,
            Label: "GET Patient/deleted",
            Description: null,
            Message: null,
            Request: null,
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
