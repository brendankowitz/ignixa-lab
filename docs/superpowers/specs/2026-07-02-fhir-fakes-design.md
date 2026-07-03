# FHIR Fakes Bench Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a fourth "Fakes" bench to the Expression Benches frontend that generates synthetic FHIR data (patient populations, clinical scenarios, single resources with edge-case fuzzing) via a real backend wired to the published `Ignixa.FhirFakes` library, and let the FHIRPath/FML/SQL-on-FHIR benches send/receive generated data to and from it.

**Architecture:** New Azure Functions endpoints in `Ignixa.Lab.Functions` consume `Ignixa.FhirFakes` 0.5.13 directly (no changes needed to the ignixa-fhir library). A new `FakesBench.tsx` React component (real-backend tier, like FHIRPath — not mocked like FML/SQL-on-FHIR) calls those endpoints through the plain-JSON `request<T>()` client pattern already used by the Conformance Runner app. Cross-bench integration (the "⚡ Fakes ↗" / "Send to" flow) lifts a small amount of shared state into `BenchesApp.tsx`, which already owns which bench is active.

**Tech Stack:** C#/.NET (Azure Functions v4, isolated worker), `Ignixa.FhirFakes` 0.5.13, React 19 + TypeScript, Vite.

## Global Constraints

- Follow the existing `SuitesFunction`/`RunFunction` convention for new endpoints: `[Function("Name")]`, `HttpTrigger(AuthorizationLevel.Anonymous, ...)`, `JsonSerializer.DeserializeAsync` with `JsonSerializerDefaults.Web`, `BadRequestObjectResult`/`OkObjectResult`. Do NOT follow `FhirPathFunctions`'s FHIR-Parameters-wire-format convention — that exists only for fhirpath-lab.com backwards compatibility and doesn't apply here.
- Frontend API calls use the shared `request<T>()` helper in `frontend/src/api/client.ts` (add new functions there or a sibling `fakesApi.ts` following the same shape), not the FHIRPath bench's Parameters-wire-format client.
- Everything the UI presents as a choice (scenario names, resource types, edge-case categories) must come from a backend discovery endpoint reflecting the real `Ignixa.FhirFakes` library, not a hardcoded list. This is a firm requirement, not a suggestion — the mockup's illustrative content (14 scenario names, 8 US states) does not match the library's real content and must not be copied verbatim into code.
- Population and Scenario modes have no working `Seed` control — real `PopulationGenerator.Generate()` and the predefined scenario factory methods (e.g. `GetDiabeticPatient`) take no seed parameter and have no determinism test coverage. Do not add a Seed input to either panel. Only Resource mode gets a real, working Seed + Reseed control — determinism there is proven (`PatientBuilderDeterminismTests`) for the `PatientBuilder`/`SchemaBasedFhirResourceFaker` path specifically. (This corrects an earlier version of this spec, which incorrectly assumed Scenario mode was also seedable before the exact method signatures were verified.)
- Edge-case category descriptions (the blurb text under each category checkbox) come from a small static lookup table in the frontend keyed by category id, sourced from the original mockup's own wording where the id matches a real category; categories with no curated entry fall back to a humanized version of the category id. This table lives in the frontend only — no backend or ignixa-fhir changes.
- Follow `C:\Users\brend\.claude\CLAUDE.md`'s C# conventions for all new backend code (naming, one constructor, `ArgumentNullException.ThrowIfNull`, no service locator, sealed classes, etc.)

---

## 1. Backend

### 1.1 Package reference

Add to `Directory.Packages.props`: `Ignixa.FhirFakes` version `0.5.13` (same version line as the already-referenced `Ignixa.FhirPath`/`Ignixa.Serialization`/`Ignixa.Specification`). Add a `<PackageReference Include="Ignixa.FhirFakes" />` to `Ignixa.Lab.Functions.csproj`.

### 1.2 New files

