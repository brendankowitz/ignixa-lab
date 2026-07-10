Set-StrictMode -Version Latest

$script:PathSystem = 'urn:ignixa-lab:testscripts:path'
$script:ActivitySystem = 'http://ignixa.io/fhir/provenance-activity'
$script:RelationshipExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-source-relationship'
$script:LicenseExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-source-license'
$script:VersionExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-source-version'
$script:NotesExtensionUrl = 'http://ignixa.io/fhir/StructureDefinition/provenance-distillation-notes'
$script:ProvenanceSidecarSuffix = '.provenance.json'
$script:AllowedActivities = @('author-testscript', 'distill-testscript')
$script:AllowedRelationships = @(
    'authored-in',
    'direct-port',
    'distilled-from',
    'inspired-by',
    'spec-reference'
)
$script:MissingSourceVersionWarning = 'not recorded during original distillation'
$script:DeclaredOpenSourceLicenseWarning = 'source-declared open-source license'
$script:RepositoryLicenseWarning = 'repository license'

function Get-TestScriptFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory,

        [string[]] $ExcludedPaths = @()
    )

    if (-not (Test-Path -LiteralPath $SuitesDirectory -PathType Container)) {
        return @()
    }

    $excludedFullPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($excludedPath in $ExcludedPaths) {
        if (-not [string]::IsNullOrWhiteSpace($excludedPath)) {
            [void] $excludedFullPaths.Add([System.IO.Path]::GetFullPath($excludedPath))
        }
    }

    Get-ChildItem -LiteralPath $SuitesDirectory -Recurse -File -Filter '*.json' |
        Where-Object {
            $_.Name -notlike '*.provenance.json' -and
            $_.Name -ne 'source-revision.txt' -and
            -not $excludedFullPaths.Contains([System.IO.Path]::GetFullPath($_.FullName))
        } |
        Sort-Object FullName
}

function Get-ProvenanceSidecarFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory,

        [string[]] $ExcludedPaths = @()
    )

    if (-not (Test-Path -LiteralPath $SuitesDirectory -PathType Container)) {
        return @()
    }

    $excludedFullPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($excludedPath in $ExcludedPaths) {
        if (-not [string]::IsNullOrWhiteSpace($excludedPath)) {
            [void] $excludedFullPaths.Add([System.IO.Path]::GetFullPath($excludedPath))
        }
    }

    Get-ChildItem -LiteralPath $SuitesDirectory -Recurse -File -Filter "*$($script:ProvenanceSidecarSuffix)" |
        Where-Object {
            -not $excludedFullPaths.Contains([System.IO.Path]::GetFullPath($_.FullName))
        } |
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
    Join-Path $directory "$baseName$($script:ProvenanceSidecarSuffix)"
}

function Get-TestScriptPathFromProvenanceSidecar {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProvenanceSidecarPath
    )

    $directory = Split-Path -Parent $ProvenanceSidecarPath
    $fileName = [System.IO.Path]::GetFileName($ProvenanceSidecarPath)

    if (-not $fileName.EndsWith($script:ProvenanceSidecarSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Provenance sidecar path must end with $($script:ProvenanceSidecarSuffix): $ProvenanceSidecarPath"
    }

    $baseName = $fileName.Substring(0, $fileName.Length - $script:ProvenanceSidecarSuffix.Length)
    Join-Path $directory "$baseName.json"
}

