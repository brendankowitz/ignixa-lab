using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>Maps a <see cref="SearchTrace"/> to the serializable <see cref="SearchTraceResponse"/>. Thin by
/// design: <see cref="IrProjector"/>, <see cref="SyntaxNode"/>, and <see cref="Ignixa.Search.Sql.Ast.PlanExplainer"/>
/// already do the flattening and the structural discrimination (kind, canonical label, referenced CTEs), so
/// this only translates shapes and projects the two non-serializable pieces.</summary>
public static class SearchTraceMapper
{
    /// <param name="fhirVersion">The version actually compiled against (<see cref="SearchEngineFactory.Resolve"/>),
    /// not the caller's raw route value — see <see cref="SearchTraceResponse.FhirVersion"/>.</param>
    /// <param name="requestedResourceType">The type passed to <c>SearchCompiler.CompileAsync</c>. Only used
    /// if the trace comes back without one, which the <c>CompileAsync</c> entry point this app uses should
    /// never do (it null-checks its <c>resourceType</c> and echoes it unmodified; only the
    /// <c>CompileFromOptionsAsync</c> overload normalizes empty to null to mark a system-level search). Kept
    /// as a defensive echo of an already-validated value rather than emitting a null resource type.</param>
    public static SearchTraceResponse ToResponse(SearchTrace trace, string fhirVersion, string requestedResourceType)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return new SearchTraceResponse(
            fhirVersion,
            trace.ResourceType ?? requestedResourceType,
            trace.Parameters.Select(ToParameterDto).ToList(),
            trace.Plan is null ? null : ToPlanDto(trace.Plan),
            trace.Sql is null ? null : new EmittedSqlDto(
                trace.Sql.Sql,
                trace.Sql.Ranges.Select(r => new SqlTextRangeDto(r.Label, r.Kind, r.Start, r.Length)).ToList()),
            trace.Implicit.Select(i => new ImplicitParameterDto(i.Name, i.Value, i.Reason)).ToList(),
            trace.Failure is null ? null : new TraceFailureDto(trace.Failure.Stage.ToString(), trace.Failure.Message, ToSpanDto(trace.Failure.Span)));
    }

    private static ParameterTraceDto ToParameterDto(ParameterTrace p) => new(
        p.Ordinal,
        p.Key,
        p.Value,
        p.KeySyntax is null ? null : ToSyntaxDto(p.KeySyntax),
        p.ValueSyntax is null ? null : ToSyntaxDto(p.ValueSyntax),
        DescribeIr(p.Ir),
        p.DataType?.ToString(),
        ToOutcomeDto(p.Outcome));

    // IrProjector.TryDescribe degrades to an empty IR list for a node kind it does not model, rather than
    // the throwing Describe -- one exotic parameter's IR should not 500 the whole bench request when the
    // other columns and parameters still render fine.
    private static IReadOnlyList<IrRowDto> DescribeIr(Expression? ir)
    {
        if (ir is null || !IrProjector.TryDescribe(ir, out var rows, out _))
        {
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
        ParameterOutcome.Failed failed => new ParameterOutcomeDto("Failed", failed.Message, failed.Stage.ToString(), ToSpanDto(failed.Span)),
        _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
    };

    private static QueryPlanDto ToPlanDto(QueryPlanTrace plan) => new(
        plan.Explain,
        plan.Rows.Select(r => new PlanExplainRowDto(r.Label, r.CanonicalLabel, r.Kind, r.Body, r.ReferencedCteIndexes)).ToList(),
        plan.Ctes.Select(c => new CteProvenanceDto(c.CteIndex, c.ParameterOrdinal, c.ContributingOrdinals, ToSpanDto(c.Span))).ToList());

    private static SpanDto ToSpanDto(SourceSpan span) => new(span.Origin.ToString(), span.Start, span.Length);

    private static SpanDto? ToSpanDto(SourceSpan? span) => span is { } s ? ToSpanDto(s) : null;
}
