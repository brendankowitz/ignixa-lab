# FML and SQL-on-FHIR bench backends — design

**Status**: Approved
**Created**: 2026-07-02

## Problem statement

`ignixa-lab`'s Expression Benches page (`docs/superpowers/specs/2026-07-02-expression-benches-design.md`)
ships three tabs — FHIRPath, FML, SQL on FHIR. Only FHIRPath talks to a real
backend; FML and SQL on FHIR run small client-side mock interpreters
(`frontend/src/benches/fml/fmlEngine.ts`, `frontend/src/benches/sof/sofEngine.ts`)
because "those engines don't exist yet in this repo."

They now do. Two sibling NuGet packages published from `ignixa-fhir`
(same publisher/version family as `Ignixa.FhirPath`, which already powers the
FHIRPath bench) implement both engines in full:

- **`Ignixa.FhirMappingLanguage`** — a complete FHIR Mapping Language (FML)
  parser and evaluator (lexer, AST, `MappingParser`, `MappingEvaluator`),
  built on the same architecture as `Ignixa.FhirPath` and already integrated
  with it for embedded FHIRPath expressions (`where`, `check`, `log`).
- **`Ignixa.SqlOnFhir`** — a spec-compliant SQL-on-FHIR v2 `ViewDefinition`
  evaluator (`SqlOnFhirEvaluator`), supporting `where`, `constant`,
  `forEach`/`forEachOrNull`, nested `select`, and `unionAll`.

This spec covers wiring both into `Ignixa.Lab.Functions` as real HTTP
endpoints, and pointing the two bench tabs at them instead of the mock
engines. Both new engines are consumed as opaque NuGet dependencies —
no parser/evaluator code is written in this repo.

## Compatibility targets

Unlike the FHIRPath bench (where fhirpath-lab.com's own wire contract is the
target — see `server-api.md`), the two engines here have different
compatibility stories:

- **FML**: fhirpath-lab.com's FML tab (`E:\data\src\fhirpath-lab\pages\fml.vue`
  / `FhirMapper2\index.vue`) calls real external engines via the FHIR-standard
  `StructureMap/$transform` operation — a `.NET` engine
  (`fhir-mapping-lab2.azurewebsites.net`), a Java engine, and `matchbox`, all
  speaking the same `Parameters`-in/`Parameters`-out shape (confirmed by
  reading `brianpos/fhirpath-lab-java2`'s `FmlTransformController.java` and
  fhirpath-lab's `executeRequest`/response-parsing code). This is a real,
  matchable wire contract, so our endpoint targets it directly — fhirpath-lab.com
  could in principle repoint its "mapper" engine selector at this app the same
  way it already can for FHIRPath.
- **SQL on FHIR**: fhirpath-lab.com's SoF tab has **no backend at all** — it
  runs the official `sql-on-fhir-v2/sof-js` reference JS library entirely
  client-side in the browser (confirmed in `pages/sqlonfhir/index.vue`, which
  imports `evaluate` from `sql-on-fhir-v2/sof-js` and calls it synchronously;
  `types/sof.d.ts` is just a type shim for that import). There is nothing to
  be "compatible with fhirpath-lab" for this tab. Per user decision, we build
  a real backend anyway, targeting the closest real standard instead: the
  official SQL-on-FHIR-v2 IG's `$viewdefinition-run` operation
  (`http://sql-on-fhir.org/OperationDefinition/$viewdefinition-run`,
  `build.fhir.org/ig/FHIR/sql-on-fhir-v2/OperationDefinition-ViewDefinitionRun.html`).
  This makes the endpoint usable by any SQL-on-FHIR-aware tooling, not just
  our own bench UI, even though it won't match fhirpath-lab.com (which has no
  equivalent to match).

## Architecture

### New packages

Add to `Directory.Packages.props` (pin to the `0.5.13` family alongside
`Ignixa.FhirPath`/`Ignixa.Serialization`/`Ignixa.Specification`):

```xml
<PackageVersion Include="Ignixa.FhirMappingLanguage" Version="0.5.13-beta" />
<PackageVersion Include="Ignixa.SqlOnFhir" Version="0.5.13" />
```

