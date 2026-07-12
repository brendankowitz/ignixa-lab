using FluentAssertions;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;

namespace Ignixa.Lab.Functions.Tests.Suites;

/// <summary>
/// Structural checks for the <c>assertionAnyOfGroup</c>/<c>assertionWhenResponseStatus</c>
/// extensions across every bundled suite. These extensions are free-form JSON, so a typo'd
/// group name or a dangling <c>sourceId</c> reference compiles and builds fine but silently
/// degrades at runtime (an orphaned single-member "group", or a condition that never
/// applies) — only discoverable by actually running the suite against a live server. Parsing
/// through the real <see cref="TestScriptParser"/> (rather than hand-walking the JSON) means
/// this exercises the same extension parsing the engine itself uses.
/// </summary>
public sealed class AssertionGroupShapeTests
{
    public static IEnumerable<object[]> BundledSuiteFiles() =>
        Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "testscripts"), "*.json", SearchOption.AllDirectories)
            .Select(file => new object[] { file });

    [Theory]
    [MemberData(nameof(BundledSuiteFiles))]
    public void EveryAssertionAnyOfGroup_HasAtLeastTwoMembersSharingTheSameSourceId(string file)
    {
        var definition = ParseSuite(file);

        CollectAssertionAnyOfGroupErrors(definition, file).Should().BeEmpty();
    }

    [Fact]
    public void EveryAssertionAnyOfGroup_RejectsGroupsWithoutAnExplicitSourceId()
    {
        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "or-group-missing-source-id" },
            Setup = [],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "group without source ids",
                    Actions =
                    [
                        new AssertExpression
                        {
                            AnyOfGroupId = "missing-source",
                            Criteria = new ResponseCodeCriteria("200"),
                            Description = "Accepted alternative A",
                        },
                        new AssertExpression
                        {
                            AnyOfGroupId = "missing-source",
                            Criteria = new ResponseCodeCriteria("204"),
                            Description = "Accepted alternative B",
                        },
                    ],
                },
            ],
        };

        var errors = CollectAssertionAnyOfGroupErrors(definition, "synthetic/or-group-missing-source-id.json").ToArray();

        errors.Should().Contain(error =>
                error.Contains("synthetic/or-group-missing-source-id.json") &&
                error.Contains("group without source ids") &&
                error.Contains("missing-source") &&
                error.Contains("non-empty sourceId"),
            "an OR group should fail closed when every member omits sourceId");
    }

    [Theory]
    [MemberData(nameof(BundledSuiteFiles))]
    public void EveryAssertionWhenResponseStatus_ReferencesAKnownResponseId(string file)
    {
        var definition = ParseSuite(file);

        CollectWhenResponseStatusErrors(definition, file).Should().BeEmpty();
    }

    [Fact]
    public void EveryAssertionWhenResponseStatus_RejectsForwardResponseReferences()
    {
        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "forward-response-reference" },
            Setup = [],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "forward reference",
                    Actions =
                    [
                        new AssertExpression
                        {
                            SourceId = "later-response",
                            Criteria = new ResponseCodeCriteria("200"),
                            Description = "Asynchronous alternative still pending",
                            WhenResponseStatus = new ResponseStatusCondition("future-response", [200]),
                        },
                        new OperationExpression
                        {
                            Type = "read",
                            Url = "Patient/123",
                            ResponseId = "future-response",
                        },
                    ],
                },
            ],
        };

        var errors = CollectWhenResponseStatusErrors(definition, "synthetic/forward-response-reference.json").ToArray();

        errors.Should().Contain(error =>
                error.Contains("synthetic/forward-response-reference.json") &&
                error.Contains("forward reference") &&
                error.Contains("future-response") &&
                error.Contains("prior setup or earlier operation responses"),
            "conditional assertions should only see response ids that already occurred");
    }

    private static IEnumerable<string> CollectAssertionAnyOfGroupErrors(TestScriptDefinition definition, string file)
    {
        foreach (var test in definition.Tests)
        {
            var groups = CollectAsserts(test.Actions)
                .Where(assert => assert.AnyOfGroupId is not null)
                .GroupBy(assert => assert.AnyOfGroupId);

            foreach (var group in groups)
            {
                if (group.Count() <= 1)
                {
                    yield return
                        $"{file}: test '{test.Name}' group '{group.Key}' has only one member; " +
                        "an assertionAnyOfGroup with a single alternative is never actually an OR " +
                        "and likely indicates a copy/paste or typo'd group name";
                    continue;
                }

                var sourceIds = group.Select(assert => assert.SourceId).ToArray();

                if (sourceIds.Any(string.IsNullOrWhiteSpace))
                {
                    yield return
                        $"{file}: test '{test.Name}' group '{group.Key}' has members without an explicit non-empty sourceId; " +
                        "every assertionAnyOfGroup alternative must point at the same captured response";
                    continue;
                }

                if (sourceIds.Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    yield return
                        $"{file}: test '{test.Name}' group '{group.Key}' members disagree on sourceId; " +
                        "alternatives within one OR-group must evaluate the same captured response";
                }
            }
        }
    }

    private static IEnumerable<string> CollectWhenResponseStatusErrors(TestScriptDefinition definition, string file)
    {
        var setupResponseIds = CollectOperations(definition.Setup)
            .Select(operation => operation.ResponseId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var test in definition.Tests)
        {
            var knownResponseIds = new HashSet<string>(setupResponseIds, StringComparer.Ordinal);

            for (var index = 0; index < test.Actions.Count; index++)
            {
                var action = test.Actions[index];

                if (action is AssertExpression assert && assert.WhenResponseStatus is { } condition)
                {
                    if (string.IsNullOrWhiteSpace(condition.SourceId) || !knownResponseIds.Contains(condition.SourceId))
                    {
                        yield return
                            $"{file}: test '{test.Name}' action #{index + 1} has an assertionWhenResponseStatus referencing " +
                            $"sourceId '{condition.SourceId}', which no prior setup or earlier operation responses produce";
                    }
                }

                if (action is OperationExpression operation && !string.IsNullOrWhiteSpace(operation.ResponseId))
                {
                    knownResponseIds.Add(operation.ResponseId);
                }
            }
        }
    }

    private static TestScriptDefinition ParseSuite(string file)
    {
        var result = TestScriptParser.Parse(File.ReadAllText(file));
        result.IsSuccess.Should().BeTrue($"{file} must parse as a valid TestScript");
        return result.Value!;
    }

    private static IEnumerable<AssertExpression> CollectAsserts(IReadOnlyList<ActionExpression> actions) =>
        actions.OfType<AssertExpression>();

    private static IEnumerable<OperationExpression> CollectOperations(IReadOnlyList<ActionExpression> actions) =>
        actions.OfType<OperationExpression>();
}