function New-ProvenanceEntity {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Source
    )

    $relationship = Get-DictionaryValue -InputObject $Source -Name 'Relationship'
    $license = Get-DictionaryValue -InputObject $Source -Name 'License'
    $notes = Get-DictionaryValue -InputObject $Source -Name 'Notes'
    $version = Get-DictionaryValue -InputObject $Source -Name 'Version'
    $reference = Get-DictionaryValue -InputObject $Source -Name 'Reference'
    $display = Get-DictionaryValue -InputObject $Source -Name 'Display'

    $extensions = @(
        [ordered] @{
            url = $script:RelationshipExtensionUrl
            valueCode = $relationship
        },
        [ordered] @{
            url = $script:LicenseExtensionUrl
            valueString = $license
        },
        [ordered] @{
            url = $script:NotesExtensionUrl
            valueString = $notes
        }
    )

    if ((Test-DictionaryContainsKey -InputObject $Source -Name 'Version') -and
        -not [string]::IsNullOrWhiteSpace([string] $version)) {
        $extensions += [ordered] @{
            url = $script:VersionExtensionUrl
            valueString = $version
        }
    }

    [ordered] @{
        role = 'source'
        what = [ordered] @{
            reference = $reference
            display = $display
        }
        extension = $extensions
    }
}

function New-TestScriptProvenance {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('author-testscript', 'distill-testscript', IgnoreCase = $false)]
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
                    code = $Activity
                    display = $activityDisplay
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

function ConvertTo-TestScriptProvenanceJson {
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [AllowNull()]
        [object] $Provenance
    )

    process {
        $Provenance | ConvertTo-Json -Depth 100
    }
}

