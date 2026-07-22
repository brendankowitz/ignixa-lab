using FluentAssertions;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class InMemorySymbolResolverTests
{
    private static SearchParameterInfo Param(string code, string url) =>
        new(code, code, SearchParamType.String, new Uri(url));

    [Fact]
    public async Task GetSearchParamIdAsync_SameParameter_ReturnsStableId()
    {
        var resolver = new InMemorySymbolResolver();
        var param = Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name");

        var first = await resolver.GetSearchParamIdAsync(param, CancellationToken.None);
        var second = await resolver.GetSearchParamIdAsync(param, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public async Task GetSearchParamIdAsync_DistinctParameters_ReturnDistinctIds()
    {
        var resolver = new InMemorySymbolResolver();
        var name = Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name");
        var gender = Param("gender", "http://hl7.org/fhir/SearchParameter/Patient-gender");

        var nameId = await resolver.GetSearchParamIdAsync(name, CancellationToken.None);
        var genderId = await resolver.GetSearchParamIdAsync(gender, CancellationToken.None);

        nameId.Should().NotBe(genderId);
    }

    [Fact]
    public async Task GetResourceTypeIdAsync_DistinctTypes_ReturnStableDistinctIds()
    {
        var resolver = new InMemorySymbolResolver();

        var patient = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        var observation = await resolver.GetResourceTypeIdAsync("Observation", CancellationToken.None);
        var patientAgain = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);

        patient.Should().NotBeNull();
        observation.Should().NotBe(patient);
        patientAgain.Should().Be(patient);
    }

    [Fact]
    public async Task SearchParamAndResourceTypeIds_AreIndependentSequences()
    {
        var resolver = new InMemorySymbolResolver();

        var typeId = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        var paramId = await resolver.GetSearchParamIdAsync(
            Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name"), CancellationToken.None);

        // Each registry assigns from its own sequence; a shared counter is a bug.
        typeId.Should().Be(paramId);
    }
}
