#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [string] $SuitesDirectory = (Join-Path $PSScriptRoot '..' 'testscripts'),
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'provenance-manifest.json'),
    [switch] $Strict
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TestScriptProvenance.psm1') -Force

$result = Invoke-TestScriptProvenanceAudit `
    -SuitesDirectory $SuitesDirectory `
    -ManifestPath $ManifestPath

foreach ($auditError in $result.Errors) {
    Write-Output "ERROR: $auditError"
}

foreach ($warning in $result.Warnings) {
    Write-Warning $warning
}

Write-Output "Provenance audit scanned $($result.ScriptCount) TestScript file(s) and found $($result.ErrorCount) error(s) and $($result.WarningCount) warning(s)."

if ($Strict -and $result.ErrorCount -gt 0) {
    exit 1
}

exit 0
