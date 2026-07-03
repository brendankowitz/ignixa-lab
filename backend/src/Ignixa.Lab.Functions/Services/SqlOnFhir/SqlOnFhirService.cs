using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.SqlOnFhir.Evaluation;
using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Services.SqlOnFhir;

/// <summary>
/// Orchestrator service for SQL-on-FHIR ViewDefinition evaluation via
/// <c>Ignixa.SqlOnFhir</c>.
/// </summary>
public sealed class SqlOnFhirService
{
    private readonly SchemaProviderFactory _schemaFactory;

    public SqlOnFhirService(SchemaProviderFactory schemaFactory)
    {
        _schemaFactory = schemaFactory;
    }

    public SqlOnFhirResult Evaluate(SqlOnFhirRequest request)
    {
        // SqlOnFhirEvaluator doesn't validate `select`'s structure before processing it -
        // a non-array `select` (e.g. a string) is silently treated as "no select items",
        // producing an empty row rather than an error. Check explicitly so malformed
        // input surfaces as a validation error instead of a silent, confusing result.
        if (request.ViewResource.MutableNode["select"] is { } selectNode and not JsonArray)
        {
            return new SqlOnFhirResult
            {
                Request = request,
                Error = "ViewDefinition evaluation error: 'select' must be an array."
            };
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

            return new SqlOnFhirResult { Request = request, Rows = rows! };
        }
        catch (Exception ex)
        {
            return new SqlOnFhirResult
            {
                Request = request,
                Error = $"ViewDefinition evaluation error: {ex.Message}"
            };
        }
    }
}
