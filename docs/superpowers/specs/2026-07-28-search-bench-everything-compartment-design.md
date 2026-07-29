# Search bench: compartment search & `Patient/$everything`

**Status**: Approved design, not yet implemented
**Date**: 2026-07-28

## Context

The Search bench (`frontend/src/benches/search/`, `backend/src/Ignixa.Lab.Functions/{Functions/SearchFunctions.cs,Services/Search/,Models/Search/}`) traces a FHIR search query from parse through the lowered plan to emitted SQL, via `Ignixa.Search.Sql`'s `SearchCompiler.CompileAsync`. Today it only handles type-level search (`GET /Patient?name=Smith`) — the compiled expression always comes from `QueryParameterParser.Parse` over a query string.

`ignixa-fhir` release `0.6.41` (verified against the actual CI build, not assumed) makes two more expression shapes reachable through the *same* `CompileAsync` entry point, via its `operationExpression` parameter: `Ignixa.Search.Expressions.PatientEverythingExpression` (`Patient/$everything`) and `Ignixa.Search.Expressions.CompartmentSearchExpression` (`Patient/{id}/{resourceType}` and `Patient/{id}/*`). Both are public types with plain constructors — no private-feed dependency, no new I/O seam. This spec covers wiring both into the bench.

This follows `docs/superpowers/specs/2026-07-27-search-bench-next-release-readiness.md`, which first identified these as reachable but flagged them as needing their own design pass rather than a same-session addition. That pass is this document.

## What was verified before design started

All of the following was confirmed by constructing the real expressions and calling `SearchCompiler.CompileAsync` directly against `ignixa-fhir`'s `0.6.41` CI build (not the partially-published nuget.org state at time of writing — see "Package availability" below), reading the full `Explain`/SQL output, not just checking for exceptions:

