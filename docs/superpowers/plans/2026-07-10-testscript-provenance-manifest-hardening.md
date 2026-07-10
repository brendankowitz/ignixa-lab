# TestScript Provenance Manifest Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace inferred TestScript attribution with a complete explicit manifest, generate accurate authored/distilled Provenance activities, and make structural provenance failures block packaging.

**Architecture:** `tools/provenance-manifest.json` becomes the authoritative mapping from every suite-relative TestScript path to a source profile, activity, and recorded date. `TestScriptProvenance.psm1` validates and resolves the manifest, builds deterministic FHIR R4 Provenance resources, and audits committed sidecars against generated expectations. Command wrappers retain advisory mode for maintainers while packaging invokes strict mode.

**Tech Stack:** PowerShell 7, FHIR R4 JSON, .NET 10, xUnit, FluentAssertions, existing NuGet suite packaging

---

## File structure

- Create `backend/src/Ignixa.Lab.Suites/tools/provenance-manifest.json`
  - Authoritative profiles and one explicit entry per TestScript path.
- Modify `backend/src/Ignixa.Lab.Suites/tools/TestScriptProvenance.psm1`
  - Manifest parsing/validation/resolution, activity-aware Provenance construction,
    deterministic serialization, and stale-sidecar audit.
- Modify `backend/src/Ignixa.Lab.Suites/tools/new-provenance-sidecars.ps1`
  - Thin manifest-driven generator; remove source profiles and path inference.
- Modify `backend/src/Ignixa.Lab.Suites/tools/verify-provenance.ps1`
  - Report blocking errors separately from warnings and support `-Strict`.
- Modify `backend/src/Ignixa.Lab.Suites/testscripts/**/*.provenance.json`
  - Regenerated output, including correct authored/distilled activities.
- Modify `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteProvenanceAuditTests.cs`
  - Process-level regression coverage for manifest, generation, audit, and strict
    mode.
- Modify `backend/pack-suites.ps1`
  - Invoke strict audit and propagate failure before packing.
- Modify `docs/development.md`
  - Document manifest-first suite authoring workflow.
- Modify `backend/README.md`
  - Replace warning-only sidecar guidance with manifest generation and strict
    verification.
- Modify `docs/features/testscript-suite-sourcing/readme.md`
  - Identify the manifest as source of truth and sidecars as generated packaged
    artifacts.

### Task 1: Make Provenance construction activity- and date-aware

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/tools/TestScriptProvenance.psm1:88-133`
- Modify: `backend/src/Ignixa.Lab.Suites/tools/new-provenance-sidecars.ps1:286-299`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteProvenanceAuditTests.cs:157-222`

- [ ] **Step 1: Change the constructor test to require explicit activity and recorded values**

Update the PowerShell invocation in
`NewTestScriptProvenance_CreatesExpectedFhirProvenanceShape`:

```csharp
New-TestScriptProvenance `
  -RelativePath 'CRUD/basic.json' `
  -Activity 'author-testscript' `
  -Recorded '2026-07-10T12:34:56Z' `
  -Sources @($source) | ConvertTo-Json -Depth 20
```

Change the assertions to:

```csharp
provenance.GetProperty("recorded").GetString().Should().Be("2026-07-10T12:34:56Z");
provenance.GetProperty("activity").GetProperty("coding")[0].GetProperty("code").GetString()
    .Should().Be("author-testscript");
provenance.GetProperty("activity").GetProperty("coding")[0].GetProperty("display").GetString()
    .Should().Be("Author TestScript");
```

- [ ] **Step 2: Run the constructor test and verify it fails**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~NewTestScriptProvenance_CreatesExpectedFhirProvenanceShape"
```

Expected: FAIL because `New-TestScriptProvenance` ignores `Activity` and still
uses the global `2026-07-07T00:00:00Z` default.

- [ ] **Step 3: Require and render supported activities**

Replace the constructor parameters/default activity block with:

```powershell
function New-TestScriptProvenance {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('author-testscript', 'distill-testscript')]
        [string] $Activity,

        [Parameter(Mandatory = $true)]
        [string] $Recorded,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary[]] $Sources
    )

    $activityDisplay = switch ($Activity) {
        'author-testscript' { 'Author TestScript' }
        'distill-testscript' { 'Distill TestScript' }
    }
