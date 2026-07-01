using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// Top-level conformance report produced by a run against a target FHIR server.
/// The JSON shape is intentionally identical to the <c>conformance/latest.json</c>
/// artifact used by the ignixa-fhir conformance dashboard so that reports are
/// interchangeable between the two projects.
/// </summary>
public sealed record ConformanceReport(
    [property: JsonPropertyName("impl")] string Impl,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("fhirVersion")] string FhirVersion,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("results")] IReadOnlyList<ConformanceResult> Results);
