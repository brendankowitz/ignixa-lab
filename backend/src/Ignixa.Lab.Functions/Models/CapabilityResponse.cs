using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Response body for <c>GET /api/capability</c>: the target server's
/// declared capabilities, normalized to the fixed interaction-column set the
/// frontend renders (read, vread, create, update, patch, delete, search,
/// history).
/// </summary>
public sealed record CapabilityResponse(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("fhirVersion")] string FhirVersion,
    [property: JsonPropertyName("resources")] IReadOnlyList<CapabilityResourceDto> Resources);

/// <summary>A single resource type's declared interactions, as parsed from the target's CapabilityStatement.</summary>
public sealed record CapabilityResourceDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("interactions")] IReadOnlyList<string> Interactions);
