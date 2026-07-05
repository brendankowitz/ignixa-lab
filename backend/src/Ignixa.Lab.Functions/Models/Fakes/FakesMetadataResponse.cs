namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class FakesMetadataResponse
{
    /// <summary>The referenced <c>Ignixa.FhirFakes</c> package version (e.g. "0.5.13"), reflected from the assembly so it can't drift out of sync with a hand-maintained badge.</summary>
    public required string LibraryVersion { get; init; }
    public required IReadOnlyList<string> FhirVersions { get; init; }
    public required IReadOnlyList<string> PopulationStates { get; init; }
    public required IReadOnlyList<ScenarioMetadata> Scenarios { get; init; }
    /// <summary>Valid FHIR resource type names, keyed by lowercase <see cref="FhirVersions"/> entry — resource types genuinely differ between versions (e.g. R6 added ~34 new types and dropped ~20 R4 types), so a single flat list would misrepresent whichever version isn't R4.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceTypesByVersion { get; init; }
    public required IReadOnlyList<string> ObservationStates { get; init; }
    public required IReadOnlyList<EdgeCaseFamilyMetadata> EdgeCaseFamilies { get; init; }
    /// <summary>City names <see cref="Ignixa.Lab.Functions.Services.Fakes.FakesService"/> can sample realistic demographics (including gender) from via <c>PatientBuilder.FromCity</c>.</summary>
    public required IReadOnlyList<string> PatientCities { get; init; }
    /// <summary>Clinical specialty names usable as a Maximum-density generation Theme (see Ignixa.FhirFakes.ClinicalDomain), excluding "Unspecified".</summary>
    public required IReadOnlyList<string> ClinicalDomains { get; init; }
    /// <summary>Discoverable workflow scenario packs (e.g. "DailyAppointmentSchedule"), same shape as <see cref="Scenarios"/>.</summary>
    public required IReadOnlyList<ScenarioMetadata> WorkflowPacks { get; init; }
}

public sealed class ScenarioMetadata
{
    public required string Id { get; init; }
    /// <summary>Free-text grouping label from the library's <c>ScenarioAttribute.Category</c>, or null if unannotated.</summary>
    public string? Category { get; init; }
    /// <summary>Clinical specialty from the library's <c>ScenarioAttribute.Domain</c>, or null if undeclared.</summary>
    public string? Domain { get; init; }
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
