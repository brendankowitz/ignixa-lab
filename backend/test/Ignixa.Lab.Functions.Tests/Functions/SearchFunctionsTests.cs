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

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "Patient", CancellationToken.None);

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

        var result = await functions.Trace(BuildGetRequest("?totally-bogus-param=x"), "Patient", CancellationToken.None);

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
            BuildGetRequest("?general-practitioner:Practitioner.name=Smith"), "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        var parameter = response.Parameters.Should().ContainSingle().Subject;
        parameter.Outcome.Kind.Should().Be("Compiled");
        parameter.KeySyntax.Should().NotBeNull("the chain structure lives on the key syntax");
        parameter.KeySyntax!.Kind.Should().Be("ForwardChain");
        parameter.ValueSyntax.Should().NotBeNull("the terminal value has its own syntax projection");
    }

    [Fact]
    public async Task Trace_EmptyResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "  ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
