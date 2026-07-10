using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Suites;

internal static class TestScriptContentNormalizer
{
    private const string RequiresCapabilityUrl = "http://ignixa.io/testscript/requiresCapability";

    /// <summary>
    /// Converts the suite DSL's test-level requiresCapability property to the
    /// FHIR extension shape understood by the TestScript parser.
    /// </summary>
    public static string Normalize(string content)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return content;
        }

        if (root?["test"] is not JsonArray tests)
        {
            return content;
        }

        var changed = false;
        foreach (var test in tests.OfType<JsonObject>())
        {
            if (test["extension"] is not null and not JsonArray)
            {
                return content;
            }

            var extensions = test["extension"] as JsonArray;
            var canonicalExtensions = extensions?.OfType<JsonObject>().Where(extension =>
                    extension["url"] is JsonValue url
                    && url.TryGetValue<string>(out var urlValue)
                    && urlValue == RequiresCapabilityUrl).ToArray() ?? [];
            var canonicalValues = canonicalExtensions
                .Select(extension => extension["valueString"] is JsonValue value
                    && value.TryGetValue<string>(out var text) ? text : null)
                .ToArray();

            if (canonicalValues.Where(value => value is not null).Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
            {
                throw new InvalidDataException("TestScript contains conflicting requiresCapability values.");
            }

            if (test["requiresCapability"] is not JsonValue requirementValue
                || !requirementValue.TryGetValue<string>(out var requirement)
                || string.IsNullOrWhiteSpace(requirement))
            {
                continue;
            }

            if (canonicalValues.Any(value => !string.Equals(value, requirement, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("TestScript contains conflicting requiresCapability values.");
            }

            if (extensions is null)
            {
                extensions = [];
                test["extension"] = extensions;
            }

            if (canonicalExtensions.Length == 0)
            {
                extensions.Add(new JsonObject
                {
                    ["url"] = RequiresCapabilityUrl,
                    ["valueString"] = requirement,
                });
            }

            test.Remove("requiresCapability");
            changed = true;
        }

        return changed ? root.ToJsonString() : content;
    }
}
