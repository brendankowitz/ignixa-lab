using System.Text.Json;
using FluentAssertions;
using Ignixa.Lab.Functions.Services.Fakes;

namespace Ignixa.Lab.Functions.Tests.Services.Fakes;

public sealed class ScenarioDiscoveryTests
{
    [Fact]
    public void All_ReturnsAtLeastOneScenario_IncludingDiabeticPatient()
    {
        var discovery = new ScenarioDiscovery();

        var scenarios = discovery.All();

        scenarios.Should().NotBeEmpty();
        scenarios.Select(s => s.Id).Should().Contain("DiabeticPatient");
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        var discovery = new ScenarioDiscovery();

        discovery.Find("NotARealScenario").Should().BeNull();
    }

    [Fact]
    public void Find_DiabeticPatient_ExposesAgeGenderAndSeverityParameters()
    {
        var discovery = new ScenarioDiscovery();

        var scenario = discovery.Find("DiabeticPatient");

        scenario.Should().NotBeNull();
        scenario!.Parameters.Select(p => p.Name).Should().Contain(["age", "gender", "severity"]);
    }

    [Fact]
    public void Invoke_DiabeticPatientWithNoOverrides_UsesDefaults()
    {
        var discovery = new ScenarioDiscovery();
        var factory = new Ignixa.Lab.Functions.Services.FhirPath.SchemaProviderFactory();
        var scenario = discovery.Find("DiabeticPatient")!;

        var context = discovery.Invoke(scenario, factory.GetSchemaProvider("R4"), parameters: null);

        context.Patient.Should().NotBeNull();
        context.AllResources.Should().NotBeEmpty();
    }

    [Fact]
    public void Invoke_DiabeticPatientWithAgeOverride_UsesOverride()
    {
        var discovery = new ScenarioDiscovery();
        var factory = new Ignixa.Lab.Functions.Services.FhirPath.SchemaProviderFactory();
        var scenario = discovery.Find("DiabeticPatient")!;
        var overrides = new Dictionary<string, JsonElement>
        {
            ["age"] = JsonDocument.Parse("30").RootElement,
        };

        var context = discovery.Invoke(scenario, factory.GetSchemaProvider("R4"), overrides);

        context.BirthDate.Year.Should().BeCloseTo(DateTime.UtcNow.Year - 30, 1);
    }
}
