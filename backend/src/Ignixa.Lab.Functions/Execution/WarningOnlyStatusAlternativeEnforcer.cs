using Ignixa.Lab.Functions.Conformance;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;

namespace Ignixa.Lab.Functions.Execution;

internal static class WarningOnlyStatusAlternativeEnforcer
{
    public static IReadOnlyList<ConformanceResult> Apply(
        IReadOnlyList<ConformanceResult> results,
        TestScriptDefinition definition)
    {
        if (results.Count == 0 || definition.Tests.Count == 0)
        {
            return results;
        }

        ConformanceResult[]? updated = null;
        var resultCount = Math.Min(results.Count, definition.Tests.Count);

        for (var i = 0; i < resultCount; i++)
        {
            var result = results[i];
            if (!string.Equals(result.Status, ConformanceStatus.Pass, StringComparison.Ordinal))
            {
                continue;
            }

            var enforcement = FindUnexpectedStatusAlternative(definition.Tests[i], result);
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

    private static StatusAlternativeFailure? FindUnexpectedStatusAlternative(
        TestPhaseDefinition test,
        ConformanceResult result)
    {
        var testSteps = result.Steps
            .Select((Step, Index) => (Step, Index))
            .Where(item => string.Equals(item.Step.Phase, "test", StringComparison.Ordinal))
            .ToArray();

        if (testSteps.Length == 0)
        {
            return null;
        }

        for (var actionIndex = 0; actionIndex < test.Actions.Count;)
        {
            if (!TryGetWarningOnlyResponseStatusCode(test.Actions[actionIndex], out var firstAlternative))
            {
                actionIndex++;
                continue;
            }

            var alternatives = new List<StatusAlternative> { firstAlternative };
            var groupEnd = actionIndex;
            while (groupEnd + 1 < test.Actions.Count
                && TryGetWarningOnlyResponseStatusCode(test.Actions[groupEnd + 1], out var nextAlternative))
            {
                alternatives.Add(nextAlternative);
                groupEnd++;
            }

            if (IsDeletedResourceGoneNotFoundAlternativeGroup(alternatives)
                && TryGetPreviousOperationStatusCode(testSteps, actionIndex, out var actualStatusCode)
                && !alternatives.Any(alternative => alternative.StatusCode == actualStatusCode))
            {
                var assertionStepIndex = testSteps[Math.Min(groupEnd, testSteps.Length - 1)].Index;
                var expected = string.Join(" or ", alternatives.Select(alternative => alternative.StatusCode));
                return new StatusAlternativeFailure(
                    assertionStepIndex,
                    $"Expected response status {expected} for deleted-resource warningOnly alternatives, but actual status was {actualStatusCode}.");
            }

            actionIndex = groupEnd + 1;
        }

        return null;
    }

    private static bool IsDeletedResourceGoneNotFoundAlternativeGroup(IReadOnlyList<StatusAlternative> alternatives)
    {
        if (alternatives.Count != 2
            || !alternatives.Any(alternative => alternative.StatusCode == 410)
            || !alternatives.Any(alternative => alternative.StatusCode == 404))
        {
            return false;
        }

        var descriptions = alternatives.Select(alternative => alternative.Description ?? string.Empty).ToArray();
        return descriptions.Any(description => ContainsAny(description, "deleted resource", "deleted resources"))
            && descriptions.Any(description => ContainsAny(description, "not tracked", "does not track", "don't distinguish deleted", "doesn't distinguish deleted", "does not distinguish deleted"));
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetPreviousOperationStatusCode(
        IReadOnlyList<(ConformanceStep Step, int Index)> testSteps,
        int beforeActionIndex,
        out int statusCode)
    {
        var stepIndex = Math.Min(beforeActionIndex - 1, testSteps.Count - 1);
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

    private static bool TryGetWarningOnlyResponseStatusCode(
        ActionExpression action,
        out StatusAlternative alternative)
    {
        if (action is AssertExpression { WarningOnly: true, Criteria: ResponseCodeCriteria responseCode }
            && int.TryParse(responseCode.Code, out var responseCodeStatusCode))
        {
            alternative = new StatusAlternative(responseCodeStatusCode, action.Description);
            return true;
        }

        if (action is AssertExpression { WarningOnly: true, Criteria: ResponseStatusCriteria responseStatus })
        {
            if (TryMapResponseStatus(responseStatus.Status, out var responseStatusCode))
            {
                alternative = new StatusAlternative(responseStatusCode, action.Description);
                return true;
            }
        }

        alternative = default;
        return false;
    }

    private static bool TryMapResponseStatus(string status, out int statusCode)
    {
        statusCode = status switch
        {
            "okay" => 200,
            "created" => 201,
            "accepted" => 202,
            "noContent" => 204,
            "bad" => 400,
            "unauthorized" => 401,
            "forbidden" => 403,
            "notFound" => 404,
            "methodNotAllowed" => 405,
            "conflict" => 409,
            "gone" => 410,
            "preconditionFailed" => 412,
            "unprocessableEntity" => 422,
            _ => 0,
        };

        return statusCode != 0;
    }

    private readonly record struct StatusAlternativeFailure(int StepIndex, string Message);

    private readonly record struct StatusAlternative(int StatusCode, string? Description);
}
