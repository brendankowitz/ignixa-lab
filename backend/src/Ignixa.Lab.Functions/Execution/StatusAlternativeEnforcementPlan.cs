using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Execution;

public enum StatusAlternativePolicy
{
    SubscriptionDeleteReadback,
    DeletedResourceReadback,
    ResponseStatusSet,
}

public sealed record StatusAlternativeRule(
    StatusAlternativePolicy Policy,
    string? Method = null,
    IReadOnlyList<int>? AllowedStatusCodes = null);

public sealed class StatusAlternativeEnforcementPlan
{
    public const string ExtensionUrl = "http://ignixa.io/testscript/statusAlternativePolicy";
    public static StatusAlternativeEnforcementPlan Empty { get; } =
        new(new Dictionary<string, StatusAlternativeRule>());

    private readonly IReadOnlyDictionary<string, StatusAlternativeRule> _rules;

    internal StatusAlternativeEnforcementPlan(IReadOnlyDictionary<string, StatusAlternativePolicy> policies)
        : this(policies.ToDictionary(
            entry => entry.Key,
            entry => new StatusAlternativeRule(entry.Value),
            StringComparer.Ordinal))
    {
    }

    internal StatusAlternativeEnforcementPlan(IReadOnlyDictionary<string, StatusAlternativeRule> rules)
    {
        _rules = rules;
    }

    public static StatusAlternativeEnforcementPlan Parse(string content)
    {
        var root = JsonNode.Parse(content)?.AsObject()
            ?? throw new InvalidDataException("TestScript content must be a JSON object.");
        var testScriptName = root["name"]?.GetValue<string>();
        var rules = new Dictionary<string, StatusAlternativeRule>(StringComparer.Ordinal);
        var tests = root["test"]?.AsArray() ?? [];
        var testNameCounts = tests
            .Select(test => test?.AsObject()["name"]?.GetValue<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var test in tests)
        {
            var testObject = test?.AsObject();
            var testName = testObject?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(testName))
            {
                continue;
            }

            var markers = (testObject?["extension"]?.AsArray() ?? [])
                .Select(extension => extension?.AsObject())
                .Where(extension => extension?["url"]?.GetValue<string>() == ExtensionUrl)
                .ToArray();
            if (markers.Length == 0)
            {
                continue;
            }

            if (markers.Length != 1)
            {
                throw new InvalidDataException($"Test '{testName}' must declare exactly one {ExtensionUrl} marker.");
            }
            if (string.IsNullOrWhiteSpace(testScriptName))
            {
                throw new InvalidDataException(
                    $"TestScript name is required to map marked test '{testName}' to an exact result identifier.");
            }
            if (testNameCounts[testName] != 1)
            {
                throw new InvalidDataException(
                    $"Marked test name '{testName}' is duplicated in TestScript '{testScriptName}'.");
            }

            var marker = markers[0]!;
            var markerValue = marker["valueCode"]?.GetValue<string>();
            var rule = markerValue switch
            {
                "subscription-delete-readback-v1" => new(StatusAlternativePolicy.SubscriptionDeleteReadback),
                "deleted-resource-readback-v1" => new(StatusAlternativePolicy.DeletedResourceReadback),
                null => ParseCompositeRule(testName, marker),
                _ => throw new InvalidDataException(
                    $"Test '{testName}' declares unsupported status-alternative policy '{markerValue}'."),
            };
            var resultId = $"{testScriptName} > {testName}";
            if (!rules.TryAdd(resultId, rule))
            {
                throw new InvalidDataException(
                    $"Marked test name '{testName}' is duplicated in TestScript '{testScriptName}'.");
            }
        }

        return new StatusAlternativeEnforcementPlan(rules);
    }

    internal bool TryGetPolicy(string resultId, out StatusAlternativePolicy policy)
    {
        if (TryGetRule(resultId, out var rule))
        {
            policy = rule.Policy;
            return true;
        }

        policy = default;
        return false;
    }

    internal bool TryGetRule(string resultId, out StatusAlternativeRule rule)
        => _rules.TryGetValue(resultId, out rule!);

    private static StatusAlternativeRule ParseCompositeRule(string testName, JsonObject marker)
    {
        var children = marker["extension"]?.AsArray()
            ?? throw new InvalidDataException(
                $"Test '{testName}' must give its {ExtensionUrl} marker a valueCode or structured extension children.");
        var childUrls = children
            .Select(child => child?.AsObject()["url"]?.GetValue<string>())
            .ToArray();
        if (childUrls.Any(url => url is not ("policy" or "method" or "status")))
        {
            throw new InvalidDataException(
                $"Test '{testName}' response-status-set-v1 policy contains an unsupported structured child.");
        }

        var policyValues = GetChildValues<string>(children, "policy", "valueCode");
        if (policyValues.Count != 1 || policyValues[0] != "response-status-set-v1")
        {
            throw new InvalidDataException(
                $"Test '{testName}' must declare exactly one supported structured status-alternative policy.");
        }

        var methods = GetChildValues<string>(children, "method", "valueCode");
        if (methods.Count != 1
            || methods[0] is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"))
        {
            throw new InvalidDataException(
                $"Test '{testName}' response-status-set-v1 policy must declare exactly one uppercase HTTP method.");
        }

        var statuses = GetChildValues<int>(children, "status", "valueInteger");
        if (statuses.Count < 2
            || statuses.Distinct().Count() != statuses.Count
            || statuses.Any(status => status is < 100 or > 599))
        {
            throw new InvalidDataException(
                $"Test '{testName}' response-status-set-v1 policy must declare at least two distinct HTTP statuses from 100 through 599.");
        }

        return new StatusAlternativeRule(
            StatusAlternativePolicy.ResponseStatusSet,
            methods[0],
            statuses);
    }

    private static IReadOnlyList<T> GetChildValues<T>(
        JsonArray children,
        string childUrl,
        string valueProperty) =>
        children
            .Select(child => child?.AsObject())
            .Where(child => child?["url"]?.GetValue<string>() == childUrl)
            .Select(child =>
            {
                var value = child![valueProperty]
                    ?? throw new InvalidDataException(
                        $"Structured status-alternative child '{childUrl}' must declare {valueProperty}.");
                return value.GetValue<T>();
            })
            .ToArray();
}
