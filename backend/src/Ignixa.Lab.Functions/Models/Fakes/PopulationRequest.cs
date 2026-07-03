namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class PopulationRequest
{
    /// <summary>Inclusive lower bound for <see cref="Count"/>, enforced by the population endpoint.</summary>
    public const int MinCount = 1;

    /// <summary>Inclusive upper bound for <see cref="Count"/>, enforced by the population endpoint.</summary>
    public const int MaxCount = 100;

    public string FhirVersion { get; init; } = "r4";
    public required string Source { get; init; }
    public int Count { get; init; } = 10;
}