function Remove-TestScriptProvenanceTrailingNewlines {
    param(
        [AllowNull()]
        [string] $Json
    )

    if ($null -eq $Json) {
        return $null
    }

    $Json.TrimEnd("`r", "`n")
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

function Test-IsDictionary {
    param(
        [AllowNull()]
        [object] $InputObject
    )

    $InputObject -is [System.Collections.IDictionary]
}

function Test-DictionaryContainsKey {
    param(
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if (-not (Test-IsDictionary -InputObject $InputObject)) {
        return $false
    }

    foreach ($key in $InputObject.Keys) {
        if ([string]::Equals([string] $key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    $false
}

function Get-DictionaryValue {
    param(
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if (-not (Test-DictionaryContainsKey -InputObject $InputObject -Name $Name)) {
        return $null
    }

    foreach ($key in $InputObject.Keys) {
        if ([string]::Equals([string] $key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $InputObject[$key]
        }
    }

    $null
}

function Get-DictionaryKey {
    param(
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if (-not (Test-IsDictionary -InputObject $InputObject)) {
        return $null
    }

    foreach ($key in $InputObject.Keys) {
        if ([string]::Equals([string] $key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [string] $key
        }
    }

    $null
}

function ConvertTo-ManifestItemArray {
    param(
        [AllowNull()]
        [object] $Value
    )

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [string]) {
        return @($Value)
    }

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [System.Collections.IDictionary]) {
        return @($Value)
    }

    @($Value)
}

function Test-IsSchemaVersionOne {
    param(
        [AllowNull()]
        [object] $Value
    )

    if ($null -eq $Value) {
        return $false
    }

    $integralTypes = @(
        [byte],
        [sbyte],
        [int16],
        [uint16],
        [int32],
        [uint32],
        [int64],
        [uint64]
    )

    foreach ($integralType in $integralTypes) {
        if ($Value -is $integralType) {
            return ([int64] $Value) -eq 1
        }
    }

    $false
}

function Test-IsRoundTripRecordedValue {
    param(
        [AllowNull()]
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    if ($Value -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$') {
        return $false
    }

    $parsed = [System.DateTimeOffset]::MinValue
    [System.DateTimeOffset]::TryParse(
        $Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref] $parsed)
}

function Assert-NoDuplicateManifestProperty {
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement] $Element,

        [string] $Path = '$'
    )

    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            $propertyPath = "$Path.$($property.Name)"
            if (-not $names.Add($property.Name)) {
                throw "Duplicate manifest property at $propertyPath"
            }

            Assert-NoDuplicateManifestProperty -Element $property.Value -Path $propertyPath
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NoDuplicateManifestProperty -Element $item -Path "$Path[$index]"
            $index++
        }
    }
}

function Read-TestScriptProvenanceManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Provenance manifest not found: $ManifestPath"
    }

    $json = Get-Content -LiteralPath $ManifestPath -Raw
    $document = [System.Text.Json.JsonDocument]::Parse($json)
    try {
        Assert-NoDuplicateManifestProperty -Element $document.RootElement
    }
    finally {
        $document.Dispose()
    }

    $json | ConvertFrom-Json -Depth 100 -AsHashtable -DateKind String
}

function Resolve-TestScriptProvenanceEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Manifest,

        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $suiteEntries = Get-DictionaryValue -InputObject $Manifest -Name 'suites'
    if (-not (Test-IsDictionary -InputObject $suiteEntries)) {
        throw 'Provenance manifest suites must be an object.'
    }

    if (-not $suiteEntries.Contains($RelativePath)) {
        throw "Missing manifest entry for TestScript: $RelativePath"
    }

    $entry = $suiteEntries[$RelativePath]
    if (-not (Test-IsDictionary -InputObject $entry)) {
        throw "Manifest suite entry must be an object: $RelativePath"
    }

    $hasProfile = (Test-DictionaryContainsKey -InputObject $entry -Name 'profile') -and
        -not [string]::IsNullOrWhiteSpace([string] (Get-DictionaryValue -InputObject $entry -Name 'profile'))
    $hasInlineSources = (Test-DictionaryContainsKey -InputObject $entry -Name 'sources') -and
        $null -ne (Get-DictionaryValue -InputObject $entry -Name 'sources')

    if ($hasProfile -eq $hasInlineSources) {
        throw "Manifest entry must contain exactly one of profile or sources: $RelativePath"
    }

    if ($hasProfile) {
        $profiles = Get-DictionaryValue -InputObject $Manifest -Name 'profiles'
        if (-not (Test-IsDictionary -InputObject $profiles)) {
            throw 'Provenance manifest profiles must be an object.'
        }

        $profileName = [string] (Get-DictionaryValue -InputObject $entry -Name 'profile')
        if (-not $profiles.Contains($profileName)) {
            throw "Unknown provenance profile for $RelativePath`: $profileName"
        }

        $profile = $profiles[$profileName]
        if (-not (Test-IsDictionary -InputObject $profile)) {
            throw "Manifest profile entry must be an object: $profileName"
        }

        $profileSourcesKey = Get-DictionaryKey -InputObject $profile -Name 'sources'
        $profileSourcesValue = $null
        if ($null -ne $profileSourcesKey) {
            $profileSourcesValue = $profile[$profileSourcesKey]
        }

        $profileSourcesIsArray = $null -ne $profileSourcesValue -and
            $profileSourcesValue -isnot [string] -and
            $profileSourcesValue -isnot [System.Collections.IDictionary] -and
            (
                $profileSourcesValue -is [System.Array] -or
                $profileSourcesValue -is [System.Collections.IEnumerable]
            )

        if (-not $profileSourcesIsArray) {
            throw "Manifest profile $profileName sources must be an array."
        }

        $sources = ConvertTo-ManifestItemArray -Value $profileSourcesValue
    }
    else {
        $entrySourcesKey = Get-DictionaryKey -InputObject $entry -Name 'sources'
        $entrySourcesValue = $null
        if ($null -ne $entrySourcesKey) {
            $entrySourcesValue = $entry[$entrySourcesKey]
        }

        $entrySourcesIsArray = $null -ne $entrySourcesValue -and
            $entrySourcesValue -isnot [string] -and
            $entrySourcesValue -isnot [System.Collections.IDictionary] -and
            (
                $entrySourcesValue -is [System.Array] -or
                $entrySourcesValue -is [System.Collections.IEnumerable]
            )

        if (-not $entrySourcesIsArray) {
            throw "Manifest entry $RelativePath sources must be an array."
        }

        $sources = ConvertTo-ManifestItemArray -Value $entrySourcesValue
    }

    [pscustomobject] @{
        Activity = [string] (Get-DictionaryValue -InputObject $entry -Name 'activity')
        Recorded = [string] (Get-DictionaryValue -InputObject $entry -Name 'recorded')
        Sources = @($sources)
    }
}

