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

    private static HttpRequest BuildGetRequest(string queryString = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(queryString); // e.g. "?name=Smith"
        return context.Request;
    }

    [Fact]
    public async Task Trace_PatientNameSmith_UsesPlanAndCompileDiagnostics()
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
        response.Plan!.Rows.Should().NotBeEmpty();
        response.Sql.Should().NotBeNull();
        response.Sql!.Parameters.Should().NotBeEmpty();
        // The lineage join the UI depends on: a CTE attributed to the parameter, and a SQL range labelled for it.
        var cte = response.Plan.Ctes.Should().Contain(c => c.ParameterOrdinal == 0).Subject;
        response.Sql.Ranges.Should().Contain(r => r.Label == $"cte{cte.CteIndex}");
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
        // Confirmed live against the real compiler: SearchSqlCompiler never validates the
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
    public async Task Trace_MalformedDateValue_RemainsBadRequestAfterPlanMigration()
    {
        // Confirmed live: an unparseable date value throws BadSearchRequestException (a FhirException) out of
        // SearchSqlCompiler itself -- parsing happens too early to be caught and recorded as a
        // per-parameter Ignored/Failed outcome the way an unrecognized parameter name is. This is the
        // library's own "the caller's request is bad" signal, so it maps to a 400 whose body is the
        // compiler's message; anything outside that family is our fault and maps to a 500 instead.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?birthdate=notadate"), "R4", "Patient", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "The date time string 'notadate' is not in a correct format." });
    }

    [Fact]
    public async Task Trace_CompilerFailure_ReturnsStructuredFailureInsteadOfInternalServerError()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?_sort=name,birthdate,gender,active"), "R4", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().NotBeNull();
        response.Failure!.Stage.Should().Be("Lower");
        response.Failure.Message.Should().Be("The search compiler could not process this query.");
    }

    [Fact]
    public async Task Trace_Cancellation_PropagatesInsteadOfBecomingAnErrorResponse()
    {
        var functions = CreateFunctions();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            functions.Trace(BuildGetRequest("?name=Smith"), "R4", "Patient", cancellation.Token));
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

    // Confirmed live against the real compiler (0.6.68-alpha, no per-parameter Ignored/Failed outcome, no
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

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
        // The full wildcard traversal is dozens of CTEs (one per (resourceType, search-param) pair in the
        // Patient compartment); a single-type scope is at most a handful. This is not an exact literal count
        // (which resource types have which search params can shift) -- it's an assertion that scoping did
        // something, not nothing.
        response.Plan!.Ctes.Count.Should().BeLessThan(10);
        // ...and something rather than *nothing*: the compartment id and type reach the SQL only as bound
        // parameters and numeric surrogate ids, so neither appears in the emitted text and a transposed or
        // dropped argument would leave a plan with no compartment traversal at all -- which "fewer than 10
        // CTEs" happily accepts, zero being fewer than 10. Naming the CTE kind closes that.
        response.Plan.Ctes.Should().NotBeEmpty();
        response.Plan.Explain.Should().Contain("CompartmentSource");
    }

    [Fact]
    public async Task CompartmentTrace_WildcardResourceType_CompilesTheFullTraversal()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", "example", "*", CancellationToken.None);

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

        var result = await functions.CompartmentTrace(BuildGetRequest("?code=1234-5"), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key == "code").Which.Outcome.Kind.Should().Be("Compiled");
    }

    [Fact]
    public async Task CompartmentTrace_UnknownCompartmentType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "NotACompartmentType", "example", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'NotACompartmentType' is not a valid FHIR compartment type." });
    }

    [Fact]
    public async Task CompartmentTrace_UnknownMemberResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", "example", "TotallyBogusResource", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CompartmentTrace_EmptyCompartmentId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", "  ", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CompartmentTrace_LowercaseCompartmentType_NormalizesAndCompiles()
    {
        // Failure == null alone would not show normalization happened -- a raw "patient" passed through to a
        // case-tolerant library looks identical. Compare against the canonically-cased request instead: the
        // compartment type reaches the SQL only as a numeric surrogate id, so if normalization stopped
        // resolving the compartment the plan would lose its CompartmentSource CTEs and the two would diverge.
        var functions = CreateFunctions();

        var lowercase = await functions.CompartmentTrace(BuildGetRequest(), "R4", "patient", "example", "Observation", CancellationToken.None);
        var canonical = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = lowercase.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan!.Explain.Should().Be(
            canonical.Should().BeOfType<OkObjectResult>().Subject.Value
                .Should().BeOfType<SearchTraceResponse>().Subject.Plan!.Explain);
    }

    [Fact]
    public async Task CompartmentTrace_EncounterRoot_ScopedToRealMemberResourceType_Compiles()
    {
        // All the other compartment tests in this file use "Patient" as the compartment root -- the frontend
        // also offers Compartment mode for Encounter (see SEARCH_MODES_BY_RESOURCE_TYPE in searchTypes.ts),
        // so a non-Patient root needs its own coverage. Confirmed live: "Condition" is a real member of the
        // R4 Encounter compartment.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Encounter", "example", "Condition", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
    }

    [Fact]
    public async Task CompartmentTrace_EncounterRoot_WildcardResourceType_CompilesTheFullTraversal()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Encounter", "example", "*", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
    }

    [Fact]
    public async Task CompartmentTrace_EncounterRoot_NonMemberResourceType_ReturnsBadRequest()
    {
        // Confirmed live (before the membership-check fix): "Patient" is NOT a member of the R4 Encounter
        // compartment (verified via ICompartmentDefinitionManager.TryGetResourceTypes(Encounter) -- Patient
        // is absent from the returned set), yet CompartmentSearchExpression happily compiled a plan for it
        // anyway: 1 CTE, Failure == null, SQL containing a literal "AND 1 = 0" (the compartment-linking
        // search parameter for that pair just doesn't exist, so the WHERE clause folds to always-false). That
        // was a 200 OK carrying a plan that looks real but can never match anything -- the exact "confidently
        // wrong 200" CompileAndRespondAsync's own resourceType check exists to prevent, just one level down
        // (compartment membership rather than resource-type existence). This pins the fixed behavior: reject
        // it with 400 instead.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Encounter", "example", "Patient", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'Patient' is not a member of the 'Encounter' compartment." });
    }

    [Fact]
    public async Task EverythingTrace_Bare_ReturnsEmptyParametersWithNonNullPlanAndSql()
    {
        // The whole thing is one expression, not parameter-driven -- there is nothing for QueryParameterParser
        // to have parsed, so Parameters comes back empty. This is the shape the frontend's empty-parameters
        // state depends on; pin it so that dependency doesn't silently break.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest(), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().BeEmpty();
        response.Plan.Should().NotBeNull();
        response.Sql.Should().NotBeNull();
    }

    [Fact]
    public async Task EverythingTrace_WithTypeFilter_CompilesToASmallerPlanThanBare()
    {
        // Confirmed live: a _type filter collapses the plan from ~800 lines to a handful of CTEs and drops
        // ReferencedTypeExpansion entirely (out of scope once _type is set).
        var functions = CreateFunctions();

        var bare = await functions.EverythingTrace(BuildGetRequest(), "R4", "example", CancellationToken.None);
        var filtered = await functions.EverythingTrace(BuildGetRequest("?_type=Observation"), "R4", "example", CancellationToken.None);

        var bareResponse = bare.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<SearchTraceResponse>().Subject;
        var filteredResponse = filtered.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<SearchTraceResponse>().Subject;
        filteredResponse.Failure.Should().BeNull();
        filteredResponse.Plan!.Ctes.Count.Should().BeLessThan(bareResponse.Plan!.Ctes.Count);
        filteredResponse.Plan.Explain.Should().NotContain("ReferencedTypeExpansion");
    }

    [Fact]
    public async Task EverythingTrace_UnknownTypeFilterValue_ReturnsBadRequest()
    {
        // Mirrors CompartmentTrace_UnknownMemberResourceType_ReturnsBadRequest's "reject unknown things with
        // a 400" philosophy: before this fix, _type was split into a HashSet and passed straight to
        // PatientEverythingExpression with no check that each value is a real resource type, unlike the
        // compartment route's member-resourceType check and CompileAndRespondAsync's own top-level check.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?_type=TotallyBogusResource"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task EverythingTrace_MixOfValidAndUnknownTypeFilterValues_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?_type=Observation,TotallyBogusResource"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'_type' contains unsupported resource type(s) for R4: TotallyBogusResource." });
    }

    [Fact]
    public async Task EverythingTrace_WithSince_CompilesCleanly()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?_since=2026-01-01T00:00:00Z"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Sql!.Sql.Should().Contain("dbo.Transactions");
    }

    [Fact]
    public async Task EverythingTrace_WithStartAndEnd_CompilesCleanly()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?start=2020-01-01T00:00:00Z&end=2026-01-01T00:00:00Z"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
    }

    [Fact]
    public async Task EverythingTrace_IncludeReferencedResourcesFalse_OmitsReferencedTypeExpansionFromExplain()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?includeReferencedResources=false"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan!.Explain.Should().NotContain("ReferencedTypeExpansion");
    }

    [Fact]
    public async Task EverythingTrace_EmptyPatientId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest(), "R4", "  ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task EverythingTrace_MalformedSince_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?_since=not-a-date"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("start")]
    [InlineData("end")]
    public async Task EverythingTrace_MalformedDateWindowBound_ReturnsBadRequest(string queryKey)
    {
        // _since is covered above; these two go through the same TryParseOptionalDate helper, so what is
        // actually at risk is a wrong key string or a mismatched out-variable at the call site.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest($"?{queryKey}=not-a-date"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = $"'{queryKey}' value 'not-a-date' is not a valid date/time." });
    }

    [Fact]
    public async Task EverythingTrace_StartAfterEnd_ReturnsBadRequest()
    {
        // Each bound parses fine on its own; only together are they impossible. Left unchecked this compiles
        // to a plan that can never match and reports it as a clean 200.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(
            BuildGetRequest("?start=2030-01-01T00:00:00Z&end=2020-01-01T00:00:00Z"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'start' (2030-01-01T00:00:00.0000000+00:00) is after 'end' (2020-01-01T00:00:00.0000000+00:00)." });
    }

    [Fact]
    public async Task EverythingTrace_NonBooleanIncludeReferencedResources_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?includeReferencedResources=yes"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'includeReferencedResources' value 'yes' is not 'true' or 'false'." });
    }

    [Fact]
    public async Task EverythingTrace_TypeFilterNotInThePatientCompartment_ReturnsBadRequest()
    {
        // Organization is a real R4 resource type, so the existence check alone waves it through -- but it is
        // not a Patient compartment member, so the lowering folds it to an always-false predicate. Because
        // $everything reports zero Parameters, that never surfaces as a KnownMiss chip; it is visible only as
        // a "1 = 0" in the emitted SQL. Same standard the compartment route applies to its member type.
        // (Referenced Organizations are pulled in by includeReferencedResources, not by naming them here.)
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?_type=Organization"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'_type' contains resource type(s) that are not members of the Patient compartment: Organization." });
    }

    [Fact]
    public async Task EverythingTrace_TypeFilterOfOnlySeparators_IsTreatedAsNoFilter()
    {
        // "?_type=," clears the IsNullOrWhiteSpace guard but splits to zero entries. Treated as "no filter",
        // identical to omitting _type -- rather than an empty set whose meaning nothing defines.
        var functions = CreateFunctions();

        var separatorsOnly = await functions.EverythingTrace(BuildGetRequest("?_type=,"), "R4", "example", CancellationToken.None);
        var absent = await functions.EverythingTrace(BuildGetRequest(), "R4", "example", CancellationToken.None);

        separatorsOnly.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject
            .Plan!.Explain.Should().Be(
                absent.Should().BeOfType<OkObjectResult>().Subject.Value
                    .Should().BeOfType<SearchTraceResponse>().Subject.Plan!.Explain);
    }

    [Fact]
    public async Task EverythingTrace_Bare_IncludesReferencedTypeExpansion()
    {
        // The positive twin of EverythingTrace_IncludeReferencedResourcesFalse_...: without it, a default
        // that flipped to false (or a flag parsed inverted) would leave every test green.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest(), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Plan!.Explain.Should().Contain("ReferencedTypeExpansion");
    }

    [Theory]
    [InlineData("start", "EndDateTime >=", "StartDateTime <=")]
    [InlineData("end", "StartDateTime <=", "EndDateTime >=")]
    public async Task EverythingTrace_OneSidedWindow_BindsThatBoundOnly(string queryKey, string expectedPredicate, string unexpectedPredicate)
    {
        // start/end/_since are three consecutive same-typed DateTimeOffset? arguments on
        // PatientEverythingExpression, and their values reach the SQL only as bound parameters (@pN) -- so a
        // swapped pair is invisible in the emitted text when both are supplied. Supplying one at a time makes
        // the swap visible: `start` alone must emit the lower-bound predicate and nothing else, `end` alone
        // the upper-bound one. Transposing the two arguments flips both cases.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(
            BuildGetRequest($"?{queryKey}=2023-05-06T00:00:00Z"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan!.Explain.Should().Contain(expectedPredicate).And.NotContain(unexpectedPredicate);
    }

    [Theory]
    [InlineData("3", "'3' is not a valid FHIR compartment type.")]
    [InlineData("Patient, Encounter", "'Patient, Encounter' is not a valid FHIR compartment type.")]
    [InlineData("0", "'0' is not a valid FHIR compartment type.")]
    public async Task CompartmentTrace_NonNameCompartmentType_ReturnsBadRequest(string compartmentType, string expectedError)
    {
        // Enum.TryParse accepts far more than a compartment name: any in-range numeric string ("3" parses to
        // Practitioner) and any comma-separated combination ("Patient, Encounter" OR-parses to Practitioner),
        // both of which Enum.IsDefined then waves through because the *result* is defined. That silently
        // traced a different compartment than the URL named and returned 200. Name-matching closes it.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", compartmentType, "example", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = expectedError });
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("bad_underscore")]
    [InlineData("way-too-long-way-too-long-way-too-long-way-too-long-way-too-long-x")]
    public async Task CompartmentTrace_IdOutsideTheFhirIdGrammar_ReturnsBadRequest(string compartmentId)
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", compartmentId, "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(
                new { error = $"'{compartmentId}' is not a valid FHIR id (expected 1-64 characters from A-Z, a-z, 0-9, '-' and '.')." });
    }

    [Theory]
    [InlineData("?_typ=Observation", "_typ")]
    [InlineData("?_Since=2020-01-01", "_Since")]
    [InlineData("?_count=5", "_count")]
    [InlineData("?name=Smith", "name")]
    public async Task EverythingTrace_UnrecognizedQueryParameter_ReturnsBadRequest(string queryString, string expectedInError)
    {
        // The other two routes hand unknown parameters to QueryParameterParser, which reports them as Ignored
        // chips. $everything compiles from typed constructor arguments and reports zero Parameters, so an
        // unrecognized key had no channel at all: it was dropped in silence and answered with a full
        // unfiltered trace identical to the bare request, while the UI explained the empty parameter list as
        // normal for $everything. A typo'd filter that silently means "no filter" is the confidently-wrong 200
        // this file rejects everywhere else. Note _Since: the lookups are ordinal, so wrong case is a
        // different key and must be rejected rather than silently ignored.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest(queryString), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new
            {
                error = $"'$everything' does not support parameter(s): {expectedInError}. " +
                        "Supported: _since, _type, end, includeReferencedResources, start.",
            });
    }

    [Fact]
    public async Task EverythingTrace_EveryRecognizedParameterTogether_IsAccepted()
    {
        // The negative twin of the test above: an allowlist is only safe if it actually admits everything the
        // handler reads. If a key were misspelled in EverythingQueryKeys, the reject-unknown check would 400
        // a request the handler fully supports -- and every existing $everything test passes one parameter at
        // a time, so none of them would catch it.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(
            BuildGetRequest("?_type=Observation&_since=2020-01-01T00:00:00Z&start=2021-01-01T00:00:00Z&end=2022-01-01T00:00:00Z&includeReferencedResources=false"),
            "R4",
            "example",
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject.Failure.Should().BeNull();
    }

    [Fact]
    public async Task EverythingTrace_PatientIdOutsideTheFhirIdGrammar_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest(), "R4", "has space", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(
                new { error = "'has space' is not a valid FHIR id (expected 1-64 characters from A-Z, a-z, 0-9, '-' and '.')." });
    }

    [Theory]
    [InlineData("a")]
    [InlineData("way-too-long-way-too-long-way-too-long-way-too-long-way-too-long")] // exactly 64
    [InlineData("has.dots.and-dashes.123")]
    public async Task CompartmentTrace_IdAtTheEdgesOfTheFhirIdGrammar_IsAccepted(string compartmentId)
    {
        // The rejection cases are covered above; without these, tightening the regex (a {1,64} that became
        // {1,63}, or a dropped '.') would narrow what the bench accepts with the whole suite still green.
        compartmentId.Length.Should().BeLessThanOrEqualTo(64);
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildGetRequest(), "R4", "Patient", compartmentId, "Observation", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject.Failure.Should().BeNull();
    }

    [Fact]
    public async Task EverythingTrace_TypeFilter_DependsOnWhichTypeWasNamed_NotJustThatOneWas()
    {
        // The existing filter test asserts only "smaller plan than bare", which a filter that resolved to the
        // *wrong* member type satisfies just as well. Asserting on the type name directly is not available:
        // resource types reach the plan as numeric surrogate ids (`CompartmentSource[56,60]`), so the Explain
        // never spells "Observation". Comparing two different single-type filters gets at the same property
        // from the other side -- if the filter's contents were ignored, or every value collapsed to the same
        // one, these two would be identical.
        var functions = CreateFunctions();

        var observation = await functions.EverythingTrace(BuildGetRequest("?_type=Observation"), "R4", "example", CancellationToken.None);
        var encounter = await functions.EverythingTrace(BuildGetRequest("?_type=Encounter"), "R4", "example", CancellationToken.None);

        var observationPlan = observation.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject.Plan!.Explain;
        var encounterPlan = encounter.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject.Plan!.Explain;

        observationPlan.Should().NotBe(encounterPlan);
    }

    [Fact]
    public async Task Trace_UnknownTokenSystem_ReportsKnownMiss()
    {
        // The end-to-end path for KnownMiss, which the mapper test can only exercise by hand-constructing the
        // outcome. InMemorySymbolResolver declines systems outside its stand-in lookup table, the compiler
        // lowers that to an always-false predicate, and the trace restamps it as KnownMiss -- the same answer
        // a real server gives for a system it has never indexed.
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?code=http://not-a-real-system.example|12345"), "R4", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("KnownMiss");
        response.Sql!.Sql.Should().Contain("1 = 0");
    }

    [Fact]
    public async Task Trace_KnownTokenSystem_Compiles()
    {
        // The other half of the pair: a system the stand-in table does know resolves normally, so KnownMiss
        // above is attributable to the system being unknown rather than to system-qualified tokens breaking.
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?code=http://loinc.org|8480-6"), "R4", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("Compiled");
    }

    [Fact]
    public async Task Trace_UnknownQuantityCode_ReportsKnownMiss()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?value-quantity=90|http://unitsofmeasure.org|not-a-ucum-code"), "R4", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("KnownMiss");
    }

    [Fact]
    public async Task Trace_KnownQuantityCode_Compiles()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?value-quantity=90|http://unitsofmeasure.org|mm[Hg]"), "R4", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle().Which.Outcome.Kind.Should().Be("Compiled");
    }

    [Fact]
    public async Task Trace_UnrecognizedFhirVersion_ReportsTheVersionActuallyUsed()
    {
        // An unrecognized version silently falls back to R4 rather than 400ing. That stays, but the response
        // now says which version was compiled against, so the substitution is detectable instead of silent.
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "R7", "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.FhirVersion.Should().Be("R4");
    }

    [Theory]
    [InlineData("R4", "R4")]
    [InlineData("r4b", "R4B")]
    [InlineData("R3", "STU3")]
    [InlineData("stu3", "STU3")]
    public async Task Trace_RecognizedFhirVersion_EchoesItCanonically(string requested, string expected)
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), requested, "Patient", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject
            .FhirVersion.Should().Be(expected);
    }

    [Fact]
    public async Task EverythingTrace_UnknownTypeFilterUnderFallbackVersion_NamesTheVersionActuallyUsed()
    {
        // The error used to interpolate the raw route value, so "?_type=Foo" under "R7" said "unsupported for
        // R7" about a determination R4 made.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildGetRequest("?_type=TotallyBogusResource"), "R7", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'_type' contains unsupported resource type(s) for R4: TotallyBogusResource." });
    }
}
