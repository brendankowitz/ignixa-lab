using FluentAssertions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class SearchEngineFactoryTests
{
    [Theory]
    [InlineData("STU3")]
    [InlineData("R3")]
    [InlineData("R4")]
    [InlineData("R4B")]
    [InlineData("R5")]
    [InlineData("R6")]
    public void Get_EveryKnownVersion_ReturnsAllThreeDependencies(string fhirVersion)
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var engine = factory.Get(fhirVersion);

        engine.Builder.Should().NotBeNull();
        engine.SearchParameters.Should().NotBeNull();
        engine.Compartments.Should().NotBeNull();
    }

    [Fact]
    public void Get_CalledTwiceForTheSameVersion_ReturnsTheSameCachedInstances()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var first = factory.Get("R4");
        var second = factory.Get("R4");

        second.Builder.Should().BeSameAs(first.Builder);
        second.SearchParameters.Should().BeSameAs(first.SearchParameters);
        second.Compartments.Should().BeSameAs(first.Compartments);
    }

    [Fact]
    public void Get_DifferentVersions_ReturnDistinctInstances()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var r4 = factory.Get("R4");
        var r5 = factory.Get("R5");

        r5.Builder.Should().NotBeSameAs(r4.Builder, "each FHIR version needs its own definition manager, not a shared one");
        r5.SearchParameters.Should().NotBeSameAs(r4.SearchParameters);
    }

    [Fact]
    public void Get_UnrecognizedVersion_FallsBackToR4()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var fallback = factory.Get("not-a-real-version");
        var r4 = factory.Get("R4");

        fallback.Builder.Should().BeSameAs(r4.Builder, "an unrecognized version string resolves to the same cached R4 engine, matching SchemaProviderFactory's fallback");
    }

    [Fact]
    public void Get_IsCaseInsensitiveAndTreatsStu3AndR3AsSynonyms()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var stu3 = factory.Get("STU3");
        var r3 = factory.Get("r3");
        var lowerR4 = factory.Get("r4");

        r3.Builder.Should().BeSameAs(stu3.Builder);
        lowerR4.Builder.Should().BeSameAs(factory.Get("R4").Builder);
    }
}
