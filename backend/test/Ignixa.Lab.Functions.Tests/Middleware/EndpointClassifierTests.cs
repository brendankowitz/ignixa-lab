using FluentAssertions;
using Ignixa.Lab.Functions.Middleware;

namespace Ignixa.Lab.Functions.Tests.Middleware;

public sealed class EndpointClassifierTests
{
    [Theory]
    [InlineData("Health", EndpointClass.Exempt)]
    [InlineData("Suites", EndpointClass.Suites)]
    [InlineData("Capability", EndpointClass.Capability)]
    [InlineData("Run", EndpointClass.Run)]
    [InlineData("ResourceValidation", EndpointClass.Validation)]
    [InlineData("FhirPathMetadata", EndpointClass.Capability)]
    [InlineData("FhirPathStu3", EndpointClass.Capability)]
    [InlineData("FhirPathR4", EndpointClass.Capability)]
    [InlineData("FhirPathR4B", EndpointClass.Capability)]
    [InlineData("FhirPathR5", EndpointClass.Capability)]
    [InlineData("FhirPathR6", EndpointClass.Capability)]
    [InlineData("FakesMetadata", EndpointClass.Capability)]
    [InlineData("FakesPopulation", EndpointClass.Capability)]
    [InlineData("FakesScenario", EndpointClass.Capability)]
    [InlineData("FakesResource", EndpointClass.Capability)]
    [InlineData("SomeFutureEndpoint", EndpointClass.Run)]
    public void Classify_MapsFunctionNameToClass(string functionName, EndpointClass expected)
    {
        EndpointClassifier.Classify(functionName, "GET").Should().Be(expected);
    }

    [Theory]
    [InlineData("Health")]
    [InlineData("Suites")]
    [InlineData("Capability")]
    [InlineData("Run")]
    public void Classify_OptionsPreflight_IsAlwaysExempt(string functionName)
    {
        EndpointClassifier.Classify(functionName, "OPTIONS").Should().Be(EndpointClass.Exempt);
    }
}
