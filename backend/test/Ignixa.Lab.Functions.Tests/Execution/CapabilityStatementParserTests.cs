using FluentAssertions;
using Ignixa.Lab.Functions.Execution;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class CapabilityStatementParserTests
{
    [Fact]
    public void TryParse_MapsPassThroughInteractionCodes()
    {
        const string json = """
            {
              "resourceType": "CapabilityStatement",
              "rest": [
                {
                  "resource": [
                    {
                      "type": "Patient",
                      "interaction": [
                        { "code": "read" },
                        { "code": "vread" },
                        { "code": "create" },
                        { "code": "update" },
                        { "code": "patch" },
                        { "code": "delete" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        resources.Should().ContainSingle();
        resources![0].Type.Should().Be("Patient");
        resources[0].Interactions.Should().BeEquivalentTo(
            new[] { "read", "vread", "create", "update", "patch", "delete" },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void TryParse_MapsSearchTypeToSearch()
    {
        const string json = """
            {
              "rest": [
                { "resource": [ { "type": "Observation", "interaction": [ { "code": "search-type" } ] } ] }
              ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out _);

        ok.Should().BeTrue();
        resources!.Single().Interactions.Should().Equal("search");
    }

    [Theory]
    [InlineData("history-instance")]
    [InlineData("history-type")]
    public void TryParse_MapsHistoryVariantsToHistory(string historyCode)
    {
        var json = $$"""
            {
              "rest": [
                { "resource": [ { "type": "Observation", "interaction": [ { "code": "{{historyCode}}" } ] } ] }
              ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out _);

        ok.Should().BeTrue();
        resources!.Single().Interactions.Should().Equal("history");
    }

    [Fact]
    public void TryParse_DeduplicatesHistoryInstanceAndHistoryType()
    {
        const string json = """
            {
              "rest": [
                {
                  "resource": [
                    {
                      "type": "Observation",
                      "interaction": [
                        { "code": "history-instance" },
                        { "code": "history-type" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out _);

        ok.Should().BeTrue();
        resources!.Single().Interactions.Should().Equal("history");
    }

    [Fact]
    public void TryParse_IgnoresUnmappedInteractionCodes()
    {
        const string json = """
            {
              "rest": [
                {
                  "resource": [
                    {
                      "type": "Bundle",
                      "interaction": [
                        { "code": "read" },
                        { "code": "transaction" },
                        { "code": "batch" },
                        { "code": "history-system" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out _);

        ok.Should().BeTrue();
        resources!.Single().Interactions.Should().Equal("read");
    }

    [Fact]
    public void TryParse_ResourceWithNoInteractions_YieldsEmptyInteractionList()
    {
        const string json = """
            {
              "rest": [ { "resource": [ { "type": "Patient" } ] } ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        resources.Should().ContainSingle();
        resources![0].Type.Should().Be("Patient");
        resources[0].Interactions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "resourceType": "CapabilityStatement" }""")]
    [InlineData("""{ "resourceType": "CapabilityStatement", "rest": [] }""")]
    [InlineData("""{ "resourceType": "CapabilityStatement", "rest": [ { "mode": "server" } ] }""")]
    public void TryParse_MissingOrEmptyRest_YieldsNoResources(string json)
    {
        var ok = CapabilityStatementParser.TryParse(json, out var resources, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        resources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ invalid")]
    [InlineData("[1, 2, 3]")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_MalformedOrMissingInput_ReturnsFalseWithError(string? json)
    {
        var ok = CapabilityStatementParser.TryParse(json, out var resources, out var error);

        ok.Should().BeFalse();
        resources.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParse_MultipleResourcesAcrossRestEntries_AreAllIncluded()
    {
        const string json = """
            {
              "rest": [
                { "resource": [ { "type": "Patient", "interaction": [ { "code": "read" } ] } ] },
                { "resource": [ { "type": "Observation", "interaction": [ { "code": "search-type" } ] } ] }
              ]
            }
            """;

        var ok = CapabilityStatementParser.TryParse(json, out var resources, out _);

        ok.Should().BeTrue();
        resources.Should().HaveCount(2);
        resources.Should().Contain(r => r.Type == "Patient" && r.Interactions.SequenceEqual(new[] { "read" }));
        resources.Should().Contain(r => r.Type == "Observation" && r.Interactions.SequenceEqual(new[] { "search" }));
    }
}
