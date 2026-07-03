using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// The structured outcome of running an FML transform, before it is formatted
/// into a FHIR Parameters response by FmlResultFormatter.
/// </summary>
public sealed class FmlResult
{
    /// <summary>
    /// The original request that produced this result.
    /// </summary>
    public required FmlRequest Request { get; init; }

    /// <summary>
    /// Top-level failure (parse error, unresolvable "uses" reference, unhandled
    /// exception) - null on success, even if <see cref="Errors"/> has entries.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Extra diagnostic context for <see cref="Error"/> (e.g. the map or resource text).
    /// </summary>
    public string? ErrorDiagnostics { get; init; }

    /// <summary>
    /// The transformed target resource. Present whenever the map executed,
    /// even if <see cref="Errors"/> is non-empty (Lenient error mode).
    /// </summary>
    public ResourceJsonNode? Output { get; init; }

    /// <summary>
    /// Log lines captured from the map's <c>log(...)</c> clauses, in execution order.
    /// </summary>
    public IReadOnlyList<string> LogLines { get; init; } = [];

    /// <summary>
    /// Per-rule execution errors collected in <see cref="ErrorMode.Lenient"/> mode.
    /// </summary>
    public IReadOnlyList<ExecutionError> Errors { get; init; } = [];

    /// <summary>
    /// Whether the request was successful (a top-level failure did not occur;
    /// individual rule errors may still be present in <see cref="Errors"/>).
    /// </summary>
    public bool IsSuccess => Error == null;
}
