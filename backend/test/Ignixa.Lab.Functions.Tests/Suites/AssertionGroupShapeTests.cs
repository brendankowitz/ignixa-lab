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

        foreach (var test in definition.Tests)
        {
            var groups = CollectAsserts(test.Actions)
                .Where(assert => assert.AnyOfGroupId is not null)
                .GroupBy(assert => assert.AnyOfGroupId);

            foreach (var group in groups)
            {
                group.Should().HaveCountGreaterThan(1,
                    $"{file}: test '{test.Name}' group '{group.Key}' has only one member; " +
                    "an assertionAnyOfGroup with a single alternative is never actually an OR " +
                    "and likely indicates a copy/paste or typo'd group name");

                group.Select(assert => assert.SourceId).Distinct().Should().HaveCount(1,
                    $"{file}: test '{test.Name}' group '{group.Key}' members disagree on sourceId; " +
                    "alternatives within one OR-group must evaluate the same captured response");
            }
        }
    }

    [Theory]
    [MemberData(nameof(BundledSuiteFiles))]
    public void EveryAssertionWhenResponseStatus_ReferencesAKnownResponseId(string file)
    {
        var definition = ParseSuite(file);

        foreach (var test in definition.Tests)
        {
            var knownResponseIds = CollectOperations(definition.Setup)
                .Concat(CollectOperations(test.Actions))
                .Select(operation => operation.ResponseId)
                .Where(id => id is not null)
                .ToHashSet();

            foreach (var assert in CollectAsserts(test.Actions))
            {
                if (assert.WhenResponseStatus is { } condition)
                {
                    knownResponseIds.Should().Contain(condition.SourceId,
                        $"{file}: test '{test.Name}' has an assertionWhenResponseStatus referencing " +
                        $"sourceId '{condition.SourceId}', which no operation's responseId in setup " +
                        "or this test produces");
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