function Test-TestScriptProvenanceManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Manifest,

        [string[]] $ExcludedPaths = @()
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()
    $resolvedSuites = [ordered] @{}

    if (-not (Test-DictionaryContainsKey -InputObject $Manifest -Name 'schemaVersion') -or
        -not (Test-IsSchemaVersionOne -Value (Get-DictionaryValue -InputObject $Manifest -Name 'schemaVersion'))) {
        $errors.Add('Unsupported provenance manifest schemaVersion: expected 1.')
    }

    if (-not (Test-DictionaryContainsKey -InputObject $Manifest -Name 'profiles')) {
        $errors.Add('Provenance manifest must contain a profiles object.')
        $profiles = [ordered] @{}
    }
    else {
        $profiles = Get-DictionaryValue -InputObject $Manifest -Name 'profiles'
        if (-not (Test-IsDictionary -InputObject $profiles)) {
            $errors.Add('Provenance manifest profiles must be an object.')
            $profiles = [ordered] @{}
        }
    }

    if (-not (Test-DictionaryContainsKey -InputObject $Manifest -Name 'suites')) {
        $errors.Add('Provenance manifest must contain a suites object.')
        $suiteEntries = [ordered] @{}
    }
    else {
        $suiteEntries = Get-DictionaryValue -InputObject $Manifest -Name 'suites'
        if (-not (Test-IsDictionary -InputObject $suiteEntries)) {
            $errors.Add('Provenance manifest suites must be an object.')
            $suiteEntries = [ordered] @{}
        }
    }

    $scriptPaths = @(
        Get-TestScriptFile -SuitesDirectory $SuitesDirectory -ExcludedPaths $ExcludedPaths |
            ForEach-Object {
                ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $_.FullName
            } |
            Sort-Object
    )

    foreach ($scriptPath in $scriptPaths) {
        if (-not $suiteEntries.Contains($scriptPath)) {
            $errors.Add("Missing manifest entry for TestScript: $scriptPath")
        }
    }

    foreach ($entryPath in @($suiteEntries.Keys | Sort-Object)) {
        if ($entryPath -like '*\*') {
            $errors.Add("Manifest suite path must use '/': $entryPath")
        }

        if ($entryPath -notin $scriptPaths) {
            $errors.Add("Manifest entry has no TestScript: $entryPath")
        }

        $entry = $suiteEntries[$entryPath]
        if (-not (Test-IsDictionary -InputObject $entry)) {
            $errors.Add("Manifest suite entry must be an object: $entryPath")
            continue
        }

        $entryErrorCount = $errors.Count

        $activity = [string] (Get-DictionaryValue -InputObject $entry -Name 'activity')
        if ([string]::IsNullOrWhiteSpace($activity) -or $script:AllowedActivities -cnotcontains $activity) {
            $errors.Add("Unsupported activity for $entryPath`: $activity")
        }

        $recorded = [string] (Get-DictionaryValue -InputObject $entry -Name 'recorded')
        if (-not (Test-IsRoundTripRecordedValue -Value $recorded)) {
            $errors.Add("Invalid recorded date for $entryPath`: $recorded")
        }

        $hasProfile = (Test-DictionaryContainsKey -InputObject $entry -Name 'profile') -and
            -not [string]::IsNullOrWhiteSpace([string] (Get-DictionaryValue -InputObject $entry -Name 'profile'))
        $hasInlineSources = (Test-DictionaryContainsKey -InputObject $entry -Name 'sources') -and
            $null -ne (Get-DictionaryValue -InputObject $entry -Name 'sources')

        if ($hasProfile -eq $hasInlineSources) {
            $errors.Add("Manifest entry must contain exactly one of profile or sources: $entryPath")
            continue
        }

        if ($hasProfile) {
            $profileName = [string] (Get-DictionaryValue -InputObject $entry -Name 'profile')
            if (-not $profiles.Contains($profileName)) {
                $errors.Add("Unknown provenance profile for $entryPath`: $profileName")
                continue
            }

            $profile = $profiles[$profileName]
            if (-not (Test-IsDictionary -InputObject $profile)) {
                $errors.Add("Manifest profile entry must be an object: $profileName")
                continue
            }

            $profileSourcesKey = Get-DictionaryKey -InputObject $profile -Name 'sources'
            $profileSourcesValue = $null
            if ($null -ne $profileSourcesKey) {
                $profileSourcesValue = $profile[$profileSourcesKey]
            }
            $profileSourcesIsArray = $null -ne $profileSourcesValue -and
                $profileSourcesValue -isnot [string] -and
                $profileSourcesValue -isnot [System.Collections.IDictionary] -and
                (
                    $profileSourcesValue -is [System.Array] -or
                    $profileSourcesValue -is [System.Collections.IEnumerable]
                )

            if (-not $profileSourcesIsArray) {
                $errors.Add("Manifest profile $profileName sources must be an array.")
                continue
            }

            $sources = ConvertTo-ManifestItemArray -Value $profileSourcesValue
        }
        else {
            $entrySourcesKey = Get-DictionaryKey -InputObject $entry -Name 'sources'
            $entrySourcesValue = $null
            if ($null -ne $entrySourcesKey) {
                $entrySourcesValue = $entry[$entrySourcesKey]
            }
            $entrySourcesIsArray = $null -ne $entrySourcesValue -and
                $entrySourcesValue -isnot [string] -and
                $entrySourcesValue -isnot [System.Collections.IDictionary] -and
                (
                    $entrySourcesValue -is [System.Array] -or
                    $entrySourcesValue -is [System.Collections.IEnumerable]
                )

            if (-not $entrySourcesIsArray) {
                $errors.Add("Manifest entry $entryPath sources must be an array.")
                continue
            }

            $sources = ConvertTo-ManifestItemArray -Value $entrySourcesValue
        }

        if ($sources.Count -eq 0) {
            $errors.Add("No provenance sources configured for $entryPath")
            continue
        }

        foreach ($source in $sources) {
            if (-not (Test-IsDictionary -InputObject $source)) {
                $errors.Add("Manifest source must be an object for $entryPath")
                continue
            }

            $sourceErrorCount = $errors.Count

            foreach ($requiredField in @('reference', 'display', 'relationship', 'license', 'notes')) {
                if (-not (Test-DictionaryContainsKey -InputObject $source -Name $requiredField) -or
                    [string]::IsNullOrWhiteSpace([string] (Get-DictionaryValue -InputObject $source -Name $requiredField))) {
                    $errors.Add("Missing source $requiredField for manifest entry: $entryPath")
                }
            }

            $relationship = [string] (Get-DictionaryValue -InputObject $source -Name 'relationship')
            if (-not [string]::IsNullOrWhiteSpace($relationship) -and
                $script:AllowedRelationships -cnotcontains $relationship) {
                $errors.Add("Unsupported source relationship for $entryPath`: $relationship")
            }

            if ($errors.Count -ne $sourceErrorCount) {
                continue
            }

            $display = [string] (Get-DictionaryValue -InputObject $source -Name 'display')
            $version = [string] (Get-DictionaryValue -InputObject $source -Name 'version')
            $license = [string] (Get-DictionaryValue -InputObject $source -Name 'license')

            if ([string]::IsNullOrWhiteSpace($version)) {
                $warnings.Add("Source version missing for manifest entry $entryPath`: $display")
            }
            elseif ([string]::Equals($version, $script:MissingSourceVersionWarning, [System.StringComparison]::Ordinal)) {
                $warnings.Add("Source version not recorded during original distillation for manifest entry $entryPath`: $display")
            }

            if ([string]::Equals($license, $script:DeclaredOpenSourceLicenseWarning, [System.StringComparison]::Ordinal) -or
                [string]::Equals($license, $script:RepositoryLicenseWarning, [System.StringComparison]::Ordinal)) {
                $warnings.Add("Source license advisory for manifest entry $entryPath`: $display ($license)")
            }
        }

        if ($errors.Count -eq $entryErrorCount) {
            $resolvedSuites[$entryPath] = [pscustomobject] @{
                Activity = $activity
                Recorded = $recorded
                Sources = @($sources)
            }
        }
    }

    [pscustomobject] @{
        Errors = @($errors | Sort-Object -Unique)
        Warnings = @($warnings | Sort-Object -Unique)
        ResolvedSuites = $resolvedSuites
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
    $errors = New-Object System.Collections.Generic.List[string]
    $rawJson = $null
    $provenance = $null

    if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
        $errors.Add("Missing provenance sidecar: $relativeSidecarPath")
        return [pscustomobject] @{
            RelativePath = $relativePath
            RelativeSidecarPath = $relativeSidecarPath
            Errors = @($errors)
            Provenance = $null
            RawJson = $null
            CanCompare = $false
        }
    }

    try {
        $rawJson = Get-Content -LiteralPath $sidecarPath -Raw
        $provenance = $rawJson | ConvertFrom-Json -Depth 100
    }
    catch {
        $errors.Add("Invalid JSON in $relativeSidecarPath`: $($_.Exception.Message)")
        return [pscustomobject] @{
            RelativePath = $relativePath
            RelativeSidecarPath = $relativeSidecarPath
            Errors = @($errors)
            Provenance = $null
            RawJson = $rawJson
            CanCompare = $false
        }
    }

    if ((Get-JsonPropertyValue -InputObject $provenance -Name 'resourceType') -ne 'Provenance') {
        $errors.Add("Invalid provenance resourceType in $relativeSidecarPath`: expected Provenance.")
    }

    $targetValueObject = Get-JsonPropertyValue -InputObject $provenance -Name 'target'
    $target = @($targetValueObject)
    if ($null -eq $targetValueObject -or $target.Count -lt 1) {
        $errors.Add("Missing target in $relativeSidecarPath")
    }
    else {
        $identifier = Get-JsonPropertyValue -InputObject $target[0] -Name 'identifier'
        $targetValue = [string] (Get-JsonPropertyValue -InputObject $identifier -Name 'value')
        if ($targetValue -ne $relativePath) {
            $errors.Add("Target mismatch in $relativeSidecarPath`: expected $relativePath, found $targetValue")
        }
    }

    $agentValueObject = Get-JsonPropertyValue -InputObject $provenance -Name 'agent'
    $agent = @($agentValueObject)
    if ($null -eq $agentValueObject -or $agent.Count -lt 1) {
        $errors.Add("Missing agent in $relativeSidecarPath")
    }

    $entityValueObject = Get-JsonPropertyValue -InputObject $provenance -Name 'entity'
    $entity = @($entityValueObject)
    if ($null -eq $entityValueObject -or $entity.Count -lt 1) {
        $errors.Add("Missing entity in $relativeSidecarPath")
    }

    [pscustomobject] @{
        RelativePath = $relativePath
        RelativeSidecarPath = $relativeSidecarPath
        Errors = @($errors)
        Provenance = $provenance
        RawJson = $rawJson
        CanCompare = $errors.Count -eq 0
    }
}