```

Use `$Activity` and `$activityDisplay` in `activity.coding[0]`; remove the
module-owned recorded default.

Until Task 3 replaces the current generator mapping, update its call to preserve
existing behavior:

```powershell
$provenance = New-TestScriptProvenance `
    -RelativePath $relativePath `
    -Activity 'distill-testscript' `
    -Recorded '2026-07-07T00:00:00Z' `
    -Sources $sourceProfiles[$profileName]
```

- [ ] **Step 4: Run focused and full provenance tests**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
```

Expected: all provenance tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1 backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs
git commit -m "Make TestScript provenance activity explicit" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 2: Add manifest parsing and validation

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/tools/TestScriptProvenance.psm1`
- Modify: `backend/src/Ignixa.Lab.Suites/tools/new-provenance-sidecars.ps1`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteProvenanceAuditTests.cs`

- [ ] **Step 1: Add test helpers for temporary manifests**

Add:

```csharp
private string WriteManifest(string json)
{
    var path = Path.Combine(_root, "provenance-manifest.json");
    File.WriteAllText(path, json);
    return path;
}

private static string CreateManifest(
    string suitePath = "CRUD/basic.json",
    string profile = "example",
    string activity = "distill-testscript",
    string relationship = "distilled-from")
{
    return $$"""
    {
      "schemaVersion": 1,
      "profiles": {
        "{{profile}}": {
          "sources": [
            {
              "reference": "https://example.test/source",
              "display": "Example source",
              "relationship": "{{relationship}}",
              "license": "MIT",
              "version": "v1.0.0",
              "notes": "Test source"
            }
          ]
        }
      },
      "suites": {
        "{{suitePath}}": {
          "profile": "{{profile}}",
          "activity": "{{activity}}",
          "recorded": "2026-07-10T12:34:56Z"
        }
      }
    }
    """;
}
```

Change the helper signature:

```csharp
private static async Task<AuditResult> RunGeneratorAsync(
    string suitesDirectory,
    string manifestPath)
```

After the existing `-SuitesDirectory` argument, add:

```csharp
process.StartInfo.ArgumentList.Add("-ManifestPath");
process.StartInfo.ArgumentList.Add(manifestPath);
```

Update every existing generator test to call `WriteManifest(CreateManifest(...))`
and pass the returned path.

- [ ] **Step 2: Add failing manifest validation tests**

Add tests with these exact expectations:

```csharp
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
}
```

Also add unknown-profile, both-profile-and-inline-sources, and neither-profile-
nor-inline-sources cases. Assert a non-zero exit and no sidecar writes.

- [ ] **Step 3: Run the new tests and verify they fail**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~NewProvenanceSidecars_Fails"
```

Expected: FAIL because the generator has no `-ManifestPath` parameter or
manifest validation.

- [ ] **Step 4: Add manifest APIs to the module**

Add these constants:

```powershell
$script:AllowedActivities = @('author-testscript', 'distill-testscript')
$script:AllowedRelationships = @(
    'authored-in',
    'direct-port',
    'distilled-from',
    'inspired-by',
    'spec-reference'
)
```

Add and export the following manifest functions:

```powershell
function Assert-NoDuplicateJsonProperty {
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement] $Element,

        [string] $Path = '$'
    )

    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) {
                throw "Duplicate manifest property at $Path.$($property.Name)"
            }

            Assert-NoDuplicateJsonProperty `
                -Element $property.Value `
                -Path "$Path.$($property.Name)"
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NoDuplicateJsonProperty -Element $item -Path "$Path[$index]"
            $index++
        }
    }
}

