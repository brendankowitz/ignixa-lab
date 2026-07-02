using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ignixa.Lab.Functions.Models;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Parses a FHIR <c>CapabilityStatement</c> JSON document into the fixed
/// resource/interaction shape the frontend renders. Isolated from HTTP so the
/// JSON-to-DTO mapping can be unit tested without a live server.
/// </summary>
public static class CapabilityStatementParser
{
    /// <summary>
    /// Maps raw FHIR interaction codes (<c>rest[].resource[].interaction[].code</c>)
    /// onto the fixed UI column set. Codes with no mapping (e.g. <c>capabilities</c>,
    /// <c>transaction</c>, <c>batch</c>, <c>history-system</c>) are ignored.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> InteractionColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["read"] = "read",
            ["vread"] = "vread",
            ["create"] = "create",
            ["update"] = "update",
            ["patch"] = "patch",
            ["delete"] = "delete",
            ["search-type"] = "search",
            ["history-instance"] = "history",
            ["history-type"] = "history",
        };

    /// <summary>
    /// Parses <paramref name="capabilityStatementJson"/> and, on success,
    /// returns one <see cref="CapabilityResourceDto"/> per declared
    /// <c>rest[].resource[]</c> entry with a <c>type</c>. A missing or empty
    /// <c>rest</c> array is not an error; it yields an empty resource list.
    /// On failure (unparseable JSON) returns <see langword="false"/> with a
    /// human-readable <paramref name="error"/>.
    /// </summary>
    public static bool TryParse(
        string? capabilityStatementJson,
        [NotNullWhen(true)] out IReadOnlyList<CapabilityResourceDto>? resources,
        [NotNullWhen(false)] out string? error)
    {
        resources = null;
        error = null;

        if (string.IsNullOrWhiteSpace(capabilityStatementJson))
        {
            error = "The server returned an empty CapabilityStatement.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(capabilityStatementJson);
        }
        catch (JsonException ex)
        {
            error = $"The server's CapabilityStatement could not be parsed as JSON: {ex.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The server's CapabilityStatement must be a JSON object.";
                return false;
            }

            resources = ParseResources(root);
            return true;
        }
    }

    private static IReadOnlyList<CapabilityResourceDto> ParseResources(JsonElement root)
    {
        var entries = new List<CapabilityResourceDto>();

        if (!root.TryGetProperty("rest", out var restElement) || restElement.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var rest in restElement.EnumerateArray())
        {
            if (rest.ValueKind != JsonValueKind.Object ||
                !rest.TryGetProperty("resource", out var resourceElement) ||
                resourceElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var resource in resourceElement.EnumerateArray())
            {
                var entry = ParseResource(resource);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    private static CapabilityResourceDto? ParseResource(JsonElement resource)
    {
        if (resource.ValueKind != JsonValueKind.Object ||
            !resource.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var type = typeElement.GetString();
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return new CapabilityResourceDto(type, ParseInteractions(resource));
    }

    private static IReadOnlyList<string> ParseInteractions(JsonElement resource)
    {
        if (!resource.TryGetProperty("interaction", out var interactionElement) ||
            interactionElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var columns = new List<string>();
        foreach (var interaction in interactionElement.EnumerateArray())
        {
            if (interaction.ValueKind != JsonValueKind.Object ||
                !interaction.TryGetProperty("code", out var codeElement) ||
                codeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var code = codeElement.GetString();
            if (code is null || !InteractionColumns.TryGetValue(code, out var column))
            {
                continue;
            }

            if (!columns.Contains(column, StringComparer.Ordinal))
            {
                columns.Add(column);
            }
        }

        return columns;
    }
}
