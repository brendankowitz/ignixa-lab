using FluentAssertions;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class SearchTraceMapperTests
{
    private static ParameterTrace Trace(int ordinal, string key, string value, ParameterOutcome outcome) =>
        new(ordinal, key, keySyntax: null, value, valueSyntax: null, ir: null, outcome, dataType: null);

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
    public void ToResponse_PreservesCteParameterOrdinalAndKindData()
    {
        // The frontend joins plan rows / SQL ranges to parameters through CteProvenance.ParameterOrdinal,
        // and rows/ranges to each other through CanonicalLabel/Kind -- this asserts those all survive the
        // mapping unchanged so the UI's lineage highlighting is trustworthy.
        var plan = new QueryPlanTrace(
            Explain: "root = ...",
            Ctes: [new CteProvenance(0, parameterOrdinal: 7, new SourceSpan(SourceOrigin.Value, 0, 5))],
            Rows: [new PlanExplainRow("root", "cte0", PlanRowKind.ParamSource, "ParamSource name", referencedCteIndexes: [])]);
        var sql = new EmittedSqlTrace("SELECT 1", [new SqlTextRange("cte0", SqlRangeKind.Cte, 0, 6)]);
        var trace = new SearchTrace("Patient", [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())], plan, sql)
        {
            Implicit = [new ImplicitParameter("_count", "10", "server default")],
        };

        var response = SearchTraceMapper.ToResponse(trace);

        response.Plan!.Ctes.Single().ParameterOrdinal.Should().Be(7);
        response.Plan.Ctes.Single().ContributingOrdinals.Should().Equal(7);
        var row = response.Plan.Rows.Single();
        row.Label.Should().Be("root");
        row.CanonicalLabel.Should().Be("cte0");
        row.Kind.Should().Be(PlanRowKind.ParamSource);
        var range = response.Sql!.Ranges.Single();
        range.Label.Should().Be("cte0");
        range.Kind.Should().Be(SqlRangeKind.Cte);
        response.Implicit.Single().Name.Should().Be("_count");
    }

    [Fact]
    public void ToResponse_ChainJoinRow_CarriesReferencedCteIndexesAndContributingOrdinals()
    {
        // A structural ChainJoin row has no ParameterOrdinal of its own, but composes cte0 -- the frontend
        // needs both ReferencedCteIndexes (to nest it under the CTE it joins) and the closed-over
        // ContributingOrdinals (to still highlight it alongside cte0's owning parameter).
        var plan = new QueryPlanTrace(
            Explain: "cte0 = ...\nroot = ...",
            Ctes:
            [
                new CteProvenance(0, parameterOrdinal: 0, span: null),
                new CteProvenance(1, parameterOrdinal: null, span: null, contributingOrdinals: [0]),
            ],
            Rows:
            [
                new PlanExplainRow("cte0", "cte0", PlanRowKind.ParamSource, "StringSearchParam[2,2]  Text LIKE @p0", referencedCteIndexes: []),
                new PlanExplainRow("root", "cte1", PlanRowKind.ChainJoin, "ChainJoin(cte0, ref=1, inner=2, output=[1], Forward)", referencedCteIndexes: [0]),
                new PlanExplainRow("sort", "sort", PlanRowKind.SortSpec, "SortSpec([], Valued)", referencedCteIndexes: []),
            ]);
        var trace = new SearchTrace("Patient", [Trace(0, "general-practitioner.name", "Smith", new ParameterOutcome.Compiled())], plan, Sql: null);

        var response = SearchTraceMapper.ToResponse(trace);

        response.Plan!.Rows[1].Kind.Should().Be(PlanRowKind.ChainJoin);
        response.Plan.Rows[1].ReferencedCteIndexes.Should().Equal(0);
        response.Plan.Ctes[1].ParameterOrdinal.Should().BeNull();
        response.Plan.Ctes[1].ContributingOrdinals.Should().Equal(0);
        response.Plan.Rows[2].Kind.Should().Be(PlanRowKind.SortSpec);
        response.Plan.Rows[2].ReferencedCteIndexes.Should().BeEmpty();
    }

    [Fact]
    public void ToResponse_DataType_MapsFromParameterTraceDirectly()
    {
        // ParameterTrace.DataType is now resolved by the library at parse time -- this is a straight
        // passthrough, no expression-tree walking left on this side.
        var trace = new ParameterTrace(
            0, "name", keySyntax: null, "Smith", valueSyntax: null, ir: null,
            new ParameterOutcome.Compiled(), dataType: SearchParamType.String);
        var response = SearchTraceMapper.ToResponse(new SearchTrace("Patient", [trace], Plan: null, Sql: null));

        response.Parameters.Single().DataType.Should().Be("String");
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
