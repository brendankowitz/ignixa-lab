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
