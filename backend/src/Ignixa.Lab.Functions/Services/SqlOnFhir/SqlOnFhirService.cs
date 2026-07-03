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
    // SqlOnFhirEvaluator exposes a parameterless ctor and a ClearCache()
    // method, implying it internally parses/caches the ViewDefinition rather
    // than holding per-execution mutable state (the same shape as
    // MappingParser/FhirPathParser, which are safely used as static fields
    // elsewhere in this codebase) - so a shared static instance is used here
    // rather than constructing one per request.
    private static readonly SqlOnFhirEvaluator Evaluator = new();

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

            var rows = Evaluator.EvaluateBatch(navigator, elements).ToList();

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
