using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Services.FhirPath;

namespace Ignixa.Lab.Functions.Tests.Services.FhirPath;

public sealed class SchemaProviderFactoryTests
{
    [Theory]
    [InlineData("R4", FhirVersion.R4)]
    [InlineData("r4", FhirVersion.R4)]
    [InlineData("STU3", FhirVersion.Stu3)]
    [InlineData("R4B", FhirVersion.R4B)]
    [InlineData("R5", FhirVersion.R5)]
    [InlineData("R6", FhirVersion.R6)]
    [InlineData("not-a-real-version", FhirVersion.R4)]
    public void GetSchemaProvider_ReturnsProviderForVersion_DefaultingToR4(string input, FhirVersion expected)
    {
        var factory = new SchemaProviderFactory();

        var provider = factory.GetSchemaProvider(input);

        provider.Version.Should().Be(expected);
    }

    [Fact]
    public void GetSchemaProvider_ExposesResourceTypeNames()
    {
        var factory = new SchemaProviderFactory();

        var provider = factory.GetSchemaProvider("R4");

        provider.ResourceTypeNames.Should().Contain("Patient");
    }
}