- **Compartment search, scoped to a resource type**: `new CompartmentSearchExpression("Patient", "example", new HashSet<string> { "Observation" })` compiles to a small, correctly-narrowed plan (2 `CompartmentSource` CTEs, one per Patient-referencing search parameter Observation has, unioned). An **earlier probe that omitted `filteredResourceTypes` was my own test mistake, not a library defect** — omitting it (or explicitly requesting `*`) correctly produces the full wildcard-compartment traversal instead, per the type's own doc comment ("If empty or null, searches all types in compartment").
- **Compartment search combined with a normal query**: `code=1234-5` layered on top of the compartment scope compiles and narrows further — compartment scoping and the existing query-string search terms are not mutually exclusive.
- **`Patient/$everything`, five scenarios, full SQL read**:
  - Bare: anchor Patient row ∪ full compartment traversal (one CTE per (resourceType, search-param) pair in the R4 Patient compartment definition — 75 of them, which is just how large that compartment is, not a defect) ∪ `ReferencedTypeExpansion` (Practitioner/Organization/Location/Medication).
  - `_type` filter: plan collapses from ~800 lines to 6 CTEs — correctly narrows to the requested types, correctly drops `ReferencedTypeExpansion` (out of scope), still includes the anchor Patient row (by design — `$everything`'s root resource is always returned regardless of `_type`).
  - `_since`: real `VisibleSinceFilter` CTE (`INNER JOIN dbo.Transactions ... WHERE VisibleDate >= @p`), intersected with compartment members but *not* the anchor Patient (correct — the anchor isn't `_since`-filtered).
  - `start`/`end` clinical date range: `TableExistsPredicate[DateTimeSearchParam]` + `Intersect`/`Except` — resources *with* a date get range-filtered; resources with no date search parameter at all pass through unfiltered rather than being silently dropped.
  - `includeReferencedResources: false`: `ReferencedTypeExpansion` cleanly disappears from `root`.
- **Compartment roots**: `Ignixa.Specification.ValueSets.Normative.CompartmentType` has exactly 5 values — `Device`, `Encounter`, `Patient`, `Practitioner`, `RelatedPerson`. Of the bench's current 3 resource types, only `Patient` and `Encounter` are valid compartment roots; `Observation` is not (it can be a compartment *member*, never a root).
- **`$everything` is Patient-only in this library.** `PatientEverythingExpression`'s doc comment says its multi-id constructor is also used for `Group $everything`, but there is no `EncounterEverythingExpression` or similar — `Encounter` gets compartment search but not `$everything`.
- **Package availability at design time**: `Ignixa.Search.Sql` is published to nuget.org at `0.6.41-alpha`, but `Ignixa.Search`, `Ignixa.Specification`, `Ignixa.Serialization`, `Ignixa.Validation`, `Ignixa.TestScript`, and `Ignixa.TestScript.Suites` are still only at `0.6.28` — a real `dotnet restore` at `0.6.41` fails with `NU1102`. All verification above used a complete, self-consistent `0.6.41` set pulled from the CI run's own build artifacts (`nuget-packages-core`, run matching `release/0.6.41`'s commit), not nuget.org. **Implementation must wait until the rest of the `0.6.41` set actually lands on nuget.org** — check with a real `dotnet restore` before starting, don't assume the tag being cut means the packages are all published.

## Scope

Both compartment search and `$everything` land together, sharing the same route-shape and UI scaffolding, rather than compartment search shipping alone first. The four breaking-change fixes and `KnownMiss` support already catalogued in the `2026-07-27` readiness doc are a prerequisite (the whole solution needs to build against `0.6.41` before this can be built on top of it) but are not re-described here.

## Backend

### Routes

Three routes on `SearchFunctions.cs` (the existing `Trace` method stays as-is):

```
GET /api/search/{fhirVersion}/{resourceType}                                     existing: type search
GET /api/search/{fhirVersion}/{compartmentType}/{compartmentId}/{resourceType}   new: compartment search (resourceType may literally be "*")
GET /api/search/{fhirVersion}/Patient/{patientId}/$everything                    new: Patient-only, per the library
```

`$everything`'s route hardcodes the literal `Patient` segment rather than parameterizing it — the library gives no other option today, and hardcoding it makes the Patient-only constraint visible in the route table rather than needing a runtime check.

### Compartment search handler

- Validate `compartmentType` is one of the 5 `CompartmentType` values, and `resourceType` (when not `*`) via the existing `engine.SearchParameters.TryGetSearchParameters` guard.
- `filteredResourceTypes`: `resourceType == "*"` → `null` (wildcard, verified producing the real broad traversal); otherwise a singleton `HashSet<string> { resourceType }` (verified producing the narrow, correct plan).
- Parse any additional query-string parameters via the existing `QueryParameterParser`, pass them alongside the `CompartmentSearchExpression` as `operationExpression` to the same `SearchCompiler.CompileAsync` call the existing `Trace` method uses.
- Map through the existing `SearchTraceMapper.ToResponse` — no DTO shape changes.

### `$everything` handler

Query parameters map directly to `PatientEverythingExpression`'s constructor: `_type` (comma-list → `filteredResourceTypes`), `_since` (→ `sinceDate`), `start`/`end` (→ `startDate`/`endDate`), `includeReferencedResources` (bool, default `true` matching the library's own default). No query-string search parameters are parsed for this route — `$everything` isn't parameter-driven, so there's nothing for `QueryParameterParser` to do here; pass an empty parameter list to `CompileAsync`.

### Frontend-visible consequence: empty `Parameters`

A bare `$everything` call (no `_type`/`_since`/date filters) returns `SearchTrace.Parameters` as an **empty list** — the whole thing is one expression, not parameter-driven, so there's nothing to trace per-parameter. The UI must render this as an explicit empty state in the Parameters/Syntax/IR panes — copy along the lines of "no parameters — this is a whole-compartment operation," not an unexplained blank box. Plan and SQL panes carry the whole story for this mode, which is a genuine feature: watching the CTE count visibly collapse when a `_type` filter is added is a good demonstration of what the compiler does, not a degraded experience.