function Read-TestScriptProvenanceManifest {
    param([Parameter(Mandatory = $true)][string] $ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Provenance manifest not found: $ManifestPath"
    }

    $json = Get-Content -LiteralPath $ManifestPath -Raw
    $document = [System.Text.Json.JsonDocument]::Parse($json)
    try {
        Assert-NoDuplicateJsonProperty -Element $document.RootElement
    }
    finally {
        $document.Dispose()
    }

    $json | ConvertFrom-Json -Depth 100 -AsHashtable
}

function Test-TestScriptProvenanceManifest {
    param(
        [Parameter(Mandatory = $true)][string] $SuitesDirectory,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary] $Manifest
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()
    $resolvedSuites = [ordered] @{}

    if (-not $Manifest.Contains('schemaVersion') -or $Manifest.schemaVersion -ne 1) {
        $errors.Add('Unsupported provenance manifest schemaVersion: expected 1.')
    }

    $profiles = if ($Manifest.Contains('profiles')) {
        $Manifest.profiles
    }
    else {
        [ordered] @{}
    }
    $suiteEntries = if ($Manifest.Contains('suites')) {
        $Manifest.suites
    }
    else {
        [ordered] @{}
    }

    $scriptPaths = @(
        Get-TestScriptFile -SuitesDirectory $SuitesDirectory |
            ForEach-Object {
                ConvertTo-SuiteRelativePath `
                    -SuitesDirectory $SuitesDirectory `
                    -Path $_.FullName
            }
    )

    foreach ($scriptPath in $scriptPaths) {
        if (-not $suiteEntries.Contains($scriptPath)) {
            $errors.Add("Missing manifest entry for TestScript: $scriptPath")
        }
    }

    foreach ($entryPath in @($suiteEntries.Keys | Sort-Object)) {
        if ($entryPath -ne $entryPath.Replace('\', '/')) {
            $errors.Add("Manifest suite path must use '/': $entryPath")
        }
        if ($entryPath -notin $scriptPaths) {
            $errors.Add("Manifest entry has no TestScript: $entryPath")
        }

        $entry = $suiteEntries[$entryPath]
        $activity = [string] $entry.activity
        if ($activity -notin $script:AllowedActivities) {
            $errors.Add("Unsupported activity for $entryPath`: $activity")
        }

        $recorded = [string] $entry.recorded
        $parsedRecorded = [DateTimeOffset]::MinValue
        if ([string]::IsNullOrWhiteSpace($recorded) -or
            -not [DateTimeOffset]::TryParse(
                $recorded,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind,
                [ref] $parsedRecorded)) {
            $errors.Add("Invalid recorded date for $entryPath`: $recorded")
        }

        $hasProfile = $entry.Contains('profile') -and
            -not [string]::IsNullOrWhiteSpace([string] $entry.profile)
        $hasSources = $entry.Contains('sources')
        if ($hasProfile -eq $hasSources) {
            $errors.Add(
                "Manifest entry must contain exactly one of profile or sources: $entryPath")
            continue
        }

        if ($hasProfile) {
            $profileName = [string] $entry.profile
            if (-not $profiles.Contains($profileName)) {
                $errors.Add("Unknown provenance profile for $entryPath`: $profileName")
                continue
            }
            $sources = @($profiles[$profileName].sources)
        }
        else {
            $sources = @($entry.sources)
        }

        if ($sources.Count -eq 0) {
            $errors.Add("No provenance sources configured for $entryPath")
            continue
        }

        foreach ($source in $sources) {
            foreach ($required in @(
                'reference',
                'display',
                'relationship',
                'license',
                'notes'
            )) {
                if (-not $source.Contains($required) -or
                    [string]::IsNullOrWhiteSpace([string] $source[$required])) {
                    $errors.Add(
                        "Missing source $required for manifest entry: $entryPath")
                }
            }

            $relationship = [string] $source.relationship
            if ($relationship -notin $script:AllowedRelationships) {
                $errors.Add(
                    "Unsupported source relationship for $entryPath`: $relationship")
            }

            $version = [string] $source.version
            if ([string]::IsNullOrWhiteSpace($version) -or
                $version -eq 'not recorded during original distillation') {
                $warnings.Add(
                    "Upstream version is not pinned for manifest entry: $entryPath")
            }

            if ([string] $source.license -in @(
                'source-declared open-source license',
                'repository license'
            )) {
                $warnings.Add(
                    "Source license is not an SPDX identifier for manifest entry: $entryPath")
            }
        }

        $resolvedSuites[$entryPath] = [pscustomobject] @{
            Activity = $activity
            Recorded = $recorded
            Sources = $sources
        }
    }

    [pscustomobject] @{
        Errors = @($errors | Sort-Object -Unique)
        Warnings = @($warnings | Sort-Object -Unique)
        ResolvedSuites = $resolvedSuites
    }
}

function Resolve-TestScriptProvenanceEntry {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary] $Manifest,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )

    $entry = $Manifest.suites[$RelativePath]
    $sources = if ($entry.Contains('profile')) {
        @($Manifest.profiles[$entry.profile].sources)
    }
    else {
        @($entry.sources)
    }

    [pscustomobject] @{
        Activity = [string] $entry.activity
        Recorded = [string] $entry.recorded
        Sources = $sources
    }
}
```

Export `Read-TestScriptProvenanceManifest`,
`Test-TestScriptProvenanceManifest`, and
`Resolve-TestScriptProvenanceEntry`. Keep validation free of broad catches and
default profiles.

- [ ] **Step 5: Replace generator inference with manifest resolution**

Add the parameter:

```powershell
[string] $ManifestPath = (Join-Path $PSScriptRoot 'provenance-manifest.json')
```

Delete `$sourceProfiles` and `Get-SourceProfileName`. Before writing:

```powershell
$manifest = Read-TestScriptProvenanceManifest -ManifestPath $ManifestPath
$validation = Test-TestScriptProvenanceManifest `
    -SuitesDirectory $SuitesDirectory `
    -Manifest $manifest

