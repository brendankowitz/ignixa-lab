using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.Population;
using Ignixa.Lab.Functions.Models.Fakes;
using Ignixa.Lab.Functions.Services.Fakes;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Synthetic FHIR data generation endpoints, powering the Expression Benches Fakes bench.</summary>
public sealed class FakesFunctions(
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery,
    FakesService fakesService)
{
    private static readonly string[] FhirVersions = ["stu3", "r4", "r4b", "r5", "r6"];
    private static readonly string[] ActiveEdgeCaseFamilies = ["Unicode", "Temporal", "StringBoundary"];
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    [Function("FakesMetadata")]
    public IActionResult GetMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "fakes/metadata")] HttpRequest request)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider("R4");
        var populationGenerator = new PopulationGenerator(schemaProvider);
        var catalog = EdgeCaseCatalog.CreateDefault();

        var response = new FakesMetadataResponse
        {
            FhirVersions = FhirVersions,
            PopulationStates = populationGenerator.AvailableStates,
            Scenarios = scenarioDiscovery.All().Select(ToScenarioMetadata).ToList(),
            ResourceTypes = schemaProvider.ResourceTypeNames.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            ObservationStates = observationStateDiscovery.Names(),
            EdgeCaseFamilies = ActiveEdgeCaseFamilies
                .Select(family => ToEdgeCaseFamilyMetadata(family, catalog))
                .Where(family => family.Categories.Count > 0)
                .ToList(),
        };

        return new OkObjectResult(response);
    }

    [Function("FakesPopulation")]
    public async Task<IActionResult> GeneratePopulation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/population")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        PopulationRequest? populationRequest;
        try
        {
            populationRequest = await JsonSerializer.DeserializeAsync<PopulationRequest>(
                request.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (populationRequest is null || string.IsNullOrWhiteSpace(populationRequest.Source))
        {
            return new BadRequestObjectResult(new { error = "A 'source' (US state name) is required." });
        }

        if (populationRequest.Count is < 1 or > 100)
        {
            return new BadRequestObjectResult(new { error = "count must be between 1 and 100." });
        }

        var result = fakesService.GeneratePopulation(populationRequest.FhirVersion, populationRequest.Source, populationRequest.Count);
        return new OkObjectResult(result);
    }

    [Function("FakesScenario")]
    public async Task<IActionResult> GenerateScenario(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/scenario")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ScenarioRequest? scenarioRequest;
        try
        {
            scenarioRequest = await JsonSerializer.DeserializeAsync<ScenarioRequest>(
                request.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (scenarioRequest is null || string.IsNullOrWhiteSpace(scenarioRequest.ScenarioId))
        {
            return new BadRequestObjectResult(new { error = "A 'scenarioId' is required." });
        }

        JsonObject? result;
        try
        {
            result = fakesService.GenerateScenario(
                scenarioRequest.FhirVersion,
                scenarioRequest.ScenarioId,
                scenarioRequest.Parameters,
                scenarioRequest.Tag,
                scenarioRequest.ResolvedReferences);
        }
        catch (InvalidScenarioParametersException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        if (result is null)
        {
            return new BadRequestObjectResult(new { error = $"Unknown scenarioId '{scenarioRequest.ScenarioId}'." });
        }

        return new OkObjectResult(result);
    }

    [Function("FakesResource")]
    public async Task<IActionResult> GenerateResource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/resource")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ResourceGenerationRequest? resourceRequest;
        try
        {
            resourceRequest = await JsonSerializer.DeserializeAsync<ResourceGenerationRequest>(
                request.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (resourceRequest is null || string.IsNullOrWhiteSpace(resourceRequest.ResourceType))
        {
            return new BadRequestObjectResult(new { error = "A 'resourceType' is required." });
        }

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(resourceRequest.FhirVersion);
        if (!schemaProvider.ResourceTypeNames.Contains(resourceRequest.ResourceType))
        {
            return new BadRequestObjectResult(new { error = $"Unknown resourceType '{resourceRequest.ResourceType}'." });
        }

        var result = fakesService.GenerateResource(
            resourceRequest.FhirVersion,
            resourceRequest.ResourceType,
            resourceRequest.Seed,
            resourceRequest.Density,
            resourceRequest.FirstName,
            resourceRequest.FamilyName,
            resourceRequest.City,
            resourceRequest.ObservationState,
            resourceRequest.EdgeCaseSelectors,
            resourceRequest.IncludeInvalid);

        return new OkObjectResult(result);
    }

    private static ScenarioMetadata ToScenarioMetadata(DiscoveredScenario scenario) => new()
    {
        Id = scenario.Id,
        Parameters = scenario.Parameters.Select(parameter => new ScenarioParameterMetadata
        {
            Name = parameter.Name!,
            Type = parameter.ParameterType.Name,
            DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
        }).ToList(),
    };

    private static EdgeCaseFamilyMetadata ToEdgeCaseFamilyMetadata(string familyName, EdgeCaseCatalog catalog) => new()
    {
        Family = familyName,
        Categories = catalog.All()
            .Where(strategy => strategy.Family.ToString() == familyName)
            .Select(strategy => new EdgeCaseCategoryMetadata { Id = strategy.Category, Intent = strategy.Intent.ToString() })
            .ToList(),
    };
}
