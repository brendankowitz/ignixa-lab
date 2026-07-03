using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.Population;
using Ignixa.Lab.Functions.Models.Fakes;
using Ignixa.Lab.Functions.Services.Fakes;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Synthetic FHIR data generation endpoints, powering the Expression Benches Fakes bench.</summary>
public sealed class FakesFunctions(
    ILogger<FakesFunctions> logger,
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery,
    FakesService fakesService)
{
    private static readonly string[] FhirVersions = ["stu3", "r4", "r4b", "r5", "r6"];
    private static readonly string[] ActiveEdgeCaseFamilies = ["Unicode", "Temporal", "StringBoundary"];
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string LibraryVersion = GetLibraryVersion();

    /// <summary>
    /// Reflects the referenced <c>Ignixa.FhirFakes</c> assembly's own informational
    /// version (e.g. "0.5.13") instead of a hand-maintained literal, so the bench's
    /// engine badge can't drift out of sync with the package actually in use — same
    /// approach as <see cref="Services.FhirPath.ResultFormatter"/>'s evaluator version.
    /// </summary>
    private static string GetLibraryVersion()
    {
        var assembly = typeof(EdgeCaseCatalog).Assembly;
        var fullVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        var version = fullVersion;
        var dashIndex = fullVersion.IndexOf('-');
        var plusIndex = fullVersion.IndexOf('+');
        if (dashIndex > 0)
        {
            version = fullVersion[..dashIndex];
        }
        else if (plusIndex > 0)
        {
            version = fullVersion[..plusIndex];
        }

        return version;
    }

    private static bool IsSupportedFhirVersion(string? fhirVersion) =>
        !string.IsNullOrWhiteSpace(fhirVersion) && FhirVersions.Contains(fhirVersion, StringComparer.OrdinalIgnoreCase);

    private static BadRequestObjectResult UnsupportedFhirVersion(string? fhirVersion) =>
        new(new { error = $"Unsupported fhirVersion '{fhirVersion}'. Supported: {string.Join(", ", FhirVersions)}." });

    [Function("FakesMetadata")]
    public IActionResult GetMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "fakes/metadata")] HttpRequest request)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider("R4");
        var populationGenerator = new PopulationGenerator(schemaProvider);
        var catalog = EdgeCaseCatalog.CreateDefault();

        var response = new FakesMetadataResponse
        {
            LibraryVersion = LibraryVersion,
            FhirVersions = FhirVersions,
            PopulationStates = populationGenerator.AvailableStates,
            Scenarios = scenarioDiscovery.All().Select(ToScenarioMetadata).ToList(),
            ResourceTypesByVersion = FhirVersions.ToDictionary(
                version => version,
                version => (IReadOnlyList<string>)schemaProviderFactory.GetSchemaProvider(version).ResourceTypeNames
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase),
            ObservationStates = observationStateDiscovery.Names(),
            EdgeCaseFamilies = ActiveEdgeCaseFamilies
                .Select(family => ToEdgeCaseFamilyMetadata(family, catalog))
                .Where(family => family.Categories.Count > 0)
                .ToList(),
            PatientCities = populationGenerator.AvailableCities.Select(city => city.Name).ToList(),
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

        if (!IsSupportedFhirVersion(populationRequest.FhirVersion))
        {
            return UnsupportedFhirVersion(populationRequest.FhirVersion);
        }

        if (populationRequest.Count is < PopulationRequest.MinCount or > PopulationRequest.MaxCount)
        {
            return new BadRequestObjectResult(new
            {
                error = $"count must be between {PopulationRequest.MinCount} and {PopulationRequest.MaxCount}.",
            });
        }

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(populationRequest.FhirVersion);
        var availableStates = new PopulationGenerator(schemaProvider).AvailableStates;
        if (!availableStates.Contains(populationRequest.Source, StringComparer.Ordinal))
        {
            return new BadRequestObjectResult(new { error = $"Unknown source state '{populationRequest.Source}'." });
        }

        try
        {
            var result = fakesService.GeneratePopulation(populationRequest.FhirVersion, populationRequest.Source, populationRequest.Count);
            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Population generation failed. fhirVersion={FhirVersion} source={Source} count={Count}",
                populationRequest.FhirVersion,
                populationRequest.Source,
                populationRequest.Count);
            return new ObjectResult(new { error = "Population generation failed." }) { StatusCode = (int)HttpStatusCode.InternalServerError };
        }
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

        if (!IsSupportedFhirVersion(scenarioRequest.FhirVersion))
        {
            return UnsupportedFhirVersion(scenarioRequest.FhirVersion);
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

        if (!IsSupportedFhirVersion(resourceRequest.FhirVersion))
        {
            return UnsupportedFhirVersion(resourceRequest.FhirVersion);
        }

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(resourceRequest.FhirVersion);
        if (!schemaProvider.ResourceTypeNames.Contains(resourceRequest.ResourceType))
        {
            return new BadRequestObjectResult(new { error = $"Unknown resourceType '{resourceRequest.ResourceType}'." });
        }

        if (!Enum.TryParse<GenerationDensity>(resourceRequest.Density, ignoreCase: true, out _))
        {
            return new BadRequestObjectResult(new
            {
                error = $"Unknown density '{resourceRequest.Density}'. Supported: {string.Join(", ", Enum.GetNames<GenerationDensity>())}.",
            });
        }

        if (!string.IsNullOrWhiteSpace(resourceRequest.ObservationState)
            && !observationStateDiscovery.Names().Contains(resourceRequest.ObservationState, StringComparer.OrdinalIgnoreCase))
        {
            return new BadRequestObjectResult(new { error = $"Unknown observationState '{resourceRequest.ObservationState}'." });
        }

        try
        {
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
        catch (UnknownEdgeCaseSelectorsException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Resource generation failed. fhirVersion={FhirVersion} resourceType={ResourceType} seed={Seed}",
                resourceRequest.FhirVersion,
                resourceRequest.ResourceType,
                resourceRequest.Seed);
            return new ObjectResult(new { error = "Resource generation failed." }) { StatusCode = (int)HttpStatusCode.InternalServerError };
        }
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
