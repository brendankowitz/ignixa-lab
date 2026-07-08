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
    OperationsExpand = @(
        @{
            Reference = 'https://hl7.org/fhir/R4/terminology-service.html'
            Display = 'FHIR R4 terminology service operations'
            Relationship = 'spec-reference'
            License = 'FHIR specification license'
            Version = 'FHIR R4'
            Notes = 'Used the FHIR operation definitions as the normative source for ValueSet expansion behavior.'
        },
        @{
            Reference = 'https://github.com/hapifhir/hapi-fhir'
            Display = 'HAPI FHIR terminology test coverage'
            Relationship = 'inspired-by'
            License = 'Apache-2.0'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Used as comparative open-source coverage while distilling black-box TestScript terminology scenarios.'
        },
        @{
            Reference = 'https://github.com/microsoft/fhir-server'
            Display = 'Microsoft FHIR Server ExpandOperationTests coverage'
            Relationship = 'distilled-from'
            License = 'MIT'
            Version = 'repository source reviewed during Ignixa Lab suite distillation'
            Notes = 'Converted Microsoft FHIR Server ValueSet $expand e2e behavior into black-box FHIR TestScript assertions, while preserving FHIR terminology-service context.'
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

    if ($RelativePath -eq 'Operations/expand-operation.json') {
        return 'OperationsExpand'
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

    if ($RelativePath.StartsWith('CRUD/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Search/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Foundation/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Operations/', [StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('Regression/', [StringComparison]::Ordinal) -or
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
