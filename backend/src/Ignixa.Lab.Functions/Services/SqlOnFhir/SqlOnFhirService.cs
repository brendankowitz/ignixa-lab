using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.SqlOnFhir.Evaluation;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Services.SqlOnFhir;

/// <summary>
/// Orchestrator service for SQL-on-FHIR ViewDefinition evaluation via
/// <c>Ignixa.SqlOnFhir</c>.
/// </summary>
public sealed class SqlOnFhirService
{
    private readonly SchemaProviderFactory _schemaFactory;
    private readonly ILogger<SqlOnFhirService> _logger;

    public SqlOnFhirService(SchemaProviderFactory schemaFactory, ILogger<SqlOnFhirService> logger)
    {
        _schemaFactory = schemaFactory;
        _logger = logger;
    }

    public SqlOnFhirResult Evaluate(SqlOnFhirRequest request, CancellationToken cancellationToken = default)
    {
        // SqlOnFhirEvaluator is fully synchronous and exposes no cancellation hook,
        // so the token can only be observed at the method boundary - a cancelled
        // request is rejected before the (CPU-bound) evaluation begins rather than
        // being interruptible mid-evaluation.
        cancellationToken.ThrowIfCancellationRequested();

        // SqlOnFhirEvaluator doesn't validate `select`'s structure before processing it -
        // a non-array `select` (e.g. a string) OR a missing `select` is silently treated
        // as "no select items", producing an empty row rather than an error. Check
        // explicitly so malformed input surfaces as a validation error instead of a
        // silent, confusing result. `select` is required and must be an array.
        if (request.ViewResource.MutableNode["select"] is not JsonArray)
        {
            return SqlOnFhirResult.Failure(request, "ViewDefinition evaluation error: 'select' must be an array.");
        }

        try
        {
            var schema = _schemaFactory.GetSchema("R4");
            var navigator = request.ViewResource.ToSourceNavigator();
            var elements = request.Resources.Select(r => r.ToElement(schema));

            // SqlOnFhirEvaluator is NOT stateless: it caches compiled
            // ViewDefinitionExpressions in a plain, unsynchronized Dictionary
            // keyed by the navigator's default (identity-based) hash code.
            // A shared/static instance would (a) never actually hit its own
            // cache, since every request builds a fresh navigator with a
            // fresh identity hash, leaking one never-evicted entry per
            // request forever, and (b) be unsafe under Azure Functions'
            // concurrent request dispatch, since the dictionary is mutated
            // via unsynchronized TryGetValue+indexer-set. Constructing one
            // per call avoids both problems at a negligible per-request cost.
            var evaluator = new SqlOnFhirEvaluator();
            var rows = evaluator.EvaluateBatch(navigator, elements).ToList();

            if (request.Limit is { } limit && limit >= 0 && limit < rows.Count)
            {
                rows = rows.Take(limit).ToList();
            }

            return SqlOnFhirResult.Success(request, rows);
        }
        catch (Exception ex)
        {
            // A failure here is an engine-side fault (not user input validation), so
            // log it server-side. Log the resource count, not the resource payloads,
            // to avoid writing potentially large/sensitive data to logs.
            _logger.LogError(ex, "SQL-on-FHIR evaluation failed ({ResourceCount} resources).", request.Resources.Count);
            return SqlOnFhirResult.Failure(request, $"ViewDefinition evaluation error: {ex.Message}");
        }
    }
}
