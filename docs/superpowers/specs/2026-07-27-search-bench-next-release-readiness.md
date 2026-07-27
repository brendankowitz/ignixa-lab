# Search bench: readiness for the next `ignixa-fhir` release

**Status**: Reference — verified against a real CI build, not yet actionable (no release published)
**Date**: 2026-07-27

## Context

The Search bench (`frontend/src/benches/search/`, `backend/src/Ignixa.Lab.Functions/{Functions/SearchFunctions.cs,Services/Search/,Models/Search/}`) targets `Ignixa.Search 0.6.28` / `Ignixa.Search.Sql 0.6.28-alpha` — the newest versions published to nuget.org as of this writing. `ignixa-fhir`'s `main` branch has moved 11 commits past the `release/0.6.28` tag (PRs #353, #357, #360, #362, #363, #365, #366), but nothing newer has been published.

To find out what changes ahead of time rather than discovering it mid-upgrade, I downloaded the `nuget-packages-core` artifact from `ignixa-fhir`'s green CI run [30235379952](https://github.com/brendankowitz/ignixa-fhir/actions/runs/30235379952) (commit `7d7198f8`, built as `0.6.39`/`0.6.39-alpha`) and ran the bench's actual engine-construction code against it in an isolated scratch project. Everything below is empirically verified against that build, not inferred from reading source. CI artifacts expire after 30 days (this one: 2026-08-26) — re-verify against a fresh run if this doc is read after that.

**Update (same day, later pass):** I subsequently built and ran the actual bench end-to-end against these packages — real backend, real frontend, `func start` + `npm run dev`, driven interactively via a browser and PowerShell — not just an isolated scratch project. That surfaced two more breaking changes (§4 below) that only show up once the whole vertical slice compiles together, plus a materially bigger finding: `Patient/$everything` and compartment search are *also* newly reachable, and §5 ("Not yet checked") below was wrong — that work already landed on `main`, it isn't stuck on a separate branch. All fixes described here were applied, verified (575/575 backend tests, clean frontend build/lint, working browser demo), and then **reverted** — the repo's committed state still targets the real published `0.6.28`. This doc is the fixup checklist for whenever a release actually ships past it.

## Breaking changes — fix these first, before anything else compiles

1. **`ISymbolResolver` gained two required members**: `Task<int?> GetSystemIdAsync(string, CancellationToken)` and `Task<int?> GetQuantityCodeIdAsync(string, CancellationToken)`. `backend/src/Ignixa.Lab.Functions/Services/Search/InMemorySymbolResolver.cs` will not compile as-is. Add both, mirroring the existing `GetSearchParamIdAsync`/`GetResourceTypeIdAsync` pattern (sequential `short`→now `int` ids from a `ConcurrentDictionary`, never returning null — a null answer here is what makes `KnownMiss` demonstrable, see below).
2. **`ReferenceSearchValueParser`'s constructor gained a required second parameter**: `IFhirBaseUriProvider`. `SearchEngineFactory.Build`'s `new ReferenceSearchValueParser(schema)` call needs `new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance)` (`NullFhirBaseUriProvider` lives in `Ignixa.Abstractions`, already a transitive dependency). This is what makes absolute/external reference search (`organization=https://other.org/fhir/Organization/123`) work — verified compiling cleanly.
3. **`SearchCompiler.CompileAsync`/`CompileWithTimeProviderAsync` gained `Expression? operationExpression = null` inserted *before* `cancellationToken`.** `SearchFunctions.cs`'s existing call passes `cancellationToken` as the 7th *positional* argument — after the insertion it silently binds to the new `operationExpression` parameter instead and fails to compile (`cannot convert from 'CancellationToken' to 'Expression?'`). Fix: pass it as a named argument, `cancellationToken: cancellationToken` (robust to any future parameter insertions, not just this one).
4. **`SearchTrace.ResourceType` is now nullable** (`string?`, was `string`), and **`EmittedSqlTrace` gained a `Parameters` member inserted before `Ranges`** (now `(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters, IReadOnlyList<SqlTextRange> Ranges)`). `SearchTraceMapper.ToResponse` needs a fallback for the resource type — the endpoint already knows the requested one from the route, so `trace.ResourceType ?? requestedResourceType` is the natural fix (means threading the requested resource type into `ToResponse` as a second parameter). Any test constructing an `EmittedSqlTrace` directly (`SearchTraceMapperTests.cs`) needs the extra `Parameters: []` argument.

All four are mechanical. Nothing else in `SearchEngineFactory`, `SearchFunctions`, or `SearchTraceMapper` failed to compile against the newer packages.

## New outcome case — `ParameterOutcome.KnownMiss`

Confirmed present and behaves exactly as the library's own docs describe: a system-qualified token or quantity code the resolver reports as unknown compiles to a predicate that can never match, not a failure. Verified live:

