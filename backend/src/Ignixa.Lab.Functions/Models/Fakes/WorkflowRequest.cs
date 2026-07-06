using System.Text.Json;

namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class WorkflowRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string PackId { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }
    public int? Seed { get; init; }
    public string? Tag { get; init; }
    public bool ResolvedReferences { get; init; }
}
