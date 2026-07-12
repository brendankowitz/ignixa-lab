using System.Collections.Immutable;
using FluentAssertions;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Execution;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class ConformanceReportMapperTests
{
    private static ActionResult Action(
        string label,
        TestScriptOutcome outcome,
        string message = "",
        string description = "",
        double durationMs = 5,
        TestActionKind kind = TestActionKind.Assertion,
        HttpExchange? exchange = null) =>
        new(label, description, outcome, message, TimeSpan.FromMilliseconds(durationMs), kind, exchange);

    private static TestScriptReport Report(
        string name,
        IReadOnlyList<TestCaseResult>? tests = null,
        TestPhaseResult? setup = null,
        TestPhaseResult? teardown = null) =>
        new()
        {
            TestScriptName = name,
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(120),
            SetupResult = setup!,
            TestResults = tests ?? Array.Empty<TestCaseResult>(),
            TeardownResult = teardown!,
        };

    [Fact]
    public void Map_ProducesOneResultPerTestCase()
    {
        var report = Report(
            "Patient CRUD",
            tests: new[]
            {
                new TestCaseResult("create", "creates a patient", new[] { Action("POST", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
                new TestCaseResult("read", "reads a patient", new[] { Action("GET", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
            });

        var results = ConformanceReportMapper.Map(report, suiteId: "crud/patient.json", category: "crud", file: "crud/patient.json");

        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().Equal("Patient CRUD > create", "Patient CRUD > read");
        results.Should().OnlyContain(r => r.Suite == "crud/patient.json" && r.Category == "crud");
    }

    [Theory]
    [InlineData(TestScriptOutcome.Pass, ConformanceStatus.Pass)]
    [InlineData(TestScriptOutcome.Warning, ConformanceStatus.Pass)]
    [InlineData(TestScriptOutcome.Skip, ConformanceStatus.Skipped)]
    [InlineData(TestScriptOutcome.Error, ConformanceStatus.Error)]
    [InlineData(TestScriptOutcome.Fail, ConformanceStatus.Fail)]
    public void Map_MapsTestCaseOutcomeToConformanceStatus(TestScriptOutcome outcome, string expectedStatus)
    {
        var report = Report(
            "suite",
            tests: new[]
            {
                new TestCaseResult("case", "desc", new[] { Action("step", outcome) }, outcome),
            });

        var results = ConformanceReportMapper.Map(report, "s", "cat", "s.json");

        results.Should().ContainSingle().Which.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void Map_FoldsSetupAndTeardownStepsIntoEachResult()
    {
        var report = Report(
            "suite",
            setup: new TestPhaseResult(new[] { Action("setup-op", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
            teardown: new TestPhaseResult(new[] { Action("teardown-op", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
            tests: new[]
            {
                new TestCaseResult("case", "desc", new[] { Action("test-op", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
            });

        var result = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single();

        result.Steps.Select(s => s.Phase).Should().Equal("setup", "test", "teardown");
        result.Steps.Select(s => s.Label).Should().Equal("setup-op", "test-op", "teardown-op");
    }

    [Fact]
    public void Map_PopulatesErrorFromFirstFailingStep()
    {
        var report = Report(
            "suite",
            tests: new[]
            {
                new TestCaseResult(
                    "case",
                    "desc",
                    new[]
                    {
                        Action("ok-op", TestScriptOutcome.Pass),
                        Action("assert status", TestScriptOutcome.Fail, message: "expected 200 but got 404"),
                    },
                    TestScriptOutcome.Fail),
            });

        var result = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single();

        result.Status.Should().Be(ConformanceStatus.Fail);
        result.Error.Should().NotBeNull();
        result.Error!.Assertion.Should().Be("assert status");
        result.Error.Received.Should().Be("expected 200 but got 404");
    }

    [Fact]
    public void Map_LeavesErrorNullForPassingResults()
    {
        var report = Report(
            "suite",
            tests: new[]
            {
                new TestCaseResult("case", "desc", new[] { Action("assert ok", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
            });

        var result = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single();

        result.Error.Should().BeNull();
    }

    [Fact]
    public void Map_MapsActionKindToStepKind()
    {
        var report = Report(
            "suite",
            tests: new[]
            {
                new TestCaseResult(
                    "case",
                    "desc",
                    new[]
                    {
                        Action("GET metadata", TestScriptOutcome.Pass, kind: TestActionKind.Operation),
                        Action("assert response is CapabilityStatement", TestScriptOutcome.Pass, kind: TestActionKind.Assertion),
                    },
                    TestScriptOutcome.Pass),
            });

        var steps = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single().Steps;

        steps[0].Kind.Should().Be("operation");
        steps[1].Kind.Should().Be("assertion");
    }

    [Fact]
    public void Map_OperationWithExchange_PopulatesRequestAndResponse()
    {
        var request = new TestRequest
        {
            Method = HttpMethod.Post,
            Url = "https://target.example/Patient",
            Headers = ImmutableDictionary<string, string>.Empty
                .Add("Content-Type", "application/fhir+json")
                .Add("Authorization", "Bearer secret-token"),
            FormBody = "{\"resourceType\":\"Patient\"}",
        };
        var response = new TestResponse
        {
            StatusCode = 201,
            Headers = ImmutableDictionary<string, string>.Empty.Add("Location", "https://target.example/Patient/1"),
            RawBody = "{\"resourceType\":\"Patient\",\"id\":\"1\"}",
        };
        var report = Report(
            "suite",
            tests: new[]
            {
                new TestCaseResult(
                    "case",
                    "desc",
                    new[] { Action("POST Patient", TestScriptOutcome.Pass, kind: TestActionKind.Operation, exchange: new HttpExchange(request, response)) },
                    TestScriptOutcome.Pass),
            });

        var step = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single().Steps.Single();

        step.Kind.Should().Be("operation");
        step.Request.Should().NotBeNull();
        step.Request!.Method.Should().Be("POST");
        step.Request.Url.Should().Be("https://target.example/Patient");
        step.Request.Body.Should().Be("{\"resourceType\":\"Patient\"}");
        step.Request.Headers["Authorization"].Should().Be("***redacted***");
        step.Request.Headers["Content-Type"].Should().Be("application/fhir+json");

        step.Response.Should().NotBeNull();
        step.Response!.StatusCode.Should().Be(201);
        step.Response.Body.Should().Be("{\"resourceType\":\"Patient\",\"id\":\"1\"}");
        step.Response.Headers["Location"].Should().Be("https://target.example/Patient/1");
    }

    [Fact]
    public void Map_AssertionWithoutExchange_LeavesRequestAndResponseNull()
    {
        var report = Report(
            "suite",
            tests: new[]
            {
                new TestCaseResult(
                    "case",
                    "desc",
                    new[] { Action("assert response is CapabilityStatement", TestScriptOutcome.Pass, kind: TestActionKind.Assertion) },
                    TestScriptOutcome.Pass),
            });

        var step = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single().Steps.Single();

        step.Kind.Should().Be("assertion");
        step.Request.Should().BeNull();
        step.Response.Should().BeNull();
    }

    [Fact]
    public void Map_EmitsSyntheticResultWhenNoTestCasesArePresent()
    {
        var report = Report(
            "setup-only-suite",
            setup: new TestPhaseResult(new[] { Action("setup", TestScriptOutcome.Error, message: "connection refused") }, TestScriptOutcome.Error));

        var results = ConformanceReportMapper.Map(report, "s", "cat", "s.json");

        var result = results.Should().ContainSingle().Subject;
        result.Id.Should().Be("setup-only-suite");
        result.Status.Should().Be(ConformanceStatus.Error);
        result.Steps.Should().ContainSingle(s => s.Phase == "setup");
    }

    [Fact]
    public void Map_SumsStepDurationsForTestCaseResults()
    {
        var report = Report(
            "suite",
            setup: new TestPhaseResult(new[] { Action("setup", TestScriptOutcome.Pass, durationMs: 10) }, TestScriptOutcome.Pass),
            tests: new[]
            {
                new TestCaseResult("case", "desc", new[] { Action("step", TestScriptOutcome.Pass, durationMs: 20) }, TestScriptOutcome.Pass),
            });

        var result = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single();

        result.DurationMs.Should().Be(30);
    }

    [Fact]
    public void Map_UsesScriptNameWhenTestCaseNameIsBlank()
    {
        var report = Report(
            "Solo Script",
            tests: new[]
            {
                new TestCaseResult(string.Empty, "desc", new[] { Action("step", TestScriptOutcome.Pass) }, TestScriptOutcome.Pass),
            });

        var result = ConformanceReportMapper.Map(report, "s", "cat", "s.json").Single();

        result.Id.Should().Be("Solo Script");
    }

    [Fact]
    public void Map_SurfacesGroupIdAndMembersOnGroupedActions()
    {
        var groupedAction = new ActionResult(
            Label: "grp",
            Description: "Deleted resource readback",
            Outcome: TestScriptOutcome.Pass,
            Message: "assertionAnyOfGroup 'grp': matched alternative 'Alternative: 404 Not Found'",
            GroupId: "grp",
            Members:
            [
                new AssertionGroupMemberResult("Preferred: 410 Gone", true, false, "Expected response 'gone' but got status 404"),
                new AssertionGroupMemberResult("Alternative: 404 Not Found", true, true, null),
            ]);

        var report = Report("GroupedTest", tests:
        [
            new TestCaseResult("case", null, [groupedAction], TestScriptOutcome.Pass),
        ]);

        var results = ConformanceReportMapper.Map(report, "suite-id", "category", "file.json");

        var step = results[0].Steps.Single(s => s.Label == "grp");
        step.GroupId.Should().Be("grp");
        step.Members.Should().NotBeNull();
        step.Members!.Should().HaveCount(2);
        step.Members![1].Passed.Should().BeTrue();
    }
}
