using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// Captured HTTP response for a step trace. Populated on a best-effort basis
/// depending on what the underlying TestScript engine exposes.
/// </summary>
public sealed record ConformanceHttpResponse(
    [property: JsonPropertyName("statusCode")] int StatusCode,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("bodyParseError")] string? BodyParseError);
