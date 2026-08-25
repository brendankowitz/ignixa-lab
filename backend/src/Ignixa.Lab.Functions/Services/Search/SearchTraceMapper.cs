using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>Maps Search.Sql diagnostics into the serializable <see cref="SearchTraceResponse"/>. Thin by
/// design: <see cref="IrProjector"/>, <see cref="SyntaxNode"/>, and
/// <see cref="Ignixa.Search.Sql.Ast.PlanExplainer"/> already do the flattening and the structural
/// discrimination (kind, canonical label, referenced CTEs), so this only translates shapes and projects the
/// non-serializable pieces.</summary>
public static class SearchTraceMapper
{
    private const string CompilationFailureMessage = "The search compiler could not process this query.";
    private const string ParameterFailureMessage = "The search parameter could not be compiled.";
    private const string PlanTraceFailureMessage = "The search plan explanation is unavailable.";

    /// <param name="fhirVersion">The version actually compiled against (<see cref="SearchEngineFactory.Resolve"/>),
    /// not the caller's raw route value — see <see cref="SearchTraceResponse.FhirVersion"/>.</param>
    /// <param name="requestedResourceType">The type passed to the compiler. Used as the response resource type
    /// because the route value is already validated upstream and the bench contract keeps that echo stable.</param>
    public static SearchTraceResponse ToResponse(CompiledSearch compiled, string fhirVersion, string requestedResourceType)
    {
        ArgumentNullException.ThrowIfNull(compiled);

        return ToResponse(
            fhirVersion,
            requestedResourceType,
            compiled.Diagnostics,
            compiled.Sql,
            compiled.Parameters,
            failure: null);
    }

    /// <param name="fhirVersion">The version actually compiled against (<see cref="SearchEngineFactory.Resolve"/>),
    /// not the caller's raw route value — see <see cref="SearchTraceResponse.FhirVersion"/>.</param>
    /// <param name="requestedResourceType">The type passed to the compiler.</param>
    public static SearchTraceResponse ToResponse(SearchCompilationFailure failure, string fhirVersion, string requestedResourceType)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return ToResponse(
            fhirVersion,
            requestedResourceType,
            failure.Diagnostics,
            sql: null,
            sqlParameters: [],
            failure: ToFailureDto(failure, "Compilation"));
    }

    private static SearchTraceResponse ToResponse(
        string fhirVersion,
        string requestedResourceType,
        SearchCompilationDiagnostics? diagnostics,
        string? sql,
        IReadOnlyList<EmittedSqlParameter> sqlParameters,
        TraceFailureDto? failure)
    {
        var traceFailure = failure ?? (diagnostics?.PlanTraceFailure is not null
            ? ToFailureDto(diagnostics.PlanTraceFailure, "PlanTrace")
            : null);

        return new SearchTraceResponse(
            fhirVersion,
            requestedResourceType,
            (diagnostics?.Parameters ?? []).Select(ToParameterDto).ToList(),
            diagnostics?.PlanTrace is null ? null : ToPlanDto(diagnostics.PlanTrace),
            sql is null ? null : new EmittedSqlDto(
                sql,
                sqlParameters.Select(p => new SqlParameterDto(p.Name, p.Value?.ToString())).ToList(),
                (diagnostics?.SqlTextRanges ?? []).Select(ToSqlTextRangeDto).ToList()),
            (diagnostics?.Implicit ?? []).Select(p => new ImplicitParameterDto(p.Name, p.Value, p.Reason)).ToList(),
            traceFailure);
    }

    private static ParameterTraceDto ToParameterDto(ParameterTrace p)
    {
        var ir = DescribeIr(p.Ir, out var irUnavailableReason);

        return new ParameterTraceDto(
            p.Ordinal,
            p.Key,
            p.Value,
            p.KeySyntax is null ? null : ToSyntaxDto(p.KeySyntax),
            p.ValueSyntax is null ? null : ToSyntaxDto(p.ValueSyntax),
            ir,
            p.DataType?.ToString(),
            ToOutcomeDto(p.Outcome),
            irUnavailableReason);
    }

    // IrProjector.TryDescribe degrades to an empty IR list for a node kind it does not model, rather than
    // the throwing Describe -- one exotic parameter's IR should not 500 the whole bench request when the
    // other columns and parameters still render fine.
    //
    // The degradation is deliberate; discarding the reason was not. An empty list renders identically whether
    // the parameter genuinely has no IR or the projector could not describe it, which for a provenance tool is
    // the worst available failure: a blank pane asserting "nothing here" when the truth is "I could not say".
    // So the reason travels with the empty list and the UI prints it.
    private static IReadOnlyList<IrRowDto> DescribeIr(Expression? ir, out string? unavailableReason)
    {
        unavailableReason = null;

        if (ir is null)
        {
            return [];
        }

        if (!IrProjector.TryDescribe(ir, out var rows, out var error))
        {
            unavailableReason = string.IsNullOrWhiteSpace(error)
                ? "The IR projector could not describe this expression."
                : error;
            return [];
        }

        return rows.Select(r => new IrRowDto(r.Kind, r.Text, r.Depth)).ToList();
    }

    private static SyntaxNodeDto ToSyntaxDto(SyntaxNode node) => new(
        node.Kind,
        ToSpanDto(node.Span),
        node.Children.Select(ToSyntaxDto).ToList());

    private static ParameterOutcomeDto ToOutcomeDto(ParameterOutcome outcome) => outcome switch
    {
        ParameterOutcome.Compiled => new ParameterOutcomeDto("Compiled", null, null, null),
        // The query is well-formed and still runs -- it's just structurally incapable of returning a row
        // for this parameter (e.g. an unknown token system or quantity code), which is otherwise visible
        // only as a "1 = 0" buried in the emitted SQL.
        ParameterOutcome.KnownMiss knownMiss => new ParameterOutcomeDto("KnownMiss", knownMiss.Reason, null, ToSpanDto(knownMiss.Span)),
        ParameterOutcome.Ignored ignored => new ParameterOutcomeDto("Ignored", ignored.Reason, null, ToSpanDto(ignored.Span)),
        ParameterOutcome.Failed failed => new ParameterOutcomeDto("Failed", ParameterFailureMessage, failed.Stage.ToString(), ToSpanDto(failed.Span)),
        _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
    };

    private static QueryPlanDto ToPlanDto(QueryPlanTrace plan) => new(
        plan.Explain,
        plan.Rows.Select(r => new PlanExplainRowDto(r.Label, r.CanonicalLabel, r.Kind, r.Body, r.ReferencedCteIndexes)).ToList(),
        plan.Ctes.Select(c => new CteProvenanceDto(c.CteIndex, c.ParameterOrdinal, c.ContributingOrdinals, ToSpanDto(c.Span))).ToList());

    private static TraceFailureDto ToFailureDto(SearchCompilationFailure failure, string scope) =>
        new(
            scope,
            failure.Stage.ToString(),
            scope == "PlanTrace" ? PlanTraceFailureMessage : CompilationFailureMessage,
            failure.ParameterCode,
            ToSpanDto(failure.Span));

    private static SpanDto ToSpanDto(SourceSpan span) => new(span.Origin.ToString(), span.Start, span.Length);

    private static SpanDto? ToSpanDto(SourceSpan? span) => span is { } s ? ToSpanDto(s) : null;

    private static SqlTextRangeDto ToSqlTextRangeDto(SqlTextRange range) => new(range.Label, range.Kind, range.Start, range.Length);
}
