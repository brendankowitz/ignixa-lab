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
    public async Task FirstSearchParamAndFirstResourceTypeId_AreBothOne_ProvingTheCountersAreNotShared()
    {
        // Asserted as literal 1s rather than `typeId.Should().Be(paramId)`: equality is the correct assertion
        // here (independent counters both start at 1; a shared counter would hand out 1 and 2) but it reads
        // exactly like a copy-paste bug, and the obvious "fix" to NotBe would invert the test while still
        // passing review. Spelling out the values leaves nothing to re-derive.
        var resolver = new InMemorySymbolResolver();

        var typeId = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        var paramId = await resolver.GetSearchParamIdAsync(
            Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name"), CancellationToken.None);

        typeId.Should().Be(1);
        paramId.Should().Be(1);
    }

    [Fact]
    public async Task GetSystemIdAsync_KnownSystem_ResolvesStably()
    {
        var resolver = new InMemorySymbolResolver();

        var first = await resolver.GetSystemIdAsync("http://loinc.org", CancellationToken.None);
        var second = await resolver.GetSystemIdAsync("http://loinc.org", CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public async Task GetSystemIdAsync_UnknownSystem_DeclinesSoTheCompilerCanReportAKnownMiss()
    {
        // Null is not a failure here -- it is the real answer a server gives for a system absent from its
        // System table, and it is what lowers the parameter to an always-false predicate the trace surfaces
        // as ParameterOutcome.KnownMiss. Always resolving would make the bench claim every system exists.
        var resolver = new InMemorySymbolResolver();

        var id = await resolver.GetSystemIdAsync("http://not-a-real-system.example", CancellationToken.None);

        id.Should().BeNull();
    }

    [Fact]
    public async Task GetQuantityCodeIdAsync_KnownAndUnknownCodes_ResolveAndDeclineRespectively()
    {
        var resolver = new InMemorySymbolResolver();

        var known = await resolver.GetQuantityCodeIdAsync("mm[Hg]", CancellationToken.None);
        var unknown = await resolver.GetQuantityCodeIdAsync("not-a-ucum-code", CancellationToken.None);

        known.Should().NotBeNull();
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task FirstSystemAndFirstQuantityCodeId_AreBothOne_ProvingTheCountersAreNotShared()
    {
        // Same reasoning as the search-param/resource-type pair above: equality is right but looks wrong, so
        // assert the values.
        var resolver = new InMemorySymbolResolver();

        var systemId = await resolver.GetSystemIdAsync("http://loinc.org", CancellationToken.None);
        var codeId = await resolver.GetQuantityCodeIdAsync("mm[Hg]", CancellationToken.None);

        systemId.Should().Be(1);
        codeId.Should().Be(1);
    }

    [Fact]
    public async Task DecliningASystem_DoesNotBurnAnIdForTheNextKnownOne()
    {
        // The decline happens before GetOrAdd, so an unknown system must not advance the counter -- otherwise
        // ids would depend on how many unknown systems a query happened to mention.
        var resolver = new InMemorySymbolResolver();

        await resolver.GetSystemIdAsync("http://unknown-a.example", CancellationToken.None);
        await resolver.GetSystemIdAsync("http://unknown-b.example", CancellationToken.None);
        var first = await resolver.GetSystemIdAsync("http://loinc.org", CancellationToken.None);

        var fresh = new InMemorySymbolResolver();
        var baseline = await fresh.GetSystemIdAsync("http://loinc.org", CancellationToken.None);

        first.Should().Be(baseline);
    }
}
