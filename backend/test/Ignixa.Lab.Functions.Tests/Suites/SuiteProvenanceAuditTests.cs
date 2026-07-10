using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace Ignixa.Lab.Functions.Tests.Suites;

public sealed class SuiteProvenanceAuditTests : IDisposable
{
    private readonly string _root;

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
    public async Task VerifyProvenance_WarnsButSucceedsWhenSidecarIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Missing provenance sidecar: CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_WarnsButSucceedsWhenTargetDoesNotMatchScript()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteProvenance(Path.Combine("CRUD", "basic.provenance.json"), "Search/basic.json");

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Target mismatch in CRUD/basic.provenance.json: expected CRUD/basic.json, found Search/basic.json");
    }

    [Fact]
    public async Task VerifyProvenance_WarnsButSucceedsWhenSidecarJsonIsInvalid()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), "{ this is not valid json");

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Invalid JSON in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_WarnsButSucceedsWhenResourceTypeIsWrong()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Patient",
          "target": [{ "identifier": { "value": "CRUD/basic.json" } }],
          "agent": [{ "who": { "display": "Ignixa Lab maintainers" } }],
          "entity": [{ "role": "source" }]
        }
        """);

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Invalid provenance resourceType in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_WarnsButSucceedsWhenTargetIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Provenance",
          "agent": [{ "who": { "display": "Ignixa Lab maintainers" } }],
          "entity": [{ "role": "source" }]
        }
        """);

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Missing target in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_WarnsButSucceedsWhenAgentIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Provenance",
          "target": [{ "identifier": { "value": "CRUD/basic.json" } }],
          "entity": [{ "role": "source" }]
        }
        """);

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Missing agent in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_WarnsButSucceedsWhenEntityIsMissing()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteSidecar(Path.Combine("CRUD", "basic.provenance.json"), """
        {
          "resourceType": "Provenance",
          "target": [{ "identifier": { "value": "CRUD/basic.json" } }],
          "agent": [{ "who": { "display": "Ignixa Lab maintainers" } }]
        }
        """);

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("WARNING");
        result.CombinedOutput.Should().Contain("Missing entity in CRUD/basic.provenance.json");
    }

    [Fact]
    public async Task VerifyProvenance_SucceedsWithoutWarningsWhenSidecarIsValid()
    {
        WriteScript(Path.Combine("CRUD", "basic.json"));
        WriteProvenance(Path.Combine("CRUD", "basic.provenance.json"), "CRUD/basic.json");

        var result = await RunAuditAsync();

        result.ExitCode.Should().Be(0);
        result.CombinedOutput.Should().Contain("Provenance audit scanned 1 TestScript file and found 0 warning(s).");
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
          -Sources @($source) | ConvertTo-Json -Depth 20
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
    public async Task NewProvenanceSidecars_GeneratesDeterministicOutputAcrossProcesses()
    {
        var firstRoot = Path.Combine(_root, "first");
        var secondRoot = Path.Combine(_root, "second");
        WriteScript(firstRoot, Path.Combine("CRUD", "basic.json"));
        WriteScript(secondRoot, Path.Combine("CRUD", "basic.json"));

        var firstResult = await RunGeneratorAsync(firstRoot);
        var secondResult = await RunGeneratorAsync(secondRoot);

        firstResult.ExitCode.Should().Be(0, firstResult.CombinedOutput);
        secondResult.ExitCode.Should().Be(0, secondResult.CombinedOutput);
        firstResult.CombinedOutput.Should().Contain("Generated 1 provenance sidecar(s); skipped 0 existing sidecar(s).");
        secondResult.CombinedOutput.Should().Contain("Generated 1 provenance sidecar(s); skipped 0 existing sidecar(s).");

        var firstSidecar = await File.ReadAllBytesAsync(Path.Combine(firstRoot, "CRUD", "basic.provenance.json"));
        var secondSidecar = await File.ReadAllBytesAsync(Path.Combine(secondRoot, "CRUD", "basic.provenance.json"));

        firstSidecar.Should().Equal(secondSidecar);
    }

    [Fact]
    public async Task NewProvenanceSidecars_MapsExpandOperationToTerminologySources()
    {
        WriteScript(Path.Combine("Operations", "expand-operation.json"));

        var result = await RunGeneratorAsync(_root);

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

        var result = await RunGeneratorAsync(_root);

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

        var result = await RunGeneratorAsync(_root);

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

        var result = await RunGeneratorAsync(_root);

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

        var result = await RunGeneratorAsync(_root);

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

        var result = await RunGeneratorAsync(_root);

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

    private void WriteScript(string relativePath)
    {
        WriteScript(_root, relativePath);
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

    private async Task<AuditResult> RunAuditAsync()
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
        process.StartInfo.ArgumentList.Add(_root);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new AuditResult(process.ExitCode, stdout + stderr);
    }

    private static async Task<AuditResult> RunGeneratorAsync(string suitesDirectory)
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
}
