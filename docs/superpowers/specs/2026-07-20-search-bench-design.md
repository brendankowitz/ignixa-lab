# Search Bench — design

**Status**: Approved
**Created**: 2026-07-20

## Problem statement

The Expression Benches tool (`frontend/src/benches/`, see
`2026-07-02-expression-benches-design.md`) ships FHIRPath (live), Validation
(live), Fakes (live), and FML/SQL-on-FHIR (disabled, "not yet implemented").
The Claude Design mockup `Ignixa Expression Benches.dc.html` (project
`8903721c-aa03-4d16-8e8d-6ccad7d9b096`) also specifies a fourth workbench,
**Search**, that has no UI or backend in this repo yet: given a FHIR search
query (`Patient?name=Smith&birthdate=gt2000-01-01`), it traces the query
through parsing, the typed expression tree, the lowered SQL plan, and the
generated SQL — with click-to-trace lineage highlighting a span/node/CTE/SQL
segment across all views simultaneously.

`ignixa-fhir` CI run
[29803356307](https://github.com/brendankowitz/ignixa-fhir/actions/runs/29803356307)
publishes two packages that make this buildable against the real engine
rather than a client-side mock: `Ignixa.Search` (0.6.23, stable — the FHIR
search parser/indexer) and `Ignixa.Search.Sql` (0.6.23-alpha — the
search-to-SQL compiler). Critically, `Ignixa.Search.Sql.Tracing` (namespace:
`SearchCompiler`, `SearchTrace`, `ParameterTrace`, `QueryPlanTrace`,
`EmittedSqlTrace`) already exists to produce exactly the cross-referenced
provenance data this UI needs — it is not something this spec has to invent.

This spec covers building the Search bench as a fifth tab, wired to a new
real backend endpoint, plus bumping every `Ignixa.*` package in
`Directory.Packages.props` from 0.6.19/0.6.19-beta to 0.6.23/0.6.23-beta
(confirmed fine until the next ignixa-fhir release).

## Sources of truth

- **Visual/interaction design**: `Ignixa Expression Benches.dc.html`, the
  `isSearch` screen block — resource-type pills, expression input + example
  chips, 3-column trace grid (`sqSearchSpans` / `sqExprBlocksData` /
  `sqAstBlocksData`), complexity badge (`sqCostTotal`), SQL panel with
  SQL/Explain tabs (`sqSqlSegments` / `sqExplainRows`), click-to-clear
  lineage affordance.
- **Engine API (code, public GitHub)**:
  `src/Core/Ignixa.Search/{Parsing,Expressions}` (parser, `IrProjector`,
  `SyntaxNode`), `src/Core/Ignixa.Search.Sql/{Symbols,Lowering,Ast,Builders,Tracing}`
  (`Resolve`, `Lower`, `SqlBuilder`, `SearchCompiler`, `SearchTrace` and its
  nested records). Both packages' `README.md` document the pipeline stages
  and support matrix.
- **Existing bench conventions**: `Functions/FhirPathFunctions.cs` +
  `Services/FhirPath/SchemaProviderFactory.cs` (backend endpoint/DI pattern),
  `frontend/src/benches/fhirpath/FhirPathBench.tsx` (live-bench component
  shape), `frontend/src/benches/BenchesApp.tsx` (tab wiring).

## Architecture

### Package bump (all `Ignixa.*` entries, one release train)

Bump every `Ignixa.*` `PackageVersion` in `Directory.Packages.props` from
`0.6.19`/`0.6.19-beta` to `0.6.23`/`0.6.23-beta`, and add two new entries:
`Ignixa.Search` (`0.6.23`) and `Ignixa.Search.Sql` (`0.6.23-alpha`, matching
its actual prerelease tag). Copy every `.nupkg` downloaded from CI run
29803356307 (`nuget-packages-core` artifact — the `nuget-packages-internal`
artifact's packages, e.g. `Ignixa.Api`/`Ignixa.Application`/`Ignixa.DataLayer.*`,
are not consumed by this repo and are not added) into `artifacts/local-feed`
alongside the existing `IgnixaLab.TestScript.Suites` package, so
`nuget.config`'s existing `local-feed` source resolves them without any new
source. Add `Ignixa.Search`/`Ignixa.Search.Sql` as `PackageReference`s in
`Ignixa.Lab.Functions.csproj`. Follow the same build-fix-then-full-test-pass
discipline as `2026-07-12-ignixa-fhir-0.6.19-migration-design.md` — build,
fix any breaking API changes the 0.6.19→0.6.23 jump introduces, run the full
backend test suite, before starting on the Search feature itself.

### Backend: `SearchFunctions.cs`

New `[Function("SearchTrace")]` endpoint, `GET /api/search/{resourceType}`,
forwarding the request's raw query string. Anonymous auth, same convention
as `FhirPathFunctions`. No FHIR-version selector in the mock — Search
targets R4 only for v1 (matching this repo's other benches' default; add a
version parameter later if needed, out of scope here).

Request handling:
1. Parse the query string into `IReadOnlyList<QueryParameter>` (`Ignixa.Search.Parsing`).
2. Build `ISearchOptionsBuilder`/`ISearchParameterDefinitionManager`/
   `ICompartmentDefinitionManager` for R4 via a new
   `Services/Search/SearchEngineFactory.cs`, following
   `SchemaProviderFactory`'s lazy-singleton-per-version pattern (even though
   only R4 is wired today, matching the existing factory shape keeps adding
   R4B/R5/STU3 later a non-event).