function Invoke-TestScriptProvenanceAudit {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SuitesDirectory,

        [Parameter(Mandatory = $true)]
        [string] $ManifestPath
    )

    $errors = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    $resolvedManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
    $scripts = @(Get-TestScriptFile -SuitesDirectory $SuitesDirectory -ExcludedPaths @($resolvedManifestPath))
    $sidecars = @(Get-ProvenanceSidecarFile -SuitesDirectory $SuitesDirectory -ExcludedPaths @($resolvedManifestPath))
    $validation = $null

    foreach ($sidecarFile in $sidecars) {
        $expectedScriptPath = Get-TestScriptPathFromProvenanceSidecar -ProvenanceSidecarPath $sidecarFile.FullName
        if (-not (Test-Path -LiteralPath $expectedScriptPath -PathType Leaf)) {
            $relativeSidecarPath = ConvertTo-SuiteRelativePath -SuitesDirectory $SuitesDirectory -Path $sidecarFile.FullName
            $errors.Add("Orphaned provenance sidecar with no TestScript: $relativeSidecarPath")
        }
    }

    try {
        $manifest = Read-TestScriptProvenanceManifest -ManifestPath $resolvedManifestPath
        $validation = Test-TestScriptProvenanceManifest `
            -SuitesDirectory $SuitesDirectory `
            -Manifest $manifest `
            -ExcludedPaths @($resolvedManifestPath)
        foreach ($validationError in $validation.Errors) {
            $errors.Add($validationError)
        }
        foreach ($warning in $validation.Warnings) {
            $warnings.Add($warning)
        }
    }
    catch {
        $errors.Add($_.Exception.Message)
        $sortedErrors = @($errors | Sort-Object -Unique)
        $sortedWarnings = @($warnings | Sort-Object -Unique)

        return [pscustomobject] @{
            ScriptCount = $scripts.Count
            ErrorCount = $sortedErrors.Count
            WarningCount = $sortedWarnings.Count
            Errors = $sortedErrors
            Warnings = $sortedWarnings
        }
    }

    foreach ($scriptFile in $scripts) {
        $sidecarResult = Test-ProvenanceSidecar -SuitesDirectory $SuitesDirectory -TestScript $scriptFile

        foreach ($sidecarError in $sidecarResult.Errors) {
            $errors.Add($sidecarError)
        }

        if ($null -eq $validation -or -not $sidecarResult.CanCompare) {
            continue
        }

        $entry = $validation.ResolvedSuites[$sidecarResult.RelativePath]
        if ($null -eq $entry) {
            continue
        }

        $expectedProvenance = New-TestScriptProvenance `
            -RelativePath $sidecarResult.RelativePath `
            -Activity $entry.Activity `
            -Recorded $entry.Recorded `
            -Sources $entry.Sources
        $expectedJson = ConvertTo-TestScriptProvenanceJson -Provenance $expectedProvenance

        if ((Remove-TestScriptProvenanceTrailingNewlines -Json $sidecarResult.RawJson) -ne
            (Remove-TestScriptProvenanceTrailingNewlines -Json $expectedJson)) {
            $errors.Add("Provenance sidecar does not match manifest: $($sidecarResult.RelativeSidecarPath)")
        }
    }

    $sortedErrors = @($errors | Sort-Object -Unique)
    $sortedWarnings = @($warnings | Sort-Object -Unique)

    [pscustomobject] @{
        ScriptCount = $scripts.Count
        ErrorCount = $sortedErrors.Count
        WarningCount = $sortedWarnings.Count
        Errors = $sortedErrors
        Warnings = $sortedWarnings
    }
}

Export-ModuleMember -Function @(
    'Get-TestScriptFile',
    'ConvertTo-SuiteRelativePath',
    'Get-ProvenanceSidecarPath',
    'Read-TestScriptProvenanceManifest',
    'Resolve-TestScriptProvenanceEntry',
    'Test-TestScriptProvenanceManifest',
    'New-TestScriptProvenance',
    'ConvertTo-TestScriptProvenanceJson',
    'Invoke-TestScriptProvenanceAudit'
)
