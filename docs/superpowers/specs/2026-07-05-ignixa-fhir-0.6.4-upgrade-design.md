# Consume ignixa-fhir 0.6.4: unified upgrade branch

## Context

ignixa-fhir released `0.6.4` (https://github.com/brendankowitz/ignixa-fhir/releases/tag/release/0.6.4), bundling five PRs relevant to ignixa-lab:

- **#299** — `ScenarioCatalog`/`ObservationStateCatalog` promoted to a public, attribute-driven discovery API in `Ignixa.FhirFakes` (previously CLI-only reflection helpers). Adds `ClinicalDomain`/`Theme` for clinically-coherent `GenerationDensity.Maximum` output, and `Category`/`Domain` metadata per scenario.
- **#300** — UK Core Patient profile (NHS Number, ONS ethnic-category extension, `en_GB` name generation) + postal-code/country generation fixes (Melbourne/Sydney/Amsterdam previously produced invalid postal codes; Organization address always said `"USA"`).
- **#301** — Granular (major/minor/patch/wildcard) FHIR version matching in `TestScriptEvaluator.IsVersionCompatible`, backward-compatible with the old exact-string comparison.
- **#302** — Workflow Scenario Packs: `WorkflowScenarioCatalog` + first pack `DailyAppointmentSchedule`, producing a multi-resource, cross-patient FHIR searchset bundle.
- **#303** — `AsString()` extension for spec-conformant FHIRPath scalar stringification (lowercase `true`/`false`, invariant-culture decimals), used by both `TestScriptEvaluator` and `VariableExtractor`.

ignixa-lab currently pins these packages at `0.5.13`/`0.5.13-beta` (`Directory.Packages.props`) and, in the gap left before #299, built its own reflection-based `ScenarioDiscovery`/`ObservationStateDiscovery` classes (`backend/src/Ignixa.Lab.Functions/Services/Fakes/`). There is also an open draft PR, **ignixa-lab #12** (`claude/fix-r4-version-skip-issue`), which patches `TestScriptRunner` to normalize release-label FHIR versions (`"R4"`) to the numeric form (`"4.0"`) the engine's `fhirVersions` gating extension expects.

Verified against the actual `release/0.6.4` engine source: `TestScriptEvaluator.IsVersionCompatible` calls `SemVersion.TryParse(fhirVersion, ...)` on the caller-supplied version. `"R4"` does not parse as semver, so #301's granular matching never activates for a release-label input — it silently falls through to the old exact-string fallback, which still fails to match a suite's numeric `"4.0"`. **PR #12's fix is a prerequisite for #301 to have any effect, not something #301 makes redundant.** This branch reimplements that normalization fresh (informed by, not cherry-picked from, PR #12) as part of consuming #301.

## Goal

One branch, based on `main`, that:
1. Bumps ignixa-lab's `Ignixa.*` package references to `0.6.4`.
2. Fixes whatever the version bump breaks (API renames from #299).
3. Deletes the now-redundant reflection-based scenario/observation-state discovery in favor of the real public catalogs.
4. Fixes FHIR version gating in `TestScriptRunner` so release-label inputs (`"R4"`) actually match numeric suite declarations, riding on top of #301's granular engine matching.
5. Surfaces the new Theme and Workflow Scenario Pack capabilities in the Fakes bench.
6. Verifies (not necessarily code-changes for) UK Core Patient generation and FHIRPath `AsString()` output.

## Non-goals

- Layer-2 `ClinicalDomain` propagation into generated scenario resources beyond the informational metadata stamp — the upstream PR explicitly defers this.
- Additional workflow packs (`PractitionerPanel` etc.) — only `DailyAppointmentSchedule` ships in 0.6.4.
- Detecting a target server's real patch-level FHIR version from its own `/metadata` for gating — the #301 PR body names this as the eventual downstream use case, but it's a separate feature, not required to consume granular gating today.
- Moving TestScript suites out of this repo — per standing project decision, they stay (see ADR-2607 discussion), unaffected by this upgrade.

## Design

### 1. Package version bump

`Directory.Packages.props`: bump `Ignixa.FhirPath`, `Ignixa.Serialization`, `Ignixa.Specification`, `Ignixa.FhirFakes` to `0.6.4`. Check NuGet.org for the matching `Ignixa.TestScript`/`Ignixa.TestScript.FhirFakes` version at implementation time — these were on a `-beta` prerelease track distinct from the core packages' numbering; pin to whatever `0.6.4`-train version those packages actually published as.

Expect compile breaks from #299's pre-freeze renames:
- `ScenarioCatalog.All()`/`.Names()` → `.GetAll()`/`.GetNames()`
- `ObservationStateCatalog.Create()` → `.TryCreate()` (now returns a bool + out-param instead of throwing/returning null)

### 2. TestScript version gating (`TestScriptRunner.cs`)

**Superseded during implementation.** The initial approach (below, as originally shipped and reviewed) added a `NormalizeFhirVersionForEngine(string)` mapping release labels to numeric major.minor before the value was passed to the engine. Once in place, it was replaced with a more correct design: instead of guessing the target's version from a UI-selected release label, `TestScriptRunner.RunAsync` now resolves the engine's `fhirVersion` from the target's own declared `CapabilityStatement.fhirVersion` (already fetched for `requiresCapability` gating), falling back to the request's `FhirVersion`/`IgnixaLabOptions.DefaultFhirVersion` only when the CapabilityStatement can't be fetched or omits the field. This realizes exactly what ignixa-fhir PR #301's own "Why" section describes as the eventual downstream use case: detecting a target's real patch-level FHIR version and gating tests against it deterministically, rather than approximating it from a label. See `ResolveFhirVersion` in `TestScriptRunner.cs`.

<details>
<summary>Original approach (superseded)</summary>

Add back a `NormalizeFhirVersionForEngine(string)` mapping release labels to numeric major.minor before the value is passed to `TestScriptEvaluator.ExecuteAsync` and stored on `ConformanceReport.FhirVersion`:

```
"STU3" / "R3" -> "3.0"
"R4"          -> "4.0"
"R4B"         -> "4.3"
"R5"          -> "5.0"
"R6"          -> "6.0"
(anything else passes through unchanged)
```

Port the intent of PR #12's test coverage: a suite declaring a numeric `fhirVersions` token runs (not skipped) when the request supplies the matching release label, and normalization is reflected in the report's `FhirVersion` field, which must stay numeric to remain interchangeable with `ignixa-fhir`'s own `conformance/latest.json` artifact.

</details>

Once merged, comment on ignixa-lab PR #12 pointing at this branch; it becomes redundant as a standalone PR once its fix ships here.

### 3. FhirFakes discovery consolidation

Delete:
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs`
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs`

Update call sites (`FakesService.cs`, `FakesFunctions.cs`, DI registration) to use `Ignixa.FhirFakes.Scenarios.ScenarioCatalog` and `Ignixa.FhirFakes.Scenarios.States.ObservationStateCatalog` directly. `ScenarioCatalog.Invoke` now validates override parameter types itself with actionable messages — `FakesService.GenerateScenario`'s existing catch clause (`InvalidOperationException`/`TargetInvocationException`/`FormatException`/`OverflowException`/`ArgumentException`) should still cover the new failure shape, but verify during implementation.

Extend the metadata contract with the catalog's new fields:
- Backend: `ScenarioMetadata` (`Models/Fakes/FakesMetadataResponse.cs`) gains `Category` and `Domain` (nullable string).
- Frontend: `ScenarioMetadata` (`fakesTypes.ts`) gains matching fields.
- `scenarioDescriptions.ts`: replace the hand-curated `group` field with the library-reported `Category` (passed in from the metadata response), keeping the hand-written `blurb` text since that's editorial content the library doesn't provide. Fall back to today's humanized-id/generic-group behavior when a scenario has no `Category`.

### 4. Theme-aware generation

- Backend: add optional `Theme` (string, `ClinicalDomain` name) to `ResourceGenerationRequest` and `FakesService.GenerateResource`/`BuildResource`, passed through to `new SchemaBasedFhirResourceFaker(...) { Density = ..., Theme = ... }`. Validate against `Enum.GetNames<ClinicalDomain>()` the same way `Density` is validated today, returning 400 on an unknown value.
- `FakesMetadataResponse` gains a `ClinicalDomains` string list (reflected from the enum) for the frontend to populate the picker.
- Frontend: a Theme dropdown next to the existing Density control in the Resource-generation UI, enabled only when Density is `Maximum` (theme has no effect otherwise, matching the library's own behavior).

### 5. Workflow Scenario Packs

- Backend: new `WorkflowScenarioCatalog`-backed service method + `POST /api/fakes/workflow` function, mirroring `/fakes/scenario`'s request/response shape (fhirVersion, packId, parameters, tag → patient(s)/resources/bundle). `FakesMetadataResponse` gains a `WorkflowPacks` list (id + parameters, same shape as `ScenarioMetadata`).
- Frontend: new `fakesApi.ts` function + a "Workflow" mode alongside Population/Scenario/Resource in `FakesBench.tsx`, listing available packs (just `DailyAppointmentSchedule` today) with their parameters, reusing existing scenario-parameter-form UI patterns where possible.

### 6. UK Core Patient + postal-code fixes

No planned code changes — `KnownCities.All` (consumed by `FakesService.BuildResource` and `PopulationGenerator.AvailableCities`) and country-keyed profile selection are already wired dynamically, so London/UK Core and corrected postal codes should appear automatically post-bump. Verification step: generate a UK Patient (London) in the bench and confirm an NHS Number identifier and a UK-shaped alphanumeric postal code appear, not a fabricated US-style numeric one.

### 7. FHIRPath `AsString()` fix

No code change — internal to `Ignixa.FhirPath`/`Ignixa.TestScript`. Output shape changes: booleans stringify lowercase, decimals stringify invariant-culture. Per standing guidance that the FHIRPath backend must stay backwards-compatible with fhirpath-lab.com's contract, this is a bug fix (previous behavior was spec-non-conformant), not a contract change, but verify by running the FHIRPath bench manually post-bump and spot-checking a boolean- and decimal-returning expression.

## Testing

- Backend: `dotnet build` across the solution; `dotnet test` (existing suites plus new coverage for the version-normalization regression, the workflow endpoint, and theme validation).
- Frontend: `npm run build` / lint.
- Manual verification (via the `run`/`verify` workflow once implemented):
  - Run a version-gated TestScript suite against an R4 target — confirm it executes rather than skipping.
  - Generate a Maximum-density themed resource and confirm coherent output.
  - Run `DailyAppointmentSchedule` and confirm a valid searchset bundle.
  - Generate a UK Patient (London) and confirm NHS Number + correct postal code shape.
  - Evaluate a boolean- and decimal-returning FHIRPath expression in the FHIRPath bench and confirm spec-conformant output.