```
Observation?code=http://loinc.org|99999-9  (resolver reports the system as unknown)
  => outcome = KnownMiss("No resource uses the token system 'http://loinc.org'.")
  => emitted SQL contains the literal "1 = 0" for that branch
  => page-level Failure: none — the query is well-formed and still runs
```

Backend changes needed:
- `Ignixa.Lab.Functions.Services.Search.SearchTraceMapper.ToOutcomeDto`'s switch currently has three arms (`Compiled`/`Ignored`/`Failed`) with a `_ => throw new NotSupportedException(...)` default. **This will throw a 500 the first time a real query hits `KnownMiss`** unless a fourth arm is added: `ParameterOutcome.KnownMiss km => new ParameterOutcomeDto("KnownMiss", km.Reason, null, ToSpanDto(km.Span))` (same shape as `Ignored`).
- `Models/Search/SearchTraceResponse.cs`'s `OutcomeKind` — check whether it's a string or an enum; either way, `"KnownMiss"` needs to be a recognized value.

Frontend changes needed:
- `frontend/src/benches/search/searchTypes.ts`: `export type OutcomeKind = 'Compiled' | 'Ignored' | 'Failed';` → add `'KnownMiss'`.
- Wherever `OutcomeKind` is rendered (likely `SearchBench.tsx`) needs a fourth visual state — amber/warning reads right, distinct from `Ignored` (grey, "never reached the query") and `Failed` (red, "request-level error"). Suggested label: "compiled — can never match."
- `SearchQueryBuilder.tsx` could gain a chip that deliberately triggers it once the backend supports it — e.g. a token search against a system-like-but-unregistered URI — to make the new outcome demonstrable, not just theoretically reachable. Needs a resolver change too: the current `InMemorySymbolResolver` never returns null for anything, so nothing hits `KnownMiss` today even after the DTO/UI work above ships. Either add a small number of "known-unknown" systems the resolver deliberately refuses (documented as a deliberate demo device, not a bug), or leave `KnownMiss` support in place for real unknown-system queries without a dedicated chip.

## New query capabilities — all verified compiling cleanly against `0.6.39-alpha`

Each of these previously rendered as a per-parameter `Failed` outcome (or was rejected outright) against `0.6.28-alpha`. All now compile with `Compiled` outcome, present `Plan`/`Sql`, no page-level `Failure`:

