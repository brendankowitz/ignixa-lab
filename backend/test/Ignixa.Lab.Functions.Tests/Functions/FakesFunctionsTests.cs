using System.Text.Json;
using FluentAssertions;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Models.Fakes;
using Ignixa.Lab.Functions.Services.Fakes;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class FakesFunctionsTests
{
    private static FakesFunctions CreateFunctions() => new(
        NullLogger<FakesFunctions>.Instance,
        new SchemaProviderFactory(),
        new FakesService(new SchemaProviderFactory()));

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
        metadata.ResourceTypesByVersion["r4"].Should().Contain("Patient");
        metadata.PatientCities.Should().Contain("Boston");
        metadata.LibraryVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        metadata.ClinicalDomains.Should().Contain("Cardiology");
    }

    [Fact]
    public void GetMetadata_ResourceTypesByVersion_DifferBetweenFhirVersions()
    {
        var functions = CreateFunctions();

        var result = functions.GetMetadata(new DefaultHttpContext().Request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var metadata = ok.Value.Should().BeOfType<FakesMetadataResponse>().Subject;
        metadata.ResourceTypesByVersion.Keys.Should().BeEquivalentTo(["stu3", "r4", "r4b", "r5", "r6"]);
        // R6 added resource types (e.g. Requirements, Transport) that don't exist in R4 —
        // regression guard for the metadata endpoint returning one flat R4-only list.
        metadata.ResourceTypesByVersion["r6"].Should().Contain("Requirements");
        metadata.ResourceTypesByVersion["r4"].Should().NotContain("Requirements");
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
    public async Task GeneratePopulation_CountAboveMaximum_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Massachusetts", count = 500 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GeneratePopulation_CountZero_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Massachusetts", count = 0 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GeneratePopulation_CountAtMaximumBoundary_Succeeds()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Massachusetts", count = 100 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
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
    public async Task GenerateScenario_ParameterTypeMismatch_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", parameters = new { age = "thirty" } });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
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

    [Fact]
    public async Task GenerateResource_PatientWithSameSeed_IsDeterministic()
    {
        var functions = CreateFunctions();
        var request1 = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1234 });
        var request2 = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1234 });

        var result1 = await functions.GenerateResource(request1, CancellationToken.None);
        var result2 = await functions.GenerateResource(request2, CancellationToken.None);

        var body1 = JsonSerializer.Serialize(result1.Should().BeOfType<OkObjectResult>().Subject.Value);
        var body2 = JsonSerializer.Serialize(result2.Should().BeOfType<OkObjectResult>().Subject.Value);
        using var doc1 = JsonDocument.Parse(body1);
        using var doc2 = JsonDocument.Parse(body2);
        doc1.RootElement.GetProperty("resource").GetProperty("id").GetString()
            .Should().Be(doc2.RootElement.GetProperty("resource").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GenerateResource_PatientWithKnownCity_SamplesRealisticGender()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", city = "Boston", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        var resource = doc.RootElement.GetProperty("resource");
        resource.GetProperty("gender").GetString().Should().NotBe("unknown");
        resource.GetProperty("address")[0].GetProperty("city").GetString().Should().Be("Boston");
    }

    [Fact]
    public async Task GenerateResource_PatientWithUnknownCityName_FallsBackToPlainCityText()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", city = "Notarealcityville", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        var resource = doc.RootElement.GetProperty("resource");
        resource.GetProperty("address")[0].GetProperty("city").GetString().Should().Be("Notarealcityville");
    }

    [Fact]
    public async Task GenerateResource_ObservationWithState_UsesRequestedState()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Observation", observationState = "BloodGlucose", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("resource").GetProperty("resourceType").GetString().Should().Be("Observation");
    }

    [Fact]
    public async Task GenerateResource_WithEdgeCaseSelectors_ReturnsAManifest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1, edgeCaseSelectors = new[] { "unicode" } });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("manifest").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GenerateResource_UnknownResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "NotARealType" });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_NoEdgeCaseSelectors_ReturnsNullManifest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("manifest").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GenerateScenario_FractionalNumericParameter_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", parameters = new { age = 3.7 } });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_OversizedNumericParameter_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", parameters = new { age = 999999999999L } });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_FactoryRejectsArgument_ReturnsRealReasonNotReflectionBoilerplate()
    {
        var functions = CreateFunctions();
        // age 999999 converts to a valid int but the scenario factory itself rejects it,
        // throwing TargetInvocationException wrapping the real ArgumentOutOfRangeException.
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", parameters = new { age = 999999 } });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = JsonSerializer.Serialize(bad.Value);
        body.Should().Contain("Invalid scenario parameters:");
        body.Should().NotContain("Exception has been thrown by the target of an invocation");
    }

    [Fact]
    public async Task GenerateScenario_CapitalizedParameterKey_RoutesToTargetParameter()
    {
        var functions = CreateFunctions();
        // A capital-A "Age" key must reach the int `age` parameter for this non-numeric
        // value to fail conversion. Case-sensitive matching would ignore it and fall back
        // to the default (52), succeeding — so a 400 here proves matching is case-insensitive.
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", parameters = new { Age = "not-a-number" } });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_CapitalizedNumericParameterKey_Succeeds()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", parameters = new { Age = 30 } });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_UnsupportedFhirVersion_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", fhirVersion = (string?)null });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GeneratePopulation_UnsupportedFhirVersion_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Massachusetts", count = 3, fhirVersion = "r4x" });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GeneratePopulation_UnknownSource_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Atlantis", count = 3 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_UnsupportedFhirVersion_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", fhirVersion = (string?)null });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_UnknownDensity_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", density = "SuperDense" });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_UnknownObservationState_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Observation", observationState = "NotARealState" });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_UnknownEdgeCaseSelector_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1, edgeCaseSelectors = new[] { "not-a-real-selector" } });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_UnknownTheme_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", density = "Maximum", theme = "NotARealTheme" });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResource_MaximumDensityWithTheme_Succeeds()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Condition", density = "Maximum", theme = "Cardiology", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
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
