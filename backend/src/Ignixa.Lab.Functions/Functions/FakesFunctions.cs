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
    ObservationStateDiscovery observationStateDiscovery)
{
    private static readonly string[] FhirVersions = ["stu3", "r4", "r4b", "r5", "r6"];
    private static readonly string[] ActiveEdgeCaseFamilies = ["Unicode", "Temporal", "StringBoundary"];

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
