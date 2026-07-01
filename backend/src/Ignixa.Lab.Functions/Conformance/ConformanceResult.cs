using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// Result of executing a single TestScript (test case) within a suite.
/// </summary>
public sealed record ConformanceResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("suite")] string Suite,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("error")] ConformanceError? Error,
    [property: JsonPropertyName("steps")] IReadOnlyList<ConformanceStep> Steps);