if ($validation.Errors.Count -gt 0) {
    throw ($validation.Errors -join [Environment]::NewLine)
}
```

Resolve each entry and call:

```powershell
$entry = Resolve-TestScriptProvenanceEntry `
    -Manifest $manifest `
    -RelativePath $relativePath
$provenance = New-TestScriptProvenance `
    -RelativePath $relativePath `
    -Activity $entry.Activity `
    -Recorded $entry.Recorded `
    -Sources $entry.Sources
```

- [ ] **Step 6: Run the manifest validation and provenance tests**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
```

Expected: all tests pass using temporary explicit manifests.

- [ ] **Step 7: Commit**

```powershell
git add backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1 backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs
git commit -m "Generate TestScript provenance from a manifest" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 3: Migrate all bundled suites to the explicit manifest

**Files:**
- Create: `backend/src/Ignixa.Lab.Suites/tools/provenance-manifest.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/**/*.provenance.json`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteProvenanceAuditTests.cs`

- [ ] **Step 1: Add repository completeness and activity tests**

Add a process helper that invokes the module against the repository manifest,
then add:

```csharp
[Fact]
public void RepositoryManifest_ClassifiesEveryBundledTestScript()
{
    var suitesRoot = Path.Combine(
        FindRepoRootDirectory(),
        "backend",
        "src",
        "Ignixa.Lab.Suites",
        "testscripts");
    var scriptPaths = Directory
        .EnumerateFiles(suitesRoot, "*.json", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith(".provenance.json", StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(suitesRoot, path).Replace('\\', '/'))
        .OrderBy(path => path)
        .ToArray();

    using var document = JsonDocument.Parse(
        File.ReadAllText(FindRepoRootTool("provenance-manifest.json")));
    var manifestPaths = document.RootElement
        .GetProperty("suites")
        .EnumerateObject()
        .Select(property => property.Name)
        .OrderBy(path => path)
        .ToArray();

    scriptPaths.Should().HaveCount(87);
    manifestPaths.Should().Equal(scriptPaths);
}

[Theory]
[InlineData("CRUD/all-resource-types.json", "author-testscript")]
[InlineData("CRUD/all-resource-types-r4-only.json", "author-testscript")]
[InlineData("Search/basic.json", "distill-testscript")]
[InlineData("Subscriptions/basic.json", "distill-testscript")]
[InlineData("Microsoft/ms-convert-data.json", "distill-testscript")]
public async Task RepositoryManifest_RecordsExpectedActivity(
    string suitePath,
    string expectedActivity)
{
    using var document = JsonDocument.Parse(
        File.ReadAllText(FindRepoRootTool("provenance-manifest.json")));
    var entry = document.RootElement
        .GetProperty("suites")
        .GetProperty(suitePath);

    entry.GetProperty("activity").GetString().Should().Be(expectedActivity);
    entry.GetProperty("recorded").GetString().Should().Be("2026-07-07T00:00:00Z");
}
```

