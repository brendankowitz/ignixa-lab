#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [string] $SuitesDirectory = (Join-Path $PSScriptRoot '..' 'testscripts'),
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'provenance-manifest.json'),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TestScriptProvenance.psm1') -Force

$manifest = Read-TestScriptProvenanceManifest -ManifestPath $ManifestPath
$validation = Test-TestScriptProvenanceManifest -SuitesDirectory $SuitesDirectory -Manifest $manifest

if ($validation.Errors.Count -gt 0) {
    throw ($validation.Errors -join [System.Environment]::NewLine)
}

$scripts = @(
    Get-TestScriptFile -SuitesDirectory $SuitesDirectory |
        Sort-Object {
            ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $_.FullName
        }
)

$created = 0
$skipped = 0

foreach ($scriptFile in $scripts) {
    $relativePath = ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $scriptFile.FullName
    $sidecarPath = Get-ProvenanceSidecarPath -TestScriptPath $scriptFile.FullName

    if ((Test-Path -LiteralPath $sidecarPath -PathType Leaf) -and -not $Force) {
        $skipped++
        continue
    }

    $entry = $validation.ResolvedSuites[$relativePath]
    if ($null -eq $entry) {
        $entry = Resolve-TestScriptProvenanceEntry -Manifest $manifest -RelativePath $relativePath
    }

    $provenance = New-TestScriptProvenance `
        -RelativePath $relativePath `
        -Activity $entry.Activity `
        -Recorded $entry.Recorded `
        -Sources $entry.Sources

    $json = $provenance | ConvertTo-Json -Depth 100
    Set-Content -LiteralPath $sidecarPath -Value $json -Encoding utf8
    $created++
}

Write-Output "Generated $created provenance sidecar(s); skipped $skipped existing sidecar(s)."
