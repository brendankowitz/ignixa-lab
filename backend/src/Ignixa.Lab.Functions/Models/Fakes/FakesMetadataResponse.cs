namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class FakesMetadataResponse
{
    public required IReadOnlyList<string> FhirVersions { get; init; }
    public required IReadOnlyList<string> PopulationStates { get; init; }
    public required IReadOnlyList<ScenarioMetadata> Scenarios { get; init; }
    public required IReadOnlyList<string> ResourceTypes { get; init; }
    public required IReadOnlyList<string> ObservationStates { get; init; }
    public required IReadOnlyList<EdgeCaseFamilyMetadata> EdgeCaseFamilies { get; init; }
    /// <summary>City names <see cref="Ignixa.Lab.Functions.Services.Fakes.FakesService"/> can sample realistic demographics (including gender) from via <c>PatientBuilder.FromCity</c>.</summary>
    public required IReadOnlyList<string> PatientCities { get; init; }
}

public sealed class ScenarioMetadata
{
    public required string Id { get; init; }
    public required IReadOnlyList<ScenarioParameterMetadata> Parameters { get; init; }
}

public sealed class ScenarioParameterMetadata
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public object? DefaultValue { get; init; }
}

public sealed class EdgeCaseFamilyMetadata
{
    public required string Family { get; init; }
    public required IReadOnlyList<EdgeCaseCategoryMetadata> Categories { get; init; }
}

public sealed class EdgeCaseCategoryMetadata
{
    public required string Id { get; init; }
    public required string Intent { get; init; }
}
