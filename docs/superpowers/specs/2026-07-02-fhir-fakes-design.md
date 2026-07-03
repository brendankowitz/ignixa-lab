# FHIR Fakes Bench Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a fourth "Fakes" bench to the Expression Benches frontend that generates synthetic FHIR data (patient populations, clinical scenarios, single resources with edge-case fuzzing) via a real backend wired to the published `Ignixa.FhirFakes` library, and let the FHIRPath/FML/SQL-on-FHIR benches send/receive generated data to and from it.

**Architecture:** New Azure Functions endpoints in `Ignixa.Lab.Functions` consume `Ignixa.FhirFakes` 0.5.13 directly (no changes needed to the ignixa-fhir library). A new `FakesBench.tsx` React component (real-backend tier, like FHIRPath — not mocked like FML/SQL-on-FHIR) calls those endpoints through the plain-JSON `request<T>()` client pattern already used by the Conformance Runner app. Cross-bench integration (the "⚡ Fakes ↗" / "Send to" flow) lifts a small amount of shared state into `BenchesApp.tsx`, which already owns which bench is active.

**Tech Stack:** C#/.NET (Azure Functions v4, isolated worker), `Ignixa.FhirFakes` 0.5.13, React 19 + TypeScript, Vite.

## Global Constraints

- Follow the existing `SuitesFunction`/`RunFunction` convention for new endpoints: `[Function("Name")]`, `HttpTrigger(AuthorizationLevel.Anonymous, ...)`, `JsonSerializer.DeserializeAsync` with `JsonSerializerDefaults.Web`, `BadRequestObjectResult`/`OkObjectResult`. Do NOT follow `FhirPathFunctions`'s FHIR-Parameters-wire-format convention — that exists only for fhirpath-lab.com backwards compatibility and doesn't apply here.
- Frontend API calls use the shared `request<T>()` helper in `frontend/src/api/client.ts` (add new functions there or a sibling `fakesApi.ts` following the same shape), not the FHIRPath bench's Parameters-wire-format client.
- Everything the UI presents as a choice (scenario names, resource types, edge-case categories) must come from a backend discovery endpoint reflecting the real `Ignixa.FhirFakes` library, not a hardcoded list. This is a firm requirement, not a suggestion — the mockup's illustrative content (14 scenario names, 8 US states) does not match the library's real content and must not be copied verbatim into code.
- Population mode has no working `Seed` control — real `PopulationGenerator.Generate()` has no seed parameter today. Do not add a Seed input to the Population panel. Scenario and Resource modes do get a real, working Seed + Reseed control (their generation paths are proven deterministic).
- Edge-case category descriptions (the blurb text under each category checkbox) come from a small static lookup table in the frontend keyed by category id, sourced from the original mockup's own wording where the id matches a real category; categories with no curated entry fall back to a humanized version of the category id. This table lives in the frontend only — no backend or ignixa-fhir changes.
- Follow `C:\Users\brend\.claude\CLAUDE.md`'s C# conventions for all new backend code (naming, one constructor, `ArgumentNullException.ThrowIfNull`, no service locator, sealed classes, etc.)

---

## 1. Backend

### 1.1 Package reference

Add to `Directory.Packages.props`: `Ignixa.FhirFakes` version `0.5.13` (same version line as the already-referenced `Ignixa.FhirPath`/`Ignixa.Serialization`/`Ignixa.Specification`). Add a `<PackageReference Include="Ignixa.FhirFakes" />` to `Ignixa.Lab.Functions.csproj`.

### 1.2 New files

