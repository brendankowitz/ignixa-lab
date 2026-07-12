using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Suites;
using Ignixa.Serialization.SourceNodes;
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
    [InlineData("""{"resourceType":"TestScript","name":"Bad","status":"active","test":[{"name":"bad","extension":{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"direct"},"action":[]}]}""")]
    public void GetSuites_SkipsMalformedCapabilityExtension(string content)
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
        var requirement = GetMetadataCapabilityRequirement(test);

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
        GetMetadataCapabilityRequirement(test).Should().Contain("updateCreate = true");
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
                GetMetadataCapabilityRequirement(test)?.Contains("versioning = 'versioned-update'", StringComparison.Ordinal) == true)
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
        var requirement = GetMetadataCapabilityRequirement(test);

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
        var requirement = GetMetadataCapabilityRequirement(ReadBundledTest("CRUD/history.json", testName));

        requirement.Should().Contain(expectedHistoryCapability);
        requirement!.Contains("code='patch'", StringComparison.Ordinal).Should().Be(requiresPatch);
    }

    [Fact]
    public void BundledHistoryDeleteTest_RequiresPatientDelete()
    {
        GetMetadataCapabilityRequirement(ReadBundledTest(
                "CRUD/history.json",
                "history entries report a well-formed response.status for every version"))
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
                var requirement = rawTest is null ? null : GetMetadataCapabilityRequirement(rawTest);
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
                GetMetadataCapabilityRequirement(test)?.Contains(expected, StringComparison.Ordinal) == true)
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
            GetMetadataCapabilityRequirement(test).Should().BeNull();
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

        tests.Select(test => GetMetadataCapabilityRequirement(test))
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
        var requirement = GetMetadataCapabilityRequirement(ReadBundledTest(relativePath, testName));

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
        GetMetadataCapabilityRequirement(tests[0]).Should().NotContain("type='Patient'");
        GetMetadataCapabilityRequirement(tests[1]).Should().NotContain("type='Patient'");
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
            "rest.resource.where(type='Subscription').interaction.where(code='read').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='update').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='delete').exists() and "
            + "rest.resource.where(type='Subscription').interaction.where(code='search-type').exists() and "
            + "rest.resource.where(type='Subscription').updateCreate = true and "
            + "rest.extension.where(url='http://hl7.org/fhir/StructureDefinition/capabilitystatement-websocket').exists()";

        GetMetadataCapabilityRequirement(suite).Should().Be(expected);
        GetMetadataCapabilityRequirement(suite).Should().NotContain("code='create'");
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
        var setupOperation = actions
            .Select(action => action?["operation"])
            .Single(operation => operation is not null);
        var setupAssertion = actions
            .Select(action => action?["assert"])
            .Single(assertion => assertion is not null);

        actions.Should().HaveCount(2, "removing the setup success assertion would let setup false-pass");
        GetStringValue(setupOperation?["responseId"]).Should().Be("setup-update-response");
        GetStringValue(setupAssertion?["response"]).Should().Be("created");
        GetStringValue(setupAssertion?["description"]).Should().Be("Setup PUT of the fresh run-scoped id must return 201 Created");
        (setupAssertion?["warningOnly"]?.GetValue<bool>() ?? false).Should().BeFalse();

        var laterUpdateAssertion = ReadBundledTest("Subscriptions/basic.json", "update replaces criteria and status while retaining websocket channel")
            ["action"]!.AsArray()
            .Select(action => action?["assert"])
            .Single(assertion => GetStringValue(assertion?["description"]) == "Existing-resource update must return 200 OK");
        GetStringValue(laterUpdateAssertion?["responseCode"]).Should().Be("200");
        GetStringValue(laterUpdateAssertion?["response"]).Should().BeNull();
        (laterUpdateAssertion?["warningOnly"]?.GetValue<bool>() ?? false).Should().BeFalse();
    }

    [Fact]
    public void BundledSubscriptionSuite_ReadSearchAndExistingUpdateRequireExact200()
    {
        var suite = ReadBundledSuite("Subscriptions/basic.json");
        var assertionDescriptions = new[]
        {
            "Initial read must return 200 OK",
            "Search must return 200 OK",
            "Existing-resource update must return 200 OK",
            "Read after update must return 200 OK",
        };
        var assertions = suite["test"]!.AsArray()
            .SelectMany(test => test!["action"]!.AsArray())
            .Select(action => action?["assert"])
            .Where(assertion => assertionDescriptions.Contains(GetStringValue(assertion?["description"])))
            .ToArray();

        assertions.Should().HaveCount(assertionDescriptions.Length,
            "deleting any exact status assertion must break this guard");
        assertions.Select(assertion => GetStringValue(assertion?["description"]))
            .Should().BeEquivalentTo(assertionDescriptions);
        assertions.Select(assertion => GetStringValue(assertion?["responseCode"]))
            .Should().OnlyContain(responseCode => responseCode == "200");
        assertions.Select(assertion => assertion?["response"])
            .Should().OnlyContain(response => response == null);
        assertions.Select(assertion => assertion?["warningOnly"]?.GetValue<bool>() ?? false)
            .Should().OnlyContain(warningOnly => !warningOnly);
    }

    [Fact]
    public void BundledSubscriptionSuite_UsesRunScopedIdThroughoutLifecycle()
    {
        var suite = ReadBundledSuite("Subscriptions/basic.json");
        var expectedId = "ignixa-sub-basic-${runId}";
        var resourceIds = suite["fixture"]!.AsArray()
            .Select(fixture => GetStringValue(fixture?["resource"]?["id"]));
        var operationUrls = suite["setup"]!["action"]!.AsArray()
            .Concat(suite["test"]!.AsArray().SelectMany(test => test!["action"]!.AsArray()))
            .Concat(suite["teardown"]!["action"]!.AsArray())
            .Select(action => GetStringValue(action?["operation"]?["url"]))
            .Where(url => url?.StartsWith("Subscription/", StringComparison.Ordinal) == true);

        resourceIds.Should().HaveCount(2).And.OnlyContain(id => id == expectedId);
        operationUrls.Should().HaveCount(7).And.OnlyContain(url => url == $"Subscription/{expectedId}");
        var searchActions = ReadBundledTest("Subscriptions/basic.json", "search by _id locates the created Subscription")
            ["action"]!.AsArray();
        var searchOperation = searchActions
            .Select(action => action?["operation"])
            .Single(operation => GetStringValue(operation?["type"]?["code"]) == "search");
        var bundleAssertion = searchActions
            .Select(action => action?["assert"])
            .Single(assertion => GetStringValue(assertion?["description"]) == "Bundle must include the created Subscription");
        GetStringValue(searchOperation?["params"]).Should().Be($"?_id={expectedId}");
        GetStringValue(bundleAssertion?["expression"]).Should().Contain(expectedId);
    }

    [Fact]
    public void BundledSubscriptionSuite_DeclaresAndExpandsRunId()
    {
        var suite = ReadBundledSuite("Subscriptions/basic.json");
        suite["variable"].Should().NotBeNull("the runner expands only declared runId variables");
        var runIdVariable = suite["variable"]!.AsArray()
            .Select(variable => variable!.AsObject())
            .Single(variable => GetStringValue(variable["name"]) == "runId");
        var rawMarkers = suite.ToJsonString().Split("${runId}", StringSplitOptions.None).Length - 1;

        GetStringValue(runIdVariable["defaultValue"]).Should().Be("unscoped");
        rawMarkers.Should().BeGreaterThanOrEqualTo(10,
            "fixture ids, criteria, operations, assertions, and teardown must all be run-scoped");

        CreateBundledCatalog().TryGet("Subscriptions/basic.json", out var entry).Should().BeTrue();
        var prepared = RunScopedDefinitionPreparer.Prepare(entry.Definition);
        var expandedRunId = prepared.Variables.Single(variable => variable.Name == "runId").DefaultValue;
        expandedRunId.Should().MatchRegex("^[0-9a-f]{32}$");
        prepared.Fixtures.Select(fixture => ((IMutableJsonNode)fixture.Resource!).MutableNode["id"]!.GetValue<string>())
            .Should().OnlyContain(id => id.Contains(expandedRunId!, StringComparison.Ordinal)
                && !id.Contains("${runId}", StringComparison.Ordinal));
    }

    [Fact]
    public void BundledSubscriptionSuite_UsesPrivacySafeAdvertisedWebsocketChannel()
    {
        var channels = ReadBundledSuite("Subscriptions/basic.json")["fixture"]!.AsArray()
            .Select(fixture => fixture!["resource"]!["channel"]!)
            .ToArray();

        channels.Should().HaveCount(2);
        channels.Select(channel => GetStringValue(channel["type"])).Should().OnlyContain(type => type == "websocket");
        channels.Select(channel => channel["endpoint"]).Should().OnlyContain(endpoint => endpoint == null);
        channels.Select(channel => channel["payload"]).Should().OnlyContain(payload => payload == null);
    }

    [Theory]
    [InlineData("Forward _include pulls in the direct reference target", "Patient:organization")]
    [InlineData("Wildcard _include=* pulls in every direct reference", "$this='*'")]
    [InlineData("_revinclude pulls in resources that reference the match", "Observation:subject")]
    [InlineData("Multiple _include params combine their targets", "Patient:general-practitioner")]
    public void BundledIncludeTests_RequireTheAdvertisedIncludeTheyExercise(string testName, string expected)
    {
        GetMetadataCapabilityRequirement(ReadBundledTest("Search/includes.json", testName))
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
    public void BundledConditionalDeletePolicy_DoesNotWeakenGateOrLifecycle()
    {
        var suite = ReadBundledSuite("CRUD/conditional-delete.json");
        var setupActions = suite["setup"]!["action"]!.AsArray();
        var teardownActions = suite["teardown"]!["action"]!.AsArray();
        var noCriteriaTest = ReadBundledTest(
            "CRUD/conditional-delete.json",
            "Conditional delete with no search criteria is rejected");
        var noCriteriaDelete = noCriteriaTest["action"]!.AsArray()
            .Select(action => action?["operation"])
            .Single(operation => GetStringValue(operation?["type"]?["code"]) == "delete");
        var singleMatchTest = ReadBundledTest(
            "CRUD/conditional-delete.json",
            "Conditional delete with exactly one matching resource removes it");
        var singleMatchActions = singleMatchTest["action"]!.AsArray();
        var singleMatchDelete = singleMatchActions
            .Select(action => action?["operation"])
            .Single(operation => GetStringValue(operation?["type"]?["code"]) == "delete");
        var successfulDeleteAssertions = FindAssertions(singleMatchTest)
            .Where(assertion => GetStringValue(assertion["response"]) == "okay")
            .ToArray();

        GetMetadataCapabilityRequirement(suite).Should().Be(
            "rest.resource.where(type='Patient').where(conditionalDelete.exists() and conditionalDelete != 'not-supported').exists()");
        suite["test"]!.AsArray().Should().HaveCount(5);
        noCriteriaDelete!["params"].Should().BeNull(
            "the rejection test must remain a collection DELETE without search criteria");
        GetStringValue(singleMatchDelete?["params"]).Should().Be(
            "?identifier=http://ignixa.io/testscript/suite/cond-delete|CD-ONEMATCH");
        successfulDeleteAssertions.Should().ContainSingle(
            "the successful conditional DELETE must remain independently strict");
        (successfulDeleteAssertions[0]["warningOnly"]?.GetValue<bool>() ?? false).Should().BeFalse();
        setupActions.Should().HaveCount(3);
        setupActions.Select(action => GetStringValue(action?["operation"]?["type"]?["code"]))
            .Should().OnlyContain(code => code == "create");
        teardownActions.Should().HaveCount(3);
        teardownActions.Select(action => GetStringValue(action?["operation"]?["type"]?["code"]))
            .Should().OnlyContain(code => code == "delete");
    }

    [Fact]
    public void BundledMaliciousNarrativeRejection_IsClearlyInformational()
    {
        var test = ReadBundledTest("CRUD/create.json", "create blocks a malicious narrative containing a script tag");
        var assertions = FindAssertions(test);

        GetStringValue(test["description"]).Should().Contain("Informational", Exactly.Once());
        GetStringValue(test["description"]).Should().Contain("not a universal base-FHIR SHALL");
        assertions.Should().ContainSingle("deleting the informational outcome assertion must break the guard");
        assertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
        assertions.Select(assertion => GetStringValue(assertion["description"]))
            .Should().OnlyContain(description => description!.Contains("Informational", StringComparison.Ordinal));
    }

    [Fact]
    public void BundledCreateLocationHistoryAssertions_RemainStrict()
    {
        var locationAssertions = FindAssertions(ReadBundledTest(
                "CRUD/create.json",
                "create returns 201 with id, version, and location metadata"))
            .Where(assertion => GetStringValue(assertion["headerField"]) == "Location")
            .ToArray();

        locationAssertions.Should().HaveCount(2);
        locationAssertions.Select(assertion => GetStringValue(assertion["value"]))
            .Should().Contain("_history");
        locationAssertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() ?? false)
            .Should().OnlyContain(warningOnly => !warningOnly);
    }

    [Theory]
    [InlineData(
        "CRUD/create.json",
        "create with unsupported Content-Type reports recommended 415 behavior",
        "application/x-www-form-urlencoded",
        "create")]
    [InlineData(
        "CRUD/update.json",
        "update with unsupported Content-Type reports recommended 415 behavior",
        "application/not-fhir+json",
        "update")]
    public void BundledUnsupportedContentTypeTests_AreFullyInformational(
        string suiteId,
        string testName,
        string contentType,
        string operationCode)
    {
        var suite = ReadBundledSuite(suiteId);
        var matchingTests = suite["test"]!.AsArray()
            .Select(test => test!.AsObject())
            .Where(test => GetStringValue(test["name"])!
                .Contains("unsupported Content-Type", StringComparison.Ordinal))
            .ToArray();
        var test = matchingTests.Should().ContainSingle(
            "deleting or duplicating the MIME-handling test must break the guard").Which;
        var actions = test["action"]!.AsArray();
        var operation = actions
            .Select(action => action?["operation"])
            .Single(candidate => candidate is not null);
        var assertions = FindAssertions(test);

        GetStringValue(test["name"]).Should().Be(testName);
        GetStringValue(test["description"]).Should().Contain("Informational");
        GetStringValue(test["description"]).Should().Contain("415 Unsupported Media Type is appropriate");
        GetStringValue(test["description"]).Should().Contain("not required");
        actions.Should().HaveCount(2, "the request and recommended response observation must remain present");
        GetStringValue(operation?["type"]?["code"]).Should().Be(operationCode);
        GetStringValue(operation?["contentType"]).Should().Be(contentType);
        assertions.Should().ContainSingle("partial warning-only weakening must break the guard");
        GetStringValue(assertions[0]["description"]).Should()
            .Be("Informational MIME handling check: 415 Unsupported Media Type is appropriate but not required by base R4");
        GetStringValue(assertions[0]["responseCode"]).Should().Be("415");
        assertions[0]["warningOnly"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BundledOrdinaryUpdateConditionalReferenceTest_IsClearlyInformational()
    {
        var test = ReadBundledTest(
            "CRUD/update.json",
            "conditional reference by identifier on generalPractitioner resolves correctly");
        var assertions = FindAssertions(test);

        GetStringValue(test["description"]).Should().Contain("Informational");
        GetStringValue(test["description"]).Should().Contain("transactions");
        GetStringValue(test["description"]).Should().Contain("ordinary PUT");
        assertions.Should().HaveCount(3, "the full request outcome must stay informational");
        assertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
    }

    [Theory]
    [InlineData("update with a body containing no id returns 400", "bad")]
    [InlineData("update to a URL id that disagrees with the body id returns 400", "bad")]
    public void BundledUpdateSpecBackedFailures_RemainStrict(string testName, string response)
    {
        var statusAssertions = FindAssertions(ReadBundledTest("CRUD/update.json", testName))
            .Where(assertion => GetStringValue(assertion["response"]) == response
                || GetStringValue(assertion["responseCode"]) == response)
            .ToArray();

        statusAssertions.Should().ContainSingle();
        (statusAssertions[0]["warningOnly"]?.GetValue<bool>() ?? false).Should().BeFalse();
    }

    [Fact]
    public void BundledCustomSearchParameterPersistencePolicySuite_IsFullyInformational()
    {
        var suite = ReadBundledSuite("Search/custom-search-param.json");
        var tests = suite["test"]!.AsArray().Select(test => test!.AsObject()).ToArray();

        GetStringValue(suite["description"]).Should().Contain("Informational");
        GetStringValue(suite["description"]).Should().Contain("server validation policy");
        tests.Should().HaveCount(8, "deleting a validation-policy case must break the guard");
        tests.Select(test => GetStringValue(test["name"]))
            .Should().OnlyContain(name => name!.StartsWith("invalid SearchParameter:", StringComparison.Ordinal));
        foreach (var test in tests)
        {
            GetStringValue(test["description"]).Should().Contain("Informational");
            GetStringValue(test["description"]).Should().Contain("persistence");
            var assertions = FindAssertions(test);
            assertions.Should().HaveCount(4, "every request outcome assertion must remain present");
            assertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
                .Should().OnlyContain(warningOnly => warningOnly);
            assertions.Select(assertion => GetStringValue(assertion["description"]))
                .Should().OnlyContain(description => description!.StartsWith("Informational persistence-policy check:", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Invalid date search value returns 400")]
    [InlineData("Out-of-range date value returns 400")]
    public void BundledInvalidDateSearchTests_AreFullyInformational(string testName)
    {
        var test = ReadBundledTest("Search/date.json", testName);
        var assertions = FindAssertions(test);

        GetStringValue(test["description"]).Should().Contain("SHOULD");
        GetStringValue(test["description"]).Should().Contain("Informational");
        assertions.Should().ContainSingle();
        assertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
    }

    [Fact]
    public void BundledEffectivePeriodDateMatch_RemainsStrict()
    {
        var test = ReadBundledTest("Search/date.json", "Date search matched against an effectivePeriod");
        var assertion = FindAssertions(test)
            .Single(assertion => GetStringValue(assertion["description"]) == "Must include obs-7 (period directly contains the instant)");

        (assertion["warningOnly"]?.GetValue<bool>() ?? false).Should().BeFalse();
    }

    [Fact]
    public void BundledInvalidIncludeTargetTest_IsFullyInformational()
    {
        var test = ReadBundledTest(
            "Search/includes.json",
            "Invalid target resource type on _include returns a Bad Request");
        var assertions = FindAssertions(test);

        GetStringValue(test["description"]).Should().Contain("Prefer: handling=strict");
        GetStringValue(test["description"]).Should().Contain("Informational");
        assertions.Should().HaveCount(2);
        assertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
            .Should().OnlyContain(warningOnly => warningOnly);
    }

    [Theory]
    [InlineData("Forward _include pulls in the direct reference target")]
    [InlineData("_revinclude pulls in resources that reference the match")]
    [InlineData("_include and _revinclude combine in a single request")]
    public void BundledAdvertisedValidIncludeTests_RemainStrict(string testName)
    {
        var assertions = FindAssertions(ReadBundledTest("Search/includes.json", testName));

        assertions.Should().NotBeEmpty();
        assertions.Select(assertion => assertion["warningOnly"]?.GetValue<bool>() ?? false)
            .Should().OnlyContain(warningOnly => !warningOnly);
    }

    [Fact]
    public void BundledEverythingTypeFilter_OnlyResultInclusionAssertionsAreInformational()
    {
        var test = ReadBundledTest(
            "Operations/everything-operation.json",
            "$everything: _type filter narrows compartment content");
        var assertions = FindAssertions(test);
        var informationalDescriptions = new[]
        {
            "Informational compartment-completeness check: result should include the patient",
            "Informational compartment-completeness check: result should include the observation",
        };

        assertions.Should().HaveCount(7, "request, Bundle, inclusion, and exclusion checks must all remain present");
        assertions.Where(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
            .Select(assertion => GetStringValue(assertion["description"]))
            .Should().Equal(informationalDescriptions);
        assertions.Where(assertion => !(assertion["warningOnly"]?.GetValue<bool>() ?? false))
            .Select(assertion => GetStringValue(assertion["description"]))
            .Should().Equal(
                "Must return HTTP 200",
                "Response must be a Bundle",
                "Must NOT include the condition (not in _type list)",
                "Must NOT include the appointment (not in _type list)",
                "Must NOT include the organization (not in _type list)");
        GetStringValue(test["description"]).Should().Contain("SHOULD");
    }

    [Fact]
    public void PackSuitesScript_InvalidatesOnlyRepoLocalFixedVersionCacheBeforePacking()
    {
        var scriptPath = FindRepositoryFile(Path.Combine("backend", "pack-suites.ps1"));
        var script = File.ReadAllText(scriptPath);
        const string cacheDeclaration =
            "$repoPackageCache = Join-Path $repoRoot 'artifacts/nuget-packages/ignixalab.testscript.suites/0.1.0-local'";
        const string cacheRemoval = "Remove-Item -Recurse -Force -LiteralPath $repoPackageCache";
        const string assetsRemoval = "Remove-Item -Force -LiteralPath $assetsFile";
        const string packCommand = "dotnet pack $project -c Release -o $outputDir /nodeReuse:false";

        script.Should().Contain(cacheDeclaration);
        script.Split("Test-Path -LiteralPath $repoPackageCache", StringSplitOptions.None)
            .Should().HaveCount(3, "the cache is checked before and after removal");
        script.Should().Contain(cacheRemoval);
        script.Should().Contain("backend/src/Ignixa.Lab.Functions/obj/project.assets.json");
        script.Should().Contain("backend/test/Ignixa.Lab.Functions.Tests/obj/project.assets.json");
        script.Should().Contain("Test-Path -LiteralPath $assetsFile");
        script.Should().Contain(assetsRemoval);
        script.IndexOf(cacheRemoval, StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf(packCommand, StringComparison.Ordinal));
        script.Should().NotContain(".nuget", "the global user cache must not be touched");
        script.Should().NotContain("NUGET_PACKAGES", "only the configured repo-local cache is in scope");
        script.Should().NotContain("Test-Path $repoPackageCache");
        script.Should().NotContain("Test-Path $assetsFile");
        script.Should().NotContain("Remove-Item -Recurse -Force -Path $repoPackageCache");
        script.Should().NotContain("Remove-Item -Force -Path $assetsFile");
    }

    [Fact]
    public void NuGetConfig_UsesRepositoryLocalPackageCache()
    {
        var configPath = FindRepositoryFile("nuget.config");
        var config = File.ReadAllText(configPath);

        config.Should().Contain("<add key=\"globalPackagesFolder\" value=\"artifacts/nuget-packages\" />");
    }

    [Fact]
    public void BundledUnrelatedCreateAssertions_RemainStrict()
    {
        var informationalTests = new[]
        {
            "create with a valid X-Provenance header links a Provenance resource",
            "create with a malformed X-Provenance header returns 400",
            "create with unsupported Content-Type reports recommended 415 behavior",
            "create with an invalid (incomplete) resource body returns 400",
            "create with a malformed dateTime returns 400",
            "create blocks a malicious narrative containing a script tag",
            "update with an illegal id format returns 400",
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

    [Fact]
    public void BundledUpdateWarningOnlyAssertions_RemainExactlyScoped()
    {
        var expectedInformationalDescriptions = new[]
        {
            "Informational: meta.versionId is unchanged when no data changed",
            "Informational MIME handling check: 415 Unsupported Media Type is appropriate but not required by base R4",
            "Location header should be present",
            "Informational ordinary-PUT check: server should return a 2xx success status",
            "Informational ordinary-PUT check: updated Patient should be readable",
            "Informational ordinary-PUT check: generalPractitioner reference should resolve to the concrete Practitioner reference",
        };
        var assertions = ReadBundledSuite("CRUD/update.json")["test"]!.AsArray()
            .SelectMany(test => test!["action"]!.AsArray())
            .Select(action => action?["assert"])
            .Where(assertion => assertion is not null)
            .Select(assertion => assertion!.AsObject())
            .ToArray();
        var informationalDescriptions = assertions
            .Where(assertion => assertion["warningOnly"]?.GetValue<bool>() == true)
            .Select(assertion => GetStringValue(assertion["description"]))
            .ToArray();

        informationalDescriptions.Should().BeEquivalentTo(expectedInformationalDescriptions);
        assertions
            .Where(assertion => !expectedInformationalDescriptions.Contains(
                GetStringValue(assertion["description"])))
            .Select(assertion => assertion["warningOnly"]?.GetValue<bool>() ?? false)
            .Should().OnlyContain(warningOnly => !warningOnly);
    }

    private static JsonNode ReadBundledSuite(string relativePath)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "testscripts");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root, relativePath)))!;
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
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