- [ ] **Step 2: Run the repository manifest tests and verify they fail**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~RepositoryManifest"
```

Expected: FAIL because `provenance-manifest.json` does not exist.

- [ ] **Step 3: Create the reusable profiles**

Move the current generator profile data into JSON profiles with lower-kebab-case
keys:

```text
bundles
fhir262-http
fhir262-search
fhir262-validation
ignixa-all-resource-types
microsoft
operations-expand
operations-terminology
subscriptions
```

Use `"version": "not recorded during original distillation"` for external
repository sources that lack verified historical pins. Preserve exact FHIR R4
spec versions and existing license/notes text. Do not substitute current
upstream HEAD revisions.

- [ ] **Step 4: Add the complete explicit suite map**

Use `recorded: "2026-07-07T00:00:00Z"` for the migrated assertions. Assign
`author-testscript` only to `ignixa-all-resource-types`; assign
`distill-testscript` to the other profiles.

The exact path assignments are:

| Profile | Suite paths |
| --- | --- |
| `bundles` | `Bundles/batch.json`, `Bundles/edge-cases.json`, `Bundles/transaction.json` |
| `ignixa-all-resource-types` | `CRUD/all-resource-types.json`, `CRUD/all-resource-types-post-stu3.json`, `CRUD/all-resource-types-pre-r5.json`, `CRUD/all-resource-types-r4-and-r5.json`, `CRUD/all-resource-types-r4-family.json`, `CRUD/all-resource-types-r4-only.json`, `CRUD/all-resource-types-r4b-plus.json`, `CRUD/all-resource-types-r5-only.json`, `CRUD/all-resource-types-stu3-only.json` |
| `fhir262-http` | `CRUD/client-id-handling.json` |
| `fhir262-search` | `Search/array-joins.json`, `Search/basic.json`, `Search/chaining.json`, `Search/intervals.json`, `Search/joins.json`, `Search/pagination.json`, `Search/sort.json`, `Search/string-modifiers.json` |
| `fhir262-validation` | `Validation/validate-op.json` |
| `operations-expand` | `Operations/expand-operation.json` |
| `operations-terminology` | `Operations/lookup-operation.json`, `Operations/subsumes-operation.json`, `Operations/translate-operation.json`, `Operations/validate-code-operation.json` |
| `subscriptions` | `Subscriptions/basic.json` |
| `microsoft` | Every exact path listed below |

The `microsoft` entries are:

```text
CRUD/basic.json
CRUD/conditional-create.json
CRUD/conditional-delete.json
CRUD/conditional-patch.json
CRUD/conditional-update.json
CRUD/create.json
CRUD/delete.json
CRUD/history.json
CRUD/patch-body.json
CRUD/patch-fhirpath.json
CRUD/patch-json.json
CRUD/read.json
CRUD/resource-type-case-sensitivity.json
CRUD/update.json
CRUD/vread.json
Foundation/cors.json
Foundation/health.json
Foundation/metadata.json
Microsoft/ms-bulk-delete.json
Microsoft/ms-bulk-update.json
Microsoft/ms-convert-data.json
Microsoft/ms-custom-headers.json
Microsoft/ms-export-anonymized.json
Microsoft/ms-hard-delete-purge-history.json
Microsoft/ms-import-basic.json
Microsoft/ms-import-history-soft-delete.json
Microsoft/ms-import-rebuild-indexes.json
Microsoft/ms-includes-operation.json
Microsoft/ms-not-expression.json
Microsoft/ms-not-referenced.json
Microsoft/ms-operation-versions.json
Microsoft/ms-reindex.json
Microsoft/ms-search-parameter-url-length.json
Microsoft/ms-search-parameter.json
Operations/docref-operation.json
Operations/everything-operation.json
Operations/export-data.json
Operations/import-search-2.json
Operations/import-search.json
Operations/member-match.json
Regression/capability-statement-version.json
Regression/exception-handling.json
Regression/observation-resolve-reference.json
Search/canonical.json
Search/chaining-and-sort.json
Search/compartments.json
Search/composite.json
Search/custom-search-param.json
Search/date.json
Search/escape-characters.json
Search/id.json
Search/includes.json
Search/number.json
Search/quantity.json
Search/reference.json
Search/token-overflow.json
Search/token.json
Search/uri.json
Validation/validate-modes.json
```

Do not use glob expressions or category defaults in the manifest.

- [ ] **Step 5: Run the repository completeness tests**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~RepositoryManifest"
```

