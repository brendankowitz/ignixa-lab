using System.Reflection;
using FluentAssertions;
using Ignixa.Lab.Functions.Middleware;
using Microsoft.Azure.Functions.Worker;

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
    [InlineData("FakesWorkflow", EndpointClass.Capability)]
    [InlineData("SearchTrace", EndpointClass.Capability)]
    [InlineData("SearchCompartmentTrace", EndpointClass.Capability)]
    [InlineData("SearchEverythingTrace", EndpointClass.Capability)]
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

    // Reflects over every [Function]-attributed HTTP-triggered method in the assembly and asserts each one's
    // name gets an explicit classification, rather than falling through to the switch's fail-safe default.
    // This is the same structural blind spot SearchFunctionsRouteDispatchTests exists to catch for route
    // dispatch: a per-file test (or a per-name InlineData list like the theory above) can only assert what
    // its author remembered to list, so it says nothing about a function nobody added a row for. Scoped to
    // HTTP-triggered functions only (skips e.g. FhirPathFunctions.WarmUp's [TimerTrigger]) since
    // EndpointClassifier/RateLimitMiddleware only ever see HTTP requests. "Run" is the one function actually
    // meant to sit at the fail-safe tier by design, not by omission, so it's the sole allowed exception.
    [Fact]
    public void Classify_EveryHttpTriggeredFunctionInTheAssembly_IsExplicitlyClassified()
    {
        var httpFunctionNames = typeof(EndpointClassifier).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null)
            .Where(method => method.GetParameters().Any(p => p.GetCustomAttribute<HttpTriggerAttribute>() is not null))
            .Select(method => method.GetCustomAttribute<FunctionAttribute>()!.Name)
            .Distinct()
            .ToArray();

        httpFunctionNames.Should().NotBeEmpty("otherwise this guard is vacuously true and proves nothing");

        foreach (var functionName in httpFunctionNames)
        {
            if (functionName == "Run")
            {
                continue;
            }

            EndpointClassifier.Classify(functionName, "GET").Should().NotBe(EndpointClass.Run,
                $"'{functionName}' should have its own explicit case in EndpointClassifier instead of silently " +
                "falling through to the fail-safe default tier meant for unrecognized endpoints");
        }
    }
}
