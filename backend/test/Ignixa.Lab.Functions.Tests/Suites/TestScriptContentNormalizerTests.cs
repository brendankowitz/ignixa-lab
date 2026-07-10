using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Suites;

namespace Ignixa.Lab.Functions.Tests.Suites;

public sealed class TestScriptContentNormalizerTests
{
    private const string CapabilityUrl = "http://ignixa.io/testscript/requiresCapability";

    [Fact]
    public void Normalize_GivenInvalidJson_ReturnsContentForParserValidation()
    {
        const string content = "{ invalid";

        var normalized = TestScriptContentNormalizer.Normalize(content);

        normalized.Should().Be(content);
    }

    [Fact]
    public void Normalize_GivenDirectCapability_PreservesExistingExtensions()
    {
        const string content = """
            {
              "resourceType": "TestScript",
              "test": [{
                "name": "gated",
                "requiresCapability": "rest.resource.exists()",
                "extension": [{
                  "url": "http://ignixa.io/testscript/fhirVersions",
                  "valueString": "4.0"
                }],
                "action": []
              }]
            }
            """;

        var normalized = JsonNode.Parse(TestScriptContentNormalizer.Normalize(content))!;
        var extensions = normalized["test"]![0]!["extension"]!.AsArray();

        extensions.Should().HaveCount(2);
        extensions.Select(extension => extension!["url"]!.GetValue<string>()).Should().Contain(
            "http://ignixa.io/testscript/fhirVersions",
            "http://ignixa.io/testscript/requiresCapability");
        normalized["test"]![0]!["requiresCapability"].Should().BeNull();
    }

    [Fact]
    public void Normalize_GivenNonArrayExtension_PreservesMalformedInput()
    {
        var content = """{"test":[{"requiresCapability":"direct","extension":{"url":"$URL","valueString":"direct"}}]}"""
            .Replace("$URL", CapabilityUrl, StringComparison.Ordinal);

        TestScriptContentNormalizer.Normalize(content).Should().Be(content);
    }

    [Fact]
    public void Normalize_GivenConflictingCanonicalCapability_RejectsInput()
    {
        var content = """{"test":[{"requiresCapability":"direct","extension":[{"url":"$URL","valueString":"different"}]}]}"""
            .Replace("$URL", CapabilityUrl, StringComparison.Ordinal);

        var act = () => TestScriptContentNormalizer.Normalize(content);

        act.Should().Throw<InvalidDataException>().WithMessage("*conflicting*");
    }

    [Fact]
    public void Normalize_GivenMultipleConflictingCanonicalCapabilities_RejectsInput()
    {
        var content = """{"test":[{"extension":[{"url":"$URL","valueString":"one"},{"url":"$URL","valueString":"two"}]}]}"""
            .Replace("$URL", CapabilityUrl, StringComparison.Ordinal);

        var act = () => TestScriptContentNormalizer.Normalize(content);

        act.Should().Throw<InvalidDataException>().WithMessage("*conflicting*");
    }
}