Reference both from `Ignixa.Lab.Functions.csproj`.

### New Function classes and services

Mirrors the existing `FhirPathFunctions.cs` → `FhirPathService` split:

```
backend/src/Ignixa.Lab.Functions/
├── Functions/
│   ├── FmlFunctions.cs            HTTP endpoint: StructureMap/$transform
│   └── SqlOnFhirFunctions.cs      HTTP endpoint: ViewDefinition/$viewdefinition-run
├── Services/
│   ├── Fml/
│   │   ├── FmlService.cs          Orchestrator: parse → evaluate → format
│   │   └── FmlResultFormatter.cs  Builds the Parameters response
│   └── SqlOnFhir/
│       ├── SqlOnFhirService.cs    Orchestrator: parse → evaluate → format
```

Both reuse existing infrastructure rather than duplicating it:

- `JsonSourceNodeFactory` / `ResourceJsonNode` / `ParametersJsonNode` for
  request/response parsing (same types `FhirPathFunctions.cs` uses).
- `SchemaProviderFactory` for FHIR-version schema (FML defaults to R4 like
  the FHIRPath endpoints; SQL on FHIR's `ViewDefinition.fhirVersion` field is
  metadata-only per `Ignixa.SqlOnFhir`'s model, so it doesn't drive schema
  selection).
- The SSRF-guarded URL-fetch path (`FhirPathService.LoadResourceFromUrl` /
  `TargetUrlValidator`) for `resource` parameters supplied as a URL instead of
  inline JSON — both new endpoints accept the same `resource` valueString-as-URL
  convention the FHIRPath endpoint does.

## FML endpoint (`StructureMap/$transform`)

### Route

