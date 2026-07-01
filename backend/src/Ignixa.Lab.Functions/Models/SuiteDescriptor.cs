using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Metadata describing a bundled TestScript suite, returned by
/// <c>GET /api/suites</c> so the SPA can present a selectable catalog.
/// </summary>
public sealed record SuiteDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("fhirVersion")] string FhirVersion,
    [property: JsonPropertyName("file")] string File);
