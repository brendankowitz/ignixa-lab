using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Model;

namespace Ignixa.Lab.Functions.Execution;

internal static class RunScopedDefinitionPreparer
{
    private const string RunIdVariableName = "runId";
    private const string RunIdToken = "${runId}";

    public static TestScriptDefinition Prepare(TestScriptDefinition definition)
    {
        var runIdVariable = definition.Variables.FirstOrDefault(variable => variable.Name == RunIdVariableName);
        if (runIdVariable is null)
        {
            return definition;
        }

        var runId = Guid.NewGuid().ToString("N");
        var variables = definition.Variables
            .Select(variable => ReferenceEquals(variable, runIdVariable)
                ? variable with { DefaultValue = runId }
                : variable)
            .ToArray();
        var fixtures = definition.Fixtures
            .Select(fixture => fixture.Resource is null
                ? fixture
                : fixture with { Resource = ExpandFixture(fixture.Resource, runId) })
            .ToArray();

        return definition with
        {
            Variables = variables,
            Fixtures = fixtures,
        };
    }

    private static ResourceJsonNode ExpandFixture(ResourceJsonNode resource, string runId)
    {
        var json = JsonNode.Parse(((IMutableJsonNode)resource).MutableNode.ToJsonString())!;
        ExpandStringValues(json, runId);
        return ResourceJsonNode.Parse(json.ToJsonString());
    }

    private static void ExpandStringValues(JsonNode node, string runId)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToArray())
                {
                    if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        obj[property.Key] = text.Replace(RunIdToken, runId, StringComparison.Ordinal);
                    }
                    else if (property.Value is not null)
                    {
                        ExpandStringValues(property.Value, runId);
                    }
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        array[index] = text.Replace(RunIdToken, runId, StringComparison.Ordinal);
                    }
                    else if (array[index] is not null)
                    {
                        ExpandStringValues(array[index]!, runId);
                    }
                }

                break;
        }
    }
}