Expected: PASS with 87 explicit entries and the expected activities.

- [ ] **Step 6: Regenerate all sidecars**

Run:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 -Force
```

Expected: `Generated 87 provenance sidecar(s); skipped 0 existing sidecar(s).`

Inspect at least:

```text
CRUD/all-resource-types.provenance.json
Search/basic.provenance.json
Subscriptions/basic.provenance.json
Microsoft/ms-convert-data.provenance.json
Operations/expand-operation.provenance.json
```

Verify the first uses `author-testscript`; the others use
`distill-testscript`; source entities remain unchanged except honest source
version wording.

- [ ] **Step 7: Run all provenance tests**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
```

Expected: all provenance tests pass.

- [ ] **Step 8: Commit**

```powershell
git add backend\src\Ignixa.Lab.Suites\tools\provenance-manifest.json backend\src\Ignixa.Lab.Suites\testscripts backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs
git commit -m "Classify bundled TestScript provenance explicitly" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 4: Detect stale sidecars and support strict audit mode

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/tools/TestScriptProvenance.psm1`
- Modify: `backend/src/Ignixa.Lab.Suites/tools/verify-provenance.ps1`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteProvenanceAuditTests.cs`

- [ ] **Step 1: Update audit helpers to pass manifest and strict mode**

Change the test helper signature:

```csharp
private async Task<AuditResult> RunAuditAsync(
    string manifestPath,
    bool strict = false)
```

Always pass `-ManifestPath`; append `-Strict` when requested. Update existing
audit tests to create a matching temporary manifest.

- [ ] **Step 2: Add failing strict and stale-output tests**

Add:

```csharp
[Fact]
public async Task VerifyProvenance_StrictModeFailsForBlockingErrors()
{
    WriteScript(Path.Combine("CRUD", "basic.json"));
    var manifest = WriteManifest(CreateManifest());

    var result = await RunAuditAsync(manifest, strict: true);

    result.ExitCode.Should().Be(1);
    result.CombinedOutput.Should().Contain("ERROR: Missing provenance sidecar");
}

[Fact]
public async Task VerifyProvenance_DefaultModeReportsErrorsButSucceeds()
{
    WriteScript(Path.Combine("CRUD", "basic.json"));
    var manifest = WriteManifest(CreateManifest());

    var result = await RunAuditAsync(manifest);

    result.ExitCode.Should().Be(0);
    result.CombinedOutput.Should().Contain("ERROR: Missing provenance sidecar");
}