| Query | Feature | Chip suggestion |
|---|---|---|
| `Observation?code=http://loinc.org\|1234-5` | System-qualified token | `SEARCH_TERMS.Observation` |
| `Observation?code=\|1234-5` | Token, explicitly-absent system | `SEARCH_TERMS.Observation` |
| `Observation?code=http://loinc.org\|` | Token, system-only (any code) | `SEARCH_TERMS.Observation` |
| `Observation?value-quantity=5.4\|http://unitsofmeasure.org\|mg` | Quantity system/code identity | `SEARCH_TERMS.Observation` |
| `Patient?birthdate=ap1970-01-01` | `:ap` (approximately) comparator | `SEARCH_TERMS.Patient` |
| `Patient?_lastUpdated=ap2026-01-01` | `:ap` on `_lastUpdated` | `SEARCH_TERMS.Patient` |
| `Condition?code:text=diabet` | `:text` token modifier | new resource type or swap in for an existing one |
| `Patient?_not-referenced=*:*` | Orphan search | `SEARCH_TERMS.Patient` |
| `Patient?_include=*` | Bare wildcard include (no `Type:` prefix) | compiles, but produces **no traced `Parameters` entry** — same shape as `_type` today (see `Trace_ExplicitTypeParameter_IsAbsorbedByTheResourceTypeRouteSegment`); confirm whether `Plan`/`Sql` alone are enough to make this a useful chip, or skip it for the same reason `_type` was skipped |
| `Patient?name:contains=<300 chars>` | String `:contains` across the inline/overflow boundary | `SEARCH_TERMS.Patient` |
| `Patient?name:exact=<300 chars>` | String `:exact` across the inline/overflow boundary | `SEARCH_TERMS.Patient` |
| `Patient?organization=https://other.org/fhir/Organization/123` | Absolute/external reference | `SEARCH_TERMS.Patient` |
| `ValueSet?url:below=http://acme.org/fhir/ValueSet` | Segment-aware URI `:below` | new resource type (`ValueSet` isn't currently offered — check whether adding it is in scope, or find a `Patient`/`Observation`-scoped URI parameter instead) |

Every row above should be re-verified the same way (a throwaway probe against the actual released package, not assumed from this list) before landing as a chip — this doc is a starting point, not a substitute for this bench's own stated verification bar.

## `Patient/$everything` and compartment search — bigger than a chip, needs its own design pass

**Correction to the first pass of this doc**: I'd originally filed this under "confirmed to live on a separate unmerged branch (`ignixa-fhir-server-adoption`), won't be in the next release." That was wrong — `ignixa-fhir` PR #365 ("Close the SQL compiler gaps blocking FHIR Server adoption," merged the day before this doc, `9c5b8de0`) landed the full unified-foundation work on `main`: `LowerOptions`, `PatientEverythingHandler` changes, `AccessConstraintApplier` (SMART access constraints), `CompartmentLoweringRule`, `KeysetContinuationToken`, `OffsetSpec`, `ResourceVisibility`, `ProjectionSpec`. PR #366 is a same-day follow-up fix specifically to `$everything`'s expansion-type resolution. Re-fetching `origin/main` confirms both are ancestors of the `7d7198f8` build this doc is based on.

**Verified live** (scratch project against the `0.6.39` build, `SearchCompiler.CompileAsync` called directly): both `Ignixa.Search.Expressions.PatientEverythingExpression` and `Ignixa.Search.Expressions.CompartmentSearchExpression` are public types with plain constructors (`new PatientEverythingExpression("example")`, `new CompartmentSearchExpression("Patient", "example")`), and passing either as the `operationExpression` argument compiles without error, no page-level `Failure`, `Plan`/`Sql` both present, for: single-patient `$everything`, `$everything` with a `filteredResourceTypes` set (`_type` filter), compartment search scoped to a specific resource type (`Patient/{id}/Observation`), and compartment wildcard search (`Patient/{id}/*`).

**Why this isn't just another `SearchQueryBuilder.tsx` chip**, unlike everything in the table above:

1. **It needs a different route shape.** The bench's only entry point today is `GET /api/search/{fhirVersion}/{resourceType}?query`, which always derives the compiled expression from the query string via `QueryParameterParser.Parse`. `$everything`/compartment search work by handing the compiler a *different* `Expression` object entirely, constructed from route segments the current route doesn't have (a patient/compartment id) — not something reachable through query-string parameters at all. Something like `Patient/{id}/$everything` and `{compartmentType}/{compartmentId}/{resourceType}` route additions, with `SearchFunctions.cs` branching on which shape it received to construct the right expression and pass it as `operationExpression`.
2. **I have not verified the emitted SQL is correct**, only that it compiles. The type-scoped compartment search (`Patient/{id}/Observation`) produced a plan with dozens of `CompartmentSource` CTEs unioned together across what looks like every resource type that can reference a Patient, not just Observation — from console output alone I could not confirm the result set is actually narrowed to Observation rather than the full compartment. This is two-day-old code; a careful read of `PlanExplainer` output (not just "no exception, `Plan` is non-null") is needed before treating it as ground truth for a UI.
3. **`$everything`'s own scope is large**: date filtering (`start`/`end`), `_since` incremental filtering, `_type` filtering, and an `includeReferencedResources` toggle (Practitioners/Organizations/Locations/Medications) are all real constructor parameters worth surfacing, not a single on/off chip.

Treat this as a follow-up design pass, not a same-session addition: what does the UI need to *ask* the user (a patient id? — the bench has no resource browser, so this may need a free-text id field or a Fakes-generated example id), what route/expression-construction shape on the backend, and a real correctness read of the plan before shipping it.

## Not yet checked

This pass covered the specific gaps `SearchQueryBuilder.tsx`'s own comments already flagged as excluded, plus `$everything`/compartment search once their existence was raised. It did **not** systematically re-probe the rest of the `ignixa-fhir` README capability table — expanded sort-key types (Token/Number/Quantity/Reference/Uri sort, not just String/Date), `_summary`/`_elements` beyond what's already offered, system-level multi-`_type` search (`GET /?_type=A,B&...`), `OffsetPage` paging, surrogate-id range partitioning, or reindex/search-parameter-hash gating. Given the $everything correction above, do not assume any of these are still stuck on an unmerged branch without checking `git log release/0.6.28..origin/main` fresh — re-scope this section once an actual release is cut and its changelog is known.

## How to act on this

1. Watch for a new `ignixa-fhir` `release/*` tag past `0.6.28`.
2. Bump `Ignixa.Search`/`Ignixa.Search.Sql`/`Ignixa.Specification`/`Ignixa.Abstractions`/`Ignixa.Serialization` together in `Directory.Packages.props`.
3. Apply the four breaking-change fixes above — the build will not succeed without them, so this is unambiguous.
4. Add the `KnownMiss` DTO/UI support.
5. Re-verify (don't just copy) the query table above against the real release, then add the ones that still hold up as new `SearchQueryBuilder.tsx` chips with matching `SearchFunctionsTests` regression tests, following the existing "confirmed live against the real compiler" convention in that file.
6. Treat `$everything`/compartment search as separate, larger follow-up work — see that section above for why. Don't fold it into the same pass as the chip additions.
