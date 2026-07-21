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
}
