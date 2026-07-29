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

    /// <summary>An expression node <c>IrProjector</c> has no case for, so <c>TryDescribe</c> declines it. Stands
    /// in for the real thing this guards: a future library expression type the projector does not yet model.</summary>
    private sealed class UndescribableExpression : Expression
    {
        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context) =>
            throw new NotSupportedException("No visitor case for this node.");

        public override string ToString() => "Undescribable()";

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode) => hashCode.Add(nameof(UndescribableExpression));

        public override bool ValueInsensitiveEquals(Expression other) => other is UndescribableExpression;
    }

    [Fact]
    public void ToResponse_CompiledOutcome_MapsKindOnly()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())],
            Plan: null, Sql: null);

        var response = SearchTraceMapper.ToResponse(trace, "R4", "Patient");

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

        var outcome = SearchTraceMapper.ToResponse(trace, "R4", "Patient").Parameters.Single().Outcome;

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

        var outcome = SearchTraceMapper.ToResponse(trace, "R4", "Patient").Parameters.Single().Outcome;

        outcome.Kind.Should().Be("Failed");
        outcome.Stage.Should().Be("Resolve");
        outcome.Reason.Should().Be("could not be resolved");
    }

    [Fact]
    public void ToResponse_KnownMissOutcome_CarriesReasonAndSpan()
    {
        // Confirmed live: a system-qualified token/quantity value the resolver reports as unknown compiles
        // to a predicate that can never match (rendered "1 = 0" in the emitted SQL) rather than failing the
        // request -- KnownMiss is how that becomes visible per-parameter instead of only as opaque SQL.
        var trace = new SearchTrace("Observation",
            [Trace(0, "code", "http://loinc.org|99999-9", new ParameterOutcome.KnownMiss("No resource uses the token system 'http://loinc.org'.", new SourceSpan(SourceOrigin.Value, 0, 24)))],
            Plan: null, Sql: null);

        var outcome = SearchTraceMapper.ToResponse(trace, "R4", "Observation").Parameters.Single().Outcome;

        outcome.Kind.Should().Be("KnownMiss");
        outcome.Reason.Should().Be("No resource uses the token system 'http://loinc.org'.");
        outcome.Stage.Should().BeNull();
        outcome.Span!.Start.Should().Be(0);
        outcome.Span.Length.Should().Be(24);
    }

    [Fact]
    public void ToResponse_ProjectsBoundSqlParameters()
    {
        // Every value a caller supplies -- the compartment id, the $everything window instants -- reaches the
        // emitted SQL only as a @pN marker, so a pane showing the SQL without these shows bind markers with
        // nothing behind them. The trace has carried Parameters since 0.6.41; this pins that we project it.
        var sql = new EmittedSqlTrace(
            "SELECT 1 WHERE Id = @p0",
            Parameters: [new EmittedSqlParameter("@p0", "example")],
            Ranges: []);
        var trace = new SearchTrace("Patient", [], Plan: null, sql);

        var response = SearchTraceMapper.ToResponse(trace, "R4", "Patient");

        var parameter = response.Sql!.Parameters.Should().ContainSingle().Subject;
        parameter.Name.Should().Be("@p0");
        parameter.Value.Should().Be("example");
    }

    [Fact]
    public void ToResponse_UndescribableIr_ReportsWhyRatherThanLookingLikeNoIrAtAll()
    {
        // An expression the projector cannot describe degrades to an empty Ir list -- byte-identical to a
        // parameter that genuinely has none. For a provenance tool those are opposite answers ("there is
        // nothing here" vs "I could not tell you"), so the reason has to survive the mapping; the discard
        // that used to sit here made the two indistinguishable in the UI and logged nothing anywhere.
        var trace = new ParameterTrace(
            0, "name", keySyntax: null, "Smith", valueSyntax: null, new UndescribableExpression(), new ParameterOutcome.Compiled(), dataType: null);

        var dto = SearchTraceMapper.ToResponse(new SearchTrace("Patient", [trace], Plan: null, Sql: null), "R4", "Patient")
            .Parameters.Single();

        dto.Ir.Should().BeEmpty();
        dto.IrUnavailableReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ToResponse_ParameterWithNoIr_LeavesTheUnavailableReasonNull()
    {
        // The other half of the pair above: a genuinely IR-less parameter must NOT carry a reason, or the UI
        // would warn on every one of them and the distinction the field exists to draw would be lost.
        var trace = Trace(0, "name", "Smith", new ParameterOutcome.Compiled());

        var dto = SearchTraceMapper.ToResponse(new SearchTrace("Patient", [trace], Plan: null, Sql: null), "R4", "Patient")
            .Parameters.Single();

        dto.Ir.Should().BeEmpty();
        dto.IrUnavailableReason.Should().BeNull();
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
        var sql = new EmittedSqlTrace("SELECT 1", Parameters: [], Ranges: [new SqlTextRange("cte0", SqlRangeKind.Cte, 0, 6)]);
        var trace = new SearchTrace("Patient", [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())], plan, sql)
        {
            Implicit = [new ImplicitParameter("_count", "10", "server default")],
        };

        var response = SearchTraceMapper.ToResponse(trace, "R4", "Patient");

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

        var response = SearchTraceMapper.ToResponse(trace, "R4", "Patient");

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
        var response = SearchTraceMapper.ToResponse(new SearchTrace("Patient", [trace], Plan: null, Sql: null), "R4", "Patient");

        response.Parameters.Single().DataType.Should().Be("String");
    }

    [Fact]
    public void ToResponse_NullPlanAndSql_MapToNull()
    {
        var trace = new SearchTrace("Patient", [], Plan: null, Sql: null)
        {
            Failure = new TraceFailure(TraceStage.Resolve, "Search parameters could not be resolved: 'bogus'.", null),
        };

        var response = SearchTraceMapper.ToResponse(trace, "R4", "Patient");

        response.Plan.Should().BeNull();
        response.Sql.Should().BeNull();
        response.Failure!.Stage.Should().Be("Resolve");
        response.Implicit.Should().BeEmpty();
    }

    [Fact]
    public void ToResponse_EchoesTheResolvedFhirVersion_NotTheResourceType()
    {
        // The response carries the version actually compiled against so an unrecognized route value falling
        // back to R4 is visible to the client rather than silent -- see SearchEngineFactory.Resolve.
        var trace = new SearchTrace("Patient", [], Plan: null, Sql: null);

        SearchTraceMapper.ToResponse(trace, "R5", "Patient").FhirVersion.Should().Be("R5");
    }

    [Fact]
    public void ToResponse_NullTraceResourceType_FallsBackToTheRequestedType()
    {
        // The CompileAsync entry point this app uses always echoes its resourceType back, so this branch is
        // defensive only -- pinned so that if a future package does start returning null (the
        // CompileFromOptionsAsync overload already normalizes empty to null for a system-level search) the
        // response carries the validated type we compiled against rather than a null.
        var trace = new SearchTrace(null!, [], Plan: null, Sql: null);

        SearchTraceMapper.ToResponse(trace, "R4", "Observation").ResourceType.Should().Be("Observation");
    }
}