[Fact]
public async Task VerifyProvenance_StrictModeDetectsStaleGeneratedSidecar()
{
    WriteScript(Path.Combine("CRUD", "basic.json"));
    var manifest = WriteManifest(CreateManifest());
    (await RunGeneratorAsync(_root, manifest)).ExitCode.Should().Be(0);
    var sidecar = Path.Combine(_root, "CRUD", "basic.provenance.json");
    var content = await File.ReadAllTextAsync(sidecar);
    await File.WriteAllTextAsync(
        sidecar,
        content.Replace("Example source", "Tampered source", StringComparison.Ordinal));

    var result = await RunAuditAsync(manifest, strict: true);

    result.ExitCode.Should().Be(1);
    result.CombinedOutput.Should().Contain(
        "ERROR: Provenance sidecar does not match manifest: CRUD/basic.provenance.json");
}

[Fact]
public async Task VerifyProvenance_StrictModeAllowsAdvisoryWarnings()
{
    WriteScript(Path.Combine("CRUD", "basic.json"));
    var manifest = WriteManifest(CreateManifest().Replace(
        "\"v1.0.0\"",
        "\"not recorded during original distillation\""));
    (await RunGeneratorAsync(_root, manifest)).ExitCode.Should().Be(0);

    var result = await RunAuditAsync(manifest, strict: true);

    result.ExitCode.Should().Be(0);
    result.CombinedOutput.Should().Contain("WARNING");
}
```

- [ ] **Step 3: Run strict audit tests and verify they fail**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~VerifyProvenance_"
```

Expected: FAIL because errors/warnings are not separated, sidecars are not
compared to manifest generation, and `-Strict` does not exist.

- [ ] **Step 4: Add deterministic serialization and expected-sidecar comparison**

Add and export:

```powershell
function ConvertTo-TestScriptProvenanceJson {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Provenance
    )

    $Provenance | ConvertTo-Json -Depth 100
}
```

Use it in both generator and audit. Compare normalized generated JSON with the
committed sidecar using trimmed trailing line endings. Report formatting/content
differences as blocking errors.

- [ ] **Step 5: Return separate errors and warnings from the audit**

Change `Invoke-TestScriptProvenanceAudit` to accept `ManifestPath`, validate the
manifest first, and return:

```powershell
[pscustomobject] @{
    ScriptCount = $scripts.Count
    ErrorCount = $errors.Count
    WarningCount = $warnings.Count
    Errors = @($errors)
    Warnings = @($warnings)
}
```

Treat missing/invalid/mismatched/stale sidecars and manifest validation failures
as errors. Add advisory warnings when a source version contains
`not recorded during original distillation` or a license is
`source-declared open-source license`/`repository license`.

- [ ] **Step 6: Add `-Strict` to the command wrapper**

Use:

```powershell
param(
    [string] $SuitesDirectory = (Join-Path $PSScriptRoot '..' 'testscripts'),
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'provenance-manifest.json'),
    [switch] $Strict
)
```

Print errors with `Write-Output "ERROR: $error"` so
`$ErrorActionPreference = 'Stop'` does not terminate before the complete report.
Print advisory warnings with `Write-Warning`. End with:

```powershell
Write-Output "Provenance audit scanned $($result.ScriptCount) TestScript $noun and found $($result.ErrorCount) error(s) and $($result.WarningCount) warning(s)."

if ($Strict -and $result.ErrorCount -gt 0) {
    exit 1
}

exit 0
```

- [ ] **Step 7: Run provenance tests and repository strict audit**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 -Strict
```

Expected: tests pass; repository audit exits 0 with 87 TestScripts, zero errors,
and advisory warnings only for honestly unpinned historical sources/licenses.

- [ ] **Step 8: Commit**

```powershell
git add backend\src\Ignixa.Lab.Suites\tools\TestScriptProvenance.psm1 backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs
git commit -m "Enforce explicit TestScript provenance integrity" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 5: Make packaging strict and document the manifest workflow

