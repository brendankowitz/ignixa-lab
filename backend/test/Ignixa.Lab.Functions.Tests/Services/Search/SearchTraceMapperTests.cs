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
        response.Sql!.Ranges.Single().Label.Should().Be("cte0");
        response.Implicit.Single().Name.Should().Be("_count");
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
