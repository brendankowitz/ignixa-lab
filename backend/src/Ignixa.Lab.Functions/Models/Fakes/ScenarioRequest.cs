using System.Text.Json;

namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class ScenarioRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string ScenarioId { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }
    public string? Tag { get; init; }
    public bool ResolvedReferences { get; init; }
}
