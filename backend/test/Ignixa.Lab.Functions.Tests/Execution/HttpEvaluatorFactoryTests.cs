using FluentAssertions;
using Ignixa.Lab.Functions.Execution;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class HttpEvaluatorFactoryTests
{
    [Theory]
    [InlineData("https://hapi.fhir.org/baseR4", "https://hapi.fhir.org/baseR4/")]
    [InlineData("https://example.org/fhir/r4", "https://example.org/fhir/r4/")]
    [InlineData("https://example.org", "https://example.org/")]
    public void NormalizeBaseAddress_AppendsTrailingSlashWhenMissing(string input, string expected)
    {
        var result = HttpEvaluatorFactory.NormalizeBaseAddress(new Uri(input));

        result.AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://hapi.fhir.org/baseR4/")]
    [InlineData("https://example.org/")]
    public void NormalizeBaseAddress_LeavesTrailingSlashUnchanged(string input)
    {
        var result = HttpEvaluatorFactory.NormalizeBaseAddress(new Uri(input));

        result.AbsoluteUri.Should().Be(input);
    }

    [Fact]
    public void NormalizeBaseAddress_ResolvesRelativePathWithoutDroppingBaseSegment()
    {
        var normalized = HttpEvaluatorFactory.NormalizeBaseAddress(new Uri("https://hapi.fhir.org/baseR4"));

        // The bug this guards against: a base without a trailing slash resolves
        // "Patient" to https://hapi.fhir.org/Patient, dropping "baseR4".
        new Uri(normalized, "Patient").AbsoluteUri
            .Should().Be("https://hapi.fhir.org/baseR4/Patient");
    }
}
