using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Suites;

namespace Ignixa.Lab.Functions.Tests.Suites;

public sealed class TestScriptContentNormalizerTests
{
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
}