3. Call `SearchCompiler.CompileAsync(resourceType, parameters, optionsBuilder, resolver, compartmentManager, searchParamManager, cancellationToken)`.
4. Map the returned `SearchTrace` to `Models/Search/SearchTraceResponse.cs`
   (a plain DTO — see below) and return as JSON (not a FHIR resource; this
   is bench-tooling, not a FHIR-spec endpoint, so no `OperationOutcome`
   wrapping — plain `400`/`200` JSON, consistent with how `ValidationFunctions`
   already departs from the FHIR-wire-format convention `FhirPathFunctions`
   uses).

#### `InMemorySymbolResolver : ISymbolResolver`

There is no live SQL Server in this repo (`Ignixa.DataLayer.SqlEntityFramework`
is not referenced). `ISymbolResolver`'s two methods
(`GetSearchParamIdAsync`/`GetResourceTypeIdAsync`) are the compiler's only I/O
seam, resolving to arbitrary `short` surrogate ids — the compiler never
interprets the id's value, only its presence/absence, so any deterministic
assignment is correct for demonstrating real plan/SQL shape. New
`Services/Search/InMemorySymbolResolver.cs`: a `ConcurrentDictionary`-backed
registry assigning sequential ids on first sight, keyed by
`(resourceType, parameter.Url)` and `resourceType` respectively — every
parameter that parses successfully (i.e. is a real, known search parameter
for that resource type per `ISearchParameterDefinitionManager`) therefore
also resolves; nothing is silently faked for parameters the parser itself
didn't recognize. This mirrors `SearchTrace.Failure`'s existing
`TraceStage.Resolve` path for genuinely unknown parameters — no special
handling needed on our side, the engine already reports it.

#### Response DTO shape

