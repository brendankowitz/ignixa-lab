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
        [System.Collections.IDictionary] $Source
    )

    $extensions = @(
        [ordered] @{
            url = $script:RelationshipExtensionUrl
            valueCode = $Source.Relationship
        },
        [ordered] @{
            url = $script:LicenseExtensionUrl
            valueString = $Source.License
        },
        [ordered] @{
            url = $script:NotesExtensionUrl
            valueString = $Source.Notes
        }
    )

    if ($Source.Contains('Version') -and -not [string]::IsNullOrWhiteSpace([string] $Source.Version)) {
        $extensions += [ordered] @{
            url = $script:VersionExtensionUrl
            valueString = $Source.Version
        }
    }

    [ordered] @{
        role = 'source'
        what = [ordered] @{
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
        [System.Collections.IDictionary[]] $Sources,

        [string] $Recorded = '2026-07-07T00:00:00Z'
    )

    [ordered] @{
        resourceType = 'Provenance'
        target = @(
            [ordered] @{
                identifier = [ordered] @{
                    system = $script:PathSystem
                    value = $RelativePath
                }
                display = $RelativePath
            }
        )
        recorded = $Recorded
        activity = [ordered] @{
            coding = @(
                [ordered] @{
                    system = $script:ActivitySystem
                    code = 'distill-testscript'
                    display = 'Distill TestScript'
                }
            )
        }
        agent = @(
            [ordered] @{
                who = [ordered] @{
                    identifier = [ordered] @{
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

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    $property.Value
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

    if ((Get-JsonPropertyValue -InputObject $provenance -Name 'resourceType') -ne 'Provenance') {
        $warnings.Add("Invalid provenance resourceType in $relativeSidecarPath`: expected Provenance.")
    }

    $targetValueObject = Get-JsonPropertyValue -InputObject $provenance -Name 'target'
    $target = @($targetValueObject)
    if ($null -eq $targetValueObject -or $target.Count -lt 1) {
        $warnings.Add("Missing target in $relativeSidecarPath")
    }
    else {
        $identifier = Get-JsonPropertyValue -InputObject $target[0] -Name 'identifier'
        $targetValue = [string] (Get-JsonPropertyValue -InputObject $identifier -Name 'value')
        if ($targetValue -ne $relativePath) {
            $warnings.Add("Target mismatch in $relativeSidecarPath`: expected $relativePath, found $targetValue")
        }
    }

    $agentValueObject = Get-JsonPropertyValue -InputObject $provenance -Name 'agent'
    $agent = @($agentValueObject)
    if ($null -eq $agentValueObject -or $agent.Count -lt 1) {
        $warnings.Add("Missing agent in $relativeSidecarPath")
    }

    $entityValueObject = Get-JsonPropertyValue -InputObject $provenance -Name 'entity'
    $entity = @($entityValueObject)
    if ($null -eq $entityValueObject -or $entity.Count -lt 1) {
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
