using System.Reflection;
using FluentAssertions;
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
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

    /// <summary>Stands in for a future <see cref="ParameterOutcome"/> subtype the mapper does not model yet.</summary>
    private sealed record UnsupportedParameterOutcome() : ParameterOutcome(new ParameterOutcome.Compiled());

    [Fact]
    public async Task Trace_PatientNameSmith_UsesPlanAndCompileDiagnostics()
    {
        var compiled = await CompileAsync("?name=Smith");

        var response = SearchTraceMapper.ToResponse(compiled, "R4", "Patient");

        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key == "name")
            .Which.Outcome.Kind.Should().Be("Compiled");
        response.Plan.Should().NotBeNull();
        response.Plan!.Rows.Should().NotBeEmpty();
        response.Sql.Should().NotBeNull();
        response.Sql!.Parameters.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Trace_UnknownParameter_ReportsFailureButStillReturnsOk()
    {
        var compiled = await CompileAsync("?totally-bogus-param=x");

        var response = SearchTraceMapper.ToResponse(compiled, "R4", "Patient");

        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key == "totally-bogus-param")
            .Which.Outcome.Kind.Should().Be("Ignored");
    }

    [Fact]
    public async Task Trace_ChainedReference_CapturesBothSyntaxProjections()
    {
        var compiled = await CompileAsync("?general-practitioner:Practitioner.name=Smith");

        var response = SearchTraceMapper.ToResponse(compiled, "R4", "Patient");

        var parameter = response.Parameters.Should().ContainSingle().Subject;
        parameter.Outcome.Kind.Should().Be("Compiled");
        parameter.KeySyntax.Should().NotBeNull("the chain structure lives on the key syntax");
        parameter.KeySyntax!.Kind.Should().Be("ForwardChain");
        parameter.ValueSyntax.Should().NotBeNull("the terminal value has its own syntax projection");
        parameter.DataType.Should().Be("String", "the value binds against the chain's terminal parameter, not the reference parameter that names it");
    }

    [Fact]
    public async Task Trace_MixedParameterTypes_ReportsEachParametersOwnDataType()
    {
        var compiled = await CompileAsync("?name=Smith&gender=male&birthdate=gt2000-01-01");

        var response = SearchTraceMapper.ToResponse(compiled, "R4", "Patient");

        response.Parameters.Should().HaveCount(3).And.OnlyContain(p => p.Outcome.Kind == "Compiled");
        response.Parameters.Single(p => p.Key == "name").DataType.Should().Be("String");
        response.Parameters.Single(p => p.Key == "gender").DataType.Should().Be("Token");
        response.Parameters.Single(p => p.Key == "birthdate").DataType.Should().Be("Date");
    }

    [Fact]
    public async Task Trace_CompositeParameter_ReportsItsOwnCompositeDataType_NotOneComponents()
    {
        var compiled = await CompileAsync("?code-value-quantity=8480-6$gt90", resourceType: "Observation");

        var response = SearchTraceMapper.ToResponse(compiled, "R4", "Observation");

        var parameter = response.Parameters.Should().ContainSingle().Subject;
        parameter.Outcome.Kind.Should().Be("Compiled");
        parameter.DataType.Should().Be("Composite", "a composite parameter reports its own declared type, not its first component's (Token)");
    }

    [Fact]
    public async Task Trace_UnknownParameter_HasNoDataType()
    {
        var compiled = await CompileAsync("?totally-bogus-param=x");
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            [Trace(0, "totally-bogus-param", "x", new ParameterOutcome.Ignored("unsupported resource type", new SourceSpan(SourceOrigin.Key, 0, 17)))]);

        var response = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient");

        response.Parameters.Should().ContainSingle().Which.DataType.Should().BeNull("an Ignored parameter never reached a successful parse, so it has no Ir to read a type from");
    }

    [Fact]
    public async Task Trace_UnknownParameter_ReportsWhyRatherThanLookingLikeNoIrAtAll()
    {
        var compiled = await CompileAsync("?name=Smith");
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            [new ParameterTrace(
                0, "name", keySyntax: null, "Smith", valueSyntax: null, new UndescribableExpression(), new ParameterOutcome.Compiled(), dataType: SearchParamType.String)]);

        var dto = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient").Parameters.Single();

        dto.Ir.Should().BeEmpty();
        dto.IrUnavailableReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Trace_ParameterWithNoIr_LeavesTheUnavailableReasonNull()
    {
        var compiled = await CompileAsync("?name=Smith");
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())]);

        var dto = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient").Parameters.Single();

        dto.Ir.Should().BeEmpty();
        dto.IrUnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task Trace_PreservesCteParameterOrdinalAndKindData()
    {
        var compiled = await CompileAsync("?name=Smith");
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            compiled.Diagnostics!.Parameters)
            with
            {
                Implicit = [new ImplicitParameter("_count", "10", "server default")],
            };

        var response = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient");

        response.Plan!.Ctes.Should().Contain(c => c.ParameterOrdinal == 0);
        response.Plan.Rows.Should().NotBeEmpty();
        response.Sql!.Ranges.Should().Contain(r => r.Label.StartsWith("cte"));
        response.Implicit.Single().Name.Should().Be("_count");
    }

    [Fact]
    public async Task Trace_ChainJoinRow_CarriesReferencedCteIndexesAndContributingOrdinals()
    {
        var compiled = await CompileAsync("?general-practitioner:Practitioner.name=Smith");

        var response = SearchTraceMapper.ToResponse(compiled, "R4", "Patient");

        response.Failure.Should().BeNull();

        var chainJoin = response.Plan!.Rows.Should().ContainSingle(r => r.Label == "root" && r.CanonicalLabel == "cte1" && r.Kind == "chainJoin").Subject;
        chainJoin.ReferencedCteIndexes.Should().Equal(0);

        response.Plan.Ctes.Should().HaveCount(2);
        response.Plan.Ctes.Should().Contain(c => c.CteIndex == 0 && c.ParameterOrdinal == 0)
            .Which.ContributingOrdinals.Should().Equal(0);
        response.Plan.Ctes.Should().Contain(c => c.CteIndex == 1 && c.ParameterOrdinal == null)
            .Which.ContributingOrdinals.Should().Equal(0);

        response.Sql!.Ranges.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Trace_UnsupportedOutcomeShape_ThrowsRatherThanSilentlyDroppingIt()
    {
        var compiled = await CompileAsync("?name=Smith");
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            [new ParameterTrace(0, "name", keySyntax: null, "Smith", valueSyntax: null, null, new UnsupportedParameterOutcome(), dataType: SearchParamType.String)]);

        Action act = () => SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient");

        act.Should().Throw<NotSupportedException>().WithMessage("*UnsupportedParameterOutcome*");
    }

    [Fact]
    public void Trace_Failure_CarriesStageMessageAndSpan()
    {
        var failure = new SearchCompilationFailure(CompilationStage.Resolve, "could not be resolved", "unknown", new SourceSpan(SourceOrigin.Value, 0, 1), new InvalidOperationException("boom"));

        var response = SearchTraceMapper.ToResponse(failure, "R4", "Patient");

        response.Failure!.Stage.Should().Be("Resolve");
        response.Failure.Scope.Should().Be("Compilation");
        response.Failure.Message.Should().Be("The search compiler could not process this query.");
        response.Failure.ParameterCode.Should().Be("unknown");
        response.Failure.Span!.Origin.Should().Be("Value");
    }

    [Fact]
    public async Task Trace_Failure_PreservesDiagnostics()
    {
        var compiled = await CompileAsync("?name=Smith");
        var diagnostics = CloneDiagnostics(compiled.Diagnostics!, compiled.Diagnostics!.Parameters)
            with
            {
                Implicit = [new ImplicitParameter("_count", "10", "server default")],
            };
        var failure = new SearchCompilationFailure(
            CompilationStage.Lower,
            "could not be lowered",
            ParameterCode: "name",
            Span: null,
            Exception: null)
        {
            Diagnostics = diagnostics,
        };

        var response = SearchTraceMapper.ToResponse(failure, "R4", "Patient");

        response.Parameters.Should().ContainSingle(p => p.Key == "name");
        response.Plan.Should().NotBeNull();
        response.Plan!.Rows.Should().NotBeEmpty();
        response.Implicit.Should().ContainSingle(p => p.Name == "_count" && p.Value == "10");
        response.Sql.Should().BeNull();
        response.Failure!.Stage.Should().Be("Lower");
        response.Failure.Scope.Should().Be("Compilation");
    }

    [Fact]
    public async Task Trace_PlanTraceFailure_IsMarkedNonFatalWhileSqlRemainsAvailable()
    {
        var compiled = await CompileAsync("?name=Smith");
        var planFailure = new SearchCompilationFailure(
            CompilationStage.Emit,
            "plan explanation unavailable",
            ParameterCode: null,
            Span: null,
            Exception: new NotSupportedException("explain shape"));
        var diagnostics = CloneDiagnostics(compiled.Diagnostics!, compiled.Diagnostics!.Parameters)
            with
            {
                PlanTraceFailure = planFailure,
            };

        var response = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient");

        response.Failure.Should().NotBeNull();
        response.Failure!.Scope.Should().Be("PlanTrace");
        response.Failure.Stage.Should().Be("Emit");
        response.Failure.Message.Should().Be("The search plan explanation is unavailable.");
        response.Plan.Should().NotBeNull();
        response.Sql.Should().NotBeNull();
    }

    [Fact]
    public async Task Trace_KnownMiss_CarriesReasonAndSpan()
    {
        var compiled = await CompileAsync("?name=Smith");
        var span = new SourceSpan(SourceOrigin.Value, 2, 5);
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            [Trace(0, "name", "Smith", new ParameterOutcome.KnownMiss("no matching symbol", span))]);

        var outcome = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient")
            .Parameters.Single().Outcome;

        outcome.Kind.Should().Be("KnownMiss");
        outcome.Reason.Should().Be("no matching symbol");
        outcome.Stage.Should().BeNull();
        outcome.Span.Should().BeEquivalentTo(new SpanDto("Value", 2, 5));
    }

    [Fact]
    public async Task Trace_FailedOutcome_CarriesMessageStageAndSpan()
    {
        var compiled = await CompileAsync("?name=Smith");
        var span = new SourceSpan(SourceOrigin.Value, 2, 5);
        var diagnostics = CloneDiagnostics(
            compiled.Diagnostics!,
            [Trace(0, "name", "Smith", new ParameterOutcome.Failed(TraceStage.Lower, "lowering failed", span))]);

        var outcome = SearchTraceMapper.ToResponse(WithDiagnostics(compiled, diagnostics), "R4", "Patient")
            .Parameters.Single().Outcome;

        outcome.Kind.Should().Be("Failed");
        outcome.Reason.Should().Be("The search parameter could not be compiled.");
        outcome.Stage.Should().Be("Lower");
        outcome.Span.Should().BeEquivalentTo(new SpanDto("Value", 2, 5));
    }

    [Fact]
    public async Task Trace_DataType_MapsFromParameterTraceDirectly()
    {
        var compiled = await CompileAsync("?name=Smith");

        SearchTraceMapper.ToResponse(compiled, "R4", "Patient").Parameters.Single().DataType.Should().Be("String");
    }

    [Fact]
    public void Trace_NullPlanAndSql_MapToNull()
    {
        var failure = new SearchCompilationFailure(CompilationStage.Resolve, "Search parameters could not be resolved: 'bogus'.", null, null, null!);

        var response = SearchTraceMapper.ToResponse(failure, "R4", "Patient");

        response.Plan.Should().BeNull();
        response.Sql.Should().BeNull();
        response.Failure!.Stage.Should().Be("Resolve");
        response.Implicit.Should().BeEmpty();
    }

    [Fact]
    public void Trace_EchoesTheResolvedFhirVersion_NotTheResourceType()
    {
        var failure = new SearchCompilationFailure(CompilationStage.Resolve, "boom", null, null, null!);

        SearchTraceMapper.ToResponse(failure, "R5", "Patient").FhirVersion.Should().Be("R5");
    }

    [Fact]
    public void Trace_FailureUsesTheRequestedResourceType()
    {
        var failure = new SearchCompilationFailure(CompilationStage.Resolve, "boom", null, null, null!);

        SearchTraceMapper.ToResponse(failure, "R4", "Observation").ResourceType.Should().Be("Observation");
    }

    private static async Task<CompiledSearch> CompileAsync(string query, string resourceType = "Patient", string version = "R4", Expression? operationExpression = null)
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());
        var engine = factory.Get(version);
        var compiler = new SearchSqlCompiler(
            new InMemorySymbolResolver(),
            engine.Builder,
            engine.Compartments,
            engine.SearchParameters,
            TimeProvider.System);

        var plan = await compiler.CreatePlanAsync(
            resourceType,
            new QueryParameterParser().Parse(query),
            new SearchPlanOptions
            {
                OperationExpression = operationExpression,
                DiagnosticsLevel = SearchDiagnosticsLevel.Full,
            },
            CancellationToken.None);

        var result = plan.TryCompile();
        result.Succeeded.Should().BeTrue($"query '{query}' should compile");
        return result.Compiled!;
    }

    private static SearchCompilationDiagnostics CloneDiagnostics(SearchCompilationDiagnostics source, IReadOnlyList<ParameterTrace> parameters) =>
        new()
        {
            Parameters = parameters,
            Implicit = source.Implicit,
            PlanTrace = source.PlanTrace,
            PlanTraceFailure = source.PlanTraceFailure,
            SqlTextRanges = source.SqlTextRanges,
        };

    private static CompiledSearch WithDiagnostics(CompiledSearch compiled, SearchCompilationDiagnostics diagnostics)
    {
        var copy = new CompiledSearch(compiled.Sql, compiled.Parameters, compiled.Query);
        typeof(CompiledSearch).GetProperty(nameof(CompiledSearch.Diagnostics))!.SetValue(copy, diagnostics);
        return copy;
    }
}
