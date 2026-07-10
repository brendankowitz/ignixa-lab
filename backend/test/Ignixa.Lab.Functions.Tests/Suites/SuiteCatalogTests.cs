using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Suites;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Tests.Suites;

public sealed class SuiteCatalogTests : IDisposable
{
    private readonly string _root;

    public SuiteCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ignixa-lab-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SuiteCatalog CreateCatalog() =>
        new(
            Options.Create(new IgnixaLabOptions { SuitesDirectory = _root }),
            NullLogger<SuiteCatalog>.Instance);

    private void WriteScript(string relativePath, string name, string description = "", string version = "4.0")
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var json = $$"""
        {
          "resourceType": "TestScript",
          "id": "{{Path.GetFileNameWithoutExtension(relativePath)}}",
          "name": "{{name}}",
          "status": "active",
          "version": "{{version}}",
          "description": "{{description}}",
          "test": [
            {
              "name": "smoke",
              "action": [
                { "operation": { "type": { "code": "read" }, "resource": "Patient", "url": "Patient/1" } }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(full, json);
    }

    [Fact]
    public void GetSuites_DiscoversScriptsRecursively()
    {
        WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD");
        WriteScript(Path.Combine("search", "patient-search.json"), "Patient Search");

        var suites = CreateCatalog().GetSuites();

        suites.Should().HaveCount(2);
        suites.Select(s => s.Name).Should().Contain(new[] { "Patient CRUD", "Patient Search" });
    }

    [Fact]
    public void GetSuites_UsesImmediateSubfolderAsCategory()
    {
        WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD");
        WriteScript(Path.Combine("capability", "metadata.json"), "Capability");

        var suites = CreateCatalog().GetSuites();

        suites.Single(s => s.Name == "Patient CRUD").Category.Should().Be("crud");
        suites.Single(s => s.Name == "Capability").Category.Should().Be("capability");
    }

    [Fact]
    public void GetSuites_OrdersByCategoryThenName()
    {
        WriteScript(Path.Combine("search", "a.json"), "Alpha");
        WriteScript(Path.Combine("crud", "z.json"), "Zeta");
        WriteScript(Path.Combine("crud", "a.json"), "Able");

        var suites = CreateCatalog().GetSuites();

        suites.Select(s => s.Name).Should().Equal("Able", "Zeta", "Alpha");
    }

    [Fact]
    public void GetSuites_ExposesMetadataAndRelativeFilePath()
    {
        WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD", description: "Full lifecycle", version: "4.3");

        var descriptor = CreateCatalog().GetSuites().Single();

        descriptor.Id.Should().Be("crud/patient.json");
        descriptor.File.Should().Be("crud/patient.json");
        descriptor.Description.Should().Be("Full lifecycle");
        descriptor.FhirVersion.Should().Be("4.3");
    }

    [Fact]
    public void GetSuites_SkipsInvalidJsonFiles()
    {
        WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD");
        var junk = Path.Combine(_root, "crud", "broken.json");
        File.WriteAllText(junk, "{ this is not valid json");

        var suites = CreateCatalog().GetSuites();

        suites.Should().ContainSingle().Which.Name.Should().Be("Patient CRUD");
    }

    [Fact]
    public void GetSuites_ReturnsEmptyWhenDirectoryMissing()
    {
        Directory.Delete(_root, recursive: true);

        var suites = CreateCatalog().GetSuites();

        suites.Should().BeEmpty();
    }

    [Fact]
    public void TryGet_ResolvesSuiteByIdCaseInsensitively()
    {
        WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD");
        var catalog = CreateCatalog();

        catalog.TryGet("CRUD/Patient.json", out var entry).Should().BeTrue();
        entry.Descriptor.Name.Should().Be("Patient CRUD");
        entry.Definition.Should().NotBeNull();
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownId()
    {
        WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD");
        var catalog = CreateCatalog();

        catalog.TryGet("does/not/exist.json", out var entry).Should().BeFalse();
        entry.Should().BeNull();
    }

    // The following tests exercise the real canonical suites (ADR-2607),
    // restored from the IgnixaLab.TestScript.Suites content package into this
    // project's own output under testscripts/ — not the synthetic scripts
    // written above. They use the default SuitesDirectory (AppContext.BaseDirectory)
    // rather than the temp root the other tests write into.

    private static SuiteCatalog CreateBundledCatalog() =>
        new(Options.Create(new IgnixaLabOptions()), NullLogger<SuiteCatalog>.Instance);

    [Fact]
    public void GetSuites_LoadsAllBundledCanonicalSuites()
    {
        var suites = CreateBundledCatalog().GetSuites();

        suites.Should().HaveCount(87);
    }

    [Fact]
    public void GetSuites_BundledCanonicalSuites_SpanExactlyNineCategories()
    {
        var suites = CreateBundledCatalog().GetSuites();

        suites.Select(s => s.Category).Distinct()
            .Should().BeEquivalentTo(new[] { "Bundles", "CRUD", "Foundation", "Microsoft", "Operations", "Regression", "Search", "Subscriptions", "Validation" });
    }

    [Fact]
    public void GetSuites_BundledCanonicalSuites_IncludeKnownIds()
    {
        var suites = CreateBundledCatalog().GetSuites();

        suites.Select(s => s.Id).Should().Contain(new[]
        {
            "CRUD/all-resource-types.json",
            "Search/chaining.json",
            "Microsoft/ms-not-expression.json",
            "Bundles/transaction.json",
            "Validation/validate-op.json",
        });
    }

    [Fact]
    public void BundledCanonicalSuites_DoNotUseVariablePlaceholdersInHeaderExpectedValues()
    {
        var violations = new List<string>();

        foreach (var (relativePath, json) in ReadBundledSuiteJson())
        {
            var root = JsonNode.Parse(json);
            VisitObjects(root, obj =>
            {
                if (!obj.TryGetPropertyValue("headerField", out _))
                {
                    return;
                }

                if (obj.TryGetPropertyValue("value", out var valueNode)
                    && valueNode?.GetValueKind() == System.Text.Json.JsonValueKind.String
                    && valueNode.GetValue<string>().Contains("${", StringComparison.Ordinal))
                {
                    var description = obj.TryGetPropertyValue("description", out var descriptionNode)
                        ? descriptionNode?.GetValue<string>()
                        : "(no description)";
                    violations.Add($"{relativePath}: header assertion '{description}' uses unsupported variable interpolation in value '{valueNode.GetValue<string>()}'.");
                }
            });
        }

        violations.Should().BeEmpty(
            "bundled suites must not use unsupported variable interpolation in header expected values. Violations:{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BundledCanonicalSuites_DoNotUseSyntacticallyInvalidContentTypes()
    {
        var violations = ReadBundledSuiteJson()
            .Where(suite => suite.Json.Contains("\"contentType\": \"Jibberish\"", StringComparison.Ordinal))
            .Select(suite => suite.RelativePath)
            .ToArray();

        violations.Should().BeEmpty(
            "unsupported media-type tests must use a syntactically valid media type so the request reaches the target server. Violations:{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BundledCanonicalSuites_DoNotAssertBundleEntryStatusWithExactReasonlessCode()
    {
        var violations = new List<string>();

        foreach (var (relativePath, json) in ReadBundledSuiteJson())
        {
            var root = JsonNode.Parse(json);
            VisitObjects(root, obj =>
            {
                var expression = obj.TryGetPropertyValue("expression", out var expressionNode)
                    ? GetStringValue(expressionNode)
                    : null;
                var value = obj.TryGetPropertyValue("value", out var valueNode)
                    ? GetStringValue(valueNode)
                    : null;
                var op = obj.TryGetPropertyValue("operator", out var operatorNode)
                    ? GetStringValue(operatorNode)
                    : null;

                if (expression is not null
                    && expression.EndsWith("response.status", StringComparison.Ordinal)
                    && value is not null
                    && value.All(char.IsDigit)
                    && string.Equals(op, "equals", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{relativePath}: use startsWith('{value}') for Bundle.entry.response.status because FHIR permits a reason phrase.");
                }
            });
        }

        violations.Should().BeEmpty(
            "Bundle.entry.response.status assertions must allow FHIR reason phrases. Violations:{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BundledUpdateCreateTest_VerifiesResourceUsingFollowUpRead()
    {
        var test = ReadBundledTest("CRUD/update.json", "update with a client-supplied id for a non-existent resource performs an upsert-create");
        var actions = test["action"]!.AsArray();

        actions
            .Select(action => (
                Code: GetStringValue(action?["operation"]?["type"]?["code"]),
                ResponseId: GetStringValue(action?["operation"]?["responseId"])))
            .Should().Contain(("read", "upsert-read-response"));
        actions.Where(action => action?["assert"]?["expression"] is not null)
            .Select(action => GetStringValue(action?["assert"]?["sourceId"]))
            .Should().OnlyContain(sourceId => sourceId == "upsert-read-response");
    }

    [Fact]
    public void BundledUpdateCreateTest_UsesSpecRequiredHeaderStrength()
    {
        var test = ReadBundledTest("CRUD/update.json", "update with a client-supplied id for a non-existent resource performs an upsert-create");
        var headerAssertions = test["action"]!.AsArray()
            .Where(action => action?["assert"]?["headerField"] is not null)
            .ToDictionary(
                action => GetStringValue(action?["assert"]?["headerField"])!,
                action => action?["assert"]?["warningOnly"]?.GetValue<bool>() == true);

        headerAssertions["ETag"].Should().BeFalse();
        headerAssertions["Last-Modified"].Should().BeFalse();
        headerAssertions["Location"].Should().BeTrue();
    }

    [Fact]
    public void BundledConditionalUpdateCreateTest_RequiresConditionalAndCreateCapabilities()
    {
        var test = ReadBundledTest("CRUD/conditional-update.json", "Conditional update with a client-supplied id and no existing match creates that id");
        var requirement = GetStringValue(test["requiresCapability"]);

        requirement.Should().Contain("conditionalUpdate = true");
        requirement.Should().Contain("updateCreate = true");
    }

    [Fact]
    public void BundledUpdateSuites_DoNotDependOnOptionalBundleTotal()
    {
        var violations = new List<string>();
        foreach (var relativePath in new[] { "CRUD/update.json", "CRUD/conditional-update.json" })
        {
            VisitObjects(ReadBundledSuite(relativePath), obj =>
            {
                if (GetStringValue(obj["expression"]) == "Bundle.total")
                {
                    violations.Add(relativePath);
                }
            });
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void BundledConditionalUpdateFixtures_UseRunScopedSearchIdentifiers()
    {
        var suite = ReadBundledSuite("CRUD/conditional-update.json");
        var runScopedPrefixes = new[] { "CU-NOMATCH", "CU-ONEMATCH", "CU-MULTI" };
        var identifierValues = suite["fixture"]!.AsArray()
            .SelectMany(fixture => fixture!["resource"]!["identifier"]!.AsArray())
            .Select(identifier => GetStringValue(identifier!["value"]))
            .Where(value => runScopedPrefixes.Any(prefix => value?.StartsWith(prefix, StringComparison.Ordinal) == true));

        identifierValues.Should().OnlyContain(value => value!.Contains("${runId}", StringComparison.Ordinal));
    }

    [Fact]
    public void BundledNoOpVersionAssertion_IsInformational()
    {
        var test = ReadBundledTest("CRUD/update.json", "repeating an identical update does not create a new version");
        var versionAssertion = test["action"]!.AsArray()
            .Select(action => action?["assert"])
            .Single(assertion => GetStringValue(assertion?["expression"]) == "Patient.meta.versionId");

        versionAssertion!["warningOnly"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BundledNonmatchingEntityTagTest_ExpectsPreconditionFailure()
    {
        var test = ReadBundledTest("CRUD/update.json", "update with nonmatching If-Match entity-tags returns 412");
        var statusAssertion = test["action"]!.AsArray()
            .Select(action => action?["assert"])
            .Single(assertion => assertion?["response"] is not null);

        GetStringValue(statusAssertion!["response"]).Should().Be("preconditionFailed");
    }

    [Fact]
    public void BundledVersionAwareUpdateTests_UseObservedVersionsAndCapabilityGate()
    {
        var suite = ReadBundledSuite("CRUD/update.json");
        var tests = suite["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Where(test => GetStringValue(test["name"]) is
                "update with changed content assigns a new version and preserves the id"
                or "repeating an identical update does not create a new version"
                or "update with a correct If-Match version precondition succeeds"
                or "update with nonmatching If-Match entity-tags returns 412")
            .ToArray();

        tests.Select(test =>
                GetStringValue(test["requiresCapability"])?.Contains("versioning = 'versioned-update'", StringComparison.Ordinal) == true)
            .Should().OnlyContain(isVersionAware => isVersionAware);

        var contentChange = tests.Single(test =>
            GetStringValue(test["name"]) == "update with changed content assigns a new version and preserves the id");
        GetStringValue(contentChange["action"]![0]!["operation"]!["requestHeader"]![0]!["value"])
            .Should().Be("W/\"${createdVersionId}\"");

        var noOp = tests.Single(test =>
            GetStringValue(test["name"]) == "repeating an identical update does not create a new version");
        GetStringValue(noOp["action"]![0]!["operation"]!["requestHeader"]![0]!["value"])
            .Should().Be("W/\"${versionAfterContentChange}\"");

        var correctPrecondition = tests.Single(test =>
            GetStringValue(test["name"]) == "update with a correct If-Match version precondition succeeds");
        GetStringValue(correctPrecondition["action"]![0]!["operation"]!["requestHeader"]![0]!["value"])
            .Should().Be("W/\"${versionAfterNoOp}\"");

        var stalePrecondition = tests.Single(test =>
            GetStringValue(test["name"]) == "update with nonmatching If-Match entity-tags returns 412");
        GetStringValue(stalePrecondition["action"]![0]!["operation"]!["requestHeader"]![0]!["value"])
            .Should().Be("W/\"${createdVersionId}\"");
    }

    private static JsonNode ReadBundledSuite(string relativePath)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "testscripts");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root, relativePath)))!;
    }

    private static JsonObject ReadBundledTest(string relativePath, string testName)
    {
        var suite = ReadBundledSuite(relativePath);
        return suite["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Single(test => GetStringValue(test["name"]) == testName);
    }

    private static string? GetStringValue(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    private static IEnumerable<(string RelativePath, string Json)> ReadBundledSuiteJson()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "testscripts");
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            yield return (Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(file));
        }
    }

    private static void VisitObjects(JsonNode? node, Action<JsonObject> visit)
    {
        switch (node)
        {
            case JsonObject obj:
                visit(obj);
                foreach (var child in obj.Select(property => property.Value))
                {
                    VisitObjects(child, visit);
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    VisitObjects(child, visit);
                }
                break;
        }
    }
}
