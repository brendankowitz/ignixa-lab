using Ignixa.Lab.Functions.Conformance;

namespace Ignixa.Lab.Functions.Execution;

internal static class WarningOnlyStatusAlternativeEnforcer
{
    public static IReadOnlyList<ConformanceResult> Apply(IReadOnlyList<ConformanceResult> results)
    {
        if (results.Count == 0)
        {
            return results;
        }

        ConformanceResult[]? updated = null;

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (!string.Equals(result.Status, ConformanceStatus.Pass, StringComparison.Ordinal))
            {
                continue;
            }

            var enforcement = FindUnexpectedStatusAlternative(result);
            if (enforcement is null)
            {
                continue;
            }

            updated ??= results.ToArray();
            updated[i] = ApplyFailure(result, enforcement.Value);
        }

        return updated ?? results;
    }

    private static ConformanceResult ApplyFailure(
        ConformanceResult result,
        StatusAlternativeFailure failure)
    {
        var steps = result.Steps.ToArray();
        var failingStep = steps[failure.StepIndex];
        steps[failure.StepIndex] = failingStep with
        {
            Status = ConformanceStatus.Fail,
            Message = failure.Message,
        };

        return result with
        {
            Status = ConformanceStatus.Fail,
            Error = new ConformanceError(
                failingStep.Label ?? failingStep.Description ?? "response status alternatives",
                failure.Message),
            Steps = steps,
        };
    }

    private static StatusAlternativeFailure? FindUnexpectedStatusAlternative(ConformanceResult result)
    {
        var testSteps = result.Steps
            .Select((Step, Index) => (Step, Index))
            .Where(item => string.Equals(item.Step.Phase, "test", StringComparison.Ordinal))
            .ToArray();

        if (testSteps.Length == 0)
        {
            return null;
        }

        for (var stepIndex = 0; stepIndex < testSteps.Length;)
        {
            if (!TryGetResponseStatusAlternative(testSteps[stepIndex].Step, out var firstAlternative))
            {
                stepIndex++;
                continue;
            }

            var alternatives = new List<StatusAlternative> { firstAlternative };
            var groupEnd = stepIndex;
            while (groupEnd + 1 < testSteps.Length
                && TryGetResponseStatusAlternative(testSteps[groupEnd + 1].Step, out var nextAlternative))
            {
                alternatives.Add(nextAlternative);
                groupEnd++;
            }

            if (IsDeletedResourceReadbackAlternativeGroup(alternatives)
                && TryGetPreviousOperationStatusCode(testSteps, stepIndex, out var actualStatusCode)
                && !alternatives.Any(alternative => alternative.StatusCode == actualStatusCode))
            {
                var assertionStepIndex = testSteps[groupEnd].Index;
                var expected = string.Join(" or ", alternatives.Select(alternative => alternative.StatusCode));
                return new StatusAlternativeFailure(
                    assertionStepIndex,
                    $"Expected response status {expected} for deleted-resource readback warningOnly alternatives, but actual status was {actualStatusCode}.");
            }

            stepIndex = groupEnd + 1;
        }

        return null;
    }

    private static bool IsDeletedResourceReadbackAlternativeGroup(IReadOnlyList<StatusAlternative> alternatives)
    {
        if (alternatives.Count != 3
            || !alternatives.Any(alternative => alternative.StatusCode == 200)
            || !alternatives.Any(alternative => alternative.StatusCode == 410)
            || !alternatives.Any(alternative => alternative.StatusCode == 404))
        {
            return false;
        }

        var text = alternatives.Select(alternative => alternative.Text).ToArray();
        return text.Any(value => ContainsAny(value, "asynchronous delete", "async delete"))
            && text.Any(value => ContainsAny(value, "deleted resource", "deleted resources"))
            && text.Any(value => ContainsAny(value, "not tracked", "does not track", "don't distinguish deleted", "doesn't distinguish deleted", "does not distinguish deleted"));
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetPreviousOperationStatusCode(
        IReadOnlyList<(ConformanceStep Step, int Index)> testSteps,
        int beforeStepIndex,
        out int statusCode)
    {
        var stepIndex = Math.Min(beforeStepIndex - 1, testSteps.Count - 1);
        for (var i = stepIndex; i >= 0; i--)
        {
            var step = testSteps[i].Step;
            if (string.Equals(step.Kind, "operation", StringComparison.Ordinal)
                && step.Response is { StatusCode: var code })
            {
                statusCode = code;
                return true;
            }
        }

        statusCode = 0;
        return false;
    }

    private static bool TryGetResponseStatusAlternative(ConformanceStep step, out StatusAlternative alternative)
    {
        if (!string.Equals(step.Kind, "assertion", StringComparison.Ordinal))
        {
            alternative = default;
            return false;
        }

        var text = string.Join(" ", step.Label, step.Description, step.Message);
        if (ContainsAny(text, "200 ok"))
        {
            alternative = new StatusAlternative(200, text);
            return true;
        }

        if (ContainsAny(text, "410", "gone"))
        {
            alternative = new StatusAlternative(410, text);
            return true;
        }

        if (ContainsAny(text, "404", "not found", "notFound"))
        {
            alternative = new StatusAlternative(404, text);
            return true;
        }

        alternative = default;
        return false;
    }

    private readonly record struct StatusAlternativeFailure(int StepIndex, string Message);

    private readonly record struct StatusAlternative(int StatusCode, string Text);
}
