using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class SearchFunctionsTests
{
    private static SearchFunctions CreateFunctions() =>
        new(NullLogger<SearchFunctions>.Instance, new SearchEngineFactory(new SchemaProviderFactory()));

    private static HttpRequest BuildGetRequest(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(queryString); // e.g. "?name=Smith"
        return context.Request;
    }

    private static HttpRequest BuildCompartmentGetRequest(string queryString = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(queryString);
        return context.Request;
    }

    [Fact]
    public async Task Trace_PatientNameSmith_CompilesToPlanAndSql()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "R4", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.ResourceType.Should().Be("Patient");
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key.StartsWith("name"));
        response.Parameters.Single().Outcome.Kind.Should().Be("Compiled");
        response.Plan.Should().NotBeNull();
        response.Sql.Should().NotBeNull();
        // The lineage join the UI depends on: a CTE attributed to the parameter, and a SQL range labelled for it.
        var cte = response.Plan!.Ctes.Should().Contain(c => c.ParameterOrdinal == 0).Subject;
        response.Sql!.Ranges.Should().Contain(r => r.Label == $"cte{cte.CteIndex}");
    }

    [Fact]
    public async Task Trace_UnknownParameter_ReportsFailureButStillReturnsOk()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?totally-bogus-param=x"), "R4", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        // Confirmed (by running against the real compiler): an unrecognized search parameter is lenient-
        // handled per-parameter rather than failing the whole page — SearchOptionsBuilder catches
        // SearchParameterNotSupportedException and records the parameter as Ignored, leaving the page-level
        // Failure null.
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key == "totally-bogus-param")
            .Which.Outcome.Kind.Should().Be("Ignored");
    }

    [Fact]
    public async Task Trace_ChainedReference_CapturesBothSyntaxProjections()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?general-practitioner:Practitioner.name=Smith"), "R4", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        var parameter = response.Parameters.Should().ContainSingle().Subject;
        parameter.Outcome.Kind.Should().Be("Compiled");
        parameter.KeySyntax.Should().NotBeNull("the chain structure lives on the key syntax");
        parameter.KeySyntax!.Kind.Should().Be("ForwardChain");
        parameter.ValueSyntax.Should().NotBeNull("the terminal value has its own syntax projection");
        // ParameterTrace.DataType reports the chain's terminal parameter -- "name" here -- since that is
        // the one "Smith" is actually matched against, not the reference parameter that names the chain.
        parameter.DataType.Should().Be("String", "the value binds against the chain's terminal parameter, not the reference parameter that names it");
    }

    [Fact]
    public async Task Trace_MixedParameterTypes_ReportsEachParametersOwnDataType()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?name=Smith&gender=male&birthdate=gt2000-01-01"), "R4", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Parameters.Should().HaveCount(3).And.OnlyContain(p => p.Outcome.Kind == "Compiled");
        response.Parameters.Single(p => p.Key == "name").DataType.Should().Be("String");
        response.Parameters.Single(p => p.Key == "gender").DataType.Should().Be("Token");
        response.Parameters.Single(p => p.Key == "birthdate").DataType.Should().Be("Date");
    }

    [Fact]
    public async Task Trace_CompositeParameter_ReportsItsOwnCompositeDataType_NotOneComponents()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?code-value-quantity=8480-6$gt90"), "R4", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        var parameter = response.Parameters.Should().ContainSingle().Subject;
        parameter.Outcome.Kind.Should().Be("Compiled");
        parameter.DataType.Should().Be("Composite", "a composite parameter reports its own declared type, not its first component's (Token)");
    }

    [Fact]
    public async Task Trace_UnknownParameter_HasNoDataType()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?totally-bogus-param=x"), "R4", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Parameters.Should().ContainSingle().Which.DataType.Should().BeNull("an Ignored parameter never reached a successful parse, so it has no Ir to read a type from");
    }

    [Fact]
    public async Task Trace_EmptyResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "R4", "  ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Trace_UnknownResourceType_ReturnsBadRequestRatherThanACompiledPlanForNothing()
    {
        // Confirmed live against the real compiler: SearchCompiler.CompileAsync never validates the
        // top-level resourceType itself (only a chain/_has TARGET resource type is validated) -- given a
        // resourceType nothing recognizes, it happily compiles a full plan/SQL against
        // `WHERE ResourceTypeId = @p0` for an ID that matches nothing. That's a confidently wrong 200 for a
        // tool whose whole job is trustworthy tracing, so SearchFunctions.Trace rejects it up front instead.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "R4", "TotallyBogusResource", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'TotallyBogusResource' is not a supported FHIR resource type for R4." });
    }

    [Fact]
    public async Task Trace_MalformedDateValue_ReturnsBadRequestViaTheGenericExceptionPath()
    {
        // Confirmed live: an unparseable date value throws a bare FormatException out of SearchCompiler
        // .CompileAsync itself (parsing happens too early to be caught and recorded as a per-parameter
        // Ignored/Failed outcome the way an unrecognized parameter name is) -- exercises the catch-all
        // `catch (Exception ex)` branch in SearchFunctions.Trace, not the "malformed resourceType" guard
        // above or the per-parameter leniency the other tests in this file cover.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?birthdate=notadate"), "R4", "Patient", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("STU3")]
    [InlineData("R4B")]
    [InlineData("R5")]
    [InlineData("R6")]
    public async Task Trace_NonR4Version_CompilesAgainstThatVersionsOwnSchema(string fhirVersion)
    {
        // Proves SearchEngineFactory.Get actually builds a distinct, working engine per version (not just
        // that the parameter is accepted) -- each of these runs the real compiler for that FHIR version.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), fhirVersion, "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("Compiled");
        response.Plan.Should().NotBeNull();
        response.Sql.Should().NotBeNull();
    }

    [Fact]
    public async Task Trace_UnrecognizedFhirVersion_FallsBackToR4RatherThanErroring()
    {
        // Matches SchemaProviderFactory's own fallback behavior elsewhere in this app -- an unrecognized
        // version string is not a 400, it silently resolves to R4.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "not-a-real-version", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("Compiled");
    }

    // Confirmed live against the real compiler (0.6.28-alpha, no per-parameter Ignored/Failed outcome, no
    // page-level Failure), the same way every SearchQueryBuilder chip is verified before being added --
    // these back the `sa`/`eb`/`:missing`/`_id`/plain-quantity chips that ship there.
    [Theory]
    [InlineData("Patient", "birthdate=sa2000-01-01")]
    [InlineData("Patient", "birthdate=eb2000-01-01")]
    [InlineData("Patient", "name:missing=true")]
    [InlineData("Patient", "name:missing=false")]
    [InlineData("Patient", "_id=example")]
    [InlineData("Patient", "general-practitioner:missing=true")]
    [InlineData("Observation", "value-quantity=gt90")]
    [InlineData("Observation", "value-quantity:missing=true")]
    [InlineData("Observation", "code:missing=true")]
    public async Task Trace_NewlyAddedChipQueries_CompileCleanly(string resourceType, string query)
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest($"?{query}"), "R4", resourceType, CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("Compiled");
    }

    [Fact]
    public async Task Trace_ExplicitTypeParameter_IsAbsorbedByTheResourceTypeRouteSegment()
    {
        // Confirmed live: an explicit `_type` matching the route's own {resourceType} never reaches
        // Parameters as a traced entry -- SearchOptionsBuilder resolves resource-type scoping from the
        // route segment already, so `_type` here would just be a redundant, non-clickable chip with no
        // CTE/SQL provenance to show. Excluded from SearchQueryBuilder for that reason.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?_type=Encounter"), "R4", "Encounter", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task CompartmentTrace_ScopedToResourceType_CompilesToANarrowPlan_NotTheFullWildcardTraversal()
    {
        // Confirmed live: omitting the resource-type scope (or passing the true wildcard) produces the full
        // ~75-CTE compartment traversal. Scoping to one type must produce a small, correctly-narrowed plan --
        // an earlier manual probe that omitted the scope by mistake produced the huge plan and looked like a
        // library defect until the actual cause (a missing argument, not a compiler bug) was found. This test
        // pins the correct, narrow shape so that mistake can't silently regress back in.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
        // The full wildcard traversal is dozens of CTEs (one per (resourceType, search-param) pair in the
        // Patient compartment); a single-type scope is at most a handful. This is not an exact literal count
        // (which resource types have which search params can shift) -- it's an assertion that scoping did
        // something, not nothing.
        response.Plan!.Ctes.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task CompartmentTrace_WildcardResourceType_CompilesTheFullTraversal()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "example", "*", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
        // The scoped test above asserts "small"; this one asserts the wildcard case is genuinely the big
        // one, so the two tests together prove the scope argument actually does the narrowing.
        response.Plan!.Ctes.Count.Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task CompartmentTrace_CombinedWithAQueryStringParameter_NarrowsFurther()
    {
        // Confirmed live: compartment scoping and the existing query-string search terms are not mutually
        // exclusive -- a normal search parameter layers on top of the compartment scope.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest("?code=1234-5"), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key == "code").Which.Outcome.Kind.Should().Be("Compiled");
    }

    [Fact]
    public async Task CompartmentTrace_UnknownCompartmentType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "NotACompartmentType", "example", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'NotACompartmentType' is not a valid FHIR compartment type." });
    }

    [Fact]
    public async Task CompartmentTrace_UnknownMemberResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "example", "TotallyBogusResource", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CompartmentTrace_EmptyCompartmentId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "  ", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