- `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs` — HTTP endpoints (thin, mirrors `RunFunction`/`SuitesFunction` style).
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs` — orchestrates calls into `Ignixa.FhirFakes`, computes population summaries, builds response DTOs.
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs` — reflection-based scenario discovery (same technique as `Ignixa.FhirFakes.Cli`'s internal `ScenarioDiscovery`, reimplemented here rather than depending on the CLI package, which is a `dotnet tool` executable, not referenceable as a library): scans `Ignixa.FhirFakes.Scenarios.Predefined` for public static methods returning `ScenarioContext` whose first parameter is `IFhirSchemaProvider`, strips a leading `Get`. Scenario signatures are **not uniform** (e.g. `GetDiabeticPatient` has `age`/`gender`/`severity`; `GetChestPainVisit` has only `age`/`gender`; `GetWellnessVisit` adds a `bool includeLipidPanel`) — report each method's actual remaining parameters (name, type, default value) generically rather than assuming a fixed age/gender/severity shape.
- `backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs` — same reflection technique applied to `Ignixa.FhirFakes.Scenarios.States.ObservationState`'s own public static, all-default-parameter factory methods (e.g. `BloodGlucose()`, `BodyTemperature()`) — this type lives in the core library itself (not the CLI), so it's genuinely referenceable and reflectable.
- `backend/src/Ignixa.Lab.Functions/Models/Fakes/*.cs` — request/response DTOs (see 1.3).

### 1.3 Endpoints

All routes under `/api/fakes/...`, all `Anonymous` auth (matching existing endpoints), all POST bodies deserialized with `JsonSerializerDefaults.Web`.

**`GET /api/fakes/metadata`** — one discovery call the frontend makes once on mount. Returns:
```json
{
  "fhirVersions": ["stu3", "r4", "r4b", "r5", "r6"],
  "populationStates": ["Arizona", "California", "Illinois", "Massachusetts", "New York", "Pennsylvania", "Texas", "Washington"],
  "scenarios": [
    { "id": "DiabeticPatient", "parameters": [
      { "name": "age", "type": "int", "defaultValue": 52 },
      { "name": "gender", "type": "string", "defaultValue": null },
      { "name": "severity", "type": "int", "defaultValue": 2 }
    ] }
  ],
  "resourceTypes": ["Patient", "Observation", "Condition", "..."],
  "observationStates": ["BloodGlucose", "BodyTemperature", "HemoglobinA1c", "..."],
  "edgeCaseFamilies": [
    { "family": "Unicode", "categories": [{ "id": "unicode.cjk", "intent": "PreservesValidity" }, "..."] }
  ]
}
```
`populationStates` is `PopulationGenerator.AvailableStates` verbatim — note the real API takes a **state** name only (`Generate(string state, int populationSize)` resolves a city internally via `DemographicsDataProvider.SelectCity(state, ...)`), so despite the mockup's "state or city" source label, only states are valid input; drop the city-chip option from the Population UI. `scenarios[].parameters` reflects each scenario's actual non-uniform parameter list (see §1.2) — the frontend renders age/gender/severity with the mockup's specialized slider/pill controls when a parameter with that exact name is present, and a generic control (checkbox for bool, number input for int/decimal, text input for string) for anything else, so scenarios like `WellnessVisit`'s `includeLipidPanel` still get a working control. `resourceTypes` comes from `IFhirSchemaProvider.ResourceTypeNames` for the requested FHIR version. `observationStates` comes from `ObservationStateDiscovery` (§1.2). Only include edge-case families that have at least one registered strategy (skip `Cardinality`/`Structural`, currently empty — confirmed via `EdgeCaseFamily`'s doc comments).

**`POST /api/fakes/population`** — body `{ fhirVersion, source, count, resolvedReferences }` (no seed field — see Global Constraints). Calls `PopulationGenerator.Generate(source, count)`, then computes `byType`/`byGender`/`byCity`/`ageBuckets` from the returned `ScenarioContext`s in the service layer (the library doesn't compute this). Returns:
```json
{ "patients": [/* Patient resources */], "resources": [/* all resources */], "summary": { "byType": {}, "byGender": {}, "byCity": {}, "ageBuckets": {} } }
```

**`POST /api/fakes/scenario`** — body `{ fhirVersion, scenarioId, parameters: { [name]: value }, tag, resolvedReferences }` (no seed field — see Global Constraints; `parameters` is a free-form bag matched against the scenario's actual reflected parameter names, so callers only need to send the ones they want to override). Invokes the discovered scenario method via reflection (missing/unrecognized keys fall back to that parameter's own default). The returned `ScenarioContext` is **already built** — there is no exposed builder to retroactively configure, so: (1) tag is stamped by writing `meta.tag` onto every resource in `context.AllResources` directly in the service, matching the JS mockup's own approach; (2) resolved references are applied via `context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved)` before building the bundle, when `resolvedReferences` is true. Returns `{ "patient": {...}, "resources": [...], "bundle": {...} }`, where `bundle` is `.ToBatchBundle()` when `resolvedReferences` is true and `.ToBundle()` otherwise (the mockup's "transaction" bundle — `ToTransactionBundle()` is a literal alias for `ToBundle()` in the library, not a distinct method).

**`POST /api/fakes/resource`** — body `{ fhirVersion, resourceType, seed, density, firstName, familyName, city, observationState, edgeCaseSelectors, includeInvalid }`. For `Patient` at any density, use `PatientBuilderFactory.Create(schemaProvider, seed).WithGivenName(...).WithFamilyName(...).WithCity(...).Build()`; for `Observation` with an `observationState` set, use `ObservationStateDiscovery.Create(observationState)` to get the real `ObservationState`, then build the observation from it; otherwise (any other resource type, or density other than `Minimal`) use `new SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density }.Generate(resourceType)`. If `edgeCaseSelectors` is non-empty, resolve them against `EdgeCaseCatalog.CreateDefault().Resolve(selectors, out _)` and run `new EdgeCasePipeline(seed, schemaProvider).Apply(resource, strategies, includeInvalid)`, returning the mutated resource plus its manifest. Returns `{ "resource": {...}, "manifest": { "resourceId": "", "seed": 0, "mutations": [{"category": "", "path": "", "before": "", "after": "", "description": ""}] } | null }`.

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
- Population: state chips populated from `metadata.populationStates` (no city option — see §1.3), no Seed control (per constraint above), count slider, output-format pills (transaction/batch — `ToBundle()`/`ToBatchBundle()`; drop the mockup's "ndjson" option unless a straightforward newline-joined serialization of `resources` is added, which needs no library support so is a cheap addition, not a hard requirement), test-isolation tag field, cohort preview (type/gender/age/city breakdown from the backend's computed `summary`), sample-patient JSON, download.
- Scenario: scenario cards from `metadata.scenarios` (label = humanized `id`, no blurb/group — the library doesn't expose those), a control per reflected parameter — the mockup's specialized age slider/gender pills/severity slider render when a parameter named exactly `age`/`gender`/`severity` is present, everything else gets a generic control by type (see §1.3), source chips, resolved-references toggle, tree/bundle view, no Seed control (per constraint above — corrected from the mockup, which showed one), CLI-string preview (informational only — no real CLI invocation happens; the string is illustrative of what `ignixa-fakes scenario ...` would look like).
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
