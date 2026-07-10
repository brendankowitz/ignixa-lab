#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs the local IgnixaLab.TestScript.Suites content package (ADR-2607) into
    the repo-local NuGet feed, ready for restore.

.DESCRIPTION
    Interim step until ignixa-fhir publishes the canonical suites artifact:
    dotnet restore/build/test expect IgnixaLab.TestScript.Suites to already exist
    in artifacts/local-feed, so this must run before restore. Paths are
    resolved relative to the repo root so it works when invoked from anywhere.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj'
$outputDir = Join-Path $repoRoot 'artifacts/local-feed'
$repoPackageCache = Join-Path $repoRoot 'artifacts/nuget-packages/ignixalab.testscript.suites/0.1.0-local'

# nuget.config references this folder unconditionally as a package source;
# NuGet fails restore with NU1301 if a local source doesn't exist on disk yet,
# so it must exist before the pack command below triggers its own restore.
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# The fixed local development version otherwise allows restore to reuse stale
# suite content. Invalidate only this repository's configured package cache.
if (Test-Path -LiteralPath $repoPackageCache) {
    Remove-Item -Recurse -Force -LiteralPath $repoPackageCache
}
if (Test-Path -LiteralPath $repoPackageCache) {
    throw "Failed to invalidate repo-local suite package cache: $repoPackageCache"
}

$consumerAssets = @(
    (Join-Path $repoRoot 'backend/src/Ignixa.Lab.Functions/obj/project.assets.json'),
    (Join-Path $repoRoot 'backend/test/Ignixa.Lab.Functions.Tests/obj/project.assets.json')
)
foreach ($assetsFile in $consumerAssets) {
    if (Test-Path -LiteralPath $assetsFile) {
        Remove-Item -Force -LiteralPath $assetsFile
    }
}

dotnet pack $project -c Release -o $outputDir /nodeReuse:false

# MSBuild's node-reuse server can cache glob/directory-enumeration results
# across the dotnet CLI invocations that follow (restore/build/test in the
# same session), which has been observed to lowercase the packaged suites'
# category folder names (CRUD/Search/Validation) when the cached node is
# reused. Shut it down so the next command starts from a clean node.
dotnet build-server shutdown | Out-Null
