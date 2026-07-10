using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace Ignixa.Lab.Functions.Tests.Suites;

public sealed class SuiteProvenanceAuditTests : IDisposable
{
    private readonly string _root;
    private const string RepositoryManifestRecorded = "2026-07-07T00:00:00Z";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    public SuiteProvenanceAuditTests()
    {
        _root = Path.Combine(
            FindRepoRootDirectory(),
            "TestResults",
            "ignixa-lab-provenance-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenSidecarIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Missing provenance sidecar: CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_StrictModeFailsWhenSidecarIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());

        var result = await RunAuditAsync(manifest, strict: true);

        result.ExitCode.Should().Be(1);
        result.CombinedOutput.Should().Contain("ERROR: Missing provenance sidecar: CRUD/basic.provenance.json");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    public async Task VerifyProvenance_DefaultModeReportsManifestReadFailureAndSkipsSidecarFallback(string failureMode)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var (manifestPath, expectedError) = CreateManifestReadFailure(failureMode);

        var result = await RunAuditAsync(manifestPath);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain($"ERROR: {expectedError}");
        result.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 1 error(s) and 0 warning(s).");
        result.CombinedOutput.Should().NotContain("Missing provenance sidecar");
        result.CombinedOutput.Should().NotContain("0 error(s)");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    public async Task VerifyProvenance_StrictModeFailsForManifestReadFailureAndSkipsSidecarFallback(string failureMode)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var (manifestPath, expectedError) = CreateManifestReadFailure(failureMode);

        var result = await RunAuditAsync(manifestPath, strict: true);

        result.ExitCode.Should().Be(1);
        result.CombinedOutput.Should().Contain($"ERROR: {expectedError}");
        result.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 1 error(s) and 0 warning(s).");
        result.CombinedOutput.Should().NotContain("Missing provenance sidecar");
        result.CombinedOutput.Should().NotContain("0 error(s)");
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenTargetDoesNotMatchScript()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        WriteProvenance(Path.Combine("CRUD", "basic.provenance.json"), "Search/basic.json");

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Target mismatch in CRUD/basic.provenance.json: expected CRUD/basic.json, found Search/basic.json");
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenSidecarJsonIsInvalid()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), "{ this is not valid json");

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Invalid JSON in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenResourceTypeIsWrong()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Patient",
          "target": [{ "identifier": { "value": "CRUD/basic.json" } }],
          "agent": [{ "who": { "display": "Ignixa Lab maintainers" } }],
          "entity": [{ "role": "source" }]
        }
        """);

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Invalid provenance resourceType in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenTargetIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Provenance",
          "agent": [{ "who": { "display": "Ignixa Lab maintainers" } }],
          "entity": [{ "role": "source" }]
        }
        """);

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Missing target in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenAgentIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Provenance",
          "target": [{ "identifier": { "value": "CRUD/basic.json" } }],
          "entity": [{ "role": "source" }]
        }
        """);

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Missing agent in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_DefaultModeReportsErrorButSucceedsWhenEntityIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Provenance",
          "target": [{ "identifier": { "value": "CRUD/basic.json" } }],
          "agent": [{ "who": { "display": "Ignixa Lab maintainers" } }]
        }
        """);

        var result = await RunAuditAsync(manifest);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("ERROR: Missing entity in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_StrictModeFailsWhenSidecarDoesNotMatchManifest()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        var generation = await RunGeneratorAsync(_root, manifest);
        generation.ExitCode.Should().Be(0, generation.CombinedOutput);
        var sidecarPath = Path.Combine(_root, "CRUD", "basic.provenance.json");
        File.WriteAllText(
            sidecarPath,
            File.ReadAllText(sidecarPath).Replace("Example source", "Tampered source", StringComparison.Ordinal));

        var result = await RunAuditAsync(manifest, strict: true);

        result.ExitCode.Should().Be(1);
        result.CombinedOutput.Should().Contain("ERROR: Provenance sidecar does not match manifest: CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_StrictModeAllowsVersionAdvisoryWarnings()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest(version: "not recorded during original distillation"));
        var generation = await RunGeneratorAsync(_root, manifest);
        generation.ExitCode.Should().Be(0, generation.CombinedOutput);

        var result = await RunAuditAsync(manifest, strict: true);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING: Source version not recorded during original distillation for manifest entry CRUD/basic.json: Example source");
        result.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 0 error(s) and 1 warning(s).");
    }

    [Theory]
    [InlineData("source-declared open-source license")]
    [InlineData("repository license")]
    public async Task VerifyProvenance_StrictModeAllowsLicenseAdvisoryWarnings(string license)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest(license: license));
        var generation = await RunGeneratorAsync(_root, manifest);
        generation.ExitCode.Should().Be(0, generation.CombinedOutput);

        var result = await RunAuditAsync(manifest, strict: true);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain($"WARNING: Source license advisory for manifest entry CRUD/basic.json: Example source ({license})");
        result.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 0 error(s) and 1 warning(s).");
    }

    [Fact]
    public async Task VerifyProvenance_StrictModeSucceedsWithoutErrorsOrWarningsWhenSidecarMatchesManifest()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest());
        var generation = await RunGeneratorAsync(_root, manifest);
        generation.ExitCode.Should().Be(0, generation.CombinedOutput);

        var result = await RunAuditAsync(manifest, strict: true);

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 0 error(s) and 0 warning(s).");
        result.CombinedOutput.Should().NotContain("ERROR:");
        result.CombinedOutput.Should().NotContain("WARNING");
    }

    [Fact]
    public async Task NewTestScriptProvenance_CreatesExpectedFhirProvenanceShape()
    {
        var module = FindRepoRootTool("TestScriptProvenance.psm1");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add($$"""
        Import-Module '{{module}}' -Force
        $source = @{
          Relationship = 'distilled-from'
          License = 'MIT'
          Notes = 'Focused distillation'
          Version = 'v1.2.3'
          Reference = 'https://example.test/source'
          Display = 'Example source'
        }
        New-TestScriptProvenance `
          -RelativePath 'CRUD/basic.json' `
          -Activity 'author-testscript' `
          -Recorded '2026-07-10T12:34:56Z' `
          -Sources @($source) | ConvertTo-TestScriptProvenanceJson
        """);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0, stderr);
        using var document = JsonDocument.Parse(stdout);
        var provenance = document.RootElement;

        provenance.GetProperty("resourceType").GetString().Should().Be("Provenance");
        provenance.GetProperty("recorded").GetString().Should().Be("2026-07-10T12:34:56Z");
        provenance.GetProperty("target")[0].GetProperty("identifier").GetProperty("system").GetString()
            .Should().Be("urn:ignixa-lab:testscripts:path");
        provenance.GetProperty("target")[0].GetProperty("identifier").GetProperty("value").GetString()
            .Should().Be("CRUD/basic.json");
        provenance.GetProperty("activity").GetProperty("coding")[0].GetProperty("system").GetString()
            .Should().Be("http://ignixa.io/fhir/provenance-activity");
        provenance.GetProperty("activity").GetProperty("coding")[0].GetProperty("code").GetString()
            .Should().Be("author-testscript");
        provenance.GetProperty("activity").GetProperty("coding")[0].GetProperty("display").GetString()
            .Should().Be("Author TestScript");
        provenance.GetProperty("agent")[0].GetProperty("who").GetProperty("display").GetString()
            .Should().Be("Ignixa Lab maintainers");

        var extensions = provenance.GetProperty("entity")[0].GetProperty("extension");
        extensions.EnumerateArray().Should().Contain(extension =>
            extension.GetProperty("url").GetString() == "http://ignixa.io/fhir/StructureDefinition/provenance-source-relationship" &&
            extension.GetProperty("valueCode").GetString() == "distilled-from");
        extensions.EnumerateArray().Should().Contain(extension =>
            extension.GetProperty("url").GetString() == "http://ignixa.io/fhir/StructureDefinition/provenance-source-license" &&
            extension.GetProperty("valueString").GetString() == "MIT");
        extensions.EnumerateArray().Should().Contain(extension =>
            extension.GetProperty("url").GetString() == "http://ignixa.io/fhir/StructureDefinition/provenance-distillation-notes" &&
            extension.GetProperty("valueString").GetString() == "Focused distillation");
        extensions.EnumerateArray().Should().Contain(extension =>
            extension.GetProperty("url").GetString() == "http://ignixa.io/fhir/StructureDefinition/provenance-source-version" &&
            extension.GetProperty("valueString").GetString() == "v1.2.3");
    }

    [Fact]
    public async Task NewTestScriptProvenance_RejectsMixedCaseActivity()
    {
        var module = FindRepoRootTool("TestScriptProvenance.psm1");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add($$"""
        Import-Module '{{module}}' -Force
        $source = @{}
        New-TestScriptProvenance `
          -RelativePath 'CRUD/basic.json' `
          -Activity 'Author-TestScript' `
          -Recorded '2026-07-10T12:34:56Z' `
          -Sources @($source) | ConvertTo-TestScriptProvenanceJson
        """);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().NotBe(0, stdout);
        stderr.Should().Contain("Cannot validate argument on parameter 'Activity'");
    }

    [Fact]
    public async Task NewProvenanceSidecars_GeneratesDeterministicOutputAcrossProcesses()
    {
        var firstRoot = Path.Combine(_root, "first");
        var secondRoot = Path.Combine(_root, "second");
        var manifest = WriteManifest(CreateManifest());
        WriteScript(firstRoot, Path.Combine("CRUD", "basic.json"));
        WriteScript(secondRoot, Path.Combine("CRUD", "basic.json"));

        var firstResult = await RunGeneratorAsync(firstRoot, manifest);
        var secondResult = await RunGeneratorAsync(secondRoot, manifest);

        firstResult.ExitCode.Should().Be(0, firstResult.CombinedOutput);
        secondResult.ExitCode.Should().Be(0, secondResult.CombinedOutput);
        firstResult.CombinedOutput.Should().Contain("Generated 1 provenance sidecar(s); skipped 0 existing sidecar(s).");
        secondResult.CombinedOutput.Should().Contain("Generated 1 provenance sidecar(s); skipped 0 existing sidecar(s).");

        var firstSidecar = await File.ReadAllBytesAsync(Path.Combine(firstRoot, "CRUD", "basic.provenance.json"));
        var secondSidecar = await File.ReadAllBytesAsync(Path.Combine(secondRoot, "CRUD", "basic.provenance.json"));

        firstSidecar.Should().Equal(secondSidecar);

        using var document = JsonDocument.Parse(firstSidecar);
        document.RootElement.GetProperty("activity").GetProperty("coding")[0].GetProperty("code").GetString()
            .Should().Be("distill-testscript");
        document.RootElement.GetProperty("recorded").GetString().Should().Be("2026-07-10T12:34:56Z");
        document.RootElement.GetProperty("entity")[0].GetProperty("what").GetProperty("reference").GetString()
            .Should().Be("https://example.test/source");
    }

    [Fact]
    public async Task NewProvenanceSidecars_MapsExpandOperationToTerminologySources()
    {
        WriteScript(Path.Combine("Operations", "expand-operation.json"));
        var manifest = WriteManifest(CreateInlineManifest(
            "Operations/expand-operation.json",
            sources:
            [
                new ManifestSource(
                    "https://hl7.org/fhir/R4/terminology-service.html",
                    "FHIR R4 terminology service operations",
                    "spec-reference",
                    "FHIR specification license",
                    "FHIR R4",
                    "Used the FHIR operation definitions as the normative source for terminology-operation behavior."),
                new ManifestSource(
                    "https://github.com/hapifhir/hapi-fhir",
                    "HAPI FHIR terminology test coverage",
                    "inspired-by",
                    "Apache-2.0",
                    "v7.0.0",
                    "Used as comparative open-source coverage while distilling black-box FHIR TestScript terminology scenarios."),
                new ManifestSource(
                    "https://github.com/microsoft/fhir-server",
                    "Microsoft FHIR Server ExpandOperationTests coverage",
                    "distilled-from",
                    "MIT",
                    "v1.0.0",
                    "Converted Microsoft FHIR Server ValueSet $expand e2e behavior into black-box FHIR TestScript assertions.")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, "Operations", "expand-operation.provenance.json")));

        var references = document.RootElement
            .GetProperty("entity")
            .EnumerateArray()
            .Select(entity => entity.GetProperty("what").GetProperty("reference").GetString())
            .ToArray();

        references.Should().Contain("https://hl7.org/fhir/R4/terminology-service.html");
        references.Should().Contain("https://github.com/hapifhir/hapi-fhir");
        references.Should().Contain("https://github.com/microsoft/fhir-server");
    }

    [Theory]
    [InlineData("Foundation", "health.json")]
    [InlineData("Foundation", "cors.json")]
    [InlineData("Regression", "exception-handling.json")]
    [InlineData("Regression", "capability-statement-version.json")]
    [InlineData("Regression", "observation-resolve-reference.json")]
    public async Task NewProvenanceSidecars_MapsMicrosoftDerivedFoundationAndRegressionSuitesToMicrosoftSource(
        string category,
        string fileName)
    {
        WriteScript(Path.Combine(category, fileName));
        var manifest = WriteManifest(CreateInlineManifest(
            $"{category}/{fileName}",
            sources:
            [
                new ManifestSource(
                    "https://github.com/microsoft/fhir-server",
                    "Microsoft FHIR Server test coverage",
                    "distilled-from",
                    "MIT",
                    "v1.0.0",
                    "Converted Microsoft FHIR Server behavior into black-box FHIR TestScript assertions.")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var sidecarName = Path.ChangeExtension(fileName, ".provenance.json");
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, category, sidecarName)));

        var references = document.RootElement
            .GetProperty("entity")
            .EnumerateArray()
            .Select(entity => entity.GetProperty("what").GetProperty("reference").GetString())
            .ToArray();

        references.Should().Contain("https://github.com/microsoft/fhir-server");
        references.Should().NotContain("https://github.com/brendankowitz/ignixa-lab");
    }

    [Theory]
    [InlineData("all-resource-types.json")]
    [InlineData("all-resource-types-post-stu3.json")]
    [InlineData("all-resource-types-pre-r5.json")]
    [InlineData("all-resource-types-r4-and-r5.json")]
    [InlineData("all-resource-types-r4-family.json")]
    [InlineData("all-resource-types-r4-only.json")]
    [InlineData("all-resource-types-r4b-plus.json")]
    [InlineData("all-resource-types-r5-only.json")]
    [InlineData("all-resource-types-stu3-only.json")]
    public async Task NewProvenanceSidecars_MapsAllResourceTypeSuitesToIgnixaAuthoredSource(string fileName)
    {
        WriteScript(Path.Combine("CRUD", fileName));
        var manifest = WriteManifest(CreateInlineManifest(
            $"CRUD/{fileName}",
            activity: "author-testscript",
            sources:
            [
                new ManifestSource(
                    "https://github.com/brendankowitz/ignixa-lab",
                    "Ignixa Lab all resource type TestScripts",
                    "authored-in",
                    "MIT",
                    "v1.0.0",
                    "Authored in Ignixa Lab to exercise create/read coverage across every concrete resource type."),
                new ManifestSource(
                    "https://hl7.org/fhir/R4/resourcelist.html",
                    "FHIR R4 resource list",
                    "spec-reference",
                    "FHIR specification license",
                    "FHIR R4",
                    "Used as normative resource taxonomy context for the generated all-resource-type suites.")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var sidecarName = Path.ChangeExtension(fileName, ".provenance.json");
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, "CRUD", sidecarName)));

        var references = document.RootElement
            .GetProperty("entity")
            .EnumerateArray()
            .Select(entity => entity.GetProperty("what").GetProperty("reference").GetString())
            .ToArray();

        references.Should().Contain("https://github.com/brendankowitz/ignixa-lab");
        references.Should().Contain("https://hl7.org/fhir/R4/resourcelist.html");
        references.Should().NotContain("https://github.com/microsoft/fhir-server");
        document.RootElement.GetProperty("activity").GetProperty("coding")[0].GetProperty("code").GetString()
            .Should().Be("author-testscript");
    }

    [Theory]
    [InlineData("CRUD", "client-id-handling.json", "https://hl7.org/fhir/R4/http.html")]
    [InlineData("Validation", "validate-op.json", "https://hl7.org/fhir/R4/resource-operation-validate.html")]
    [InlineData("Search", "array-joins.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "basic.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "chaining.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "intervals.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "joins.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "pagination.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "sort.json", "https://hl7.org/fhir/R4/search.html")]
    [InlineData("Search", "string-modifiers.json", "https://hl7.org/fhir/R4/search.html")]
    public async Task NewProvenanceSidecars_MapsFhir262SuitesToFhir262Source(
        string category,
        string fileName,
        string expectedSpecReference)
    {
        WriteScript(Path.Combine(category, fileName));
        var manifest = WriteManifest(CreateInlineManifest(
            $"{category}/{fileName}",
            sources:
            [
                new ManifestSource(
                    "https://github.com/fhir-fi/fhir262",
                    "fhir262 tests",
                    "distilled-from",
                    "MIT",
                    "v1.0.0",
                    "Ported fhir262 coverage into black-box FHIR TestScript assertions."),
                new ManifestSource(
                    expectedSpecReference,
                    "FHIR R4 specification context",
                    "spec-reference",
                    "FHIR specification license",
                    "FHIR R4",
                    "Used as normative operation context for the assertions.")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var sidecarName = Path.ChangeExtension(fileName, ".provenance.json");
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, category, sidecarName)));

        var references = document.RootElement
            .GetProperty("entity")
            .EnumerateArray()
            .Select(entity => entity.GetProperty("what").GetProperty("reference").GetString())
            .ToArray();

        references.Should().Contain("https://github.com/fhir-fi/fhir262");
        references.Should().Contain(expectedSpecReference);
        references.Should().NotContain("https://github.com/microsoft/fhir-server");
    }

    [Theory]
    [InlineData("Search", "custom-search-param.json")]
    [InlineData("Microsoft", "ms-search-parameter-url-length.json")]
    public async Task NewProvenanceSidecars_PreservesMicrosoftAttributionForMicrosoftDerivedSuites(
        string category,
        string fileName)
    {
        WriteScript(Path.Combine(category, fileName));
        var manifest = WriteManifest(CreateInlineManifest(
            $"{category}/{fileName}",
            sources:
            [
                new ManifestSource(
                    "https://github.com/microsoft/fhir-server",
                    "Microsoft FHIR Server test coverage",
                    "distilled-from",
                    "MIT",
                    "v1.0.0",
                    "Converted Microsoft FHIR Server behavior into black-box FHIR TestScript assertions.")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var sidecarName = Path.ChangeExtension(fileName, ".provenance.json");
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, category, sidecarName)));

        var references = document.RootElement
            .GetProperty("entity")
            .EnumerateArray()
            .Select(entity => entity.GetProperty("what").GetProperty("reference").GetString())
            .ToArray();

        references.Should().Contain("https://github.com/microsoft/fhir-server");
        references.Should().NotContain("https://github.com/fhir-fi/fhir262");
    }

    [Fact]
    public async Task NewProvenanceSidecars_PreservesSubscriptionAttribution()
    {
        WriteScript(Path.Combine("Subscriptions", "basic.json"));
        var manifest = WriteManifest(CreateInlineManifest(
            "Subscriptions/basic.json",
            sources:
            [
                new ManifestSource(
                    "https://hl7.org/fhir/R4/subscription.html",
                    "FHIR R4 Subscription resource",
                    "spec-reference",
                    "FHIR specification license",
                    "FHIR R4",
                    "Used the FHIR Subscription definition for basic create/read/search/update/delete expectations."),
                new ManifestSource(
                    "https://github.com/medplum/fhir-candle",
                    "fhir-candle Subscription coverage",
                    "inspired-by",
                    "Apache-2.0",
                    "v1.0.0",
                    "Used as comparative open-source coverage for Subscription round-tripping."),
                new ManifestSource(
                    "https://github.com/LinuxForHealth/FHIR",
                    "LinuxForHealth FHIR Subscription coverage",
                    "inspired-by",
                    "Apache-2.0",
                    "v1.0.0",
                    "Used as comparative open-source coverage for Subscription resource behavior.")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, "Subscriptions", "basic.provenance.json")));

        var references = document.RootElement
            .GetProperty("entity")
            .EnumerateArray()
            .Select(entity => entity.GetProperty("what").GetProperty("reference").GetString())
            .ToArray();

        references.Should().Contain("https://hl7.org/fhir/R4/subscription.html");
        references.Should().Contain("https://github.com/medplum/fhir-candle");
        references.Should().Contain("https://github.com/LinuxForHealth/FHIR");
        references.Should().NotContain("https://github.com/microsoft/fhir-server");
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenScriptIsMissingFromManifest()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest("Search/basic.json"));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Missing manifest entry for TestScript: CRUD/basic.json");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestContainsOrphanedSuite()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest("Search/basic.json"));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Manifest entry has no TestScript: Search/basic.json");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestReferencesUnknownProfile()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateProfileManifest(
            "CRUD/basic.json",
            definedProfile: "example",
            referencedProfile: "missing",
            sources:
            [
                new ManifestSource(
                    "https://example.test/source",
                    "Example source",
                    "distilled-from",
                    "MIT",
                    "v1.0.0",
                    "Test source")
            ]));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Unknown provenance profile");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestEntryContainsBothProfileAndInlineSources()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "display": "Example source",
                  "relationship": "distilled-from",
                  "license": "MIT",
                  "version": "v1.0.0",
                  "notes": "Test source"
                }
              ]
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z",
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "display": "Example source",
                  "relationship": "distilled-from",
                  "license": "MIT",
                  "version": "v1.0.0",
                  "notes": "Test source"
                }
              ]
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("exactly one of profile or sources");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestEntryContainsNeitherProfileNorInlineSources()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {},
          "suites": {
            "CRUD/basic.json": {
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("exactly one of profile or sources");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("unsupported-activity", "distilled-from", "Unsupported activity")]
    [InlineData("distill-testscript", "copied-from", "Unsupported source relationship")]
    public async Task NewProvenanceSidecars_FailsForUnsupportedVocabulary(
        string activity,
        string relationship,
        string expectedMessage)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest(
            activity: activity,
            relationship: relationship));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain(expectedMessage);
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestContainsDuplicateJsonProperties()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "display": "Example source",
                  "relationship": "distilled-from",
                  "license": "MIT",
                  "notes": "Test source"
                }
              ]
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "profile": "duplicate",
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Duplicate manifest property");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("relationship")]
    public async Task NewProvenanceSidecars_FailsWhenManifestContainsNestedDuplicateJsonProperties(
        string duplicateProperty)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest($$"""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "{{duplicateProperty}}": "first",
                  "display": "Example source",
                  "{{duplicateProperty}}": "second",
                  "relationship": "distilled-from",
                  "license": "MIT",
                  "notes": "Test source"
                }
              ]
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Duplicate manifest property");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestContainsCaseVariantSuiteEntryDuplicateProperties()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "display": "Example source",
                  "relationship": "distilled-from",
                  "license": "MIT",
                  "notes": "Test source"
                }
              ]
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "Profile": "example",
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Duplicate manifest property");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenManifestContainsCaseVariantNestedDuplicateProperties()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "display": "Example source",
                  "relationship": "distilled-from",
                  "Relationship": "distilled-from",
                  "license": "MIT",
                  "notes": "Test source"
                }
              ]
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Duplicate manifest property");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenProfileSourcesIsAnObjectInsteadOfAnArray()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": {
                "reference": "https://example.test/source",
                "display": "Example source",
                "relationship": "distilled-from",
                "license": "MIT",
                "notes": "Test source"
              }
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("sources must be an array");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenInlineSourcesIsAnObjectInsteadOfAnArray()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {},
          "suites": {
            "CRUD/basic.json": {
              "activity": "distill-testscript",
              "recorded": "2026-07-10T12:34:56Z",
              "sources": {
                "reference": "https://example.test/source",
                "display": "Example source",
                "relationship": "distilled-from",
                "license": "MIT",
                "notes": "Test source"
              }
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("sources must be an array");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_IgnoresManifestPathInsideSuitesRootWhenNamedDifferently()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest(), Path.Combine("manifests", "custom-suite-manifest.json"));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("Generated 1 provenance sidecar(s); skipped 0 existing sidecar(s).");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeTrue();
    }

    [Fact]
    public async Task NewProvenanceSidecars_GeneratesAndAuditsTestScriptNamedProvenanceManifestWhenManifestPathDiffers()
    {
        WriteScript(Path.Combine("Search", "provenance-manifest.json"));
        var manifest = WriteManifest(
            CreateManifest(suitePath: "Search/provenance-manifest.json"),
            Path.Combine("manifests", "custom-suite-manifest.json"));

        var generation = await RunGeneratorAsync(_root, manifest);

        generation.ExitCode.Should().Be(0, generation.CombinedOutput);
        generation.CombinedOutput.Should().Contain("Generated 1 provenance sidecar(s); skipped 0 existing sidecar(s).");
        var sidecarPath = Path.Combine(_root, "Search", "provenance-manifest.provenance.json");
        File.Exists(sidecarPath).Should().BeTrue();
        using (var document = JsonDocument.Parse(File.ReadAllText(sidecarPath)))
        {
            document.RootElement.GetProperty("target")[0].GetProperty("identifier").GetProperty("value").GetString()
                .Should().Be("Search/provenance-manifest.json");
        }

        var audit = await RunAuditAsync(manifest, strict: true);

        audit.ExitCode.Should().Be(0, audit.CombinedOutput);
        audit.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 0 error(s) and 0 warning(s).");
        audit.CombinedOutput.Should().NotContain("ERROR:");
    }

    [Theory]
    [InlineData("2")]
    [InlineData("\"1\"")]
    public async Task NewProvenanceSidecars_FailsWhenManifestSchemaVersionIsInvalid(string schemaVersion)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(
            CreateManifest().Replace("\"schemaVersion\": 1", $"\"schemaVersion\": {schemaVersion}"));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Unsupported provenance manifest schemaVersion");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("display")]
    [InlineData("relationship")]
    [InlineData("license")]
    [InlineData("notes")]
    public async Task NewProvenanceSidecars_FailsWhenRequiredSourceMetadataIsMissing(
        string propertyName)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifestWithMissingSourceField(propertyName));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain($"Missing source {propertyName}");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public async Task NewProvenanceSidecars_FailsWhenRecordedPropertyIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest("""
        {
          "schemaVersion": 1,
          "profiles": {
            "example": {
              "sources": [
                {
                  "reference": "https://example.test/source",
                  "display": "Example source",
                  "relationship": "distilled-from",
                  "license": "MIT",
                  "version": "v1.0.0",
                  "notes": "Test source"
                }
              ]
            }
          },
          "suites": {
            "CRUD/basic.json": {
              "profile": "example",
              "activity": "distill-testscript"
            }
          }
        }
        """);

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Invalid recorded date");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026-07-10")]
    [InlineData("2026-07-10T12:34:56")]
    public async Task NewProvenanceSidecars_FailsWhenRecordedDateIsInvalid(string recorded)
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        var manifest = WriteManifest(CreateManifest(recorded: recorded));

        var result = await RunGeneratorAsync(_root, manifest);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Invalid recorded date");
        File.Exists(Path.Combine(_root, "CRUD", "basic.provenance.json")).Should().BeFalse();
    }

    [Fact]
    public void RepositoryManifest_ClassifiesEveryBundledTestScript()
    {
        var suitesDirectory = GetBundledSuitesDirectory();
        var bundledSuitePaths = Directory
            .EnumerateFiles(suitesDirectory, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".provenance.json", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(suitesDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var manifest = ReadRepositoryManifest();
        var manifestPaths = manifest.Suites.Keys
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        bundledSuitePaths.Should().HaveCount(87);
        manifestPaths.Should().Equal(bundledSuitePaths);
    }

    [Fact]
    public void RepositoryManifest_UsesExpectedSchemaProfilesAndActivityRules()
    {
        var manifest = ReadRepositoryManifest();

        manifest.SchemaVersion.Should().Be(1);
        manifest.Profiles.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Should()
            .Equal(
                "bundles",
                "fhir262-http",
                "fhir262-search",
                "fhir262-validation",
                "ignixa-all-resource-types",
                "microsoft",
                "operations-expand",
                "operations-terminology",
                "subscriptions");

        manifest.Suites.Should().HaveCount(87);

        foreach (var suite in manifest.Suites.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            suite.Value.Recorded.Should().Be(RepositoryManifestRecorded);
            suite.Value.Activity.Should().Be(
                suite.Key.StartsWith("CRUD/all-resource-types", StringComparison.Ordinal)
                    ? "author-testscript"
                    : "distill-testscript");
        }
    }

    [Theory]
    [InlineData("CRUD/all-resource-types.json", "author-testscript")]
    [InlineData("CRUD/all-resource-types-r4-only.json", "author-testscript")]
    [InlineData("Search/basic.json", "distill-testscript")]
    [InlineData("Subscriptions/basic.json", "distill-testscript")]
    [InlineData("Microsoft/ms-convert-data.json", "distill-testscript")]
    public void RepositoryManifest_RecordsExpectedActivity(string suitePath, string expectedActivity)
    {
        var suites = ReadRepositoryManifest().Suites;
        suites.TryGetValue(suitePath, out var entry).Should().BeTrue();

        entry!.Activity.Should().Be(expectedActivity);
        entry.Recorded.Should().Be(RepositoryManifestRecorded);
    }

    [Fact]
    public void RepositoryProvenanceSidecars_MatchManifest()
    {
        var suitesDirectory = GetBundledSuitesDirectory();
        var manifest = ReadRepositoryManifest();
        var sidecarPaths = Directory
            .EnumerateFiles(suitesDirectory, "*.provenance.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(suitesDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        sidecarPaths.Should().HaveCount(87);
        sidecarPaths.Should().Equal(
            manifest.Suites.Keys
                .Select(path => Path.ChangeExtension(path, ".provenance.json")!.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal));

        foreach (var suite in manifest.Suites.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var sidecarPath = Path.Combine(suitesDirectory, Path.ChangeExtension(suite.Key, ".provenance.json")!);
            using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            var provenance = document.RootElement;

            provenance.GetProperty("target")[0].GetProperty("identifier").GetProperty("value").GetString()
                .Should().Be(suite.Key);
            provenance.GetProperty("recorded").GetString().Should().Be(suite.Value.Recorded);
            provenance.GetProperty("activity").GetProperty("coding")[0].GetProperty("code").GetString()
                .Should().Be(suite.Value.Activity);

            var actualSources = provenance.GetProperty("entity")
                .EnumerateArray()
                .Select(ParseSidecarSource)
                .ToArray();
            actualSources.Should().Equal(manifest.Profiles[suite.Value.Profile]);
        }
    }

    [Fact]
    public async Task VerifyProvenance_StrictModeSucceedsForRepositoryManifestAndSidecars()
    {
        var result = await RunAuditAsync(FindRepoRootTool("provenance-manifest.json"), strict: true, suitesDirectory: GetBundledSuitesDirectory());

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("Provenance audit scanned 87 TestScript files and found 0 error(s)");
        result.CombinedOutput.Should().NotContain("ERROR:");
    }

    private void WriteScript(string relativePath)
    {
        WriteScript(_root, relativePath);
    }

    private static string GetBundledSuitesDirectory()
    {
        return Path.Combine(
            FindRepoRootDirectory(),
            "backend",
            "src",
            "Ignixa.Lab.Suites",
            "testscripts");
    }

    private static RepositoryManifest ReadRepositoryManifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoRootTool("provenance-manifest.json")));
        var root = document.RootElement;

        var profiles = root
            .GetProperty("profiles")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value
                    .GetProperty("sources")
                    .EnumerateArray()
                    .Select(ParseManifestSource)
                    .ToArray(),
                StringComparer.Ordinal);

        var suites = root
            .GetProperty("suites")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => new RepositoryManifestSuite(
                    property.Value.GetProperty("profile").GetString()!,
                    property.Value.GetProperty("activity").GetString()!,
                    property.Value.GetProperty("recorded").GetString()!),
                StringComparer.Ordinal);

        return new RepositoryManifest(root.GetProperty("schemaVersion").GetInt32(), profiles, suites);
    }

    private static ManifestSource ParseManifestSource(JsonElement source)
    {
        return new ManifestSource(
            source.GetProperty("reference").GetString()!,
            source.GetProperty("display").GetString()!,
            source.GetProperty("relationship").GetString()!,
            source.GetProperty("license").GetString()!,
            source.TryGetProperty("version", out var version) ? version.GetString() : null,
            source.GetProperty("notes").GetString()!);
    }

    private static ManifestSource ParseSidecarSource(JsonElement entity)
    {
        string? relationship = null;
        string? license = null;
        string? version = null;
        string? notes = null;

        foreach (var extension in entity.GetProperty("extension").EnumerateArray())
        {
            switch (extension.GetProperty("url").GetString())
            {
                case "http://ignixa.io/fhir/StructureDefinition/provenance-source-relationship":
                    relationship = extension.GetProperty("valueCode").GetString();
                    break;
                case "http://ignixa.io/fhir/StructureDefinition/provenance-source-license":
                    license = extension.GetProperty("valueString").GetString();
                    break;
                case "http://ignixa.io/fhir/StructureDefinition/provenance-source-version":
                    version = extension.GetProperty("valueString").GetString();
                    break;
                case "http://ignixa.io/fhir/StructureDefinition/provenance-distillation-notes":
                    notes = extension.GetProperty("valueString").GetString();
                    break;
            }
        }

        return new ManifestSource(
            entity.GetProperty("what").GetProperty("reference").GetString()!,
            entity.GetProperty("what").GetProperty("display").GetString()!,
            relationship!,
            license!,
            version,
            notes!);
    }

    private string WriteManifest(string json, string relativePath = "provenance-manifest.json")
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static string CreateManifest(
        string suitePath = "CRUD/basic.json",
        string profile = "example",
        string activity = "distill-testscript",
        string relationship = "distilled-from",
        string recorded = "2026-07-10T12:34:56Z",
        string license = "MIT",
        string? version = "v1.0.0",
        string reference = "https://example.test/source",
        string display = "Example source",
        string notes = "Test source")
    {
        return CreateProfileManifest(
            suitePath,
            definedProfile: profile,
            referencedProfile: profile,
            activity: activity,
            recorded: recorded,
            sources:
            [
                new ManifestSource(
                    reference,
                    display,
                    relationship,
                    license,
                    version,
                    notes)
            ]);
    }

    private static string CreateProfileManifest(
        string suitePath,
        string definedProfile,
        string referencedProfile,
        string activity = "distill-testscript",
        string recorded = "2026-07-10T12:34:56Z",
        params ManifestSource[] sources)
    {
        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                profiles = new Dictionary<string, object?>
                {
                    [definedProfile] = new
                    {
                        sources = sources.Select(ToManifestSource).ToArray()
                    }
                },
                suites = new Dictionary<string, object?>
                {
                    [suitePath] = new
                    {
                        profile = referencedProfile,
                        activity,
                        recorded
                    }
                }
            },
            ManifestJsonOptions);
    }

    private static string CreateInlineManifest(
        string suitePath,
        string activity = "distill-testscript",
        string recorded = "2026-07-10T12:34:56Z",
        params ManifestSource[] sources)
    {
        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                profiles = new Dictionary<string, object?>(),
                suites = new Dictionary<string, object?>
                {
                    [suitePath] = new
                    {
                        activity,
                        recorded,
                        sources = sources.Select(ToManifestSource).ToArray()
                    }
                }
            },
            ManifestJsonOptions);
    }

    private static string CreateManifestWithMissingSourceField(string missingField)
    {
        var source = new Dictionary<string, string?>
        {
            ["reference"] = "https://example.test/source",
            ["display"] = "Example source",
            ["relationship"] = "distilled-from",
            ["license"] = "MIT",
            ["version"] = "v1.0.0",
            ["notes"] = "Test source"
        };

        source.Remove(missingField);

        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                profiles = new Dictionary<string, object?>
                {
                    ["example"] = new
                    {
                        sources = new object[] { source }
                    }
                },
                suites = new Dictionary<string, object?>
                {
                    ["CRUD/basic.json"] = new
                    {
                        profile = "example",
                        activity = "distill-testscript",
                        recorded = "2026-07-10T12:34:56Z"
                    }
                }
            },
            ManifestJsonOptions);
    }

    private static object ToManifestSource(ManifestSource source)
    {
        var manifestSource = new Dictionary<string, string?>
        {
            ["reference"] = source.Reference,
            ["display"] = source.Display,
            ["relationship"] = source.Relationship,
            ["license"] = source.License,
            ["notes"] = source.Notes
        };

        if (source.Version is not null)
        {
            manifestSource["version"] = source.Version;
        }

        return manifestSource;
    }

    private (string ManifestPath, string ExpectedError) CreateManifestReadFailure(string failureMode)
    {
        switch (failureMode)
        {
            case "missing":
                var missingPath = Path.Combine(_root, "manifests", "missing-manifest.json");
                return (missingPath, $"Provenance manifest not found: {missingPath}");
            case "malformed":
                var malformedPath = WriteManifest("{ not json", Path.Combine("manifests", "malformed-manifest.json"));
                return (malformedPath, "Exception calling \"Parse\" with \"1\" argument(s): \"'n' is an invalid start of a property name. Expected a '\"'. LineNumber: 0 | BytePositionInLine: 2.\"");
            default:
                throw new ArgumentOutOfRangeException(nameof(failureMode), failureMode, null);
        }
    }

    private static void WriteScript(string root, string relativePath)
    {
        var full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, """
        {
          "resourceType": "TestScript",
          "name": "CRUD/basic",
          "status": "active",
          "test": [
            {
              "name": "smoke",
              "action": [
                { "assert": { "description": "smoke", "warningOnly": true } }
              ]
            }
          ]
        }
        """);
    }

    private void WriteSidecar(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void WriteProvenance(string relativePath, string target)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, $$"""
        {
          "resourceType": "Provenance",
          "target": [
            {
              "identifier": {
                "system": "urn:ignixa-lab:testscripts:path",
                "value": "{{target}}"
              },
              "display": "{{target}}"
            }
          ],
          "recorded": "2026-07-07T00:00:00Z",
          "agent": [
            {
              "who": {
                "display": "Ignixa Lab maintainers"
              }
            }
          ],
          "entity": [
            {
              "role": "source",
              "what": {
                "reference": "https://github.com/brendankowitz/ignixa-lab",
                "display": "Ignixa Lab"
              }
            }
          ]
        }
        """);
    }

    private async Task<AuditResult> RunAuditAsync(
        string manifestPath,
        bool strict = false,
        string? suitesDirectory = null)
    {
        var script = FindRepoRootTool("verify-provenance.ps1");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add("-SuitesDirectory");
        process.StartInfo.ArgumentList.Add(suitesDirectory ?? _root);
        process.StartInfo.ArgumentList.Add("-ManifestPath");
        process.StartInfo.ArgumentList.Add(manifestPath);
        if (strict)
        {
            process.StartInfo.ArgumentList.Add("-Strict");
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new AuditResult(process.ExitCode, stdout + stderr);
    }

    private static async Task<AuditResult> RunGeneratorAsync(
        string suitesDirectory,
        string manifestPath)
    {
        var script = FindRepoRootTool("new-provenance-sidecars.ps1");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add("-SuitesDirectory");
        process.StartInfo.ArgumentList.Add(suitesDirectory);
        process.StartInfo.ArgumentList.Add("-ManifestPath");
        process.StartInfo.ArgumentList.Add(manifestPath);
        process.StartInfo.ArgumentList.Add("-Force");

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new AuditResult(process.ExitCode, stdout + stderr);
    }

    private static string FindRepoRootTool(string fileName)
    {
        return Path.Combine(
            FindRepoRootDirectory(),
            "backend",
            "src",
            "Ignixa.Lab.Suites",
            "tools",
            fileName);
    }

    private static string FindRepoRootDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "backend", "src", "Ignixa.Lab.Suites", "tools");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root from the test output directory.");
    }

    private sealed record AuditResult(int ExitCode, string CombinedOutput);

    private sealed record RepositoryManifest(
        int SchemaVersion,
        IReadOnlyDictionary<string, ManifestSource[]> Profiles,
        IReadOnlyDictionary<string, RepositoryManifestSuite> Suites);

    private sealed record RepositoryManifestSuite(
        string Profile,
        string Activity,
        string Recorded);

    private sealed record ManifestSource(
        string Reference,
        string Display,
        string Relationship,
        string License,
        string? Version,
        string Notes);
}