- `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs` — HTTP endpoints (thin, mirrors `RunFunction`/`SuitesFunction` style).
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs` — orchestrates calls into `Ignixa.FhirFakes`, computes population summaries, builds response DTOs.
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs` — reflection-based scenario discovery (same technique as `Ignixa.FhirFakes.Cli`'s internal `ScenarioDiscovery`, reimplemented here rather than depending on the CLI package): scans `Ignixa.FhirFakes.Scenarios.Predefined` for public static methods returning `ScenarioContext` whose first parameter is `IFhirSchemaProvider`, strips a leading `Get`, and reflects each method's remaining parameters (name, type, default value) so the API can report which of age/gender/severity/etc. a given scenario actually accepts.
- `backend/src/Ignixa.Lab.Functions/Models/Fakes/*.cs` — request/response DTOs (see 1.3).

### 1.3 Endpoints

All routes under `/api/fakes/...`, all `Anonymous` auth (matching existing endpoints), all POST bodies deserialized with `JsonSerializerDefaults.Web`.

**`GET /api/fakes/metadata`** — one discovery call the frontend makes once on mount. Returns:
```json
{
  "fhirVersions": ["stu3", "r4", "r4b", "r5", "r6"],
  "populationSources": { "states": ["Massachusetts", "..."], "cities": [{"name": "Boston", "state": "Massachusetts"}, "..."] },
  "scenarios": [
    { "id": "DiabeticPatient", "hasAge": true, "defaultAge": 52, "hasGender": true, "defaultGender": null, "hasSeverity": true, "defaultSeverity": 2 }
  ],
  "resourceTypes": ["Patient", "Observation", "Condition", "..."],
  "observationStates": ["BloodGlucose", "BloodPressureSystolic", "..."],
  "edgeCaseFamilies": [
    { "family": "Unicode", "categories": [{ "id": "unicode.cjk", "intent": "PreservesValidity" }, "..."] }
  ]
}
```
`resourceTypes` comes from the schema provider's known-type list for the requested FHIR version (fall back to a fixed common-resource-types list only if the schema provider has no enumeration API). `observationStates` is reflected with the same convention as scenario discovery (§1.2): scan for the same kind of by-convention static state factory the CLI's `StateDiscovery` uses, so it stays accurate as the library adds states, rather than a hand-copied list that drifts. Only include edge-case families that have at least one registered strategy (skip `Cardinality`/`Structural`, currently empty).

**`POST /api/fakes/population`** — body `{ fhirVersion, source, count, resolvedReferences }` (no seed field). Calls `PopulationGenerator.Generate(source, count)`, then computes `byType`/`byGender`/`byCity`/`ageBuckets` from the returned `ScenarioContext`s in the service layer (the library doesn't compute this). Returns:
```json
{ "patients": [/* Patient resources */], "resources": [/* all resources */], "summary": { "byType": {}, "byGender": {}, "byCity": {}, "ageBuckets": {} } }
```

**`POST /api/fakes/scenario`** — body `{ fhirVersion, scenarioId, age, gender, severity, tag, resolvedReferences, seed }` (only the fields the scenario's reflected signature actually accepts are used; others ignored). Invokes the discovered scenario method via reflection, applies `.WithTag()`/`.WithResolvedReferences()`/`.WithUrnUuidReferences()` via `ScenarioBuilder` as needed to reach the returned `ScenarioContext`. Returns `{ "patient": {...}, "resources": [...], "bundle": {...} }`, where `bundle` is `.ToBatchBundle()` when `resolvedReferences` is true and `.ToTransactionBundle()` otherwise (matching the mockup's transaction/batch distinction — `ToBundle()` is not used here since the response always needs one of the two reference styles).

**`POST /api/fakes/resource`** — body `{ fhirVersion, resourceType, seed, density, firstName, familyName, city, observationState, edgeCaseSelectors, includeInvalid }`. For `Patient`/`Observation <state>` at `Minimal` density, use the hand-built paths (`PatientBuilderFactory`, `StateDiscovery.CreateObservationState`); otherwise use `SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density }.Generate(resourceType)`. If `edgeCaseSelectors` is non-empty, run `EdgeCasePipeline(seed, schemaProvider).Apply(resource, selectors, includeInvalid)` and return the mutated resource plus its manifest. Returns `{ "resource": {...}, "manifest": { "seed": 0, "mutations": [{"category": "", "path": "", "before": "", "after": "", "description": ""}] } | null }`.

### 1.4 DI registration

In `Program.cs`, register the new service(s) as singletons alongside the existing FHIRPath services (`FakesService`, `ScenarioDiscovery` if stateful/cached).

---

## 2. Frontend

### 2.1 New files under `frontend/src/benches/fakes/`

- `FakesBench.tsx` — top-level component, mirrors `FhirPathBench.tsx`'s structure (mode-tab-driven panels instead of result-tab-driven).
- `fakesApi.ts` — thin wrapper calling `/api/fakes/*` via the shared `request<T>()` pattern from `frontend/src/api/client.ts`.
- `fakesTypes.ts` — TypeScript types mirroring the backend DTOs.
- `edgeCaseDescriptions.ts` — the static category-id → blurb lookup table described in Global Constraints.

### 2.2 UI structure (mirrors the design mockup)

Three mode tabs — **Population**, **Scenario**, **Resource** — matching the mockup's layout, panels, sliders, and pills, with these adaptations to match what's real:
- Population: source chips populated from `metadata.populationSources`, no Seed control (per constraint above), count slider, output-format pills (transaction/batch/ndjson — verify which formats `ToBundle()`/`.ToBatchBundle()`/`.ToTransactionBundle()`/raw actually support), test-isolation tag field, cohort preview (type/gender/age/city breakdown from the backend's computed `summary`), sample-patient JSON, download.
- Scenario: scenario cards from `metadata.scenarios` (label = humanized `id`, no blurb/group — the library doesn't expose those), age/gender/severity controls shown only when the selected scenario's reflected signature actually has that parameter (hide the severity slider entirely for a scenario with no `severity` param, etc.), source chips, resolved-references toggle, tree/bundle view, seed + reseed, CLI-string preview (informational only — no real CLI invocation happens; the string is illustrative of what `ignixa-fakes scenario ...` would look like).
- Resource: resource-type pills from `metadata.resourceTypes` (+ "More" picker for the full list), Observation clinical-state pills from `metadata.observationStates` when type is Observation, demographics overrides for Patient, density pills for Minimal/Maximum only (`Realistic` is identical to `Minimal` in the library today, so it's a distinction without a difference — omit it from the UI rather than showing two pills that behave the same), seed + reseed, resource/manifest view toggle, edge-case fuzzing catalog (families/categories from `metadata.edgeCaseFamilies`, blurbs from `edgeCaseDescriptions.ts`, `includeInvalid` toggle gating `MayViolate`/`AlwaysInvalid` categories).

### 2.3 Cross-bench integration

Lift into `BenchesApp.tsx` (which already owns `bench` state):
- `fakesReturnTo: BenchId | null` — set when a bench opens Fakes via its "⚡ Fakes ↗" button; shows the mockup's return banner ("Generating for the X bench...") on the Fakes bench, with "→ Use in X" / "Dismiss" actions.
- `fakesSentToast: { bench: BenchId; label: string } | null` — set right after a send; renders the mockup's global "✓ Received {label}" toast (shown on whichever bench the data landed on) and auto-clears after ~6s, matching the mockup's `setTimeout`.

Each of `FhirPathBench`/`FmlBench`/`SofBench` gets a "⚡ Fakes ↗" button next to its resource-input textarea (Test resource / Source resource / Resources), calling a callback prop that sets `bench: 'fakes'` and `fakesReturnTo` to that bench's id. The Fakes bench's "Send to" bar (FHIRPath / FML / SQL on FHIR buttons, shown in Scenario/Resource modes only — Population mode has no single-resource payload to send) calls a callback that writes the generated payload into the target bench's resource-input state, switches `bench` to it, and sets the toast.

---

## 3. Testing

- Backend: unit tests for `FakesService` (population summary computation, scenario-parameter reflection/filtering, resource density/edge-case wiring) and `ScenarioDiscovery` (reflects real scenarios from the referenced `Ignixa.FhirFakes` package — assert the discovered set is non-empty and every entry actually invokes without throwing). Integration-style tests hitting each `FakesFunctions` endpoint through the ASP.NET Core test host, matching the existing `CapabilityFunctionTests`/`TestScriptRunnerTests` style already in the test project.
- Frontend: no existing test runner is configured for the frontend (confirmed — `package.json` has no `test` script); rely on `npm run lint` + `npm run build` (tsc) as done for the existing benches, plus manual browser verification of all three modes and the send/receive flow across all three target benches, per this repo's established practice for the Expression Benches feature.

## 4. Out of scope

- Modifying `Ignixa.FhirFakes` itself (e.g., adding a seed parameter to `PopulationGenerator`, adding static description strings to edge-case strategies) — that's a separate ignixa-fhir change, not part of this PR.
- Wiring `Ignixa.FhirFakes`/`Ignixa.TestScript.FhirFakes` into the Conformance Runner app's TestScript fixture pipeline — that's the `Ignixa.TestScript.FhirFakes` package's purpose and a different feature entirely from this Expression Benches integration.
- A real `ignixa-fakes` CLI invocation from the browser — the "$ ignixa-fakes ..." string shown in the UI is cosmetic/illustrative only, matching the mockup.
