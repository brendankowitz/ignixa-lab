using FluentAssertions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class SearchEngineFactoryTests
{
    [Fact]
    public void GetR4_ReturnsAllThreeDependencies()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var engine = factory.GetR4();

        engine.Builder.Should().NotBeNull();
        engine.SearchParameters.Should().NotBeNull();
        engine.Compartments.Should().NotBeNull();
    }

    [Fact]
    public void GetR4_CalledTwice_ReturnsTheSameCachedInstances()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var first = factory.GetR4();
        var second = factory.GetR4();

        second.Builder.Should().BeSameAs(first.Builder);
        second.SearchParameters.Should().BeSameAs(first.SearchParameters);
        second.Compartments.Should().BeSameAs(first.Compartments);
    }
}
