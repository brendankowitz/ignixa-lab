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

    [Theory]
    [InlineData("""{"resourceType":"TestScript","name":"Bad","status":"active","test":[{"name":"bad","requiresCapability":"direct","extension":{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"direct"},"action":[]}]}""")]
    [InlineData("""{"resourceType":"TestScript","name":"Bad","status":"active","test":[{"name":"bad","requiresCapability":"direct","extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"different"}],"action":[]}]}""")]
    public void GetSuites_SkipsMalformedOrConflictingCapabilityMetadata(string content)
    {
        var file = Path.Combine(_root, "bad.json");
        File.WriteAllText(file, content);

        CreateCatalog().GetSuites().Should().BeEmpty();
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
    public void BundledConditionalUpdateMultiMatch_UsesRunScopedClientIdsForCleanup()
    {
        var suite = ReadBundledSuite("CRUD/conditional-update.json");
        var expectedIds = new[]
        {
            "ignixa-cond-upd-multi-a-${runId}",
            "ignixa-cond-upd-multi-b-${runId}",
        };
        var fixtureIds = suite["fixture"]!.AsArray()
            .Select(fixture => GetStringValue(fixture?["resource"]?["id"]))
            .Where(id => id is not null);
        var test = ReadBundledTest("CRUD/conditional-update.json", "Conditional update with multiple existing matches fails");
        var teardownUrls = suite["teardown"]!["action"]!.AsArray()
            .Select(action => GetStringValue(action?["operation"]?["url"]));

        fixtureIds.Should().Contain(expectedIds);
        GetStringValue(test["requiresCapability"]).Should().Contain("updateCreate = true");
        teardownUrls.Should().Contain(expectedIds.Select(id => $"Patient/{id}"));
    }

    [Theory]
    [InlineData("CRUD/update.json", "ignixa-update-newpat-${runId}")]
    [InlineData("CRUD/update.json", "UPDATE-MISMATCH-${runId}")]
    [InlineData("CRUD/conditional-update.json", "ignixa-cond-upd-explicit-${runId}")]
    [InlineData("CRUD/conditional-update.json", "CU-EXPLICIT-${runId}")]
    [InlineData("CRUD/conditional-update.json", "CU-NOMATCH-${runId}")]
    [InlineData("CRUD/conditional-update.json", "CU-ONEMATCH-${runId}")]
    [InlineData("CRUD/conditional-update.json", "CU-MULTI-${runId}")]
    public void BundledRunScopedMarkers_ArePresentAcrossRelevantSuites(string relativePath, string marker)
    {
        ReadBundledSuite(relativePath).ToJsonString().Should().Contain(marker);
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

    [Theory]
    [InlineData("CRUD/patch-body.json", "interaction.where(code='patch').exists()")]
    [InlineData("CRUD/patch-fhirpath.json", "interaction.where(code='patch').exists()")]
    [InlineData("CRUD/patch-json.json", "interaction.where(code='patch').exists()")]
    [InlineData("CRUD/vread.json", "interaction.where(code='vread').exists()")]
    [InlineData("CRUD/conditional-delete.json", "conditionalDelete.exists()")]
    [InlineData("Operations/import-search.json", "searchParam.where(name='_tag').exists()")]
    [InlineData("Operations/import-search-2.json", "searchParam.where(name='_tag').exists()")]
    [InlineData("Search/custom-search-param.json", "type='SearchParameter'")]
    public void BundledOptionalBehaviorSuites_DeclareMetadataCapabilityGate(string relativePath, string expected)
    {
        var requirement = GetMetadataCapabilityRequirement(ReadBundledSuite(relativePath));
        requirement.Should().Contain(expected);

        CreateBundledCatalog().TryGet(relativePath, out var entry).Should().BeTrue();
        entry.Definition.Metadata.RequiresCapability.Should().Be(requirement);
    }

    [Theory]
    [InlineData("CRUD/update.json", "Patient", "Practitioner")]
    [InlineData("CRUD/conditional-update.json", "Patient")]
    [InlineData("Operations/import-search.json", "Observation", "DocumentReference", "Patient", "RiskAssessment")]
    [InlineData("Operations/import-search-2.json", "Organization", "Practitioner", "Patient", "Observation", "ValueSet")]
    [InlineData("Search/includes.json", "Organization", "Practitioner", "Patient", "Observation", "DiagnosticReport", "Location")]
    [InlineData("Search/chaining.json", "Organization", "Patient", "Observation")]
    [InlineData("Search/chaining-and-sort.json", "Location", "Practitioner", "HealthcareService", "PractitionerRole")]
    public void BundledSuitesWithFixedIdSetup_RequireUpdateCreateBeforeSetup(string relativePath, params string[] resourceTypes)
    {
        var requirement = GetMetadataCapabilityRequirement(ReadBundledSuite(relativePath));

        foreach (var resourceType in resourceTypes)
        {
            requirement.Should().Contain($"type='{resourceType}'");
        }

        requirement.Should().Contain("updateCreate = true");
    }

    [Fact]
    public void BundledDeletedResourceVreadTest_RequiresVreadAndVersioning()
    {
        var test = ReadBundledTest(
            "CRUD/delete.json",
            "delete removes the resource; a plain read is Gone/NotFound while the original version remains vread-able");
        var requirement = GetStringValue(test["requiresCapability"]);

        requirement.Should().Contain("interaction.where(code='vread').exists()");
        requirement.Should().Contain("versioning != 'no-version'");
    }

    [Theory]
    [InlineData("system-level history returns a non-empty bundle", "rest.interaction.where(code='history-system').exists()", false)]
    [InlineData("type-level history returns a non-empty bundle", "type='Patient').interaction.where(code='history-type').exists()", false)]
    [InlineData("instance history reflects create then update, newest first by default", "code='history-instance'", true)]
    [InlineData("instance history with explicit ascending sort returns oldest first", "code='history-instance'", true)]
    [InlineData("instance history with explicit descending sort matches default order", "code='history-instance'", true)]
    [InlineData("history entries report a well-formed response.status for every version", "code='history-instance'", true)]
    [InlineData("_summary=count on history returns totals without entries", "code='history-type'", false)]
    [InlineData("a far-past _since does not wrongly exclude existing history", "rest.interaction.where(code='history-system').exists()", false)]
    [InlineData("a far-future _before returns 400 Bad Request", "rest.interaction.where(code='history-system').exists()", false)]
    [InlineData("_sort=_id on history is unsupported and returns 400 Bad Request", "code='history-type'", false)]
    public void BundledHistoryTests_UseInteractionSpecificCapabilityGates(
        string testName,
        string expectedHistoryCapability,
        bool requiresPatch)
    {
        var suite = ReadBundledSuite("CRUD/history.json");
        GetMetadataCapabilityRequirement(suite).Should().Contain("history-system");
        GetMetadataCapabilityRequirement(suite).Should().Contain("history-type");
        GetMetadataCapabilityRequirement(suite).Should().Contain("history-instance");
        var requirement = GetStringValue(ReadBundledTest("CRUD/history.json", testName)["requiresCapability"]);

        requirement.Should().Contain(expectedHistoryCapability);
        requirement!.Contains("code='patch'", StringComparison.Ordinal).Should().Be(requiresPatch);
    }

    [Fact]
    public void BundledHistoryDeleteTest_RequiresPatientDelete()
    {
        GetStringValue(ReadBundledTest(
                "CRUD/history.json",
                "history entries report a well-formed response.status for every version")["requiresCapability"])
            .Should().Contain("interaction.where(code='delete').exists()");
    }

    [Fact]
    public void BundledCapabilityGates_AreParsedIntoExecutableDefinitions()
    {
        var catalog = CreateBundledCatalog();

        catalog.TryGet("CRUD/patch-json.json", out var patchSuite).Should().BeTrue();
        patchSuite.Definition.Metadata.RequiresCapability.Should().Contain("code='patch'");

        catalog.TryGet("CRUD/delete.json", out var deleteSuite).Should().BeTrue();
        deleteSuite.Definition.Tests
            .Single(test => test.Name.StartsWith("delete removes the resource", StringComparison.Ordinal))
            .RequiresCapability.Should().Contain("code='vread'");
    }

    [Fact]
    public void BundledDirectTestCapabilityGates_AreAllParsedIntoExecutableDefinitions()
    {
        var catalog = CreateBundledCatalog();

        foreach (var descriptor in catalog.GetSuites())
        {
            catalog.TryGet(descriptor.Id, out var entry).Should().BeTrue();
            var rawTests = ReadBundledSuite(descriptor.Id)["test"]!.AsArray();
            rawTests.Should().HaveSameCount(entry.Definition.Tests);

            foreach (var (rawTest, parsedTest) in rawTests.Zip(entry.Definition.Tests))
            {
                var requirement = GetStringValue(rawTest?["requiresCapability"]);
                if (requirement is not null)
                {
                    parsedTest.RequiresCapability.Should().Be(requirement);
                }
            }
        }
    }

    [Theory]
    [InlineData("Search/includes.json", "_include", "searchInclude")]
    [InlineData("Search/includes.json", "_revinclude", "searchRevInclude")]
    [InlineData("Search/chaining.json", "_has", "name='_has'")]
    [InlineData("Search/chaining-and-sort.json", null, "name='_has'")]
    public void BundledOptionalSearchTests_DeclareTestLevelCapabilityGates(
        string relativePath,
        string? testNameFragment,
        string expected)
    {
        var matchingTests = ReadBundledSuite(relativePath)["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Where(test => testNameFragment is null
                || GetStringValue(test["name"])!.Contains(testNameFragment, StringComparison.OrdinalIgnoreCase))
            .Where(test => relativePath != "Search/includes.json"
                || !GetStringValue(test["name"])!.Contains(":iterate", StringComparison.Ordinal))
            .ToArray();

        matchingTests.Should().NotBeEmpty();
        matchingTests.Select(test =>
                GetStringValue(test["requiresCapability"])?.Contains(expected, StringComparison.Ordinal) == true)
            .Should().OnlyContain(hasRequirement => hasRequirement);
    }

    [Fact]
    public void BundledForwardChainingTests_AreInformationalWithoutMisleadingCapabilityGate()
    {
        var tests = ReadBundledSuite("Search/chaining.json")["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Where(test => GetStringValue(test["name"])!.StartsWith("Forward chain", StringComparison.Ordinal))
            .ToArray();

        tests.Should().HaveCount(2);
        foreach (var test in tests)
        {
            GetStringValue(test["requiresCapability"]).Should().BeNull();
            var assertions = test["action"]!.AsArray()
                .Select(action => action?["assert"])
                .Where(assertion => assertion is not null)
                .ToArray();
            assertions.Select(assertion => assertion!["warningOnly"]?.GetValue<bool>() == true)
                .Should().OnlyContain(isStrict => isStrict);
        }
    }

    [Theory]
    [InlineData("Search/chaining.json", "_has", "Patient")]
    [InlineData("Search/chaining-and-sort.json", null, "HealthcareService")]
    public void BundledReverseChainingTests_ScopeHasCapabilityToSearchedResource(
        string relativePath,
        string? testNameFragment,
        string resourceType)
    {
        var tests = ReadBundledSuite(relativePath)["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Where(test => testNameFragment is null
                || GetStringValue(test["name"])!.Contains(testNameFragment, StringComparison.OrdinalIgnoreCase));

        tests.Select(test => GetStringValue(test["requiresCapability"]))
            .Should().OnlyContain(requirement =>
                requirement!.Contains($"type='{resourceType}'", StringComparison.Ordinal)
                && !requirement.Contains("rest.resource.searchParam", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Search/chaining-and-sort.json", "_sort=name orders chained results ascending (missing name last)", "_sort", "HealthcareService")]
    [InlineData("Search/chaining-and-sort.json", "_summary=count on the combined query reports the total without returning entries", "_summary", "HealthcareService")]
    [InlineData("Search/chaining-and-sort.json", "_total=accurate on the combined query reports the exact total", "_total", "HealthcareService")]
    [InlineData("Search/includes.json", "_summary=count excludes included resources from the reported total", "_summary", "Patient")]
    [InlineData("Search/includes.json", "_total=accurate excludes included resources from the reported total", "_total", "Patient")]
    public void BundledSearchControlTests_RequireAdvertisedControl(
        string relativePath,
        string testName,
        string control,
        string resourceType)
    {
        var requirement = GetStringValue(ReadBundledTest(relativePath, testName)["requiresCapability"]);

        requirement.Should().Contain($"type='{resourceType}'");
        requirement.Should().Contain($"name='{control}'");
    }

    [Fact]
    public void BundledIterateTests_KeepBaseIncludeStrictAndIteratedHopInformational()
    {
        var tests = ReadBundledSuite("Search/includes.json")["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Where(test => GetStringValue(test["name"])!.Contains("_include:iterate", StringComparison.Ordinal))
            .ToArray();

        tests.Should().HaveCount(3);
        GetStringValue(tests[0]["requiresCapability"]).Should().NotContain("type='Patient'");
        GetStringValue(tests[1]["requiresCapability"]).Should().NotContain("type='Patient'");
        foreach (var test in tests)
        {
            test["action"]!.AsArray()
                .Select(action => action?["assert"])
                .Where(assertion => assertion is not null)
                .Select(assertion => assertion!["warningOnly"]?.GetValue<bool>() == true)
                .Should().OnlyContain(warningOnly => warningOnly);
        }
    }

    [Fact]
    public void BundledCustomSearchParameterSuite_RequiresUpdateInteraction()
    {
        var requirement = GetMetadataCapabilityRequirement(ReadBundledSuite("Search/custom-search-param.json"));

        requirement.Should().Contain("interaction.where(code='update').exists()");
        requirement.Should().Contain("updateCreate = true");
        requirement.Should().NotContain("code='create'");
    }

    [Fact]
    public void BundledSubscriptionSuite_RequiresExactClassicLifecycleCapabilities()
    {
        var suite = ReadBundledSuite("Subscriptions/basic.json");
        const string expected =
            "rest.resource.where(type='Subscription').interaction.where(code='create').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='read').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='update').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='delete').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='search-type').exists() and "
            + "rest.resource.where(type='Subscription').updateCreate = true";

        GetMetadataCapabilityRequirement(suite).Should().Be(expected);
        GetStringValue(suite["requiresCapability"]).Should().BeNull(
            "top-level capability gates must use the executable extension form");
    }

    [Fact]
    public void BundledSubscriptionSuite_IsRestrictedToClassicR4AndR4B()
    {
        var suite = ReadBundledSuite("Subscriptions/basic.json");
        var tests = suite["test"]!.AsArray();

        GetStringValue(suite["description"]).Should().Contain("classic only");
        GetStringValue(suite["description"]).Should().Contain("Topic/backport subscriptions require separate coverage");
        tests.Should().NotBeEmpty();
        foreach (var test in tests)
        {
            test!["extension"]!.AsArray()
                .Where(extension => GetStringValue(extension?["url"]) == "http://ignixa.io/testscript/fhirVersions")
                .Select(extension => GetStringValue(extension?["valueString"]))
                .Should().Equal("4.0,4.3");
        }
    }

    [Fact]
    public void BundledSubscriptionSuite_SetupCapturesResponseAndStrictlyRequiresSuccess()
    {
        var actions = ReadBundledSuite("Subscriptions/basic.json")["setup"]!["action"]!.AsArray();

        actions.Should().HaveCount(2, "removing the setup success assertion would let setup false-pass");
        GetStringValue(actions[0]?["operation"]?["responseId"]).Should().Be("setup-update-response");
        var assertion = actions[1]?["assert"];
        GetStringValue(assertion?["response"]).Should().Be("okay");
        GetStringValue(assertion?["description"]).Should().Be("Setup PUT must return a success status (2xx)");
        (assertion?["warningOnly"]?.GetValue<bool>() ?? false).Should().BeFalse();
    }

    [Fact]
    public void BundledSubscriptionSuite_DeletedReadUsesNarrowEnforcedWarningAlternatives()
    {
        var test = ReadBundledTest("Subscriptions/basic.json", "read after delete returns 410 or 404");
        var actions = test["action"]!.AsArray();
        var assertions = actions
            .Skip(1)
            .Select(action => action?["assert"])
            .ToArray();

        actions.Should().HaveCount(3, "both accepted statuses and the read operation are required");
        assertions.Should().HaveCount(2, "deleting either alternative must break this guard");
        assertions.Select(assertion => GetStringValue(assertion?["response"]))
            .Should().Equal("gone", "notFound");
        assertions.Select(assertion => assertion?["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
        assertions.Select(assertion => GetStringValue(assertion?["description"]))
            .Should().Equal(
                "Accepted alternative: 410 Gone when the server tracks the deleted resource",
                "Accepted alternative: 404 Not Found when deleted resources are not tracked");
    }

    [Theory]
    [InlineData("Forward _include pulls in the direct reference target", "Patient:organization")]
    [InlineData("Wildcard _include=* pulls in every direct reference", "$this='*'")]
    [InlineData("_revinclude pulls in resources that reference the match", "Observation:subject")]
    [InlineData("Multiple _include params combine their targets", "Patient:general-practitioner")]
    public void BundledIncludeTests_RequireTheAdvertisedIncludeTheyExercise(string testName, string expected)
    {
        GetStringValue(ReadBundledTest("Search/includes.json", testName)["requiresCapability"])
            .Should().Contain(expected);
    }

    [Theory]
    [InlineData("Foundation/cors.json", "CORS", "HTTP hosting behavior", "not universal FHIR conformance")]
    [InlineData("Foundation/health.json", "/health/check", "not part of base FHIR", null)]
    public void BundledHostingBehaviorSuites_AreClearlyInformational(
        string relativePath,
        string descriptionFragment,
        string rationaleFragment,
        string? additionalRationaleFragment)
    {
        var suite = ReadBundledSuite(relativePath);
        var descriptions = suite["test"]!.AsArray()
            .Select(test => GetStringValue(test?["description"]))
            .Prepend(GetStringValue(suite["description"]))
            .ToArray();
        var assertions = FindAssertions(suite);
        string[] expectedAssertionDescriptions = relativePath switch
        {
            "Foundation/cors.json" =>
            [
                "Informational CORS hosting check: preflight OPTIONS should return HTTP 204 No Content",
                "Informational CORS hosting check: Access-Control-Allow-Origin should reflect the requesting origin",
                "Informational CORS hosting check: Access-Control-Allow-Methods should include the requested method",
                "Informational CORS hosting check: Access-Control-Allow-Headers should be present",
                "Informational CORS hosting check: Access-Control-Allow-Headers should include 'authorization' (casing may vary)",
                "Informational CORS hosting check: Access-Control-Allow-Headers should include 'content-type' (casing may vary)",
                "Informational CORS hosting check: Access-Control-Max-Age should be 1440 seconds",
            ],
            "Foundation/health.json" =>
            [
                "Informational hosting check: /health/check should return HTTP 200 but is not part of base FHIR",
                "Informational hosting check: /health/check should return HTTP 200 but is not part of base FHIR",
                "Informational hosting check: /health/check should return JSON but is not part of base FHIR",
            ],
            _ => throw new InvalidOperationException($"No expected assertions configured for {relativePath}."),
        };

        foreach (var description in descriptions)
        {
            description.Should().Contain(descriptionFragment);
            description.Should().Contain(rationaleFragment);
            if (additionalRationaleFragment is not null)
            {
                description.Should().Contain(additionalRationaleFragment);
            }
        }

        assertions.Select(assertion => GetStringValue(assertion["description"]))
            .Should().Equal(expectedAssertionDescriptions);
        assertions.Select(assertion => assertion!["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
    }

    [Theory]
    [InlineData("create with a valid X-Provenance header links a Provenance resource")]
    [InlineData("create with a malformed X-Provenance header returns 400")]
    public void BundledXProvenanceTests_AreClearlyInformational(string testName)
    {
        var test = ReadBundledTest("CRUD/create.json", testName);
        var assertions = FindAssertions(test);
        string[] expectedAssertionDescriptions = testName switch
        {
            "create with a valid X-Provenance header links a Provenance resource" =>
            [
                "Informational Microsoft FHIR Server X-Provenance extension check: server should return 201 Created",
                "Informational Microsoft FHIR Server X-Provenance extension check: search should return HTTP 200",
                "Informational Microsoft FHIR Server X-Provenance extension check: search response should be a Bundle",
                "Informational Microsoft FHIR Server X-Provenance extension check: Bundle should contain linked Provenance",
            ],
            "create with a malformed X-Provenance header returns 400" =>
            [
                "Informational Microsoft FHIR Server X-Provenance extension check: malformed header should return 400",
            ],
            _ => throw new InvalidOperationException($"No expected assertions configured for {testName}."),
        };

        GetStringValue(test["description"]).Should().Contain("Microsoft FHIR Server extension");
        GetStringValue(test["description"]).Should().Contain("not part of base FHIR");
        assertions.Select(assertion => GetStringValue(assertion["description"]))
            .Should().Equal(expectedAssertionDescriptions);
        assertions.Select(assertion => assertion!["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
    }

    [Fact]
    public void BundledCreateSuite_SeparatesBaseConformanceFromInformationalXProvenance()
    {
        var description = GetStringValue(ReadBundledSuite("CRUD/create.json")["description"]);

        description.Should().Contain("base FHIR create checks are conformance");
        description.Should().Contain("X-Provenance coverage is Microsoft FHIR Server informational");
    }

    [Fact]
    public void BundledUnrelatedCreateAssertions_RemainStrict()
    {
        var informationalTests = new[]
        {
            "create with a valid X-Provenance header links a Provenance resource",
            "create with a malformed X-Provenance header returns 400",
        };
        var assertions = ReadBundledSuite("CRUD/create.json")["test"]!.AsArray()
            .Where(test => !informationalTests.Contains(GetStringValue(test?["name"])))
            .SelectMany(test => test!["action"]!.AsArray())
            .Select(action => action?["assert"])
            .Where(assertion => assertion is not null)
            .ToArray();

        assertions.Should().NotBeEmpty();
        assertions.Select(assertion => assertion!["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => !warningOnly);
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

    private static string? GetMetadataCapabilityRequirement(JsonNode suite) =>
        suite["extension"]?.AsArray()
            .Select(extension => extension?.AsObject())
            .Where(extension => GetStringValue(extension?["url"]) == "http://ignixa.io/testscript/requiresCapability")
            .Select(extension => GetStringValue(extension?["valueString"]))
            .SingleOrDefault();

    private static string? GetStringValue(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    private static JsonObject[] FindAssertions(JsonNode node)
    {
        var assertions = new List<JsonObject>();
        VisitObjects(node, obj =>
        {
            if (obj["assert"] is JsonObject assertion)
            {
                assertions.Add(assertion);
            }
        });
        return assertions.ToArray();
    }

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
