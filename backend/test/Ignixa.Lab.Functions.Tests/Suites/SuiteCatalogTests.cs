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
