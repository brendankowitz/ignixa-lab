namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// The structured outcome of running a SQL-on-FHIR ViewDefinition, before it
/// is serialized to a plain JSON array response by SqlOnFhirFunctions. Construct
/// via the <see cref="Success"/> / <see cref="Failure"/> factories: they are the
/// only way to build a complete instance, which keeps the success/failure
/// invariant (exactly one of <see cref="Rows"/> / <see cref="Error"/> is
/// meaningful) enforceable rather than a convention.
/// </summary>
public sealed class SqlOnFhirResult
{
    private SqlOnFhirResult(SqlOnFhirRequest request) => Request = request;

    /// <summary>
    /// The original request that produced this result.
    /// </summary>
    public SqlOnFhirRequest Request { get; }

    /// <summary>
    /// Failure message (malformed ViewDefinition, evaluation error) - null on success.
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Extra diagnostic context for <see cref="Error"/>.
    /// </summary>
    public string? ErrorDiagnostics { get; private init; }

    /// <summary>
    /// The resulting rows, one dictionary per input resource (subject to <see cref="SqlOnFhirRequest.Limit"/>).
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; private init; } = [];

    /// <summary>
    /// Whether the evaluation succeeded.
    /// </summary>
    public bool IsSuccess => Error == null;

    /// <summary>
    /// Builds a successful result carrying the evaluated <paramref name="rows"/>.
    /// </summary>
    public static SqlOnFhirResult Success(SqlOnFhirRequest request, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);

        return new SqlOnFhirResult(request) { Rows = rows };
    }

    /// <summary>
    /// Builds a failed result carrying an <paramref name="error"/> message.
    /// </summary>
    public static SqlOnFhirResult Failure(SqlOnFhirRequest request, string error, string? errorDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(error);

        return new SqlOnFhirResult(request) { Error = error, ErrorDiagnostics = errorDiagnostics };
    }
}
