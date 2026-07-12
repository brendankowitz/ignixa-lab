using System.Text.RegularExpressions;
using FluentAssertions;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Model;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed partial class RunScopedDefinitionPreparerTests
{
    [Fact]
    public void Prepare_WithoutRunId_ReturnsOriginalDefinition()
    {
        var definition = DefinitionWithoutRunId();

        var prepared = RunScopedDefinitionPreparer.Prepare(definition);

        prepared.Should().BeSameAs(definition);
    }

    [Fact]
    public void Prepare_WithRunId_GeneratesLowercaseHexDefault()
    {
        var definition = DefinitionWithRunId();

        var prepared = RunScopedDefinitionPreparer.Prepare(definition);

        var runId = prepared.Variables.Single(variable => variable.Name == "runId").DefaultValue;
        runId.Should().MatchRegex(LowercaseGuidPattern());
    }

    [Fact]
    public void Prepare_WithRunId_ExpandsSameValueAcrossFixtureStrings()
    {
        var definition = DefinitionWithRunId();

        var prepared = RunScopedDefinitionPreparer.Prepare(definition);

        var runId = prepared.Variables.Single(variable => variable.Name == "runId").DefaultValue;
        var resource = ((IMutableJsonNode)prepared.Fixtures.Single().Resource!).MutableNode;
        resource["id"]!.GetValue<string>().Should().Be($"patient-{runId}");
        resource["identifier"]![0]!["value"]!.GetValue<string>().Should().Be($"marker-{runId}");
    }

    [Fact]
    public void Prepare_WithRunId_DoesNotMutateOriginalDefinitionOrFixture()
    {
        var definition = DefinitionWithRunId();

        var prepared = RunScopedDefinitionPreparer.Prepare(definition);

        prepared.Should().NotBeSameAs(definition);
        definition.Variables.Single(variable => variable.Name == "runId").DefaultValue.Should().Be("unscoped");
        ((IMutableJsonNode)definition.Fixtures.Single().Resource!).MutableNode["id"]!.GetValue<string>().Should().Be("patient-${runId}");
        ((IMutableJsonNode)definition.Fixtures.Single().Resource!).MutableNode["identifier"]![0]!["value"]!.GetValue<string>()
            .Should().Be("marker-${runId}");
    }

    [Fact]
    public void Prepare_CalledTwice_GeneratesDifferentRunIds()
    {
        var definition = DefinitionWithRunId();

        var first = RunScopedDefinitionPreparer.Prepare(definition);
        var second = RunScopedDefinitionPreparer.Prepare(definition);

        first.Variables.Single(variable => variable.Name == "runId").DefaultValue
            .Should().NotBe(second.Variables.Single(variable => variable.Name == "runId").DefaultValue);
    }

    private static TestScriptDefinition DefinitionWithoutRunId() => new()
    {
        Metadata = new TestScriptMetadata { Name = "NoRunId" },
        Variables = [new VariableDefinition { Name = "other", DefaultValue = "value" }],
    };

    private static TestScriptDefinition DefinitionWithRunId() => new()
    {
        Metadata = new TestScriptMetadata { Name = "RunScoped" },
        Variables =
        [
            new VariableDefinition
            {
                Name = "runId",
                DefaultValue = "unscoped",
                Description = "Per-run id",
            },
        ],
        Fixtures =
        [
            new FixtureDefinition
            {
                Id = "patient",
                Autocreate = false,
                Autodelete = false,
                Resource = ResourceJsonNode.Parse(
                    """
                    {
                      "resourceType": "Patient",
                      "id": "patient-${runId}",
                      "identifier": [
                        {
                          "system": "http://example.org/test",
                          "value": "marker-${runId}"
                        }
                      ]
                    }
                    """),
            },
        ],
    };

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex LowercaseGuidPattern();
}
