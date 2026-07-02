using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// Summary of the first failing assertion for a test case, surfaced in the
/// dashboard's failure details panel.
/// </summary>
public sealed record ConformanceError(
    [property: JsonPropertyName("assertion")] string? Assertion,
    [property: JsonPropertyName("received")] string? Received);
