using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Represents the input parameters for a FHIR Mapping Language (FML) transform request.
/// </summary>
public sealed class FmlRequest
{
    /// <summary>
    /// The FML source text (a StructureMap definition) to parse and execute.
    /// </summary>
    public required string Map { get; init; }

    /// <summary>
    /// The FHIR resource to transform.
    /// </summary>
    public required ResourceJsonNode Resource { get; init; }
}
