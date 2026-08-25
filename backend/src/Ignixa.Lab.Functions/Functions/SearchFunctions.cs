using System.Text.RegularExpressions;
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Search-trace endpoints powering the Expression Benches "Search" bench. Given a FHIR search query, it
/// traces the query through parse → typed expression → lowered SQL plan → generated SQL via the
/// request-scoped <see cref="SearchSqlCompiler"/>, returning the cross-referenced provenance as plain JSON
/// (not a FHIR resource — this is bench tooling, so no OperationOutcome wrapping). Supports the same FHIR
/// version set as <see cref="Services.FhirPath.SchemaProviderFactory"/> (STU3, R4, R4B, R5, R6) via
/// <see cref="SearchEngineFactory.Get"/>, which defaults an unrecognized value to R4 rather than rejecting
/// the request — same permissive fallback the rest of this app uses for FHIR version strings. The version
/// actually used comes back as <see cref="SearchTraceResponse.FhirVersion"/> so that fallback is visible
/// rather than silent.
/// </summary>
public sealed partial class SearchFunctions(ILogger<SearchFunctions> logger, SearchEngineFactory engineFactory)
{
    /// <summary>The FHIR <c>id</c> grammar. Ids reach the emitted SQL as bound parameters, so this is not an
    /// injection guard — it is the same "reject what can never match rather than returning a confidently
    /// wrong 200" rule the resource-type and compartment-membership checks below apply.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9\-\.]{1,64}$")]
    private static partial Regex FhirIdPattern { get; }

    /// <summary>Every query key <see cref="EverythingTrace"/> understands. Ordinal because that is how the
    /// <see cref="IQueryCollection"/> lookups below read them — a wrong-cased key is a different key, and is
    /// rejected rather than quietly ignored.</summary>
    private static readonly HashSet<string> EverythingQueryKeys =
        new(["_type", "_since", "start", "end", "includeReferencedResources"], StringComparer.Ordinal);

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

