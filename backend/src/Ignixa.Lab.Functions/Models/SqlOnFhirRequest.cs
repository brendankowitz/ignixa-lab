using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Represents the input parameters for a SQL-on-FHIR ViewDefinition run request.
/// </summary>
public sealed class SqlOnFhirRequest
{
    /// <summary>
    /// The inline ViewDefinition resource describing the tabular projection to run.
    /// </summary>
    public required ResourceJsonNode ViewResource { get; init; }

    /// <summary>
    /// The FHIR resources to run the view against.
    /// </summary>
    public required IReadOnlyList<ResourceJsonNode> Resources { get; init; }

    /// <summary>
    /// Optional cap on the number of returned rows (the "_limit" parameter).
    /// </summary>
    public int? Limit { get; init; }
}