`SearchTraceResponse` mirrors `SearchTrace` field-for-field, substituting
serializable projections for the two non-serializable pieces
(`ParameterTrace.Ir` and the plan's raw `Expression` graph):

```
SearchTraceResponse
  ResourceType: string
  Parameters: ParameterTraceDto[]
    Ordinal, Key, Value
    KeySyntax, ValueSyntax: SyntaxNode (Kind, Span{Start,Length}, Children[]) | null
    Ir: IrRowDto[] (Kind, Text, Depth) — via IrProjector.Describe(trace.Ir), trace.Ir != null
    Outcome: { Kind: "Compiled" | "Ignored" | "Failed", Reason?, Stage?, Span? }
  Plan: { Explain: string, Rows: PlanExplainRowDto[] (Label, Body), Ctes: CteProvenanceDto[] (CteIndex, ParameterOrdinal, Span) } | null
  Sql: { Sql: string, Ranges: SqlTextRangeDto[] (Label, Start, Length) } | null
  Implicit: ImplicitParameterDto[] (Name, Value, Reason)
  Failure: { Stage, Message, Span } | null
```

`SourceSpan`'s own shape (fetch during implementation — referenced
throughout but its exact field names weren't pulled in this design pass)
determines the exact `Span` projection; treat it as `{Start, Length}` unless
the real type differs, and adjust the DTO to match rather than guessing
further.

A "complexity" figure for the mock's cost badge is not part of the engine's
output — derive it entirely client-side from `Plan.Ctes.length` (CTE count is
a reasonable, honest proxy for query complexity, and avoids inventing a
metric the backend would have to maintain).

### Frontend: `SearchBench.tsx`

New `frontend/src/benches/search/` following `fhirpath/`'s file split:
`searchTypes.ts` (mirrors the DTO above), `searchApi.ts` (debounced
auto-run GET on expression/resource-type change, in-flight abort on
retrigger — same trigger model as the FHIRPath bench), `SearchBench.tsx`.

Layout ports the mockup's `isSearch` block directly: resource-type pills
(`sqRtTabs` — Patient/Observation/Encounter, matching the mock's
`hint-placeholder-count="3"`), expression textarea with `GET
/{resourceType}?` prefix label, example chips. Three-column trace grid:

- **Search** column: renders `Parameters[].KeySyntax`/`ValueSyntax` as
  clickable spans over the literal query string (reconstructed client-side
  from resource type + expression, since spans are byte offsets into that
  same string) — recursing `SyntaxNode.Children` for sub-spans.
- **Search Expression** column: one block per parameter, its `Ir` rows
  rendered as an indented kind-chip + text list per `IrRow.Depth`.
- **Lowered AST** column: `Plan.Rows` rendered the same way, each row's
  highlight/click state driven by matching its `Label` (`"cte{i}"`) against
  `Plan.Ctes[i].ParameterOrdinal`.

Click-to-trace: a single `selectedOrdinal: number | null` piece of state.
Clicking any syntax span, IR row, or plan row sets it to that node's owning
parameter ordinal (plan rows resolve through `Ctes[].ParameterOrdinal`); all
three columns and the SQL panel highlight every element whose own ordinal
(directly, or via `Ctes`/`Ranges` label lookup) matches. SQL panel: `Sql.Sql`
rendered as a single `<pre>`, sliced into highlightable spans at each
`Ranges[]` offset; a `Ranges[].Label` of `"cte{i}"` joins back to
`Ctes[i].ParameterOrdinal` the same way plan rows do. "Explain" tab shows
`Plan.Explain` as plain preformatted text (no per-line interactivity needed
there — `Plan.Rows` already covers the interactive case).

Failure states: `Failure != null` renders the same error-banner pattern
`fpHasError`/`sofHasError` already use elsewhere; a `Parameters[].Outcome.Kind
=== "Ignored"` renders its reason inline in the Search column (dashed/muted,
distinct from a normal span) rather than as a page-level error, matching how
FHIR lenient-handling drops are meant to read (a warning about one
parameter, not a failed request).

### Tab wiring

Add `'search'` to `BenchId` in `BenchesApp.tsx`, a `{ id: 'search', label:
'Search' }` entry in `BENCH_TABS` (not disabled — this is a live tab from
day one, unlike FML/SQL-on-FHIR's placeholders), add `'search'` to the
`live engine` label condition, and render `<SearchBench />` alongside the
other four. No "send to Fakes" integration in v1 (the mock doesn't show a
Fakes handoff for Search, unlike FHIRPath/Validation/SQL-on-FHIR) and no
share-link state in v1 (`shareLinks.ts` isn't extended) — both are
plausible fast-follows, not required for parity with the mock.

## Testing

- **Backend**: unit tests for `InMemorySymbolResolver` (deterministic id
  assignment, distinct ids per resource type / param), for the
  `SearchTrace → SearchTraceResponse` mapper (one test per `Outcome` variant,
  one asserting `Ctes[].ParameterOrdinal` survives the mapping unchanged so
  the frontend's join key is trustworthy), and an integration-style test
  hitting `SearchFunctions` end-to-end for 2-3 representative queries
  (`Patient?name=Smith`, a chained reference, an unresolvable/unknown
  parameter) asserting on response shape and the `Failure`/`Ignored` paths.
- **Frontend**: no existing bench has component tests beyond
  `jsonPathResolver.test.ts` (a pure-function test) — follow that precedent;
  add pure-function tests for the query-string span reconstruction and the
  ordinal-matching/highlight-membership logic, not full component rendering
  tests.
- **Both**: `dotnet build && dotnet test` (backend) and the frontend
  lint/build check must pass after the package bump, before any Search-bench
  code is written — a clean baseline first, per the 0.6.19 migration spec's
  discipline.

## Out of Scope

- FHIR versions other than R4 for the Search bench (factory shape supports
  adding them later; no UI selector in v1).
- A live SQL Server / real `ISymbolResolver` implementation — this bench is
  exploration tooling, not a production search endpoint.
- Fakes handoff and share-link state for the Search tab.
- Enabling the FML / SQL-on-FHIR disabled tabs — unrelated, no new packages
  for those engines were part of this CI run.
- Wiring `Ignixa.Search`/`Ignixa.Search.Sql` into any production data path —
  `Ignixa.Search.Sql` is explicitly alpha/experimental per its own README.
