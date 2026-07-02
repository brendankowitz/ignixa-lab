using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// FHIRPath expression evaluation endpoints for fhirpath-lab.com's "Ignixa"
/// dotnet engine. Ported from Brian's fhirpath-lab-dotnet
/// (FhirPathLab-DotNetEngine) — routes are unchanged from that repo so the
/// fhirpath-lab.com UI can repoint at this app without a UI-side change.
/// </summary>
public sealed class FhirPathFunctions(ILogger<FhirPathFunctions> logger, FhirPathService fhirPathService)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Function("FhirPathMetadata")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public IActionResult RunCapabilityStatement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "metadata")] HttpRequest request)
    {
        var capabilityStatement = new JsonObject
        {
            ["resourceType"] = "CapabilityStatement",
            ["title"] = "FHIRPath Lab DotNet expression evaluator (Ignixa)",
            ["description"] = "Supports FHIR STU3, R4, R4B, R5, R6. Features: AST output, trace, validation.",
            ["status"] = "active",
            // Static publish date for this CapabilityStatement; not tied to deployment time.
            ["date"] = "2026-01-19",
            ["kind"] = "instance",
            ["fhirVersion"] = "4.0.1",
            ["format"] = new JsonArray { "application/fhir+json" },
            ["implementationGuide"] = new JsonArray { "STU3", "R4", "R4B", "R5", "R6" },
            ["rest"] = new JsonArray
            {
                new JsonObject
                {
                    ["mode"] = "server",
                    ["security"] = new JsonObject { ["cors"] = true },
                    ["operation"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "fhirpath",
                            ["definition"] = "http://fhirpath-lab.org/OperationDefinition/fhirpath"
                        }
                    }
                }
            }
        };

        return new ContentResult
        {
            Content = capabilityStatement.ToJsonString(JsonOptions),
            ContentType = "application/fhir+json",
            StatusCode = (int)HttpStatusCode.OK
        };
    }

    [Function("FhirPathStu3")]
    public Task<IActionResult> RunFhirPathTestStu3(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "$fhirpath-stu3")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessFhirPathRequest(request, "STU3", cancellationToken);

    [Function("FhirPathR4")]
    public Task<IActionResult> RunFhirPathTestR4(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "$fhirpath-r4")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessFhirPathRequest(request, "R4", cancellationToken);

    [Function("FhirPathR4B")]
    public Task<IActionResult> RunFhirPathTestR4B(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "$fhirpath-r4b")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessFhirPathRequest(request, "R4B", cancellationToken);

    [Function("FhirPathR5")]
    public Task<IActionResult> RunFhirPathTestR5(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "$fhirpath-r5")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessFhirPathRequest(request, "R5", cancellationToken);

    [Function("FhirPathR6")]
    public Task<IActionResult> RunFhirPathTestR6(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "$fhirpath-r6")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessFhirPathRequest(request, "R6", cancellationToken);

    [Function("FhirPathWarmer")]
    public void WarmUp([TimerTrigger("0 */15 * * * *")] TimerInfo timer)
    {
        logger.LogInformation("FhirPath warmer function executed at: {Time}", DateTime.Now);
    }

    private async Task<IActionResult> ProcessFhirPathRequest(HttpRequest request, string fhirVersion, CancellationToken cancellationToken)
    {
        logger.LogInformation("FhirPath Expression Evaluation (Ignixa) - FHIR {Version}", fhirVersion);

        var (operationParameters, parseError) = await ParseOperationParameters(request, cancellationToken);
        if (parseError != null)
        {
            return CreateErrorResponse(parseError);
        }

        var (built, error, errorDiagnostics) = await BuildFhirPathRequestAsync(operationParameters!, fhirVersion, cancellationToken);

        if (error != null)
        {
            return CreateErrorResponse(error, errorDiagnostics);
        }

        var result = fhirPathService.ProcessRequest(built!);

        return new ContentResult
        {
            Content = result.SerializeToString(pretty: true),
            ContentType = "application/fhir+json",
            StatusCode = (int)HttpStatusCode.OK
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

    private async Task<(FhirPathRequest? Request, string? Error, string? ErrorDiagnostics)> BuildFhirPathRequestAsync(
        ParametersJsonNode operationParameters,
        string fhirVersion,
        CancellationToken cancellationToken)
    {
        var resourceParam = operationParameters.FindParameter("resource");
        var contextParam = operationParameters.FindParameter("context");
        var expressionParam = operationParameters.FindParameter("expression");
        var terminologyParam = operationParameters.FindParameter("terminologyserver");
        var variablesParam = operationParameters.FindParameter("variables");
        var debugTraceParam = operationParameters.FindParameter("debug_trace");

        // Extracting the embedded resource can throw if its "resourceType" is
        // present but not the JSON string FHIR requires (e.g. a number) - a
        // plausible malformed shape on this fully attacker-controlled request
        // body. Guard it here so it becomes a structured 400 response instead
        // of an unhandled exception.
        ResourceJsonNode? resource;
        try
        {
            resource = resourceParam?.Resource;
        }
        catch (Exception ex)
        {
            return (null, $"Invalid resource: {ex.Message}", null);
        }

        string? resourceId = resource == null ? resourceParam?.GetValueAs<string>() : null;
        string? context = contextParam?.GetValueAs<string>();
        string? expression = expressionParam?.GetValueAs<string>();
        string? terminologyServerUrl = terminologyParam?.GetValueAs<string>();
        bool debugTrace = debugTraceParam?.GetValueAs<bool>() ?? false;

        // Load resource from remote server if URL provided
        if (resource == null && !string.IsNullOrEmpty(resourceId) &&
            resourceId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var (loadedResource, error) = await fhirPathService.LoadResourceFromUrl(resourceId, cancellationToken);
            if (error != null)
            {
                return (null, error, resourceId);
            }
            resource = loadedResource;
        }

        if (string.IsNullOrEmpty(expression))
        {
            return (null, "Expression parameter is required", null);
        }

        var request = new FhirPathRequest
        {
            Resource = resource,
            ResourceId = resourceId,
            Context = context,
            Expression = expression,
            TerminologyServerUrl = terminologyServerUrl,
            Variables = variablesParam,
            DebugTrace = debugTrace,
            FhirVersion = fhirVersion
        };

        return (request, null, null);
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
