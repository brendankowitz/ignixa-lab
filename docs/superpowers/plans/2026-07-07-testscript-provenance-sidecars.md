# TestScript Provenance Sidecars Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add packaged, warning-only FHIR R4 Provenance sidecars for bundled distilled TestScripts without changing runtime APIs or frontend behavior.

**Architecture:** Provenance sidecars live beside each executable TestScript as `<suite>.provenance.json`. `SuiteCatalog` treats `*.provenance.json` as metadata, not executable suites. Maintainer-facing PowerShell tooling validates sidecars warning-only and is invoked during suite packaging so CI exposes attribution gaps without failing.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, PowerShell 7 (`pwsh`), MSBuild/NuGet content package, FHIR R4 JSON resources.

---

## File structure

- Modify `backend\src\Ignixa.Lab.Functions\Suites\SuiteCatalog.cs`: filter out provenance sidecars before parsing TestScript files.
- Modify `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs`: add a regression test proving provenance sidecars do not create invalid-suite warnings or affect suite count.
- Create `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTestLogger.cs`: focused logger helper for warning assertions.
- Create `backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1`: reusable PowerShell functions for discovering TestScripts, generating FHIR Provenance resources, and auditing sidecars.
- Create `backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1`: warning-only audit command used by maintainers and packaging.
- Create `backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1`: deterministic retrofit generator for the current suite set.
- Modify `backend\pack-suites.ps1`: run warning-only provenance audit before `dotnet pack`.
- Create `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs`: xUnit tests that invoke the PowerShell audit command and prove it exits successfully while warning.
- Create `backend\src\Ignixa.Lab.Suites\testscripts\**\*.provenance.json`: one sidecar per executable TestScript.
- Modify `backend\README.md`, `docs\development.md`, and `docs\features\testscript-suite-sourcing\readme.md`: document provenance sidecars and fix stale suite paths/count wording.

## Task 1: Make SuiteCatalog ignore provenance sidecars

**Files:**
- Modify: `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs`
- Create: `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTestLogger.cs`
- Modify: `backend\src\Ignixa.Lab.Functions\Suites\SuiteCatalog.cs`

- [ ] **Step 1: Write the failing logger helper**

Create `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTestLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Tests.Suites;

internal sealed class SuiteCatalogTestLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add((logLevel, formatter(state, exception)));
    }
}
```

- [ ] **Step 2: Write the failing SuiteCatalog test**

In `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs`, add this helper after `WriteScript`:

```csharp
private void WriteProvenanceSidecar(string relativeScriptPath)
{
    var relativeSidecarPath = Path.ChangeExtension(relativeScriptPath, ".provenance.json");
    var full = Path.Combine(_root, relativeSidecarPath);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    var target = relativeScriptPath.Replace(Path.DirectorySeparatorChar, '/');
    var json = $$"""
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
    """;
    File.WriteAllText(full, json);
}
```

Then add this test after `GetSuites_SkipsInvalidJsonFiles`:

```csharp
[Fact]
public void GetSuites_IgnoresProvenanceSidecarsWithoutWarnings()
{
    WriteScript(Path.Combine("crud", "patient.json"), "Patient CRUD");
    WriteProvenanceSidecar(Path.Combine("crud", "patient.json"));
    var logger = new SuiteCatalogTestLogger<SuiteCatalog>();
    var catalog = new SuiteCatalog(
        Options.Create(new IgnixaLabOptions { SuitesDirectory = _root }),
        logger);

    var suites = catalog.GetSuites();

    suites.Should().ContainSingle().Which.Id.Should().Be("crud/patient.json");
    logger.Entries.Should().NotContain(entry =>
        entry.Level >= LogLevel.Warning &&
        entry.Message.Contains("patient.provenance.json", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 3: Run the new test and verify it fails**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests.GetSuites_IgnoresProvenanceSidecarsWithoutWarnings"
```

Expected: FAIL because `SuiteCatalog` enumerates `*.json`, attempts to parse `patient.provenance.json`, and logs a warning containing `patient.provenance.json`.

