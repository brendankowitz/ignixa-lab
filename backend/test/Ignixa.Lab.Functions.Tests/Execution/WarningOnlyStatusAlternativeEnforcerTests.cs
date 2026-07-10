using FluentAssertions;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Execution;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class WarningOnlyStatusAlternativeEnforcerTests
{
    [Fact]
    public void Parse_UsesTestLevelStructuredPolicyMarker()
    {
        var plan = StatusAlternativeEnforcementPlan.Parse("""
            {
              "resourceType": "TestScript",
              "name": "Uploaded",
              "test": [{
                "name": "marked lifecycle",
                "extension": [{
                  "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                  "valueCode": "subscription-delete-readback-v1"
                }],
                "action": []
              }]
            }
            """);

        plan.TryGetPolicy("Uploaded > marked lifecycle", out var policy).Should().BeTrue();
        policy.Should().Be(StatusAlternativePolicy.SubscriptionDeleteReadback);
        plan.TryGetPolicy("Uploaded > other test", out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_MapsConditionalDeleteMethodCorrelatedStatusSets()
    {
        var plan = StatusAlternativeEnforcementPlan.Parse("""
            {
              "resourceType": "TestScript",
              "name": "CRUD/conditional-delete",
              "test": [
                {
                  "name": "Conditional delete with no search criteria is rejected",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "extension": [
                      { "url": "policy", "valueCode": "response-status-set-v1" },
                      { "url": "method", "valueCode": "DELETE" },
                      { "url": "status", "valueInteger": 400 },
                      { "url": "status", "valueInteger": 412 }
                    ]
                  }],
                  "action": []
                },
                {
                  "name": "Conditional delete with exactly one matching resource removes it",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "extension": [
                      { "url": "policy", "valueCode": "response-status-set-v1" },
                      { "url": "method", "valueCode": "GET" },
                      { "url": "status", "valueInteger": 404 },
                      { "url": "status", "valueInteger": 410 }
                    ]
                  }],
                  "action": []
                }
              ]
            }
            """);

        plan.TryGetRule(
            "CRUD/conditional-delete > Conditional delete with no search criteria is rejected",
            out var rejection).Should().BeTrue();
        rejection.Policy.Should().Be(StatusAlternativePolicy.ResponseStatusSet);
        rejection.Method.Should().Be("DELETE");
        rejection.AllowedStatusCodes.Should().Equal(400, 412);
        plan.TryGetRule(
            "CRUD/conditional-delete > Conditional delete with exactly one matching resource removes it",
            out var readback).Should().BeTrue();
        readback.Policy.Should().Be(StatusAlternativePolicy.ResponseStatusSet);
        readback.Method.Should().Be("GET");
        readback.AllowedStatusCodes.Should().Equal(404, 410);
    }

    [Fact]
    public void Parse_MapsOnlyExactResultIdentifiersWithoutSuffixCollisions()
    {
        var plan = StatusAlternativeEnforcementPlan.Parse("""
            {
              "resourceType": "TestScript",
              "name": "Collision suite",
              "test": [
                {
                  "name": "x",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "valueCode": "subscription-delete-readback-v1"
                  }],
                  "action": []
                },
                {
                  "name": "y > x",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "valueCode": "deleted-resource-readback-v1"
                  }],
                  "action": []
                }
              ]
            }
            """);

        plan.TryGetPolicy("Collision suite > x", out var xPolicy).Should().BeTrue();
        xPolicy.Should().Be(StatusAlternativePolicy.SubscriptionDeleteReadback);
        plan.TryGetPolicy("Collision suite > y > x", out var nestedPolicy).Should().BeTrue();
        nestedPolicy.Should().Be(StatusAlternativePolicy.DeletedResourceReadback);
        plan.TryGetPolicy("Other suite > x", out _).Should().BeFalse();
        plan.TryGetPolicy("x", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"url":"http://ignixa.io/testscript/statusAlternativePolicy"}""")]
    [InlineData("""{"url":"http://ignixa.io/testscript/statusAlternativePolicy","valueCode":"unknown-policy"}""")]
    public void Parse_RejectsMalformedOrUnknownPolicyMarker(string extension)
    {
        var content = $$"""
            {
              "resourceType": "TestScript",
              "test": [{
                "name": "marked lifecycle",
                "extension": [{{extension}}],
                "action": []
              }]
            }
            """;

        var act = () => StatusAlternativeEnforcementPlan.Parse(content);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_RejectsDuplicateMarkedTestNames()
    {
        var content = """
            {
              "resourceType": "TestScript",
              "name": "Duplicate suite",
              "test": [
                {
                  "name": "duplicate",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "valueCode": "subscription-delete-readback-v1"
                  }],
                  "action": []
                },
                {
                  "name": "duplicate",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "valueCode": "deleted-resource-readback-v1"
                  }],
                  "action": []
                }
              ]
            }
            """;

        var act = () => StatusAlternativeEnforcementPlan.Parse(content);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("Marked test name 'duplicate' is duplicated in TestScript 'Duplicate suite'.");
    }

    [Fact]
    public void Parse_RejectsMarkedNameDuplicatedByUnmarkedTest()
    {
        var content = """
            {
              "resourceType": "TestScript",
              "name": "Duplicate suite",
              "test": [
                {
                  "name": "duplicate",
                  "extension": [{
                    "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                    "valueCode": "subscription-delete-readback-v1"
                  }],
                  "action": []
                },
                {
                  "name": "duplicate",
                  "action": []
                }
              ]
            }
            """;

        var act = () => StatusAlternativeEnforcementPlan.Parse(content);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("Marked test name 'duplicate' is duplicated in TestScript 'Duplicate suite'.");
    }

    [Fact]
    public void Apply_WhenUnmarkedResultUsesSameThreeStatusProse_DoesNotEnforce()
    {
        var result = PassingResult("unmarked near collision", DeletedResourceLifecycleSteps(201, 500));

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result]);

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Fact]
    public void Apply_ResponseStatusSetWithNoTestSteps_FailsClosed()
    {
        var result = PassingResult("Suite > marked create", []);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(
            [result],
            ResponseStatusPlan(result.Id));

        var failed = updated.Should().ContainSingle().Which;
        failed.Status.Should().Be(ConformanceStatus.Fail);
        failed.Error!.Received.Should().Contain("exactly one executed POST operation").And.Contain("found 0");
        failed.Steps.Should().BeEmpty();
    }

    [Theory]
    [InlineData("DELETE", 400, 400, 412)]
    [InlineData("DELETE", 412, 400, 412)]
    [InlineData("GET", 404, 404, 410)]
    [InlineData("GET", 410, 404, 410)]
    public void Apply_ConditionalDeleteStatusSets_AcceptOnlyDeclaredAlternatives(
        string method,
        int actualStatus,
        int firstAllowed,
        int secondAllowed)
    {
        var result = PassingResult(
            $"Conditional delete > {method}",
            [OperationStep(method, actualStatus)]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(
            [result],
            ResponseStatusPlan(result.Id, method, firstAllowed, secondAllowed));

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Theory]
    [InlineData("DELETE", 204, 400, 412)]
    [InlineData("GET", 200, 404, 410)]
    [InlineData("GET", 500, 404, 410)]
    public void Apply_ConditionalDeleteStatusSets_RejectStatusesOutsideDeclaredAlternatives(
        string method,
        int actualStatus,
        int firstAllowed,
        int secondAllowed)
    {
        var result = PassingResult(
            $"Conditional delete > {method}",
            [OperationStep(method, actualStatus)]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(
            [result],
            ResponseStatusPlan(result.Id, method, firstAllowed, secondAllowed));

        var failed = updated.Should().ContainSingle().Which;
        failed.Status.Should().Be(ConformanceStatus.Fail);
        failed.Error!.Received.Should().Contain(actualStatus.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Apply_ResponseStatusSetWithTwoMatchingOperations_FailsClosed()
    {
        var result = PassingResult(
            "Conditional delete > ambiguous GET",
            [OperationStep("GET", 404), OperationStep("GET", 410)]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(
            [result],
            ResponseStatusPlan(result.Id, "GET", 404, 410));

        var failed = updated.Should().ContainSingle().Which;
        failed.Status.Should().Be(ConformanceStatus.Fail);
        failed.Error!.Received.Should().Contain("exactly one executed GET operation").And.Contain("found 2");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    public void Apply_WhenDeleteReturnsAllowedStatus_Passes(int deleteStatus)
    {
        var result = PassingResult("allowed delete", DeletedResourceLifecycleSteps(deleteStatus, 404));

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Fact]
    public void Apply_WhenDeleteReturns201_Fails()
    {
        var result = PassingResult("invalid delete", DeletedResourceLifecycleSteps(201, 404));

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

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

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

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

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(
            results,
            Plan(results.Select(result => result.Id).ToArray()));

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

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

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

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
    }

    [Fact]
    public void Apply_WhenMarkedDeleteAlternativesFollowNonDeleteOperation_DoesNotEnforce()
    {
        var result = PassingResult(
            "wrong delete method",
            [
                OperationStep("PUT", 201),
                AssertionStep("Accepted DELETE response: 200 OK for completed deletion"),
                AssertionStep("Accepted DELETE response: 202 Accepted for asynchronous deletion"),
                AssertionStep("Accepted DELETE response: 204 No Content for completed deletion"),
            ]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Fact]
    public void Apply_WhenMarkedReadbackAlternativesFollowNonGetOperation_DoesNotEnforce()
    {
        var result = PassingResult(
            "wrong read method",
            [
                .. DeleteSteps(202),
                OperationStep("POST", 500),
                AssertionStep("Accepted alternative: 200 OK while an asynchronous delete is still pending"),
                AssertionStep("Accepted alternative: 410 Gone when the server tracks the deleted resource"),
                AssertionStep("Accepted alternative: 404 Not Found when deleted resources are not tracked"),
            ]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], Plan(result.Id));

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Theory]
    [InlineData(404, "pass")]
    [InlineData(410, "pass")]
    [InlineData(500, "fail")]
    public void Apply_DeletedResourceReadbackPolicy_EnforcesOnly404Or410(int statusCode, string expected)
    {
        var result = PassingResult(
            "classic deleted readback",
            [
                OperationStep("DELETE", 204),
                OperationStep("GET", statusCode),
                AssertionStep("Preferred: 410 Gone for a deleted resource"),
                AssertionStep("Alternative: 404 Not Found when deleted resources are not tracked"),
            ]);
        var plan = PolicyPlan(result.Id, StatusAlternativePolicy.DeletedResourceReadback);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply([result], plan);

        updated.Should().ContainSingle().Which.Status.Should().Be(expected);
    }

    [Fact]
    public void Apply_DeletedResourceReadbackPolicy_RequiresSameUrlDeleteAndGet()
    {
        var result = PassingResult(
            "uncorrelated classic readback",
            [
                OperationStep("DELETE", 204, "https://example.test/Patient/other"),
                OperationStep("GET", 500),
                AssertionStep("Preferred: 410 Gone for a deleted resource"),
                AssertionStep("Alternative: 404 Not Found when deleted resources are not tracked"),
            ]);

        var updated = WarningOnlyStatusAlternativeEnforcer.Apply(
            [result],
            PolicyPlan(result.Id, StatusAlternativePolicy.DeletedResourceReadback));

        updated.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    private static StatusAlternativeEnforcementPlan Plan(params string[] resultIds) =>
        new(resultIds.ToDictionary(
            resultId => resultId,
            _ => StatusAlternativePolicy.SubscriptionDeleteReadback,
            StringComparer.Ordinal));

    private static StatusAlternativeEnforcementPlan PolicyPlan(
        string resultId,
        StatusAlternativePolicy policy) =>
        new(new Dictionary<string, StatusAlternativePolicy>(StringComparer.Ordinal)
        {
            [resultId] = policy,
        });

    private static StatusAlternativeEnforcementPlan ResponseStatusPlan(string resultId) =>
        ResponseStatusPlan(resultId, "POST", 400, 422);

    private static StatusAlternativeEnforcementPlan ResponseStatusPlan(
        string resultId,
        string method,
        params int[] allowedStatuses) =>
        new(new Dictionary<string, StatusAlternativeRule>(StringComparer.Ordinal)
        {
            [resultId] = new(
                StatusAlternativePolicy.ResponseStatusSet,
                method,
                allowedStatuses),
        });

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
