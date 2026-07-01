using Ignixa.Lab.Functions.Conformance;
using Ignixa.TestScript.Reporting;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Maps the engine's <see cref="TestScriptReport"/> into the conformance report
/// schema consumed by the dashboard. One <see cref="ConformanceResult"/> is
/// produced per test case; setup and teardown phases are folded into each
/// result's step list so failures there are visible alongside the test.
/// </summary>
public static class ConformanceReportMapper
{
    /// <summary>
    /// Converts a single suite's <paramref name="report"/> into conformance
    /// results. A suite that defines multiple test cases yields multiple
    /// results; a suite with no test cases yields a single synthetic result so
    /// setup/teardown outcomes are still surfaced.
    /// </summary>
    public static IReadOnlyList<ConformanceResult> Map(
        TestScriptReport report,
        string suiteId,
        string category,
        string file)
    {
        var setupSteps = MapPhase(report.SetupResult, TestPhaseType.Setup);
        var teardownSteps = MapPhase(report.TeardownResult, TestPhaseType.Teardown);
        var results = new List<ConformanceResult>();

        if (report.TestResults.Count == 0)
        {
            var steps = Concat(setupSteps, Array.Empty<ConformanceStep>(), teardownSteps);
            results.Add(BuildResult(
                id: report.TestScriptName,
                file: file,
                suite: suiteId,
                category: category,
                outcome: report.OverallOutcome,
                durationMs: DurationMs(report.StartTime, report.EndTime),
                steps: steps));
            return results;
        }

        foreach (var testCase in report.TestResults)
        {
            var testSteps = MapActions(testCase.Actions, TestPhaseType.Test);
            var steps = Concat(setupSteps, testSteps, teardownSteps);
            var id = string.IsNullOrWhiteSpace(testCase.Name)
                ? report.TestScriptName
                : $"{report.TestScriptName} > {testCase.Name}";

            results.Add(BuildResult(
                id: id,
                file: file,
                suite: suiteId,
                category: category,
                outcome: testCase.Outcome,
                durationMs: SumDuration(steps),
                steps: steps));
        }

        return results;
    }

    private static ConformanceResult BuildResult(
        string id,
        string file,
        string suite,
        string category,
        TestScriptOutcome outcome,
        long durationMs,
        IReadOnlyList<ConformanceStep> steps)
    {
        var status = ConformanceStatus.FromOutcome(outcome);
        var error = status is ConformanceStatus.Pass or ConformanceStatus.Skipped
            ? null
            : BuildError(steps);

        return new ConformanceResult(
            Id: id,
            File: file,
            Suite: suite,
            Category: category,
            Status: status,
            DurationMs: durationMs,
            Error: error,
            Steps: steps);
    }

    private static ConformanceError? BuildError(IReadOnlyList<ConformanceStep> steps)
    {
        var failing = steps.FirstOrDefault(s =>
            s.Status is ConformanceStatus.Fail or ConformanceStatus.Error);

        if (failing is null)
        {
            return null;
        }

        var assertion = failing.Label ?? failing.Description ?? failing.Kind;
        return new ConformanceError(assertion, failing.Message);
    }

    private static IReadOnlyList<ConformanceStep> MapPhase(TestPhaseResult? phase, TestPhaseType phaseType)
    {
        if (phase is null || phase.Actions.Count == 0)
        {
            return Array.Empty<ConformanceStep>();
        }

        return MapActions(phase.Actions, phaseType);
    }

    private static IReadOnlyList<ConformanceStep> MapActions(
        IReadOnlyList<ActionResult> actions,
        TestPhaseType phaseType)
    {
        if (actions.Count == 0)
        {
            return Array.Empty<ConformanceStep>();
        }

        var steps = new ConformanceStep[actions.Count];
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            steps[i] = new ConformanceStep(
                Phase: phaseType.ToString().ToLowerInvariant(),
                Kind: InferKind(action),
                Status: ConformanceStatus.FromOutcome(action.Outcome),
                DurationMs: (long)action.Duration.TotalMilliseconds,
                Label: NullIfEmpty(action.Label),
                Description: NullIfEmpty(action.Description),
                Message: NullIfEmpty(action.Message),
                Request: null,
                Response: null);
        }

        return steps;
    }

    /// <summary>
    /// Infers whether an action is an assertion or an operation. The published
    /// engine does not expose the action kind directly, so we heuristically
    /// classify based on the label/description wording.
    /// </summary>
    private static string InferKind(ActionResult action)
    {
        var text = $"{action.Label} {action.Description}".ToLowerInvariant();
        return text.Contains("assert", StringComparison.Ordinal) ? "assertion" : "operation";
    }

    private static IReadOnlyList<ConformanceStep> Concat(
        IReadOnlyList<ConformanceStep> setup,
        IReadOnlyList<ConformanceStep> test,
        IReadOnlyList<ConformanceStep> teardown)
    {
        if (setup.Count == 0 && teardown.Count == 0)
        {
            return test;
        }

        var combined = new List<ConformanceStep>(setup.Count + test.Count + teardown.Count);
        combined.AddRange(setup);
        combined.AddRange(test);
        combined.AddRange(teardown);
        return combined;
    }

    private static long SumDuration(IReadOnlyList<ConformanceStep> steps)
    {
        long total = 0;
        foreach (var step in steps)
        {
            total += step.DurationMs;
        }

        return total;
    }

    private static long DurationMs(DateTimeOffset start, DateTimeOffset end)
    {
        var ms = (end - start).TotalMilliseconds;
        return ms < 0 ? 0 : (long)ms;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