        return CompileAndRespondAsync(fhirVersion, resourceType, request, operationExpression: null, cancellationToken);
    }

    [Function("SearchCompartmentTrace")]
    public async Task<IActionResult> CompartmentTrace(
        // This route's {resourceType} segment is structurally identical to SearchEverythingTrace's literal
        // "$everything" segment for a real request -- both templates are five segments, and this one's
        // {compartmentType}/{compartmentId}/{resourceType} are unconstrained parameters that match
        // "Patient/example/$everything" segment-for-segment. The Functions host maps routes in function-name
        // order (alphabetical, not
        // declaration order) and does not re-rank an ambiguous match by literal-vs-parameter specificity the
        // way plain ASP.NET Core MVC would, so without this constraint "Patient/example/$everything" silently
        // lands here instead of SearchEverythingTrace. The regex excludes only the "$everything" segment;
        // every real resource type and the "*" wildcard still match freely. The lowercase spelling is not a
        // case-sensitivity gap -- inline regex constraints compile with RegexOptions.IgnoreCase and literal
        // route segments match case-insensitively, so "$Everything" is excluded here and matched there, in
        // step. SearchFunctionsRouteDispatchTests pins the mutual exclusivity in both registration orders and
        // for case variants, so none of these premises has to be taken on trust.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{compartmentType}/{compartmentId}/{resourceType:regex(^(?!\\$everything$).+$)}")] HttpRequest request,
        string fhirVersion,
        string compartmentType,
        string compartmentId,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (!TryValidateFhirId(compartmentId, "compartment id", out var trimmedCompartmentId, out var idError))
        {
            return new BadRequestObjectResult(new { error = idError });
        }

        // Match on the enum's names rather than Enum.TryParse, which accepts far more than a compartment
        // name: any numeric string in range ("3" parses to Practitioner) and any comma-separated combination
        // ("Patient, Encounter" OR-parses to Practitioner), both of which Enum.IsDefined then waves through
        // because the *result* is a defined value. That silently traced a different compartment than the one
        // in the URL and returned 200 -- the exact "confidently wrong 200" the checks in this file exist to
        // prevent. Name-matching also gives us the canonical casing to pass downstream, so lowercase
        // "patient" is normalized here rather than 400ing later with a less clear error.
        var normalizedCompartmentType = Enum.GetNames<CompartmentType>()
            .FirstOrDefault(name => name.Equals(compartmentType, StringComparison.OrdinalIgnoreCase));
        if (normalizedCompartmentType is null)
        {
            return new BadRequestObjectResult(new { error = $"'{compartmentType}' is not a valid FHIR compartment type." });
        }
        var parsedCompartmentType = Enum.Parse<CompartmentType>(normalizedCompartmentType);

        // "*" (Patient/{id}/*) means "every resource type in the compartment" -- null filteredResourceTypes
        // is what the compiler reads as that wildcard. A specific type narrows to just it. The resource type
        // passed to the compiler for a wildcard is the compartment root itself (there is no single member
        // type to name); for a scoped search it's the actual member type being searched.
        var wildcard = resourceType == "*";
        var filteredResourceTypes = wildcard ? null : new HashSet<string> { resourceType };
        var compileResourceType = wildcard ? normalizedCompartmentType : resourceType;

        // Resolved for both branches, not just the scoped one: "is this compartment defined for this FHIR
        // version at all" is a question the wildcard case needs answered too. A wildcard that skipped the
        // lookup would compile a traversal over a compartment the version does not define -- no compartment
        // CTEs at all, returned as a confident 200.
        if (!engineFactory.Get(fhirVersion).Compartments.TryGetResourceTypes(parsedCompartmentType, out var memberResourceTypes))
        {
            return new BadRequestObjectResult(new
            {
                error = $"The '{normalizedCompartmentType}' compartment is not defined for {SearchEngineFactory.Resolve(fhirVersion)}.",
            });
        }

        // Nothing in the compiler rejects a resourceType that is a real FHIR resource but simply not a member
        // of this compartment (e.g. Patient is not a member of the Encounter compartment): the lowering stage
        // compiles it straight through to a plan whose WHERE clause folds to a literal "1 = 0" -- the
        // compartment-linking search parameter for that pair just doesn't exist -- and returns 200 with no
        // Failure set. That is the same confidently-wrong 200 the resourceType check in the shared helper
        // below prevents, so reject it here the same way, before compiling, using the compartment definition
        // manager as the source of truth for membership. (Stated as an invariant deliberately: the compiler
        // has not enforced this consistently across package versions, so what matters is that *we* do.)
        if (!wildcard && !memberResourceTypes.Contains(resourceType))
        {
            return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a member of the '{normalizedCompartmentType}' compartment." });
        }

        var operationExpression = new CompartmentSearchExpression(normalizedCompartmentType, trimmedCompartmentId, filteredResourceTypes);

        return await CompileAndRespondAsync(fhirVersion, compileResourceType, request, operationExpression, cancellationToken);
    }

    [Function("SearchEverythingTrace")]
    public async Task<IActionResult> EverythingTrace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/Patient/{patientId}/$everything")] HttpRequest request,
        string fhirVersion,
        string patientId,
        CancellationToken cancellationToken)
    {
        if (!TryValidateFhirId(patientId, "patient id", out var trimmedPatientId, out var idError))
        {
            return new BadRequestObjectResult(new { error = idError });
        }

        // The other two routes hand their whole query string to QueryParameterParser, so a name this app does
        // not recognize still comes back as an Ignored chip with a reason. $everything reads five fixed keys
        // and compiles from typed constructor arguments instead, so without this check anything else -- a
        // typo'd "_typ", a wrong-cased "_Since" (these lookups are ordinal), a stray "_count" -- is dropped
        // in silence and answered with a full unfiltered trace that looks exactly like the bare request. The
        // UI compounds it by explaining the empty parameter list as expected for $everything, so there is
        // nothing anywhere for the user to notice. Reject instead, same standard as an unknown _type value.
        var unknownKeys = request.Query.Keys
            .Where(key => !EverythingQueryKeys.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownKeys.Length > 0)
        {
            return new BadRequestObjectResult(new
            {
                error = $"'$everything' does not support parameter(s): {string.Join(", ", unknownKeys)}. " +
                        $"Supported: {string.Join(", ", EverythingQueryKeys.Order(StringComparer.Ordinal))}.",
            });
        }

        var engine = engineFactory.Get(fhirVersion);
        var resolvedVersion = SearchEngineFactory.Resolve(fhirVersion);

        if (!TryParseTypeFilter(request, engine, resolvedVersion, out var filteredResourceTypes, out var typeError))
        {
            return new BadRequestObjectResult(new { error = typeError });
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

        // Independently valid, jointly impossible: an inverted window compiles to a plan that can never
        // match, which is the same class of confidently-wrong 200 as every other check here.
        if (startDate is { } start && endDate is { } end && start > end)
        {
            return new BadRequestObjectResult(new { error = $"'start' ({start:O}) is after 'end' ({end:O})." });
        }

        var includeReferencedResources = true;
        if (request.Query.TryGetValue("includeReferencedResources", out var includeValues) && !string.IsNullOrWhiteSpace(includeValues.ToString()))
        {
            if (!bool.TryParse(includeValues.ToString(), out includeReferencedResources))
            {
                return new BadRequestObjectResult(new { error = $"'includeReferencedResources' value '{includeValues}' is not 'true' or 'false'." });
            }
        }

        var operationExpression = new PatientEverythingExpression(trimmedPatientId, startDate, endDate, sinceDate, filteredResourceTypes, includeReferencedResources);

        // $everything isn't parameter-driven: the five keys it accepts became typed constructor arguments on
        // the expression above, so no FHIR *search* parameters are left for QueryParameterParser (any other
        // key already 400'd at the EverythingQueryKeys check). Hence parameters: [] -- the empty Parameters
        // list the frontend's empty-state note describes (parameterSource: null says exactly that). The resource
        // type the compiler compiles against is always "Patient", the operation's anchor type; this route
        // only ever accepts Patient (PatientEverythingExpression is the library's only $everything
        // expression -- IExpressionVisitor declares a single VisitPatientEverything).
        return await CompileAndRespondAsync(fhirVersion, "Patient", parameterSource: null, operationExpression, cancellationToken);
    }

    private static IReadOnlyList<QueryParameter> ParseQuery(HttpRequest request)
    {
        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;
        return new QueryParameterParser().Parse(rawQuery);
    }

    private static bool TryValidateFhirId(string? id, string label, out string trimmed, out string? error)
    {
        trimmed = id?.Trim() ?? string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = $"A {label} is required.";
            return false;
        }

        if (!FhirIdPattern.IsMatch(trimmed))
        {
            error = $"'{trimmed}' is not a valid FHIR id (expected 1-64 characters from A-Z, a-z, 0-9, '-' and '.').";
            return false;
        }

        return true;
    }

    /// <summary>Parses <c>_type</c> into the expression's filter set. Null means "no filter" — both when the
    /// parameter is absent and when it holds nothing but separators (<c>?_type=,</c>), which is the same
    /// request in every respect that matters.</summary>
    private static bool TryParseTypeFilter(
        HttpRequest request,
        SearchEngine engine,
        string resolvedVersion,
        out HashSet<string>? filteredResourceTypes,
        out string? error)
    {
        filteredResourceTypes = null;
        error = null;

        if (!request.Query.TryGetValue("_type", out var typeValues) || string.IsNullOrWhiteSpace(typeValues.ToString()))
        {
            return true;
        }

        var requestedTypes = typeValues.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (requestedTypes.Count == 0)
        {
            return true;
        }

        // Two checks, not one. Existence catches a typo; Patient-compartment membership catches a real
        // resource type that $everything can still never return -- the lowering folds a non-member to an
        // always-false predicate, and because $everything reports zero Parameters that never surfaces as a
        // KnownMiss chip, only as a "1 = 0" in the SQL. Same standard the compartment route applies to its
        // member type. Note this is about _type specifically: referenced Practitioner/Organization/Location/
        // Medication resources are pulled in by includeReferencedResources, not by naming them in _type.
        var unknownTypes = requestedTypes
            .Where(type => !engine.SearchParameters.TryGetSearchParameters(type, out _))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownTypes.Length > 0)
        {
            error = $"'_type' contains unsupported resource type(s) for {resolvedVersion}: {string.Join(", ", unknownTypes)}.";
            return false;
        }

        if (!engine.Compartments.TryGetResourceTypes(CompartmentType.Patient, out var patientMembers))
        {
            error = $"The Patient compartment is not defined for {resolvedVersion}.";
            return false;
        }

        var nonMembers = requestedTypes
            .Where(type => !patientMembers.Contains(type))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (nonMembers.Length > 0)
        {
            error = $"'_type' contains resource type(s) that are not members of the Patient compartment: {string.Join(", ", nonMembers)}.";
            return false;
        }

        filteredResourceTypes = requestedTypes;
        return true;
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

    // SearchSqlCompiler never validates the top-level resourceType itself -- it only rejects an
    // unknown resource type when one appears as a chain/_has target (via SearchKeyBinder resolving a
    // ReferenceSearchParameter's target types). Given a resource type nothing recognizes, it happily
    // compiles a full plan and SQL against `dbo.Resource WHERE ResourceTypeId = @p0` for an ID that will
    // never match anything -- a confidently wrong 200, not a 400, for exactly the tool whose whole job is to
    // be trusted provenance. Reject it here instead, the same way an unknown search parameter is already
    // rejected per-parameter deeper in the pipeline. Shared by every route above (type/compartment/
    // $everything all pass the resource type they're compiling against).
    /// <param name="parameterSource">The request whose query string supplies the FHIR search parameters, or
    /// null for an operation that takes none ($everything). Parsing inside the try lets the catches below
    /// classify parser failures alongside compiler failures instead of letting them escape to the host as a bare
    /// 500.</param>
    private async Task<IActionResult> CompileAndRespondAsync(
        string fhirVersion,
        string resourceType,
        HttpRequest? parameterSource,
        Expression? operationExpression,
        CancellationToken cancellationToken)
    {
        var resolvedVersion = SearchEngineFactory.Resolve(fhirVersion);

        SearchCompilationResult compiled;
        try
        {
            var engine = engineFactory.Get(fhirVersion);
            if (!engine.SearchParameters.TryGetSearchParameters(resourceType, out _))
            {
                return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a supported FHIR resource type for {resolvedVersion}." });
            }

            var compiler = new SearchSqlCompiler(
                new InMemorySymbolResolver(),
                engine.Builder,
                engine.Compartments,
                engine.SearchParameters,
                TimeProvider.System);

            var parameters = parameterSource is null ? [] : ParseQuery(parameterSource);
            var planOptions = new SearchPlanOptions
            {
                OperationExpression = operationExpression,
                DiagnosticsLevel = SearchDiagnosticsLevel.Full,
            };

            var plan = await compiler.CreatePlanAsync(
                resourceType,
                parameters,
                planOptions,
                cancellationToken);

            compiled = plan.TryCompile();
        }
        catch (OperationCanceledException)
        {
            // Before the catches below, so a client disconnect (TaskCanceledException derives from this)
            // isn't reported back as the client's malformed query.
            throw;
        }
        catch (Exception ex) when (ex is BadSearchRequestException or SearchResourceNotSupportedException)
        {
            // Input the compiler rejects outright rather than recording as trace data -- e.g.
            // BadSearchRequestException ("The date time string 'notadate' is not in a correct format.") for a
            // malformed value. These messages are written for the person who typed the query, so echoing them
            // is the useful answer for a bench.
            //
            // Allowlisted by concrete type, deliberately, rather than by their FhirException base: that base
            // is the library's *whole* fault hierarchy, not a "bad request" marker. InternalServerErrorException
            // and InvalidDefinitionException (the likely shape of a package-bump regression -- this app pins an
            // alpha and bumps it often) both derive from it, so catching the base reported our own faults to an
            // anonymous caller as their bad query, echoed the raw message, and logged at Information -- the
            // exact defect the 500 arm below was written to fix. Anything not named here falls through to it.
            logger.LogInformation(ex, "Rejected search trace for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        catch (SearchCompilationException ex)
        {
            // Preserve expected compiler failures as trace data instead of returning a generic 500.
            compiled = SearchCompilationResult.Failed(ex.Failure);
        }
        catch (NotSupportedException ex)
        {
            // A query shape the alpha SQL compiler has not implemented yet. Still the caller's input, so 400
            // rather than 500 -- but with our own wording: these messages are addressed to the library's
            // maintainers and have been observed carrying internal repo paths, which should not be echoed to
            // an anonymous caller. The detail stays in the log.
            logger.LogWarning(ex, "Unsupported search shape for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
            return new BadRequestObjectResult(new { error = "This query uses a shape the SQL compiler does not support yet." });
        }
        catch (Exception ex)
        {
            // Anything else is a fault on our side, not the caller's. It used to be reported as a 400 with
            // ex.Message, which meant a version-skewed deployment (MissingMethodException/TypeLoadException
            // after a package bump) told the user their query was invalid, echoed assembly identities back to
            // an anonymous caller, and -- being a warning-level 4xx -- tripped no failure-rate alert.
            logger.LogError(ex, "Unexpected failure compiling search trace for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
            return new ObjectResult(new { error = "The search trace could not be compiled due to an internal error." })
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            };
        }

        // Mapped in its own try, not the one above: SearchTraceMapper throws NotSupportedException for a
        // ParameterOutcome it does not model, and this PR adding KnownMiss is the proof that outcome types do
        // get added -- inside the block above, that mapper gap would be reported to the caller as "this query
        // uses a shape the SQL compiler does not support yet", blaming their query for our missing arm. Left
        // unguarded it escaped the method entirely, losing the {FhirVersion}/{ResourceType} context every
        // other failure path here attaches.
        try
        {
            return compiled.Succeeded
                ? new OkObjectResult(SearchTraceMapper.ToResponse(compiled.Compiled, resolvedVersion, resourceType))
                : new OkObjectResult(SearchTraceMapper.ToResponse(compiled.Failure, resolvedVersion, resourceType));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to map search trace for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
            return new ObjectResult(new { error = "The search trace could not be serialized due to an internal error." })
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            };
        }
    }
}
