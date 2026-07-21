using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Search-trace endpoint powering the Expression Benches "Search" bench. Given a FHIR search query, it
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
    public async Task<IActionResult> Trace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{resourceType}")] HttpRequest request,
        string fhirVersion,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return new BadRequestObjectResult(new { error = "A resource type is required." });
        }

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;

        var parameters = new QueryParameterParser().Parse(rawQuery);
        var engine = engineFactory.Get(fhirVersion);
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
                cancellationToken);
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
            logger.LogWarning(ex, "Search trace failed for {FhirVersion}/{ResourceType}?{Query}", fhirVersion, resourceType, rawQuery);
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        return new OkObjectResult(SearchTraceMapper.ToResponse(trace));
    }
}
