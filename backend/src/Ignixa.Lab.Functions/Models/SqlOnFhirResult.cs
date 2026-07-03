namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// The structured outcome of running a SQL-on-FHIR ViewDefinition, before it
/// is serialized to a plain JSON array response by SqlOnFhirFunctions.
/// </summary>
public sealed class SqlOnFhirResult
{
    /// <summary>
    /// The original request that produced this result.
    /// </summary>
    public required SqlOnFhirRequest Request { get; init; }

    /// <summary>
    /// Failure message (malformed ViewDefinition, evaluation error) - null on success.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Extra diagnostic context for <see cref="Error"/>.
    /// </summary>
    public string? ErrorDiagnostics { get; init; }

    /// <summary>
    /// The resulting rows, one dictionary per input resource (subject to <see cref="SqlOnFhirRequest.Limit"/>).
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>
    /// Whether the evaluation succeeded.
    /// </summary>
    public bool IsSuccess => Error == null;
}
