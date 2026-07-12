using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// One member's outcome within a grouped (<c>assertionAnyOfGroup</c>) assertion step —
/// mirrors <see cref="Ignixa.TestScript.Reporting.AssertionGroupMemberResult"/> for the
/// dashboard's JSON contract.
/// </summary>
public sealed record ConformanceGroupMember(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("message")] string? Message);
