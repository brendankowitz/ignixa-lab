using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// A single phase/action within a test case (setup, test, or teardown step).
/// Mirrors the <c>ConformanceStep</c> shape consumed by the dashboard.
/// </summary>
public sealed record ConformanceStep(
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("request")] ConformanceHttpRequest? Request,
    [property: JsonPropertyName("response")] ConformanceHttpResponse? Response,
    [property: JsonPropertyName("group_id")] string? GroupId = null,
    [property: JsonPropertyName("members")] IReadOnlyList<ConformanceGroupMember>? Members = null);
