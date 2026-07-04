namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class ResourceGenerationRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string ResourceType { get; init; }
    public int Seed { get; init; } = 42;
    public string Density { get; init; } = "Minimal";
    public string? FirstName { get; init; }
    public string? FamilyName { get; init; }
    public string? City { get; init; }
    public string? ObservationState { get; init; }
    public IReadOnlyList<string>? EdgeCaseSelectors { get; init; }
    public bool IncludeInvalid { get; init; }
}
