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
        new ObservationStateDiscovery());

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
}
