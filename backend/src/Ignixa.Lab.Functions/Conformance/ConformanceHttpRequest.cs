using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// Captured HTTP request for a step trace. Populated on a best-effort basis
/// depending on what the underlying TestScript engine exposes.
/// </summary>
public sealed record ConformanceHttpRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("body")] string? Body);
