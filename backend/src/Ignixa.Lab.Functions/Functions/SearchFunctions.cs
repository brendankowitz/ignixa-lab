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
        // This route's {resourceType} segment is structurally identical to SearchEverythingTrace's literal
        // "$everything" segment for a real request -- both are 4 segments deep with {fhirVersion} and a
        // Patient-shaped middle. The Functions host maps routes in function-name order (alphabetical, not
        // declaration order) and does not re-rank an ambiguous match by literal-vs-parameter specificity the
        // way plain ASP.NET Core MVC would, so without this constraint "Patient/example/$everything" silently
        // lands here instead of SearchEverythingTrace. The regex excludes only the literal "$everything";
        // every real resource type and the "*" wildcard still match freely.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{compartmentType}/{compartmentId}/{resourceType:regex(^(?!\\$everything$).+$)}")] HttpRequest request,
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

        // Enum.TryParse(ignoreCase: true) alone accepts numeric strings too (e.g. "999" parses as a
        // valid-looking-but-undefined enum value) -- IsDefined catches that. Capturing the parsed value (not
        // discarding it) lets normalizedCompartmentType below pass the canonical name downstream instead of
        // the raw string, so e.g. lowercase "patient" doesn't sail past this check only to 400 later via a
        // different, less clear error out of CompartmentSearchExpression.
        if (!Enum.TryParse<CompartmentType>(compartmentType, ignoreCase: true, out var parsedCompartmentType) || !Enum.IsDefined(parsedCompartmentType))
        {
            return new BadRequestObjectResult(new { error = $"'{compartmentType}' is not a valid FHIR compartment type." });
        }
        var normalizedCompartmentType = parsedCompartmentType.ToString();

        // "*" (Patient/{id}/*) means "every resource type in the compartment" -- null filteredResourceTypes
        // is what CompartmentSearchExpression reads as that wildcard. A specific type narrows to just it.
        // The resource type passed to the compiler for a wildcard is the compartment root itself (there is
        // no single member type to name); for a scoped search it's the actual member type being searched.
        var wildcard = resourceType == "*";
        var filteredResourceTypes = wildcard ? null : new HashSet<string> { resourceType };
        var compileResourceType = wildcard ? normalizedCompartmentType : resourceType;

        if (!wildcard)
        {
            // Confirmed live: CompartmentSearchExpression does NOT reject a resourceType that's a real FHIR
            // resource but simply not a member of this compartment (e.g. Patient is not a member of the
            // Encounter compartment) -- it happily compiles a plan/SQL with a literal `1 = 0` folded into the
            // WHERE clause (the compartment-linking search parameter for that pair just doesn't exist) and
            // returns 200 with no Failure set. That's exactly the "confidently wrong 200" CompileAndRespondAsync's
            // own resourceType check exists to prevent for the top-level type -- reject it here the same way,
            // before compiling, using the compartment definition manager as the source of truth for membership.
            var membershipEngine = engineFactory.Get(fhirVersion);
            if (!membershipEngine.Compartments.TryGetResourceTypes(parsedCompartmentType, out var memberResourceTypes) ||
                !memberResourceTypes.Contains(resourceType))
            {
                return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a member of the '{normalizedCompartmentType}' compartment." });
            }
        }

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;
        var parameters = new QueryParameterParser().Parse(rawQuery);

        var operationExpression = new CompartmentSearchExpression(normalizedCompartmentType, compartmentId.Trim(), filteredResourceTypes);

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

            // Same "reject unknown things with a 400" philosophy CompileAndRespondAsync's own resourceType
            // check and the compartment route's member-resourceType check already apply -- an unrecognized
            // _type value would otherwise sail straight into PatientEverythingExpression and just never match
            // anything, a confidently wrong 200 rather than a 400 for a tool whose whole job is trustworthy
            // tracing.
            var engineForTypeCheck = engineFactory.Get(fhirVersion);
            var unknownTypes = filteredResourceTypes
                .Where(type => !engineForTypeCheck.SearchParameters.TryGetSearchParameters(type, out _))
                .ToArray();
            if (unknownTypes.Length > 0)
            {
                return new BadRequestObjectResult(new { error = $"'_type' contains unsupported resource type(s) for {fhirVersion}: {string.Join(", ", unknownTypes)}." });
            }
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

        var operationExpression = new PatientEverythingExpression(patientId.Trim(), startDate, endDate, sinceDate, filteredResourceTypes, includeReferencedResources);

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

        // Invariant culture + AssumeUniversal|AdjustToUniversal so the same request URL parses to the same
        // instant regardless of the host's culture (e.g. "01/02/2020" is culture-ambiguous) or local
        // timezone (an offset-less value like "2026-01-01T00:00:00" would otherwise silently pick up the
        // server's local offset, not UTC) -- this is bench tooling that must produce the same SQL parameter
        // values wherever it runs.
        if (!DateTimeOffset.TryParse(
                rawValues.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
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
