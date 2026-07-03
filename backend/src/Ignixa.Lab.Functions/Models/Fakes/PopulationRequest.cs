namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class PopulationRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string Source { get; init; }
    public int Count { get; init; } = 10;
}