- [ ] **Step 4: Add the catalog filter**

In `backend\src\Ignixa.Lab.Functions\Suites\SuiteCatalog.cs`, replace:

```csharp
var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories);
```

with:

```csharp
var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
    .Where(file => !file.EndsWith(".provenance.json", StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 5: Run the focused test and verify it passes**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests.GetSuites_IgnoresProvenanceSidecarsWithoutWarnings"
```

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```powershell
git add backend\src\Ignixa.Lab.Functions\Suites\SuiteCatalog.cs backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTestLogger.cs
git commit -m "Ignore TestScript provenance sidecars in suite catalog" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Add warning-only provenance audit tooling

**Files:**
- Create: `backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1`
- Create: `backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1`
- Create: `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs`

- [ ] **Step 1: Write failing audit command tests**

Create `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs`:

```csharp
using System.Diagnostics;
using FluentAssertions;

namespace Ignixa.Lab.Functions.Tests.Suites;

public sealed class SuiteProvenanceAuditTests : IDisposable
{
    private readonly string _root;

    public SuiteProvenanceAuditTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ignixa-lab-provenance-tests", Guid.NewGuid().ToString("N"));
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
        result.CombinedOutput.Should().Contain("Target mismatch in CRUD/basic.provenance.json");
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

    private void WriteScript(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
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
        var script = FindRepoRootScript();
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
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new AuditResult(process.ExitCode, stdout + stderr);
    }

    private static string FindRepoRootScript()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "backend",
                "src",
                "Ignixa.Lab.Suites",
                "tools",
                "verify-provenance.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not find verify-provenance.ps1 from the test output directory.");
    }

    private sealed record AuditResult(int ExitCode, string CombinedOutput);
}
```

- [ ] **Step 2: Run audit tests and verify they fail**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
```

Expected: FAIL because `backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1` does not exist.

- [ ] **Step 3: Add the reusable PowerShell provenance module**

Create `backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1`:

