using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Execution;

public enum StatusAlternativePolicy
{
    SubscriptionDeleteReadback,
    DeletedResourceReadback,
}

public sealed class StatusAlternativeEnforcementPlan
{
    public const string ExtensionUrl = "http://ignixa.io/testscript/statusAlternativePolicy";
    public static StatusAlternativeEnforcementPlan Empty { get; } =
        new(new Dictionary<string, StatusAlternativePolicy>());

    private readonly IReadOnlyDictionary<string, StatusAlternativePolicy> _policies;

    internal StatusAlternativeEnforcementPlan(IReadOnlyDictionary<string, StatusAlternativePolicy> policies)
    {
        _policies = policies;
    }

    public static StatusAlternativeEnforcementPlan Parse(string content)
    {
        var root = JsonNode.Parse(content)?.AsObject()
            ?? throw new InvalidDataException("TestScript content must be a JSON object.");
        var policies = new Dictionary<string, StatusAlternativePolicy>(StringComparer.Ordinal);

        foreach (var test in root["test"]?.AsArray() ?? [])
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

            var markerValue = markers[0]?["valueCode"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(markerValue))
            {
                throw new InvalidDataException($"Test '{testName}' must give its {ExtensionUrl} marker a valueCode.");
            }

            policies[testName] = markerValue switch
            {
                "subscription-delete-readback-v1" => StatusAlternativePolicy.SubscriptionDeleteReadback,
                "deleted-resource-readback-v1" => StatusAlternativePolicy.DeletedResourceReadback,
                _ => throw new InvalidDataException(
                    $"Test '{testName}' declares unsupported status-alternative policy '{markerValue}'."),
            };
        }

        return new StatusAlternativeEnforcementPlan(policies);
    }

    internal bool TryGetPolicy(string resultId, out StatusAlternativePolicy policy)
    {
        foreach (var entry in _policies)
        {
            if (string.Equals(resultId, entry.Key, StringComparison.Ordinal)
                || resultId.EndsWith($" > {entry.Key}", StringComparison.Ordinal))
            {
                policy = entry.Value;
                return true;
            }
        }

        policy = default;
        return false;
    }
}
