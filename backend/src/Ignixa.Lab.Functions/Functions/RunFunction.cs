using System.Text.Json;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Executes the selected suites against a target FHIR server and returns a
/// conformance report. Invalid requests return HTTP 400 with an explanatory
/// message; successful runs return HTTP 200 with the report body.
/// </summary>
public sealed class RunFunction(TestScriptRunner runner, ILogger<RunFunction> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Function("Run")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "run")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        RunRequest? runRequest;
        try
        {
            runRequest = await JsonSerializer.DeserializeAsync<RunRequest>(
                request.Body, SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (runRequest is null)
        {
            return new BadRequestObjectResult(new { error = "A request body is required." });
        }

        var outcome = await runner.RunAsync(runRequest, cancellationToken);
        if (!outcome.IsValid)
        {
            return new BadRequestObjectResult(new { error = outcome.Error });
        }

        logger.LogInformation(
            "Completed run against {Target} with {Count} results.",
            outcome.Report!.Target,
            outcome.Report.Results.Count);

        return new OkObjectResult(outcome.Report);
    }
}
