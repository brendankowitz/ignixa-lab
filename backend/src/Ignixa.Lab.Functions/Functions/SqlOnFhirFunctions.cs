using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// SQL-on-FHIR-v2 ViewDefinition evaluation endpoint. Scoped to what a
/// stateless bench can support: inline ViewDefinition + inline resources
/// only - no server-stored views/resources, since this app has no
/// persistent FHIR data store.
/// </summary>
public sealed class SqlOnFhirFunctions(ILogger<SqlOnFhirFunctions> logger, SqlOnFhirService sqlOnFhirService)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // These all presuppose a persistent, server-side FHIR data store this
    // app doesn't have (server-stored views/resources, patient/group
    // compartment filtering, incremental sync) - reject explicitly rather
    // than silently ignoring them.
    private static readonly string[] UnsupportedParameterNames =
        ["viewReference", "patient", "group", "source", "_since"];

    [Function("SqlOnFhirViewDefinitionRun")]
    public async Task<IActionResult> RunViewDefinition(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "ViewDefinition/$viewdefinition-run")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SQL on FHIR ViewDefinition run (Ignixa)");

        ParametersJsonNode operationParameters;
        try
        {
            operationParameters = await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(request.Body, cancellationToken);
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse($"Invalid request body: {ex.Message}");
        }

        var (built, error) = BuildSqlOnFhirRequest(operationParameters);
        if (error != null)
        {
            return CreateErrorResponse(error);
        }

        var result = sqlOnFhirService.Evaluate(built!);
        if (!result.IsSuccess)
        {
            return CreateErrorResponse(result.Error!, result.ErrorDiagnostics);
        }

        return new ContentResult
        {
            Content = JsonSerializer.Serialize(result.Rows, JsonOptions),
            ContentType = "application/json",
            StatusCode = (int)HttpStatusCode.OK
        };
    }

    private static (SqlOnFhirRequest? Request, string? Error) BuildSqlOnFhirRequest(ParametersJsonNode operationParameters)
    {
        foreach (var unsupported in UnsupportedParameterNames)
        {
            if (operationParameters.FindParameter(unsupported) != null)
            {
                return (null, $"The '{unsupported}' parameter is not supported: this endpoint has no persistent FHIR data store to resolve it against.");
            }
        }

        var formatParam = operationParameters.FindParameter("_format");
        var format = formatParam?.GetValueAs<string>();
        if (!string.IsNullOrEmpty(format) && !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"Unsupported _format '{format}': only 'json' is supported.");
        }

        var viewResourceParam = operationParameters.FindParameter("viewResource");
        ResourceJsonNode? viewResource;
        try
        {
            viewResource = viewResourceParam?.Resource;
        }
        catch (Exception ex)
        {
            return (null, $"Invalid viewResource: {ex.Message}");
        }
        if (viewResource == null)
        {
            return (null, "The 'viewResource' parameter is required and must be an embedded ViewDefinition resource.");
        }

        var resources = new List<ResourceJsonNode>();
        foreach (var resourceParam in operationParameters.Parameter.Where(p => p.Name == "resource"))
        {
            ResourceJsonNode? resource;
            try
            {
                resource = resourceParam.Resource;
            }
            catch (Exception ex)
            {
                return (null, $"Invalid resource: {ex.Message}");
            }
            if (resource == null)
            {
                // Previously this silently skipped the malformed entry (e.g. a
                // 'resource' parameter sent as valueString instead of an embedded
                // resource), so the ViewDefinition would run against fewer
                // resources than the caller sent with no indication anything
                // was dropped. Fail loudly instead, matching 'viewResource'.
                return (null, "Each 'resource' parameter must be an embedded resource.");
            }
            resources.Add(resource);
        }

        if (resources.Count == 0)
        {
            return (null, "At least one 'resource' parameter is required.");
        }

        int? limit = null;
        var limitParam = operationParameters.FindParameter("_limit");
        if (limitParam != null)
        {
            // GetValueAs<int>() swallows conversion failures and returns 0 for
            // any non-integer value (a numeric string, a decimal, a boolean,
            // or a mistyped value key), which SqlOnFhirService then silently
            // interprets as "truncate to zero rows" - a wrong-but-plausible
            // 200 response instead of a 400 validation error. Validate the
            // underlying JSON value directly instead.
            if (limitParam.GetValue() is not JsonValue limitValue || !limitValue.TryGetValue<int>(out var limitInt))
            {
                return (null, "The '_limit' parameter must be an integer.");
            }
            limit = limitInt;
        }

        return (new SqlOnFhirRequest { ViewResource = viewResource, Resources = resources, Limit = limit }, null);
    }

    private static IActionResult CreateErrorResponse(string message, string? diagnostics = null)
    {
        var outcome = ResultFormatter.CreateOperationOutcomeResult("error", "invalid", message, diagnostics);

        return new ContentResult
        {
            Content = outcome.SerializeToString(pretty: true),
            ContentType = "application/fhir+json",
            StatusCode = (int)HttpStatusCode.BadRequest
        };
    }
}