**Files:**
- Modify: `backend/pack-suites.ps1:25-30`
- Modify: `docs/development.md:60-77`
- Modify: `backend/README.md:225-230`
- Modify: `docs/features/testscript-suite-sourcing/readme.md:28-37`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteProvenanceAuditTests.cs`

- [ ] **Step 1: Add a failing packaging wiring test**

Add:

```csharp
[Fact]
public async Task PackSuites_InvokesProvenanceAuditInStrictMode()
{
    var packScript = Path.Combine(FindRepoRootDirectory(), "backend", "pack-suites.ps1");
    var content = await File.ReadAllTextAsync(packScript);

    content.Should().Contain("& $provenanceAudit");
    content.Should().Contain("-Strict");
    content.Should().Contain("if ($LASTEXITCODE -ne 0)");
}
```

- [ ] **Step 2: Run the packaging wiring test and verify it fails**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~PackSuites_InvokesProvenanceAuditInStrictMode"
```

Expected: FAIL because packaging invokes warning-only mode and does not propagate
the audit exit code.

- [ ] **Step 3: Invoke strict audit before packing**

Replace the audit call with:

```powershell
if (Test-Path -LiteralPath $provenanceAudit -PathType Leaf) {
    & $provenanceAudit `
        -SuitesDirectory (Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/testscripts') `
        -ManifestPath (Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/tools/provenance-manifest.json') `
        -Strict

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

- [ ] **Step 4: Update authoring documentation**

Document this exact workflow in all three docs:

1. Add or materially update the TestScript.
2. Add/update its explicit entry in `tools/provenance-manifest.json`.
3. Use `author-testscript` for locally created coverage and
   `distill-testscript` for transformed external tests.
4. Record an exact upstream commit/tag/file and SPDX license when known; use
   honest historical uncertainty rather than current upstream HEAD.
5. Run:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 -Force
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 -Strict
.\backend\pack-suites.ps1
```

Explain that sidecars are generated, committed, packaged metadata and are not
manually authoritative.

- [ ] **Step 5: Run focused tests and package suites**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteProvenanceAuditTests"
.\backend\pack-suites.ps1
```

Expected: tests pass; strict audit reports zero errors; package is created.

- [ ] **Step 6: Commit**

```powershell
git add backend\pack-suites.ps1 backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteProvenanceAuditTests.cs docs\development.md backend\README.md docs\features\testscript-suite-sourcing\readme.md
git commit -m "Require valid provenance when packing suites" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 6: Final verification and review

**Files:**
- Verify all files changed by Tasks 1-5.

- [ ] **Step 1: Prove regeneration is clean**

Run:

```powershell
git status --short
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 -Force
git diff --exit-code -- backend\src\Ignixa.Lab.Suites\testscripts
```

Expected: generator reports 87 sidecars and `git diff --exit-code` returns 0,
proving committed sidecars match the manifest.

- [ ] **Step 2: Run the strict audit and package**

Run:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 -Strict
.\backend\pack-suites.ps1
```

Expected: both exit 0; audit reports zero errors; package creation succeeds.

- [ ] **Step 3: Run the Release build and full test suite**

Run:

```powershell
dotnet build Ignixa.Lab.sln -c Release
dotnet test Ignixa.Lab.sln
```

Expected: build succeeds with zero errors and all tests pass.

- [ ] **Step 4: Inspect repository state**

Run:

```powershell
git diff --check
git status --short
git log --oneline -8
```

Expected: no whitespace errors; only intended changes, if any, remain.

- [ ] **Step 5: Request code review**

Dispatch `superpowers:code-reviewer` with:

- base SHA: the commit before Task 1;
- head SHA: current `HEAD`;
- requirements: the approved manifest-hardening design;
- verification evidence from Steps 1-3.

Fix every Critical or Important issue, rerun the affected focused tests, then
repeat Steps 1-4.

- [ ] **Step 6: Push the existing PR branch**

Run:

```powershell
git push origin brendankowitz-provenance-design
```

Expected: the open provenance PR is updated with the hardening commits.