## Frontend

### Mode selection: resource type pills expand per type

Only the modes a given resource type actually supports are ever shown — determined by the backend capability, not left to the user to discover by trial and error:

| Resource type | Modes offered |
|---|---|
| Patient | Type search · Compartment · `$everything` |
| Encounter | Type search · Compartment |
| Observation | Type search only |

### Compartment mode

Selecting "Compartment" under a root type (Patient or Encounter) locks that pill into id-entry mode; the *other* resource-type pills become the member-type selector, plus a wildcard option:

```
Resource type: [Patient ▾ Compartment] id:[________]  (examples: "example")
within compartment, search: (Observation) (Encounter) (* all types)

GET /Patient/{id}/Observation?code=1234-5
```

The existing `SearchQueryBuilder` chips (search terms, `_include`, sort, `_summary`) stay active underneath, scoped to whichever member type is selected — verified they compose with compartment scoping rather than needing separate handling.

### `$everything` mode

```
Resource type: [Patient ▾ $everything] id:[________]
_type filter: (Observation) (Condition) (* all)   _since:[____]   start:[____] end:[____]
☑ include referenced resources (Practitioner / Organization / Location / Medication)

GET /Patient/{id}/$everything?_type=Observation,Condition
```

No `SearchQueryBuilder` chips in this mode — replaced entirely by the typed options above, which map 1:1 onto `PatientEverythingExpression`'s constructor parameters. `includeReferencedResources` defaults checked (`true`), matching the library default.

### Shared pieces

- The `GET /...` breadcrumb already shown above the query box updates to match whichever route is active (type search / compartment / `$everything`).
- The id field is free text with a couple of pre-verified example ids as one-click chips (reusing the bench's existing `example` fixture-id convention), not a resource browser and not wired into Fakes generation — consistent with the rest of the bench's minimal, self-contained style.
- No `SearchTraceResponse` DTO changes are needed for either mode — the frontend already knows which mode it invoked by which endpoint it called, and the response shape (`Parameters`/`Plan`/`Sql`/`Implicit`/`Failure`) is identical either way.

## Explicitly out of scope

- Browsing/picking a real patient id (no resource browser exists in the bench; out of scope here as it was for the original bench).
- `Group/$everything` (the library's multi-id constructor exists for it, but the bench has no Group resource type today).
- Any resource type beyond the bench's existing three (Patient/Observation/Encounter) gaining compartment-root status.
- The four `0.6.41` breaking-change fixes and `KnownMiss` support — already fully specified in `2026-07-27-search-bench-next-release-readiness.md`; this doc assumes that work is done first.

## Verification plan

1. Confirm `dotnet restore` succeeds against real nuget.org `0.6.41` packages before starting (not just the CI artifact used for this design's verification) — the readiness doc's partial-publish issue may still be open.
2. Apply the breaking-change fixes and `KnownMiss` support from the `2026-07-27` doc first; confirm 575+/575+ tests green before adding anything from this doc.
3. New `SearchFunctionsTests` cases per route: compartment search scoped to a type (assert the plan is small/narrow, not the full wildcard traversal — this is exactly the mistake this design's own verification caught), compartment wildcard search, compartment search combined with a query-string parameter, `$everything` bare, `$everything` with each of `_type`/`_since`/`start`+`end`/`includeReferencedResources=false` — mirroring the five scenarios already verified by hand above, now as permanent regression tests.
4. A `Trace_BareEverything_ReturnsEmptyParametersWithNonNullPlanAndSql` test pinning the empty-`Parameters` behavior the frontend depends on.
5. Frontend: build/lint clean, then manually drive the actual running bench (as was done for the earlier chip additions) through all three modes for Patient, both modes for Encounter, and confirm Observation shows no compartment/`$everything` options at all.
6. Re-verify the `_type`-filter CTE-count collapse is visible in the actual rendered Plan pane, not just in a scratch console probe — it's the headline demonstration this feature is built around.