`POST/GET/OPTIONS /api/StructureMap/$transform` — same path shape as
fhirpath-lab.com's `mapper_server` config entries
(`https://fhir-mapping-lab2.azurewebsites.net/StructureMap/$transform?debug=true`),
just with the `/api` prefix our other endpoints already use (see
`ignixa_r4: .../api/$fhirpath-r4` in fhirpath-lab's `config.json`). A `debug`
query-string flag (`?debug=true`) toggles verbose trace output, matching the
reference engines' own convention.

### Request

FHIR `Parameters`:

| Parameter | Cardinality | Meaning |
|---|---|---|
| `map` | 1..1, valueString | The FML source text to parse and execute. |
| `resource` | 1..1 | Either an embedded `resource`, or a `valueString` containing raw JSON text, or a `valueString` URL (fetched via the existing SSRF-guarded path). |
| `model` | 0..* | Extra `StructureDefinition`s (embedded `resource`, a `Bundle` of them, or raw JSON/XML text) for `uses` clauses referencing non-base-FHIR (e.g. logical) models. **v1 gap** — see below. |

### Processing

1. Parse `map` via `Ignixa.FhirMappingLanguage`'s `MappingParser().Parse(fml)`
   → `MapExpression`. Parse failure → `OperationOutcome` error response
   (400), diagnostics = parser error message.
2. Resolve the source/target types and variable names from the map's `uses`
   declarations (`uses "..." alias X as source`, `... as target`).
3. Parse the `resource` parameter into an `IElement` against the resolved
   schema (reusing the existing `ResourceJsonNode.ToElement(schema)` pattern
   from `ExpressionEvaluator`).
4. Build a target shell resource (`{ "resourceType": "<TargetType>" }` as a
   `ResourceJsonNode`) from the target `uses` type, mirroring the reference
   Java engine's `getTargetResourceFromStructureMap`.
5. Build a `MappingContext`: `SetSource(sourceAlias, sourceElement)`,
   `SetTargetResource(targetAlias, targetResourceJsonNode)`, wire a
   `JsonNodeMutator` so target rule assignments mutate the real JSON node,
   set `Logger` to append to a trace-entry list (surfaces FML `log(...)`
   clause output as `trace` parameter parts in the response, the direct
   analog of the FHIRPath bench's `.trace('label')` handling), and run in
   `ErrorMode.Lenient` so partial output plus a full `ExecutionError` list are
   both available regardless of the `debug` flag.
6. `new MappingEvaluator(options).Execute(map, context)`.
7. Serialize the resulting target `ResourceJsonNode` to pretty JSON text.

### Response

FHIR `Parameters`, matching the shape fhirpath-lab.com's `fml.vue` already
parses (`entry.name === 'parameters'|'trace'|'result'`, config
`part[0].name === 'evaluator'`):

- `parameters` part → nested `evaluator` (valueString, e.g.
  `"Ignixa .NET (FML)"`) and echoed `map` (valueString) — matches both
  reference engines' config-echo convention.
- `trace` part(s) → `valueString` = log label, nested `part[]` = traced
  values, populated from `context.Logger` calls and (when `?debug=true`)
  additional per-rule execution detail from `context.Errors`/rule tracking.
- `result` part → `valueString` containing the transformed resource as
  pretty-printed JSON text (both reference engines return the output as a
  *string*, not an embedded `resource` — matched here for byte-shape
  compatibility, even though fhirpath-lab.com's parser tolerates either).
- `outcome` part → `OperationOutcome`: informational
  ("Transformation completed successfully") on a clean run, or one `issue`
  per `ExecutionError` (error severity, diagnostics = `ExecutionError.ToString()`)
  when `context.Errors` is non-empty.
- Hard failures (parse error, unresolvable resource, unhandled exception) →
  top-level `OperationOutcome` + HTTP 400, same pattern as
  `FhirPathFunctions.CreateErrorResponse`.

### v1 gaps (explicitly out of scope, flagged for follow-up)

- **`model` parameter**: registering caller-supplied custom/logical
  `StructureDefinition`s into the schema mid-request (the Java engine's
  `readCustomStructureDefinitions`) has no confirmed equivalent hook in
  `Ignixa.FhirMappingLanguage` today. v1 supports `uses` clauses against base
  FHIR resource types only (resolved via `SchemaProviderFactory`); maps
  requiring `model`-supplied logical models return a clear
  "unsupported model reference" `OperationOutcome` error rather than
  silently mis-transforming. Revisit once/if the package exposes a schema
  registration API.
- **XML** input/output — JSON only, matching the existing FHIRPath endpoints'
  scope (the reference engines support XML; fhirpath-lab.com's own default
  usage is JSON).
- **`?debug=true` step-level parse/position trace** (the per-node debug tree
  the FHIRPath bench explicitly excluded in its own spec) — only rule-level
  `log()`/error trace, not a node-by-node execution trace.

## SQL on FHIR endpoint (`ViewDefinition/$viewdefinition-run`)

### Route

`POST /api/ViewDefinition/$viewdefinition-run` — the real HL7 SQL-on-FHIR-v2
IG operation name and type-level URL shape
(`[base]/ViewDefinition/$viewdefinition-run`), so this endpoint means
something to SQL-on-FHIR tooling beyond our own bench UI.

### Request

FHIR `Parameters`, scoped to the subset of the official operation that makes
sense for a stateless bench (no persistent FHIR store behind this app):

| Parameter | Cardinality | Meaning |
|---|---|---|
| `viewResource` | 1..1 | Inline `ViewDefinition` resource (embedded `resource`). |
| `resource` | 0..* | Inline FHIR resources to run the view against. At least one effectively required (no server-side data store to fall back to). |
| `_format` | 0..1, valueCode | v1 supports `json` only (default). Other IG-defined values (`ndjson`, `csv`, `parquet`) rejected with a 400 `OperationOutcome` in v1. |
| `_limit` | 0..1, valueInteger | Caps the number of returned rows. |

`viewReference`, `patient`, `group`, `source`, `_since` are **not
supported** — they all presuppose a FHIR server-side data store this app
doesn't have. Requests using them get a 400 `OperationOutcome`.

### Processing

1. Parse `viewResource` into an `ISourceNavigator` (`JsonSourceNodeFactory`
   + `ToSourceNavigator()`, per `Ignixa.SqlOnFhir`'s own documented usage).
2. Parse each `resource` parameter into an `IElement` against the resolved
   schema (default R4, same `SchemaProviderFactory` as the other endpoints).
3. `new SqlOnFhirEvaluator().EvaluateBatch(viewDefinitionNavigator, elements)`
   → `IEnumerable<Dictionary<string, object?>>`.
4. Apply `_limit` if present.

### Response

Per the IG's own description of the JSON format ("Returns an array of
objects"): HTTP 200, `Content-Type: application/json`, body is a **plain
JSON array** of row objects — not wrapped in a `Parameters` envelope. The
frontend derives its `columns[]`/`rows[]` grid client-side by taking the
union of keys across all returned row objects (a `ViewDefinition`'s
`select` columns are fixed, so in practice every row has the same keys;
the union guards against `forEachOrNull`-induced sparse rows).

Errors (malformed `ViewDefinition`, evaluation failure, unsupported
`_format`) → `OperationOutcome` + appropriate 4xx, same pattern as the other
two endpoints.

### v1 gaps (explicitly out of scope)

- `ndjson` / `csv` / `parquet` output formats.
- `viewReference` (server-stored views), `patient`/`group`/`_since`
  filtering, `source` (external data source) — all require a persistent
  data store this app doesn't have.
- Instance-level (`ViewDefinition/{id}/$viewdefinition-run`) and system-level
  (R4-mode `$viewdefinition-run`) invocation forms — type-level only.

## Frontend wiring

Both bench tabs currently call local, synchronous mock engines. Replace with
network clients mirroring `frontend/src/benches/fhirpath/fhirPathApi.ts`:

- `frontend/src/benches/fml/fmlApi.ts` — builds the `Parameters` request
  (`map`, `resource`), POSTs to `/api/StructureMap/$transform?debug=true`,
  parses the `Parameters` response into the shape `FmlBench.tsx` renders
  (output/log/trace). Replaces `fmlEngine.ts`'s `runFml`.
- `frontend/src/benches/sof/sofApi.ts` — builds the `Parameters` request
  (`viewResource`, repeated `resource`), POSTs to
  `/api/ViewDefinition/$viewdefinition-run`, parses the JSON array response
  into `columns[]`/`rows[]`. Replaces `sofEngine.ts`'s `runSof`.
- `frontend/src/benches/shared/miniFhirPath.ts` is retired — it existed only
  to power the two mock engines' internal path evaluation.
- Keep each tab's existing explicit "▶ Run" button (no auto-run) — these now
  hit a network endpoint, so debounced auto-run isn't appropriate, unlike the
  FHIRPath bench's live-typing feel.
- `FmlBench.tsx`/`SofBench.tsx` gain loading/in-flight and error-banner
  states matching the FHIRPath bench's pattern (abort in-flight request on a
  new Run click).

## Testing / verification

- Backend: xUnit tests in `Ignixa.Lab.Functions.Tests`, mirroring the
  existing `FhirPathFunctions`/`FhirPathService` test structure — one
  suite per new Function class and service, covering: happy-path transform
  and view evaluation, parse errors, missing/invalid `resource`, the
  `model`/`_format`-unsupported 400 paths, and SSRF guard reuse for
  URL-supplied resources.
- Frontend: existing `tsc -b` / `oxlint` / production build gate (no new
  test infra, consistent with the Expression Benches spec's own convention).
  Manual walkthrough: FML tab against the local Functions host with the
  existing `PatientToPerson` sample map; SQL on FHIR tab against the existing
  sample `ViewDefinition` + sample Patients fixture.
- `dotnet test Ignixa.Lab.sln` and `cd frontend && npm run build && npm run lint`
  gate both changes, per existing repo convention.

## Out of scope

- No changes to `FhirPathFunctions.cs` / the FHIRPath bench.
- No `model` (custom/logical StructureDefinition) support for FML in v1 (see
  gap above).
- No XML support for FML in v1.
- No `ndjson`/`csv`/`parquet`, `viewReference`, or resource-filtering support
  for SQL on FHIR in v1.
- No changes to `Ignixa.FhirMappingLanguage` or `Ignixa.SqlOnFhir` themselves
  — both are consumed as-is from NuGet; any gap found during implementation
  that needs an upstream fix is a separate piece of work in `ignixa-fhir`.
- No CapabilityStatement changes beyond optionally listing the two new
  operations in the existing `/metadata` endpoint (nice-to-have, not
  required for either tab to function).
