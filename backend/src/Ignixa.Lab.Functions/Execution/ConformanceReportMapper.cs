using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Http;
using Ignixa.TestScript.Reporting;
using Microsoft.Extensions.Logging;

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
    /// <param name="exchanges">
    /// HTTP request/response pairs captured while the engine executed
    /// <paramref name="report"/>'s TestScript, in call order. The engine runs
    /// setup once, then each test case's actions, then teardown once, and
    /// only "operation" actions make an HTTP call — so exchanges are
    /// attached to operation steps in that same execution order. Omit (or
    /// pass an empty list) when no capture was performed; steps are then left
    /// without a request/response, matching the previous behaviour.
    /// </param>
    /// <param name="logger">
    /// Optional logger used to record a warning when the number of operation
    /// steps does not match the number of captured exchanges (classification
    /// of operation vs. assertion is heuristic). Never used to throw.
    /// </param>
    public static IReadOnlyList<ConformanceResult> Map(
        TestScriptReport report,
        string suiteId,
        string category,
        string file,
        IReadOnlyList<CapturedExchange>? exchanges = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        exchanges ??= Array.Empty<CapturedExchange>();

        var setupSteps = MapPhase(report.SetupResult, TestPhaseType.Setup);
        var teardownSteps = MapPhase(report.TeardownResult, TestPhaseType.Teardown);
        var results = new List<ConformanceResult>();

        if (report.TestResults.Count == 0)
        {
            LogCountMismatch(suiteId, CountOperations(setupSteps) + CountOperations(teardownSteps), exchanges.Count, logger);

            var exchangeQueue = new Queue<CapturedExchange>(exchanges);
            var attachedSetup = ConformanceStepCorrelator.Attach(setupSteps, exchangeQueue);
            var attachedTeardown = ConformanceStepCorrelator.Attach(teardownSteps, exchangeQueue);
            var steps = Concat(attachedSetup, Array.Empty<ConformanceStep>(), attachedTeardown);

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

        var testStepsByCase = new List<IReadOnlyList<ConformanceStep>>(report.TestResults.Count);
        var operationCount = CountOperations(setupSteps) + CountOperations(teardownSteps);
        foreach (var testCase in report.TestResults)
        {
            var testSteps = MapActions(testCase.Actions, TestPhaseType.Test);
            testStepsByCase.Add(testSteps);
            operationCount += CountOperations(testSteps);
        }

        LogCountMismatch(suiteId, operationCount, exchanges.Count, logger);

        // Attach in true execution order: setup once, then each test case's
        // actions in order, then teardown once — matching the queue built
        // from the exchanges the engine actually made.
        var queue = new Queue<CapturedExchange>(exchanges);
        var attachedSetupSteps = ConformanceStepCorrelator.Attach(setupSteps, queue);
        var attachedTestStepsByCase = new List<IReadOnlyList<ConformanceStep>>(testStepsByCase.Count);
        foreach (var testSteps in testStepsByCase)
        {
            attachedTestStepsByCase.Add(ConformanceStepCorrelator.Attach(testSteps, queue));
        }

        var attachedTeardownSteps = ConformanceStepCorrelator.Attach(teardownSteps, queue);

        for (var i = 0; i < report.TestResults.Count; i++)
        {
            var testCase = report.TestResults[i];
            var steps = Concat(attachedSetupSteps, attachedTestStepsByCase[i], attachedTeardownSteps);
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

    private static void LogCountMismatch(string suiteId, int operationStepCount, int exchangeCount, ILogger? logger)
    {
        if (operationStepCount == exchangeCount)
        {
            return;
        }

        logger?.LogWarning(
            "Suite {Suite}: {OperationSteps} operation step(s) but {ExchangeCount} captured HTTP exchange(s); some steps may be missing a request/response trace.",
            suiteId,
            operationStepCount,
            exchangeCount);
    }

    private static int CountOperations(IReadOnlyList<ConformanceStep> steps)
    {
        var count = 0;
        foreach (var step in steps)
        {
            if (step.Kind == "operation")
            {
                count++;
            }
        }

        return count;
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
