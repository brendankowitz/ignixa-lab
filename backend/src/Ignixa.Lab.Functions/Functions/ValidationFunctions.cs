using System.Net;
using System.Text.Json;
using Ignixa.Lab.Functions.Models.Validation;
using Ignixa.Lab.Functions.Services.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Resource validation endpoint powering the Expression Benches validation bench.</summary>
public sealed class ValidationFunctions(
    ILogger<ValidationFunctions> logger,
    ResourceValidationService validationService)
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    [Function("ResourceValidation")]
    public async Task<IActionResult> ValidateResource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "validate")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ResourceValidationRequest? validationRequest;
        try
        {
            validationRequest = await JsonSerializer.DeserializeAsync<ResourceValidationRequest>(
                request.Body,
                RequestJsonOptions,
                cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (validationRequest is null)
        {
            return new BadRequestObjectResult(new { error = "A request body is required." });
        }

        try
        {
            var result = await validationService.ValidateAsync(validationRequest, cancellationToken);
            return new OkObjectResult(result);
        }
        catch (InvalidResourceException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        catch (UnsupportedValidationOptionException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Resource validation failed. fhirVersion={FhirVersion} depth={Depth}",
                validationRequest.FhirVersion,
                validationRequest.Depth);
            return new ObjectResult(new { error = "Resource validation failed." })
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
            };
        }
    }
}
