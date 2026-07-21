using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>Maps a <see cref="SearchTrace"/> to the serializable <see cref="SearchTraceResponse"/>. Thin by
/// design: <see cref="IrProjector"/> and <see cref="SyntaxNode"/> already do the flattening, so this only
/// translates shapes and projects the two non-serializable pieces.</summary>
public static class SearchTraceMapper
{
    public static SearchTraceResponse ToResponse(SearchTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return new SearchTraceResponse(
            trace.ResourceType,
            trace.Parameters.Select(ToParameterDto).ToList(),
            trace.Plan is null ? null : ToPlanDto(trace.Plan),
            trace.Sql is null ? null : new EmittedSqlDto(
                trace.Sql.Sql,
                trace.Sql.Ranges.Select(r => new SqlTextRangeDto(r.Label, r.Start, r.Length)).ToList()),
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
        FindDataType(p.Ir),
        ToOutcomeDto(p.Outcome));

    /// <summary>Finds the parameter's own declared <c>SearchParamType</c> by walking to the first
    /// search-parameter-bearing node — the same four node kinds <c>SearchCompiler.ParametersOf</c> (in
    /// <c>Ignixa.Search.Sql.Tracing</c>) recognizes as naming a search parameter, checked at the OUTERMOST
    /// level first so a composite reports its own "Composite" type rather than one component's, and a
    /// union/AND wrapper (comma-separated OR values, or a `:not`) recurses to the first branch that has
    /// one — every branch of a union for the same parameter key shares the same type by construction.</summary>
    private static string? FindDataType(Expression? ir) => ir switch
    {
        null => null,
        SearchParameterExpression sp => sp.Parameter.Type.ToString(),
        SearchParameterPredicateExpression p => p.Parameter.Type.ToString(),
        ChainedExpression c => c.ReferenceSearchParameter.Type.ToString(),
        MissingSearchParameterExpression m => m.Parameter.Type.ToString(),
        CompositeComponentExpression cc => cc.ComponentSearchParameter.Type.ToString(),
        MultiaryExpression m => m.Expressions.Select(FindDataType).FirstOrDefault(t => t is not null),
        UnionExpression u => u.Expressions.Select(FindDataType).FirstOrDefault(t => t is not null),
        NotExpression n => FindDataType(n.Expression),
        _ => null,
    };

    // IrProjector.Describe is documented to throw NotSupportedException loudly on a node kind it does not
    // model. In a bench that would turn one exotic parameter into a 500 for the whole request, so degrade to
    // an empty IR list for that parameter instead — the other columns and parameters still render.
    private static IReadOnlyList<IrRowDto> DescribeIr(Expression? ir)
    {
        if (ir is null)
        {
            return [];
        }

        try
        {
            return IrProjector.Describe(ir).Select(r => new IrRowDto(r.Kind, r.Text, r.Depth)).ToList();
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private static SyntaxNodeDto ToSyntaxDto(SyntaxNode node) => new(
        node.Kind,
        ToSpanDto(node.Span),
        node.Children.Select(ToSyntaxDto).ToList());

    private static ParameterOutcomeDto ToOutcomeDto(ParameterOutcome outcome) => outcome switch
    {
        ParameterOutcome.Compiled => new ParameterOutcomeDto("Compiled", null, null, null),
        ParameterOutcome.Ignored ignored => new ParameterOutcomeDto("Ignored", ignored.Reason, null, ToSpanDto(ignored.Span)),
        ParameterOutcome.Failed failed => new ParameterOutcomeDto("Failed", failed.Message, failed.Stage.ToString(), ToSpanDto(failed.Span)),
        _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
    };

    // PlanExplainer.Describe emits exactly one row per CTE, in cteIndex order (0..Ctes.Count-1), before
    // appending any non-CTE rows (inc{i}/sort/page/countOnly) -- so a row's position IS its cteIndex for
    // every row within that prefix, regardless of whether PlanExplainer labelled it "cte{i}" or "root".
    private static QueryPlanDto ToPlanDto(QueryPlanTrace plan) => new(
        plan.Explain,
        plan.Rows.Select((r, i) => new PlanExplainRowDto(
            r.Label,
            PlanRowKindClassifier.Classify(r.Label, r.Body),
            i < plan.Ctes.Count ? i : null,
            r.Body)).ToList(),
        plan.Ctes.Select(c => new CteProvenanceDto(c.CteIndex, c.ParameterOrdinal, ToSpanDto(c.Span))).ToList());

    private static SpanDto ToSpanDto(SourceSpan span) => new(span.Origin.ToString(), span.Start, span.Length);

    private static SpanDto? ToSpanDto(SourceSpan? span) => span is { } s ? ToSpanDto(s) : null;
}
