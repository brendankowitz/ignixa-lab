using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Search-trace endpoints powering the Expression Benches "Search" bench. Given a FHIR search query, it
/// traces the query through parse → typed expression → lowered SQL plan → generated SQL via
/// <see cref="SearchCompiler"/>, returning the cross-referenced provenance as plain JSON (not a FHIR
/// resource — this is bench tooling, so no OperationOutcome wrapping). Supports the same FHIR version set
/// as <see cref="Services.FhirPath.SchemaProviderFactory"/> (STU3, R4, R4B, R5, R6) via
/// <see cref="SearchEngineFactory.Get"/>, which defaults an unrecognized value to R4 rather than rejecting
/// the request — same permissive fallback the rest of this app uses for FHIR version strings.
/// </summary>
public sealed class SearchFunctions(ILogger<SearchFunctions> logger, SearchEngineFactory engineFactory)
{
    [Function("SearchTrace")]
    public Task<IActionResult> Trace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{resourceType}")] HttpRequest request,
        string fhirVersion,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return Task.FromResult<IActionResult>(new BadRequestObjectResult(new { error = "A resource type is required." }));
        }

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;
        var parameters = new QueryParameterParser().Parse(rawQuery);

        return CompileAndRespondAsync(fhirVersion, resourceType, parameters, operationExpression: null, cancellationToken);
    }

    [Function("SearchCompartmentTrace")]
    public async Task<IActionResult> CompartmentTrace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{compartmentType}/{compartmentId}/{resourceType}")] HttpRequest request,
        string fhirVersion,
        string compartmentType,
        string compartmentId,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(compartmentId))
        {
            return new BadRequestObjectResult(new { error = "A compartment id is required." });
        }

        if (!Enum.TryParse<CompartmentType>(compartmentType, ignoreCase: true, out _))
        {
            return new BadRequestObjectResult(new { error = $"'{compartmentType}' is not a valid FHIR compartment type." });
        }

        // "*" (Patient/{id}/*) means "every resource type in the compartment" -- null filteredResourceTypes
        // is what CompartmentSearchExpression reads as that wildcard. A specific type narrows to just it.
        // The resource type passed to the compiler for a wildcard is the compartment root itself (there is
        // no single member type to name); for a scoped search it's the actual member type being searched.
        var wildcard = resourceType == "*";
        var filteredResourceTypes = wildcard ? null : new HashSet<string> { resourceType };
        var compileResourceType = wildcard ? compartmentType : resourceType;

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;
        var parameters = new QueryParameterParser().Parse(rawQuery);

        var operationExpression = new CompartmentSearchExpression(compartmentType, compartmentId, filteredResourceTypes);

        return await CompileAndRespondAsync(fhirVersion, compileResourceType, parameters, operationExpression, cancellationToken);
    }

    [Function("SearchEverythingTrace")]
    public async Task<IActionResult> EverythingTrace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/Patient/{patientId}/$everything")] HttpRequest request,
        string fhirVersion,
        string patientId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return new BadRequestObjectResult(new { error = "A patient id is required." });
        }

        HashSet<string>? filteredResourceTypes = null;
        if (request.Query.TryGetValue("_type", out var typeValues) && !string.IsNullOrWhiteSpace(typeValues.ToString()))
        {
            filteredResourceTypes = typeValues.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet();
        }

        if (!TryParseOptionalDate(request, "_since", out var sinceDate, out var sinceError))
        {
            return new BadRequestObjectResult(new { error = sinceError });
        }

        if (!TryParseOptionalDate(request, "start", out var startDate, out var startError))
        {
            return new BadRequestObjectResult(new { error = startError });
        }

        if (!TryParseOptionalDate(request, "end", out var endDate, out var endError))
        {
            return new BadRequestObjectResult(new { error = endError });
        }

        var includeReferencedResources = true;
        if (request.Query.TryGetValue("includeReferencedResources", out var includeValues) && !string.IsNullOrWhiteSpace(includeValues.ToString()))
        {
            if (!bool.TryParse(includeValues.ToString(), out includeReferencedResources))
            {
                return new BadRequestObjectResult(new { error = $"'includeReferencedResources' value '{includeValues}' is not 'true' or 'false'." });
            }
        }

        var operationExpression = new PatientEverythingExpression(patientId, startDate, endDate, sinceDate, filteredResourceTypes, includeReferencedResources);

        // $everything isn't parameter-driven -- there's no query string for QueryParameterParser to parse,
        // every option above became a typed constructor argument on the expression instead. The resource
        // type the compiler compiles against is always "Patient", the operation's anchor type; this route
        // only ever accepts Patient (there is no EncounterEverythingExpression or similar in the library).
        return await CompileAndRespondAsync(fhirVersion, "Patient", parameters: [], operationExpression, cancellationToken);
    }

    private static bool TryParseOptionalDate(HttpRequest request, string queryKey, out DateTimeOffset? value, out string? error)
    {
        value = null;
        error = null;

        if (!request.Query.TryGetValue(queryKey, out var rawValues) || string.IsNullOrWhiteSpace(rawValues.ToString()))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(rawValues.ToString(), out var parsed))
        {
            error = $"'{queryKey}' value '{rawValues}' is not a valid date/time.";
            return false;
        }

        value = parsed;
        return true;
    }

    // SearchCompiler.CompileAsync never validates the top-level resourceType itself -- it only rejects an
    // unknown resource type when one appears as a chain/_has target (via SearchKeyBinder resolving a
    // ReferenceSearchParameter's target types). Given a resource type nothing recognizes, it happily
    // compiles a full plan and SQL against `dbo.Resource WHERE ResourceTypeId = @p0` for an ID that will
    // never match anything -- a confidently wrong 200, not a 400, for exactly the tool whose whole job is to
    // be trusted provenance. Reject it here instead, the same way an unknown search parameter is already
    // rejected per-parameter deeper in the pipeline. Shared by every route below (type/compartment/
    // $everything all pass the resource type they're compiling against).
    private async Task<IActionResult> CompileAndRespondAsync(
        string fhirVersion,
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        Expression? operationExpression,
        CancellationToken cancellationToken)
    {
        var engine = engineFactory.Get(fhirVersion);

        if (!engine.SearchParameters.TryGetSearchParameters(resourceType, out _))
        {
            return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a supported FHIR resource type for {fhirVersion}." });
        }

        var resolver = new InMemorySymbolResolver();

        SearchTrace trace;
        try
        {
            trace = await SearchCompiler.CompileAsync(
                resourceType,
                parameters,
                engine.Builder,
                resolver,
                engine.Compartments,
                engine.SearchParameters,
                operationExpression,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SearchCompiler records Resolve/Lower/Emit failures as trace data rather than throwing; a throw
            // here is an unexpected shape (e.g. a malformed query the parser rejected outright). Surface it
            // as a 400 rather than a 500, consistent with the bench's plain-JSON error convention.
            logger.LogWarning(ex, "Search trace failed for {FhirVersion}/{ResourceType}", fhirVersion, resourceType);
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        return new OkObjectResult(SearchTraceMapper.ToResponse(trace, resourceType));
    }
}
