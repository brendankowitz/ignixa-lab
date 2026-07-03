using System.Net;
using System.Text.Json;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// FHIR Mapping Language (FML) transform endpoint, compatible with the
/// request/response shape fhirpath-lab.com's "mapper_server" UI expects
/// from a StructureMap/$transform backend.
/// </summary>
public sealed class FmlFunctions(
    ILogger<FmlFunctions> logger,
    FmlService fmlService,
    FmlResultFormatter resultFormatter,
    FhirPathService fhirPathService)
{
    [Function("FmlTransform")]
    public Task<IActionResult> RunTransform(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "StructureMap/$transform")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessTransformRequest(request, cancellationToken);

    private async Task<IActionResult> ProcessTransformRequest(HttpRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("FML Transform (Ignixa)");

        var (operationParameters, parseError) = await ParseOperationParameters(request, cancellationToken);
        if (parseError != null)
        {
            return CreateErrorResponse(parseError);
        }

        var (built, error, errorDiagnostics) = await BuildFmlRequestAsync(operationParameters!, cancellationToken);
        if (error != null)
        {
            return CreateErrorResponse(error, errorDiagnostics);
        }

        var result = fmlService.Transform(built!);
        var debug = bool.TryParse(request.Query["debug"], out var debugFlag) && debugFlag;
        var formatted = resultFormatter.FormatResult(result, debug);

        return new ContentResult
        {
            Content = formatted.SerializeToString(pretty: true),
            ContentType = "application/fhir+json",
            StatusCode = result.IsSuccess ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest
        };
    }

    private static async Task<(ParametersJsonNode? Parameters, string? Error)> ParseOperationParameters(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method != "POST")
        {
            var parameters = new ParametersJsonNode();
            foreach (var key in request.Query.Keys)
            {
                var param = new ParameterJsonNode { Name = key };
                param.SetValue("valueString", request.Query[key].ToString());
                parameters.Parameter.Add(param);
            }
            return (parameters, null);
        }

        try
        {
            return (await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(request.Body, cancellationToken), null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid request body: {ex.Message}");
        }
    }

    private async Task<(FmlRequest? Request, string? Error, string? ErrorDiagnostics)> BuildFmlRequestAsync(
        ParametersJsonNode operationParameters,
        CancellationToken cancellationToken)
    {
        var mapParam = operationParameters.FindParameter("map");
        var resourceParam = operationParameters.FindParameter("resource");

        var map = mapParam?.GetValueAs<string>();
        if (string.IsNullOrEmpty(map))
        {
            return (null, "The 'map' parameter is required", null);
        }

        ResourceJsonNode? resource;
        try
        {
            resource = resourceParam?.Resource;
        }
        catch (Exception ex)
        {
            return (null, $"Invalid resource: {ex.Message}", null);
        }

        if (resource == null)
        {
            var resourceText = resourceParam?.GetValueAs<string>();
            if (string.IsNullOrEmpty(resourceText))
            {
                return (null, "The 'resource' parameter is required", null);
            }

            if (resourceText.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var (loadedResource, error) = await fhirPathService.LoadResourceFromUrl(resourceText, cancellationToken);
                if (error != null)
                {
                    return (null, error, resourceText);
                }
                resource = loadedResource;
            }
            else
            {
                try
                {
                    resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(resourceText);
                }
                catch (JsonException ex)
                {
                    return (null, $"Invalid resource JSON: {ex.Message}", resourceText);
                }
            }
        }

        return (new FmlRequest { Map = map, Resource = resource! }, null, null);
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
