using Ignixa.Lab.Functions.Conformance;

namespace Ignixa.Lab.Functions.Execution;

internal static class WarningOnlyStatusAlternativeEnforcer
{
    public static IReadOnlyList<ConformanceResult> Apply(
        IReadOnlyList<ConformanceResult> results,
        StatusAlternativeEnforcementPlan? plan = null)
    {
        if (results.Count == 0 || plan is null)
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

            var enforcement = plan.TryGetPolicy(result.Id, out var policy)
                ? FindUnexpectedStatusAlternative(result, policy)
                : null;
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
        ConformanceResult result,
        StatusAlternativePolicy policy)
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

            if (policy == StatusAlternativePolicy.SubscriptionDeleteReadback
                && IsDeleteResponseAlternativeGroup(alternatives)
                && TryGetPreviousOperation(testSteps, stepIndex, out var deleteOperation)
                && string.Equals(deleteOperation.Method, "DELETE", StringComparison.OrdinalIgnoreCase)
                && !alternatives.Any(alternative => alternative.StatusCode == deleteOperation.StatusCode))
            {
                return CreateUnexpectedStatusFailure(
                    testSteps[groupEnd].Index,
                    alternatives,
                    deleteOperation.StatusCode,
                    "DELETE warningOnly alternatives");
            }

            if (policy == StatusAlternativePolicy.SubscriptionDeleteReadback
                && IsDeletedResourceReadbackAlternativeGroup(alternatives)
                && TryGetPreviousOperation(testSteps, stepIndex, out var readOperation)
                && string.Equals(readOperation.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                if (!alternatives.Any(alternative => alternative.StatusCode == readOperation.StatusCode))
                {
                    return CreateUnexpectedStatusFailure(
                        testSteps[groupEnd].Index,
                        alternatives,
                        readOperation.StatusCode,
                        "deleted-resource readback warningOnly alternatives");
                }

                if (readOperation.StatusCode == 200
                    && (!TryGetPreviousDeleteStatusCode(
                            testSteps,
                            readOperation.StepIndex,
                            readOperation.RequestUrl,
                            out var deleteStatusCode)
                        || deleteStatusCode != 202))
                {
                    return new StatusAlternativeFailure(
                        testSteps[groupEnd].Index,
                        $"A 200 readback is accepted only after a 202 asynchronous DELETE, but the preceding DELETE status was {(deleteStatusCode == 0 ? "unavailable" : deleteStatusCode)}.");
                }
            }

            if (policy == StatusAlternativePolicy.DeletedResourceReadback
                && IsClassicDeletedResourceReadbackAlternativeGroup(alternatives)
                && TryGetPreviousOperation(testSteps, stepIndex, out var classicReadOperation)
                && string.Equals(classicReadOperation.Method, "GET", StringComparison.OrdinalIgnoreCase)
                && TryGetPreviousDeleteStatusCode(
                    testSteps,
                    classicReadOperation.StepIndex,
                    classicReadOperation.RequestUrl,
                    out _)
                && !alternatives.Any(alternative => alternative.StatusCode == classicReadOperation.StatusCode))
            {
                return CreateUnexpectedStatusFailure(
                    testSteps[groupEnd].Index,
                    alternatives,
                    classicReadOperation.StatusCode,
                    "deleted-resource readback warningOnly alternatives");
            }

            stepIndex = groupEnd + 1;
        }

        return null;
    }

    private static StatusAlternativeFailure CreateUnexpectedStatusFailure(
        int assertionStepIndex,
        IReadOnlyList<StatusAlternative> alternatives,
        int actualStatusCode,
        string groupName)
    {
        var expected = string.Join(" or ", alternatives.Select(alternative => alternative.StatusCode));
        return new StatusAlternativeFailure(
            assertionStepIndex,
            $"Expected response status {expected} for {groupName}, but actual status was {actualStatusCode}.");
    }

    private static bool IsDeleteResponseAlternativeGroup(IReadOnlyList<StatusAlternative> alternatives)
    {
        if (alternatives.Count != 3
            || !alternatives.Any(alternative => alternative.StatusCode == 200)
            || !alternatives.Any(alternative => alternative.StatusCode == 202)
            || !alternatives.Any(alternative => alternative.StatusCode == 204))
        {
            return false;
        }

        return alternatives.All(alternative => ContainsAny(alternative.Text, "delete response"));
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

    private static bool IsClassicDeletedResourceReadbackAlternativeGroup(
        IReadOnlyList<StatusAlternative> alternatives) =>
        alternatives.Count == 2
        && alternatives.Any(alternative => alternative.StatusCode == 410)
        && alternatives.Any(alternative => alternative.StatusCode == 404);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetPreviousOperation(
        IReadOnlyList<(ConformanceStep Step, int Index)> testSteps,
        int beforeStepIndex,
        out OperationStatus operation)
    {
        var stepIndex = Math.Min(beforeStepIndex - 1, testSteps.Count - 1);
        for (var i = stepIndex; i >= 0; i--)
        {
            var step = testSteps[i].Step;
            if (string.Equals(step.Kind, "operation", StringComparison.Ordinal)
                && step.Response is { StatusCode: var code })
            {
                operation = new OperationStatus(i, code, step.Request?.Method, step.Request?.Url);
                return true;
            }
        }

        operation = default;
        return false;
    }

    private static bool TryGetPreviousDeleteStatusCode(
        IReadOnlyList<(ConformanceStep Step, int Index)> testSteps,
        int beforeStepIndex,
        string? requestUrl,
        out int statusCode)
    {
        if (string.IsNullOrEmpty(requestUrl))
        {
            statusCode = 0;
            return false;
        }

        for (var i = beforeStepIndex - 1; i >= 0; i--)
        {
            var step = testSteps[i].Step;
            if (string.Equals(step.Kind, "operation", StringComparison.Ordinal)
                && string.Equals(step.Request?.Method, "DELETE", StringComparison.OrdinalIgnoreCase)
                && string.Equals(step.Request?.Url, requestUrl, StringComparison.Ordinal)
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

        if (ContainsAny(text, "202 accepted"))
        {
            alternative = new StatusAlternative(202, text);
            return true;
        }

        if (ContainsAny(text, "204 no content"))
        {
            alternative = new StatusAlternative(204, text);
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

    private readonly record struct OperationStatus(int StepIndex, int StatusCode, string? Method, string? RequestUrl);
}
