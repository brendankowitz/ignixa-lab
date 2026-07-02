#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs the local Ignixa.TestScript.Suites content package (ADR-2607) into
    the repo-local NuGet feed, ready for restore.

.DESCRIPTION
    Interim step until ignixa-fhir publishes the canonical suites artifact:
    dotnet restore/build/test expect Ignixa.TestScript.Suites to already exist
    in artifacts/local-feed, so this must run before restore. Paths are
    resolved relative to the repo root so it works when invoked from anywhere.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj'
$outputDir = Join-Path $repoRoot 'artifacts/local-feed'

dotnet pack $project -c Release -o $outputDir /nodeReuse:false

# MSBuild's node-reuse server can cache glob/directory-enumeration results
# across the dotnet CLI invocations that follow (restore/build/test in the
# same session), which has been observed to lowercase the packaged suites'
# category folder names (CRUD/Search/Validation) when the cached node is
# reused. Shut it down so the next command starts from a clean node.
dotnet build-server shutdown | Out-Null