```powershell
Set-StrictMode -Version Latest

$script:PathSystem = 'urn:ignixa-lab:testscripts:path'
$script:ActivitySystem = 'http://ignixa.io/fhir/provenance-activity'
$script:RelationshipExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-source-relationship'
$script:LicenseExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-source-license'
$script:VersionExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-source-version'
$script:NotesExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-distillation-notes'

function Get-TestScriptFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory
    )

    if (-not (Test-Path -LiteralPath $SuitesDirectory -PathType Container)) {
        return @()
    }

    Get-ChildItem -LiteralPath $SuitesDirectory -Recurse -File -Filter '*.json' |
        Where-Object { $_.Name -notlike '*.provenance.json' -and $_.Name -ne 'source-revision.txt' } |
        Sort-Object FullName
}

function ConvertTo-SuiteRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $root = [System.IO.Path]::GetFullPath($SuitesDirectory)
    $full = [System.IO.Path]::GetFullPath($Path)
    [System.IO.Path]::GetRelativePath($root, $full).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
}

function Get-ProvenanceSidecarPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $TestScriptPath
    )

    $directory = Split-Path -Parent $TestScriptPath
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($TestScriptPath)
    Join-Path $directory "$baseName.provenance.json"
}

function New-ProvenanceEntity {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Source
    )

    $extensions = @(
        @{
            url = $script:RelationshipExtensionUrl
            valueCode = $Source.Relationship
        },
        @{
            url = $script:LicenseExtensionUrl
            valueString = $Source.License
        },
        @{
            url = $script:NotesExtensionUrl
            valueString = $Source.Notes
        }
    )

    if ($Source.ContainsKey('Version') -and -not [string]::IsNullOrWhiteSpace([string] $Source.Version)) {
        $extensions += @{
            url = $script:VersionExtensionUrl
            valueString = $Source.Version
        }
    }

    @{
        role = 'source'
        what = @{
            reference = $Source.Reference
            display = $Source.Display
        }
        extension = $extensions
    }
}

function New-TestScriptProvenance {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath,

        [Parameter(Mandatory = $true)]
        [hashtable[]] $Sources,

        [string] $Recorded = '2026-07-07T00:00:00Z'
    )

    @{
        resourceType = 'Provenance'
        target = @(
            @{
                identifier = @{
                    system = $script:PathSystem
                    value = $RelativePath
                }
                display = $RelativePath
            }
        )
        recorded = $Recorded
        activity = @{
            coding = @(
                @{
                    system = $script:ActivitySystem
                    code = 'distill-testscript'
                    display = 'Distill TestScript'
                }
            )
        }
        agent = @(
            @{
                who = @{
                    identifier = @{
                        system = 'https://github.com/brendankowitz/ignixa-lab'
                        value = 'maintainers'
                    }
                    display = 'Ignixa Lab maintainers'
                }
            }
        )
        entity = @($Sources | ForEach-Object { New-ProvenanceEntity -Source $_ })
    }
}

function Test-ProvenanceSidecar {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory,

        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $TestScript
    )

    $relativePath = ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $TestScript.FullName
    $sidecarPath = Get-ProvenanceSidecarPath -TestScriptPath $TestScript.FullName
    $relativeSidecarPath = ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $sidecarPath
    $warnings = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
        $warnings.Add("Missing provenance sidecar: $relativeSidecarPath")
        return $warnings
    }

    try {
        $provenance = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json -Depth 100
    }
    catch {
        $warnings.Add("Invalid JSON in $relativeSidecarPath`: $($_.Exception.Message)")
        return $warnings
    }

    if ($provenance.resourceType -ne 'Provenance') {
        $warnings.Add("Invalid provenance resourceType in $relativeSidecarPath`: expected Provenance.")
    }

    if ($null -eq $provenance.target -or $provenance.target.Count -lt 1) {
        $warnings.Add("Missing target in $relativeSidecarPath")
    }
    else {
        $targetValue = [string] $provenance.target[0].identifier.value
        if ($targetValue -ne $relativePath) {
            $warnings.Add("Target mismatch in $relativeSidecarPath`: expected $relativePath, found $targetValue")
        }
    }

    if ($null -eq $provenance.agent -or $provenance.agent.Count -lt 1) {
        $warnings.Add("Missing agent in $relativeSidecarPath")
    }

    if ($null -eq $provenance.entity -or $provenance.entity.Count -lt 1) {
        $warnings.Add("Missing entity in $relativeSidecarPath")
    }

    $warnings
}

function Invoke-TestScriptProvenanceAudit {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory
    )

    $warnings = New-Object System.Collections.Generic.List[string]
    $scripts = @(Get-TestScriptFile -SuitesDirectory $SuitesDirectory)

    foreach ($scriptFile in $scripts) {
        foreach ($warning in Test-ProvenanceSidecar -SuitesDirectory $SuitesDirectory -TestScript $scriptFile) {
            $warnings.Add($warning)
        }
    }

    [pscustomobject] @{
        ScriptCount = $scripts.Count
        WarningCount = $warnings.Count
        Warnings = @($warnings)
    }
}

Export-ModuleMember -Function @(
    'Get-TestScriptFile',
    'ConvertTo-SuiteRelativePath',
    'Get-ProvenanceSidecarPath',
    'New-TestScriptProvenance',
    'Invoke-TestScriptProvenanceAudit'
)
```

- [ ] **Step 4: Add the warning-only audit command**

Create `backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1`:

```powershell
#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [string] $SuitesDirectory = (Join-Path $PSScriptRoot '..' 'testscripts')
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TestScriptProvenance.psm1') -Force

$result = Invoke-TestScriptProvenanceAudit -SuitesDirectory $SuitesDirectory

foreach ($warning in $result.Warnings) {
    Write-Warning $warning
}

$noun = if ($result.ScriptCount -eq 1) { 'file' } else { 'files' }
Write-Output "Provenance audit scanned $($result.ScriptCount) TestScript $noun and found $($result.WarningCount) warning(s)."

exit 0
```

- [ ] **Step 5: Run audit tests and verify they pass**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
```

