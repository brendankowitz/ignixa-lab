using System.Text.Json;
using FluentAssertions;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Models.Fakes;
using Ignixa.Lab.Functions.Services.Fakes;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class FakesFunctionsTests
{
    private static FakesFunctions CreateFunctions() => new(
        new SchemaProviderFactory(),
        new ScenarioDiscovery(),
        new ObservationStateDiscovery(),
        new FakesService(new SchemaProviderFactory(), new ScenarioDiscovery()));

    [Fact]
    public void GetMetadata_ReturnsPopulationStatesScenariosAndEdgeCaseFamilies()
    {
        var functions = CreateFunctions();

        var result = functions.GetMetadata(new DefaultHttpContext().Request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var metadata = ok.Value.Should().BeOfType<FakesMetadataResponse>().Subject;
        metadata.PopulationStates.Should().Contain("Massachusetts");
        metadata.Scenarios.Select(s => s.Id).Should().Contain("DiabeticPatient");
        metadata.ObservationStates.Should().Contain("BloodGlucose");
        metadata.EdgeCaseFamilies.Select(f => f.Family).Should().Contain(["Unicode", "Temporal", "StringBoundary"]);
        metadata.EdgeCaseFamilies.Select(f => f.Family).Should().NotContain(["Cardinality", "Structural"]);
        metadata.ResourceTypes.Should().Contain("Patient");
    }

    [Fact]
    public async Task GeneratePopulation_ValidRequest_ReturnsPatientsResourcesAndSummary()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Massachusetts", count = 3 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("patients").GetArrayLength().Should().Be(3);
        doc.RootElement.GetProperty("resources").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        doc.RootElement.GetProperty("summary").GetProperty("byGender").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GeneratePopulation_MissingSource_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { count = 3 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_DiabeticPatientWithTag_StampsTagOnEveryResource()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", tag = "test-run-123" });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        var resources = doc.RootElement.GetProperty("resources");
        resources.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var resource in resources.EnumerateArray())
        {
            resource.GetProperty("meta").GetProperty("tag")[0].GetProperty("code").GetString().Should().Be("test-run-123");
        }
    }

    [Fact]
    public async Task GenerateScenario_UnknownScenarioId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "NotARealScenario" });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_ResolvedReferencesTrue_ReturnsBatchBundle()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", resolvedReferences = true });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("bundle").GetProperty("type").GetString().Should().Be("batch");
    }

    private static HttpRequest BuildJsonPostRequest(object body)
    {
        var context = new DefaultHttpContext();
        var json = JsonSerializer.Serialize(body);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        return context.Request;
    }
}
