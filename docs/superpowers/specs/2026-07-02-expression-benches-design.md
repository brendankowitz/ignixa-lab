# Ignixa Expression Benches — design

**Status**: Approved
**Created**: 2026-07-02

## Problem statement

`ignixa-lab`'s frontend currently ships one tool: the TestScript conformance
runner (`frontend/src/App.tsx`). The FHIRPath evaluator backend was just
ported into the same Function App (`FhirPathFunctions.cs`, see
`docs/features/fhirpath-evaluator/readme.md`) but has no UI in this repo yet.

A Claude Design mockup (`Ignixa Expression Benches.dc.html`, project
`8903721c-aa03-4d16-8e8d-6ccad7d9b096`) specifies a new "Expression Benches"
tool with three tabs — **FHIRPath**, **FML** (FHIR Mapping Language), and
**SQL on FHIR** — each a self-contained editor/run/results workbench. This
spec covers building that tool as a second page in the existing frontend,
with the FHIRPath bench wired to the real backend and the other two shipped
as interactive client-side mocks (their backends don't exist yet).

**Hard constraint**: `ignixa-lab`'s FHIRPath endpoints are meant to serve
fhirpath-lab.com as a first-class "Ignixa" engine option (see
`docs/features/fhirpath-evaluator/readme.md`), not just this repo's own UI.
This work is frontend-only and must not touch `FhirPathFunctions.cs` /
`ResultFormatter.cs` / `JsonAstVisitor.cs` in any way that changes the shape
or meaning of existing fields or routes — the Expression Benches frontend
only *reads* the contract documented in `server-api.md`. Additive,
backwards-compatible backend extensions remain fine in general, but none are
needed or in scope for this spec.

## Sources of truth

- **Visual/interaction design**: `Ignixa Expression Benches.dc.html`
  (Claude Design project `8903721c-aa03-4d16-8e8d-6ccad7d9b096`) — layout,
  tabs, chip styling, theme tokens (light defaults inline, dark overrides in
  the mock's `renderVals()`), and the FML/SQL-on-FHIR mock engines to port
  verbatim.
- **FHIRPath wire format**: `E:\data\src\fhirpath-lab\server-api.md` — the
  canonical Parameters-in/Parameters-out contract that `FhirPathFunctions.cs`
  implements (routes are byte-for-shape compatible with fhirpath-lab.com per
  `docs/features/fhirpath-evaluator/readme.md`).
- **Real-world parsing/rendering reference**: `E:\data\src\fhirpath-lab\pages\FhirPath\index.vue`
  and `E:\data\src\fhirpath-lab\components\ParseTreeTab.vue` — the production
  Vue implementation of this exact contract. Consult these for request
  building and response parsing logic (Results/Trace/Parse-tree tabs) rather
  than re-deriving it; port the logic to React/TS, not the Vue code itself.
- **Backend contract (code)**: `backend/src/Ignixa.Lab.Functions/Functions/FhirPathFunctions.cs`,
  `Services/FhirPath/ResultFormatter.cs`, `Serialization/JsonAstVisitor.cs`.

## Architecture

### Multi-page frontend, no router

The existing app has no router (`App.tsx` uses component-local tab state) by
deliberate design ("no need for deep-linkable screens"). Expression Benches
is a conceptually separate tool from the conformance runner — the mockup
itself models this as two separate `.dc.html` files linked by a plain
`<a href>`. We mirror that:

- New Vite entry `frontend/benches.html` + `frontend/src/benches-main.tsx`,
  mounting a new `BenchesApp` root under `frontend/src/benches/`.
- `frontend/vite.config.ts` gets a second `build.rollupOptions.input` entry
  (`main` + `benches`). No new dependency — Vite supports multi-page apps
  natively.
- No CI changes needed: `.github/workflows/pages.yml` uploads the whole
  `frontend/dist` directory, so the second HTML entry ships automatically.
- Each app's top bar links to the other (`benches.html` ↔ `./`), matching the
  mockup's cross-link.

### Shared design system

`frontend/src/theme/variables.ts` and `frontend/src/hooks/useTheme.ts` are
reused as-is (same `localStorage` key `ignixa-lab-theme`, same light/dark
toggle). The mockup's bench screens reference additional CSS custom
properties not yet in `theme/variables.ts`:

`--chip-vio-bg/-fg`, `--chip-teal-bg/-fg`, `--chip-pink-bg/-fg`,
`--chip-amb-bg/-fg`, `--chip-ind-bg/-fg`, `--chip-red-bg`,
`--chip-gray-bg/-fg/-gray2-fg`, `--hl-arrow`.

Add these to `THEME_VARIABLES` for both `light` and `dark`: the mockup only
overrides them in dark mode (via `renderVals()`'s `dark ? {...} : {}`); its
inline `var(--x, #fallback)` defaults *are* the light-mode values, so use
those fallbacks as the light entries.

## FHIRPath bench (real backend)

### Request

POST to `/api/$fhirpath-{stu3,r4,r4b,r5,r6}` (route chosen by a version
selector added to the top bar — not present in the mockup, defaults to R4).
Vite's existing `/api` dev proxy (`vite.config.ts`) covers this with no
extra config; production uses `VITE_API_BASE_URL` like the rest of the app.

Body is a FHIR `Parameters` resource per `server-api.md`:

```
{ resourceType: "Parameters", parameter: [
  { name: "expression", valueString: <expr> },
  { name: "context", valueString: <context> },          // omit if empty
  { name: "variables", part: [{ name, valueString }] },  // omit if none
  { name: "resource", resource: <JSON.parse(resourceText)> },
  { name: "terminologyserver", valueString: <url> }       // omit if empty
]}
```

### Trigger

Debounced auto-run (~450ms after the last edit to expression / context /
resource / variables / version), replacing the mockup's synchronous
client-side re-evaluation with a network call while keeping the live feel.
An in-flight request is aborted if a new one is triggered before it resolves.

### Response parsing

- **Results tab**: for each `result` parameter, `valueString` is the context
  label (omitted parameter → single unlabeled group). Each `part` is one
  output value: `part.name` is the datatype, value comes from
  `part.value{Type}`, `part.resource`, or the
  `http://fhir.forms-lab.com/StructureDefinition/json-value` extension for
  types with no `value[x]` slot. Maps onto the mockup's `fpGroups`/`fpItems`
  shape.
- **Trace tab**: `part`s named `trace` nested inside each `result` (from
  `.trace('label')` calls in the expression) — `valueString` is the trace
  label, its own `part[]` are the traced values. This is exactly what the
  mockup's Trace tab copy already describes ("Add `.trace('label')`
  anywhere...").
- **Parse tree tab**: `parameters.part[name="parseDebugTree"].valueString` is
  a JSON string shaped `{ ExpressionType, Name, Arguments?: [...],
  ReturnType?, Position?, Length? }` (recursive) — render as an indented
  tree analogous to the mockup's `fpAstLines`, using `ExpressionType` for the
  kind chip and `Name` (+ `ReturnType` if present) for the label.

### Errors

- Non-2xx response → `OperationOutcome`; surface `issue[0].details.text`/`diagnostics`
  in the mockup's red error banner.
- 200 response with a `parameter[name="error"]` → same banner, from
  `valueString`.
- `parameters.part[name="debugOutcome"]` (validation issues, if present) is a
  nice-to-have — surface as non-blocking warnings if time allows; not
  required for v1.

### Fixtures

Port the mockup's two sample resources (Patient `example`, Observation
`blood-pressure`) verbatim as the sample-resource chips.

## FML bench (mocked)

Port the mockup's client-side StructureMap-subset interpreter into
`frontend/src/benches/fml/fmlEngine.ts`:

- `runFml` — regex-based `group Name(source src : Type, target tgt : Type) { rules }`
  parser, applies each rule (`src.path as alias -> tgt.path = rhs "ruleName";`)
  to build a target JSON object, producing an execution log (applied/skipped/error).
- `fmlHighlight` — tokenizer-based syntax highlighter for the FML editor pane.
- `diffLines` — LCS line diff between actual and expected output.

Fully interactive: editable map/source/expected-output, `▶ Run map` button,
Output / Diff vs expected / Execution log tabs — no backend involved. Keep
the mockup's sample map (`PatientToPerson`) and expected-output fixture.

`E:\data\src\fhirpath-lab\pages\fml.vue` / `pages\FhirMapper*\index.vue` are
available as UX reference for a real FML tool if useful, but the mockup's
own engine is small and self-contained enough to port directly without
consulting them.

## SQL on FHIR bench (mocked)

Port the mockup's ViewDefinition runner into
`frontend/src/benches/sof/sofEngine.ts`: `sofSelectRows` walks
`select[].column[]` (FHIRPath `path` per column) and `select[].forEach` /
`forEachOrNull` (row-multiplying sub-selects), producing a flattened table.
Editable ViewDefinition + resources JSON, `▶ Run view` button, result table
with `∅` for null cells. Keep the mockup's sample ViewDefinition and three
sample Patients.

## Shared internal FHIRPath-subset evaluator

Both the FML and SQL-on-FHIR mock engines depend on the mockup's own small
client-side FHIRPath tokenizer/parser/evaluator (`fpTokenize`/`fpParse`/`fpEval`/`fpGet`)
for `path`/`forEach` expressions. Port this once into
`frontend/src/benches/shared/miniFhirPath.ts` as an internal support module
used only by FML/SoF — the FHIRPath bench itself talks to the real backend
engine and does not use it.

## Shared bench UI primitives

Extract small pieces repeated across all three bench screens into
`frontend/src/benches/components/`: card container, tab-pill group, mono
error banner, mono JSON/code textarea. Follows the existing
per-concern component pattern (`SetupScreen`/`RunnerScreen`/etc.) rather than
tripling styling across three near-identical bench layouts.

## Testing / verification

The frontend has no automated test runner today — only `tsc -b` (via
`npm run build`) and `oxlint` (`npm run lint`) gate CI. This spec follows
that existing convention rather than introducing new test infrastructure.
Verification is: type-check, lint, production build (both HTML entries),
and a manual walkthrough in the dev server — both `/` and `/benches.html`,
light and dark theme, all three bench tabs, at least one real FHIRPath call
against the local Functions host.

## Out of scope

- No backend changes — the FHIRPath endpoints already exist and work as-is.
- No new frontend dependencies (no router, no test framework).
- No wiring FML or SQL-on-FHIR benches to real backends — those engines
  don't exist yet in this repo.
- No `debug-trace` step-by-step parsing (the per-node position/index debug
  trace from `server-api.md`) — only the `trace()`-call-based Trace tab
  described above.