Expected: PASS.

- [ ] **Step 6: Commit Task 2**

```powershell
git add backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1 backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs
git commit -m "Add warning-only TestScript provenance audit" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Wire the audit into suite packaging

**Files:**
- Modify: `backend\pack-suites.ps1`

- [ ] **Step 1: Run current pack command as baseline**

Run:

```powershell
.\backend\pack-suites.ps1
```

Expected: succeeds and produces `artifacts\local-feed\IgnixaLab.TestScript.Suites.0.1.0-local.nupkg`.

- [ ] **Step 2: Call the warning-only audit from packaging**

In `backend\pack-suites.ps1`, after the `New-Item` call and before `dotnet pack`, add:

```powershell
$provenanceAudit = Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/tools/verify-provenance.ps1'
if (Test-Path -LiteralPath $provenanceAudit -PathType Leaf) {
    & $provenanceAudit -SuitesDirectory (Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/testscripts')
}
```

The surrounding section should read:

```powershell
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$provenanceAudit = Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/tools/verify-provenance.ps1'
if (Test-Path -LiteralPath $provenanceAudit -PathType Leaf) {
    & $provenanceAudit -SuitesDirectory (Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/testscripts')
}

dotnet pack $project -c Release -o $outputDir /nodeReuse:false
```

- [ ] **Step 3: Run pack and confirm warnings do not fail packaging**

Run:

```powershell
.\backend\pack-suites.ps1
```

Expected before sidecars are generated: exit code 0, warning lines for missing provenance sidecars, and a package under `artifacts\local-feed`.

- [ ] **Step 4: Commit Task 3**

```powershell
git add backend\pack-suites.ps1
git commit -m "Run provenance audit while packing suites" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Generate sidecars for the existing bundled suite set

**Files:**
- Create: `backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1`
- Create: `backend\src\Ignixa.Lab.Suites\testscripts\**\*.provenance.json`

- [ ] **Step 1: Add the deterministic sidecar generator**

Create `backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1`:

```powershell
#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [string] $SuitesDirectory = (Join-Path $PSScriptRoot '..' 'testscripts'),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TestScriptProvenance.psm1') -Force

$sourceProfiles = @{
    Microsoft = @(
        @{
            Reference = 'https://github.com/microsoft/fhir-server'
            Display = 'Microsoft FHIR Server test coverage'
            Relationship = 'distilled-from'
            License = 'MIT'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Converted Microsoft FHIR Server behavior into black-box FHIR TestScript assertions and kept Microsoft-specific operations in the Microsoft category.'
        }
    )
    OperationsTerminology = @(
        @{
            Reference = 'https://hl7.org/fhir/R4/terminology-service.html'
            Display = 'FHIR R4 terminology service operations'
            Relationship = 'spec-reference'
            License = 'FHIR specification license'
            Version = 'FHIR R4'
            Notes = 'Used the FHIR operation definitions as the normative source for terminology-operation behavior.'
        },
        @{
            Reference = 'https://github.com/hapifhir/hapi-fhir'
            Display = 'HAPI FHIR terminology test coverage'
            Relationship = 'inspired-by'
            License = 'Apache-2.0'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Used as comparative open-source coverage while distilling black-box TestScript terminology scenarios.'
        }
    )
    Subscriptions = @(
        @{
            Reference = 'https://hl7.org/fhir/R4/subscription.html'
            Display = 'FHIR R4 Subscription resource'
            Relationship = 'spec-reference'
            License = 'FHIR specification license'
            Version = 'FHIR R4'
            Notes = 'Used the FHIR Subscription definition for basic create/read/search/update/delete expectations.'
        },
        @{
            Reference = 'https://github.com/medplum/fhir-candle'
            Display = 'fhir-candle Subscription coverage'
            Relationship = 'inspired-by'
            License = 'source-declared open-source license'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Used as comparative open-source coverage for Subscription round-tripping.'
        },
        @{
            Reference = 'https://github.com/LinuxForHealth/FHIR'
            Display = 'LinuxForHealth FHIR Subscription coverage'
            Relationship = 'inspired-by'
            License = 'Apache-2.0'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Used as comparative open-source coverage for Subscription resource behavior.'
        }
    )
    Bundles = @(
        @{
            Reference = 'https://hl7.org/fhir/R4/http.html'
            Display = 'FHIR R4 RESTful API batch and transaction interactions'
            Relationship = 'spec-reference'
            License = 'FHIR specification license'
            Version = 'FHIR R4'
            Notes = 'Used FHIR REST semantics as the normative source for bundle interaction expectations.'
        },
        @{
            Reference = 'https://github.com/microsoft/fhir-server'
            Display = 'Microsoft FHIR Server bundle test coverage'
            Relationship = 'distilled-from'
            License = 'MIT'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Converted server-specific e2e bundle behavior into black-box TestScript assertions.'
        }
    )
    Ignixa = @(
        @{
            Reference = 'https://github.com/brendankowitz/ignixa-lab'
            Display = 'Ignixa Lab bundled TestScript suite'
            Relationship = 'direct-port'
            License = 'repository license'
            Version = 'current repository revision'
            Notes = 'Authored or curated directly in Ignixa Lab as a canonical bundled TestScript.'
        },
        @{
            Reference = 'https://github.com/brendankowitz/ignixa-fhir'
            Display = 'ignixa-fhir TestScript engine and conformance coverage'
            Relationship = 'inspired-by'
            License = 'repository license'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Aligned with ignixa-fhir TestScript execution behavior and conformance dashboard expectations.'
        }
    )
    FhirSpec = @(
        @{
            Reference = 'https://hl7.org/fhir/R4/'
            Display = 'FHIR R4 specification'
            Relationship = 'spec-reference'
            License = 'FHIR specification license'
            Version = 'FHIR R4'
            Notes = 'Distilled from normative FHIR behavior rather than a single upstream server test.'
        }
    )
}

function Get-SourceProfileName {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    if ($RelativePath -eq 'Subscriptions/basic.json') {
        return 'Subscriptions'
    }

    if ($RelativePath -in @(
        'Operations/lookup-operation.json',
        'Operations/validate-code-operation.json',
        'Operations/subsumes-operation.json',
        'Operations/translate-operation.json'
    )) {
        return 'OperationsTerminology'
    }

    if ($RelativePath.StartsWith('Microsoft/', [StringComparison]::Ordinal)) {
        return 'Microsoft'
    }

    if ($RelativePath.StartsWith('Bundles/', [StringComparison]::Ordinal)) {
        return 'Bundles'
    }

    if ($RelativePath.StartsWith('Regression/', [StringComparison]::Ordinal) -or
        $RelativePath -eq 'Foundation/health.json' -or
        $RelativePath -eq 'Foundation/cors.json') {
        return 'Ignixa'
    }

    if ($RelativePath.StartsWith('CRUD/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Search/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Foundation/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Operations/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Validation/', [StringComparison]::Ordinal)) {
        return 'Microsoft'
    }

    return 'FhirSpec'
}

$scripts = @(Get-TestScriptFile -SuitesDirectory $SuitesDirectory)
$created = 0
$skipped = 0

foreach ($scriptFile in $scripts) {
    $relativePath = ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $scriptFile.FullName
    $sidecarPath = Get-ProvenanceSidecarPath -TestScriptPath $scriptFile.FullName

    if ((Test-Path -LiteralPath $sidecarPath -PathType Leaf) -and -not $Force) {
        $skipped++
        continue
    }

    $profileName = Get-SourceProfileName -RelativePath $relativePath
    $provenance = New-TestScriptProvenance -RelativePath $relativePath -Sources $sourceProfiles[$profileName]
    $json = $provenance | ConvertTo-Json -Depth 100
    Set-Content -LiteralPath $sidecarPath -Value $json -Encoding utf8
    $created++
}

Write-Output "Generated $created provenance sidecar(s); skipped $skipped existing sidecar(s)."
```

- [ ] **Step 2: Generate sidecars**

Run:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1
```

Expected: creates one `*.provenance.json` file next to each executable TestScript and prints `Generated 87 provenance sidecar(s); skipped 0 existing sidecar(s).`

- [ ] **Step 3: Verify audit is quiet after generation**

Run:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1
```

Expected: `Provenance audit scanned 87 TestScript files and found 0 warning(s).`

- [ ] **Step 4: Spot-check representative sidecars**

Run:

```powershell
Get-Content backend\src\Ignixa.Lab.Suites\testscripts\Microsoft\ms-convert-data.provenance.json -Raw
Get-Content backend\src\Ignixa.Lab.Suites\testscripts\Operations\lookup-operation.provenance.json -Raw
Get-Content backend\src\Ignixa.Lab.Suites\testscripts\Subscriptions\basic.provenance.json -Raw
```

Expected:
- `Microsoft\ms-convert-data.provenance.json` targets `Microsoft/ms-convert-data.json` and cites `https://github.com/microsoft/fhir-server`.
- `Operations\lookup-operation.provenance.json` targets `Operations/lookup-operation.json` and cites FHIR terminology service plus HAPI FHIR.
- `Subscriptions\basic.provenance.json` targets `Subscriptions/basic.json` and cites FHIR Subscription plus fhir-candle/LinuxForHealth.

- [ ] **Step 5: Commit Task 4**

```powershell
git add backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 backend\src\Ignixa.Lab.Suites\testscripts
git commit -m "Add Provenance sidecars for bundled TestScripts" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Prove sidecars are packaged without becoming suites

**Files:**
- Modify: `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs`

- [ ] **Step 1: Add bundled package assertions**

In `backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs`, add these tests after `GetSuites_BundledCanonicalSuites_IncludeKnownIds`:

```csharp
[Fact]
public void GetSuites_BundledCanonicalSuites_IgnorePackagedProvenanceSidecars()
{
    var suites = CreateBundledCatalog().GetSuites();

    suites.Select(s => s.Id).Should().NotContain(id =>
        id.EndsWith(".provenance.json", StringComparison.OrdinalIgnoreCase));
    suites.Should().HaveCount(87);
}

[Fact]
public void GetSuites_BundledCanonicalSuites_CopyProvenanceSidecars()
{
    var sidecar = Path.Combine(
        AppContext.BaseDirectory,
        "testscripts",
        "Microsoft",
        "ms-convert-data.provenance.json");

    File.Exists(sidecar).Should().BeTrue();
}
```

- [ ] **Step 2: Repack suites before running bundled-output tests**

Run:

```powershell
.\backend\pack-suites.ps1
```

Expected: provenance audit scans 87 TestScript files, finds 0 warnings, and `dotnet pack` succeeds.

- [ ] **Step 3: Run the focused bundled tests**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests.GetSuites_BundledCanonicalSuites_"
```

Expected: PASS, including the unchanged 87-suite count and sidecar copy assertion.

- [ ] **Step 4: Commit Task 5**

```powershell
git add backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs
git commit -m "Cover packaged TestScript provenance sidecars" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: Update documentation

**Files:**
- Modify: `backend\README.md`
- Modify: `docs\development.md`
- Modify: `docs\features\testscript-suite-sourcing\readme.md`

- [ ] **Step 1: Update suite count and provenance guidance in backend README**

In `backend\README.md`, update the `## Suites` section so the opening paragraph reads:

```markdown
The 87 canonical FHIR TestScript suites (`backend/src/Ignixa.Lab.Suites/testscripts/{Bundles,CRUD,Foundation,Microsoft,Operations,Regression,Search,Subscriptions,Validation}/*.json`)
are packed into a local NuGet content package, `IgnixaLab.TestScript.Suites`, by
the `Ignixa.Lab.Suites` project and consumed by `Ignixa.Lab.Functions` (and
its test project) via `PackageReference`.
```

After the paragraph describing `build/IgnixaLab.TestScript.Suites.targets`, add:

```markdown
Each distilled TestScript should have a sibling FHIR R4 Provenance sidecar named
`<suite>.provenance.json`. Sidecars are packaged with the suites but ignored by
`SuiteCatalog`; they record the source repositories, specifications, or APIs
used while distilling the executable TestScript. `pack-suites.ps1` runs the
warning-only provenance audit so missing or invalid sidecars show up in CI logs
without failing the build.
```

- [ ] **Step 2: Update suite authoring docs**

In `docs\development.md`, replace the `Adding a TestScript suite` steps with:

```markdown
1. Drop a FHIR TestScript JSON file under
   `backend/src/Ignixa.Lab.Suites/testscripts/<category>/`.
   The `<category>` folder name becomes the suite's category.
2. Add or update the sibling FHIR R4 Provenance sidecar named
   `<suite>.provenance.json`. The sidecar targets the TestScript's path relative
   to `testscripts/` and lists the repositories, specifications, APIs, or prior
   tests used while distilling the suite.
3. Run `pwsh -NoLogo -NoProfile -NonInteractive -File backend/src/Ignixa.Lab.Suites/tools/verify-provenance.ps1`
   to check the sidecar. The audit is warning-only, but new warnings should be
   resolved before review.
4. Run `./backend/pack-suites.ps1` before restore/build/test so the
   `IgnixaLab.TestScript.Suites` package in `artifacts/local-feed` includes the
   new TestScript and its provenance sidecar.
5. The TestScript appears in `GET /api/suites` and becomes selectable in the SPA;
   the provenance sidecar is packaged for auditability but is not exposed through
   runtime APIs yet.
```

- [ ] **Step 3: Update feature sourcing doc**

In `docs\features\testscript-suite-sourcing\readme.md`, append:

```markdown
## Provenance sidecars

Bundled TestScripts carry per-file FHIR R4 Provenance sidecars named
`<suite>.provenance.json` beside the executable TestScript. The sidecars are
packaged with `IgnixaLab.TestScript.Suites`, ignored by `SuiteCatalog`, and used
for auditability rather than runtime behavior. They complement the package-level
`source-revision.txt`: the revision identifies the ignixa-lab commit that was
packed, while each Provenance resource records the upstream source entities that
influenced the distilled TestScript.
```

- [ ] **Step 4: Commit Task 6**

```powershell
git add backend\README.md docs\development.md docs\features\testscript-suite-sourcing\readme.md
git commit -m "Document TestScript provenance sidecars" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 7: Final backend verification

**Files:**
- No code changes unless verification exposes a defect in files touched by earlier tasks.

- [ ] **Step 1: Run provenance audit directly**

Run:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1
```

Expected: `Provenance audit scanned 87 TestScript files and found 0 warning(s).`

- [ ] **Step 2: Pack suites**

Run:

```powershell
.\backend\pack-suites.ps1
```

Expected: audit prints 0 warnings and `dotnet pack` succeeds.

- [ ] **Step 3: Build solution**

Run:

```powershell
dotnet build Ignixa.Lab.sln -c Release
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run backend tests**

Run:

```powershell
dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity normal
```

Expected: all xUnit tests pass.

- [ ] **Step 5: Inspect final diff**

Run:

```powershell
git --no-pager status --short
git --no-pager log --oneline -8
```

Expected: working tree contains only intentional uncommitted generated artifacts if any are ignored by git, and recent commits correspond to Tasks 1-6 plus the design/plan commits.

## Self-review checklist

- Spec goal 1 is covered by Task 4: creates one FHIR R4 Provenance sidecar per bundled TestScript.
- Spec goal 2 is covered by Tasks 3 and 5: sidecars are packaged and copied, with no API or frontend contract changes.
- Spec goal 3 is covered by Tasks 4 and 6: existing suites are retrofitted and docs require future sidecars.
- Spec goal 4 is covered by Tasks 2 and 3: audit warns and exits successfully.
- Spec goal 5 is covered by Tasks 1 and 5: `SuiteCatalog` ignores sidecars and count remains 87.
- Documentation updates are covered by Task 6.
- Final verification is covered by Task 7.
