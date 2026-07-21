using FluentAssertions;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class SearchTraceMapperTests
{
    private static ParameterTrace Trace(int ordinal, string key, string value, ParameterOutcome outcome) =>
        new(ordinal, key, value, KeySyntax: null, ValueSyntax: null, Ir: null, outcome);

    [Fact]
    public void ToResponse_CompiledOutcome_MapsKindOnly()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())],
            Plan: null, Sql: null);

        var response = SearchTraceMapper.ToResponse(trace);

        response.ResourceType.Should().Be("Patient");
        var outcome = response.Parameters.Single().Outcome;
        outcome.Kind.Should().Be("Compiled");
        outcome.Reason.Should().BeNull();
        outcome.Stage.Should().BeNull();
    }

    [Fact]
    public void ToResponse_IgnoredOutcome_CarriesReasonAndSpan()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "birthdate:exact", "2000", new ParameterOutcome.Ignored("modifier not allowed on date", new SourceSpan(SourceOrigin.Key, 10, 5)))],
            Plan: null, Sql: null);

        var outcome = SearchTraceMapper.ToResponse(trace).Parameters.Single().Outcome;

        outcome.Kind.Should().Be("Ignored");
        outcome.Reason.Should().Be("modifier not allowed on date");
        outcome.Span!.Origin.Should().Be("Key");
        outcome.Span.Start.Should().Be(10);
        outcome.Span.Length.Should().Be(5);
    }

    [Fact]
    public void ToResponse_FailedOutcome_CarriesStageAndMessage()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "unknown", "x", new ParameterOutcome.Failed(TraceStage.Resolve, "could not be resolved", new SourceSpan(SourceOrigin.Value, 0, 1)))],
            Plan: null, Sql: null);

        var outcome = SearchTraceMapper.ToResponse(trace).Parameters.Single().Outcome;

        outcome.Kind.Should().Be("Failed");
        outcome.Stage.Should().Be("Resolve");
        outcome.Reason.Should().Be("could not be resolved");
    }

    [Fact]
    public void ToResponse_PreservesCteParameterOrdinalUnchanged()
    {
        // The frontend joins plan rows / SQL ranges to parameters through CteProvenance.ParameterOrdinal —
        // this asserts that join key survives the mapping so the UI's lineage highlighting is trustworthy.
        var plan = new QueryPlanTrace(
            Explain: "root = ...",
            Ctes: [new CteProvenance(0, ParameterOrdinal: 7, new SourceSpan(SourceOrigin.Value, 0, 5))],
            Rows: [new PlanExplainRow("cte0", "ParamSource name")]);
        var sql = new EmittedSqlTrace("SELECT 1", [new SqlTextRange("cte0", 0, 6)]);
        var trace = new SearchTrace("Patient", [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())], plan, sql)
        {
            Implicit = [new ImplicitParameter("_count", "10", "server default")],
        };

        var response = SearchTraceMapper.ToResponse(trace);

        response.Plan!.Ctes.Single().ParameterOrdinal.Should().Be(7);
        response.Plan.Rows.Single().Label.Should().Be("cte0");
        response.Plan.Rows.Single().CteIndex.Should().Be(0);
        response.Sql!.Ranges.Single().Label.Should().Be("cte0");
        response.Implicit.Single().Name.Should().Be("_count");
    }

    [Fact]
    public void ToResponse_RowPositionMapsToCteIndex_EvenForTheRelabelledRootRow()
    {
        // PlanExplainer relabels whichever CTE is the plan's match/output as "root" instead of "cte{i}" --
        // CteIndex must still reflect its real position (1 here, the second CTE), not be null or 0, so the
        // frontend can look it up in Ctes and inherit an ordinal from what it references (a structural
        // ChainJoin has no ParameterOrdinal of its own -- see the classifier/lineage design notes).
        var plan = new QueryPlanTrace(
            Explain: "cte0 = ...\nroot = ...",
            Ctes:
            [
                new CteProvenance(0, ParameterOrdinal: 0, Span: null),
                new CteProvenance(1, ParameterOrdinal: null, Span: null),
            ],
            Rows:
            [
                new PlanExplainRow("cte0", "StringSearchParam[2,2]  Text LIKE @p0"),
                new PlanExplainRow("root", "ChainJoin(cte0, ref=1, inner=2, output=[1], Forward)"),
                new PlanExplainRow("sort", "SortSpec([], Valued)"),
            ]);
        var trace = new SearchTrace("Patient", [Trace(0, "general-practitioner.name", "Smith", new ParameterOutcome.Compiled())], plan, Sql: null);

        var rows = SearchTraceMapper.ToResponse(trace).Plan!.Rows;

        rows[0].CteIndex.Should().Be(0);
        rows[1].CteIndex.Should().Be(1, "the 'root' row is still the second CTE positionally");
        rows[2].CteIndex.Should().BeNull("sort is not a CTE row at all");
    }

    [Fact]
    public void ToResponse_NullPlanAndSql_MapToNull()
    {
        var trace = new SearchTrace("Patient", [], Plan: null, Sql: null)
        {
            Failure = new TraceFailure(TraceStage.Resolve, "Search parameters could not be resolved: 'bogus'.", null),
        };

        var response = SearchTraceMapper.ToResponse(trace);

        response.Plan.Should().BeNull();
        response.Sql.Should().BeNull();
        response.Failure!.Stage.Should().Be("Resolve");
        response.Implicit.Should().BeEmpty();
    }
}
