using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// The structured outcome of running an FML transform, before it is formatted
/// into a FHIR Parameters response by FmlResultFormatter. Construct via the
/// <see cref="Success"/> / <see cref="Failure"/> factories: they are the only
/// way to build a complete instance, which keeps the success/failure invariant
/// (exactly one of <see cref="Output"/> / <see cref="Error"/> is meaningful)
/// enforceable rather than a convention.
/// </summary>
public sealed class FmlResult
{
    private FmlResult(FmlRequest request) => Request = request;

    /// <summary>
    /// The original request that produced this result.
    /// </summary>
    public FmlRequest Request { get; }

    /// <summary>
    /// Top-level failure (parse error, unresolvable "uses" reference, unhandled
    /// exception) - null on success, even if <see cref="Errors"/> has entries.
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Extra diagnostic context for <see cref="Error"/> (e.g. the map or resource text).
    /// </summary>
    public string? ErrorDiagnostics { get; private init; }

    /// <summary>
    /// The transformed target resource. Non-null whenever <see cref="IsSuccess"/>
    /// is true, even if <see cref="Errors"/> is non-empty (Lenient error mode).
    /// </summary>
    public ResourceJsonNode? Output { get; private init; }

    /// <summary>
    /// Log lines captured from the map's <c>log(...)</c> clauses, in execution order.
    /// </summary>
    public IReadOnlyList<string> LogLines { get; private init; } = [];

    /// <summary>
    /// Per-rule execution errors collected in <see cref="ErrorMode.Lenient"/> mode.
    /// </summary>
    public IReadOnlyList<ExecutionError> Errors { get; private init; } = [];

    /// <summary>
    /// Whether the request was successful (a top-level failure did not occur;
    /// individual rule errors may still be present in <see cref="Errors"/>).
    /// </summary>
    public bool IsSuccess => Error == null;

    /// <summary>
    /// Builds a successful result. <paramref name="output"/> is the transformed
    /// target resource and is guaranteed non-null on the returned instance.
    /// </summary>
    public static FmlResult Success(
        FmlRequest request,
        ResourceJsonNode output,
        IReadOnlyList<string> logLines,
        IReadOnlyList<ExecutionError> errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(logLines);
        ArgumentNullException.ThrowIfNull(errors);

        return new FmlResult(request) { Output = output, LogLines = logLines, Errors = errors };
    }

    /// <summary>
    /// Builds a failed result carrying a top-level <paramref name="error"/> message.
    /// </summary>
    public static FmlResult Failure(FmlRequest request, string error, string? errorDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(error);

        return new FmlResult(request) { Error = error, ErrorDiagnostics = errorDiagnostics };
    }
}
