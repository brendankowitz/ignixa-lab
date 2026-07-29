using System.Text.RegularExpressions;
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Serialization.Abstractions;
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
/// the request — same permissive fallback the rest of this app uses for FHIR version strings. The version
/// actually used comes back as <see cref="SearchTraceResponse.FhirVersion"/> so that fallback is visible
/// rather than silent.
/// </summary>
public sealed partial class SearchFunctions(ILogger<SearchFunctions> logger, SearchEngineFactory engineFactory)
{
    /// <summary>The FHIR <c>id</c> grammar. Ids reach the emitted SQL as bound parameters, so this is not a
    /// injection guard — it is the same "reject what can never match rather than returning a confidently
    /// wrong 200" rule the resource-type and compartment-membership checks below apply.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9\-\.]{1,64}$")]
    private static partial Regex FhirIdPattern { get; }

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

        var parameters = ParseQuery(request);

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
        // every real resource type and the "*" wildcard still match freely. SearchFunctionsRouteDispatchTests
        // pins the mutual exclusivity in both registration orders, so neither premise has to be taken on
        // trust.
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

        if (!wildcard)
        {
            // Nothing in the compiler rejects a resourceType that is a real FHIR resource but simply not a
            // member of this compartment (e.g. Patient is not a member of the Encounter compartment): the
            // lowering stage compiles it straight through to a plan whose WHERE clause folds to a literal
            // "1 = 0" -- the compartment-linking search parameter for that pair just doesn't exist -- and
            // returns 200 with no Failure set. That is the same confidently-wrong 200 CompileAndRespondAsync's
            // own resourceType check prevents one level up, so reject it here the same way, before compiling,
            // using the compartment definition manager as the source of truth for membership. (Stated as an
            // invariant deliberately: the compiler has enforced neither behaviour consistently across
            // versions -- 0.6.28 threw here instead of folding -- so what matters is that *we* enforce it.)
            var membershipEngine = engineFactory.Get(fhirVersion);
            if (!membershipEngine.Compartments.TryGetResourceTypes(parsedCompartmentType, out var memberResourceTypes) ||
                !memberResourceTypes.Contains(resourceType))
            {
                return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a member of the '{normalizedCompartmentType}' compartment." });
            }
        }

        var parameters = ParseQuery(request);
        var operationExpression = new CompartmentSearchExpression(normalizedCompartmentType, trimmedCompartmentId, filteredResourceTypes);

        return await CompileAndRespondAsync(fhirVersion, compileResourceType, parameters, operationExpression, cancellationToken);
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

        // $everything isn't parameter-driven -- there's no query string for QueryParameterParser to parse,
        // every option above became a typed constructor argument on the expression instead. The resource
        // type the compiler compiles against is always "Patient", the operation's anchor type; this route
        // only ever accepts Patient (PatientEverythingExpression is the library's only $everything
        // expression -- IExpressionVisitor declares a single VisitPatientEverything).
        return await CompileAndRespondAsync(fhirVersion, "Patient", parameters: [], operationExpression, cancellationToken);
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

    // SearchCompiler.CompileAsync never validates the top-level resourceType itself -- it only rejects an
    // unknown resource type when one appears as a chain/_has target (via SearchKeyBinder resolving a
    // ReferenceSearchParameter's target types). Given a resource type nothing recognizes, it happily
    // compiles a full plan and SQL against `dbo.Resource WHERE ResourceTypeId = @p0` for an ID that will
    // never match anything -- a confidently wrong 200, not a 400, for exactly the tool whose whole job is to
    // be trusted provenance. Reject it here instead, the same way an unknown search parameter is already
    // rejected per-parameter deeper in the pipeline. Shared by every route above (type/compartment/
    // $everything all pass the resource type they're compiling against).
    private async Task<IActionResult> CompileAndRespondAsync(
        string fhirVersion,
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        Expression? operationExpression,
        CancellationToken cancellationToken)
    {
        var engine = engineFactory.Get(fhirVersion);
        var resolvedVersion = SearchEngineFactory.Resolve(fhirVersion);

        if (!engine.SearchParameters.TryGetSearchParameters(resourceType, out _))
        {
            return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a supported FHIR resource type for {resolvedVersion}." });
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
            // Before the catches below, so a client disconnect (TaskCanceledException derives from this)
            // isn't reported back as the client's malformed query.
            throw;
        }
        catch (Exception ex) when (ex is FhirException or FormatException)
        {
            // Input the compiler rejects outright rather than recording as trace data -- e.g.
            // BadSearchRequestException ("The date time string 'notadate' is not in a correct format.") for a
            // malformed value. These messages are written for the person who typed the query, so echoing them
            // is the useful answer for a bench.
            logger.LogInformation(ex, "Rejected search trace for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
            return new BadRequestObjectResult(new { error = ex.Message });
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

        return new OkObjectResult(SearchTraceMapper.ToResponse(trace, resolvedVersion, resourceType));
    }
}
