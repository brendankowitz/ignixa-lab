# Compartment Search & Patient/$everything Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add compartment search (`Patient/{id}/{resourceType}`, `Patient/{id}/*`) and `Patient/$everything` to the Search bench, on top of a package bump to `ignixa-fhir` 0.6.41.

**Architecture:** Both new search shapes reach the compiler through `SearchCompiler.CompileAsync`'s existing `operationExpression` parameter — no new I/O seam, no new compiler concept. The backend gains two new HTTP routes on `SearchFunctions.cs` that construct `CompartmentSearchExpression`/`PatientEverythingExpression` (both public, plain-constructor types in `Ignixa.Search`) instead of parsing a query string, then share the exact same compile-and-map path the existing type-search route already uses. The frontend generalizes its single `(fhirVersion, resourceType, query)` request shape into a small discriminated union so one hook/API function serves all three modes, and adds a "Mode" pill row whose options are filtered per the currently-selected resource type (only `Patient` gets all three modes; `Encounter` gets compartment but not `$everything`; `Observation` gets neither).

**Tech Stack:** .NET 10 Azure Functions (isolated worker), `Ignixa.Search`/`Ignixa.Search.Sql` 0.6.41(-alpha), xUnit + FluentAssertions, React 19 + TypeScript, Vite.

## Global Constraints

- Every `Ignixa.*` `PackageVersion` in `Directory.Packages.props` must move to `0.6.41` (stable packages) / `0.6.41-alpha` (`Ignixa.Search.Sql`) / `0.6.41-beta` (`Ignixa.TestScript`, `Ignixa.TestScript.FhirFakes`, `Ignixa.TestScript.Suites`) together — confirmed all six are published to nuget.org before this plan starts (verified 2026-07-28; re-check with `dotnet restore` if resuming this plan later, since `2026-07-27-search-bench-next-release-readiness.md` recorded a real prior incident of a release tag existing before all its packages were actually published).
- `dotnet build Ignixa.Lab.sln -c Release` must be zero warnings, zero errors at the end of every task (warnings-as-errors is on repo-wide).
- New backend tests follow the existing `SearchFunctionsTests.cs` convention: construct `SearchFunctions` directly (no HTTP host), assert on the returned `IActionResult`, comment any surprising/verified-live behavior the same way existing tests do ("Confirmed live against the real compiler: ...").
- New C# code: no comments narrating what changed: only non-obvious WHY, matching every file touched in this plan.
- Frontend: `npm run build && npm run lint` must be clean (oxlint) after every frontend task. The one pre-existing `HttpMessage.tsx` warning is unrelated and expected to remain.
- Do not touch `frontend/src/benches/search/searchSpans.ts`, `searchLineage.ts`, `sqlHighlight.ts`, `queryBuilder.ts`, or their `.test.ts` files — none of this plan's work changes span/lineage/highlight/query-string logic.

---

## Task 1: Bump to `Ignixa.*` 0.6.41 and fix the resulting breaking changes

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Search/InMemorySymbolResolver.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs:58`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs:59-66,81`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs:15,20`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs` (all `ToResponse(...)` call sites, one `EmittedSqlTrace` construction)

**Interfaces:**
- Produces: `SearchTraceMapper.ToResponse(SearchTrace trace, string requestedResourceType)` — the new two-argument signature every later task's `SearchFunctions.cs` code calls.

- [ ] **Step 1: Bump every `Ignixa.*` package version**

In `Directory.Packages.props`, change every `Ignixa.*` `PackageVersion` from `0.6.28`/`0.6.28-alpha`/`0.6.28-beta` to `0.6.41`/`0.6.41-alpha`/`0.6.41-beta` respectively (same suffix pattern each package already has — `Ignixa.Search.Sql` stays `-alpha`, `Ignixa.TestScript`/`Ignixa.TestScript.FhirFakes`/`Ignixa.TestScript.Suites` stay `-beta`, everything else has no suffix).

- [ ] **Step 2: Restore and confirm the packages are really there**

Run: `dotnet restore Ignixa.Lab.sln`
Expected: succeeds. If it fails with `NU1102` naming a package still stuck at an older version, stop — the release isn't fully published yet, matching the exact failure mode `2026-07-27-search-bench-next-release-readiness.md` already documented once. Do not proceed with a partial version bump.

- [ ] **Step 3: Build and observe the expected breaking-change errors**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore`
Expected: `FAILED`, with these errors (in `SearchTraceMapperTests.cs`, `SearchEngineFactory.cs`, `SearchFunctions.cs`):
- `CS0535: 'InMemorySymbolResolver' does not implement interface member 'ISymbolResolver.GetSystemIdAsync(string, CancellationToken)'` (and `GetQuantityCodeIdAsync`)
- `CS7036: There is no argument given that corresponds to the required parameter 'baseUriProvider' of 'ReferenceSearchValueParser.ReferenceSearchValueParser(IFhirSchemaProvider, IFhirBaseUriProvider)'`
- `CS1503: Argument 7: cannot convert from 'CancellationToken' to 'Expression?'`
- `CS8604: Possible null reference argument for parameter 'ResourceType'`
- `CS7036: There is no argument given that corresponds to the required parameter 'Ranges' of 'EmittedSqlTrace.EmittedSqlTrace(string, IReadOnlyList<EmittedSqlParameter>, IReadOnlyList<SqlTextRange>)'`

- [ ] **Step 4: Fix `InMemorySymbolResolver` — add the two new `ISymbolResolver` members**

Replace the whole file with:

```csharp
using System.Collections.Concurrent;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>
/// The Search bench has no live SQL Server (<c>Ignixa.DataLayer.SqlEntityFramework</c> is not referenced),
/// so this stands in for the compiler's only I/O seam. <see cref="ISymbolResolver"/> resolves search
/// parameters, resource types, token systems, and quantity codes to surrogate ids; the compiler only cares
/// whether an id is present, never its value, so any deterministic assignment produces real plan/SQL shape.
/// Ids are assigned sequentially on first sight from four independent registries.
///
/// Search parameters are keyed by their globally-unique <see cref="SearchParameterInfo.Url"/> (falling back
/// to <see cref="SearchParameterInfo.Code"/> if <c>Url</c> is null), ensuring the same parameter always
/// resolves to the same id within a request. Resource types, token systems, and quantity codes are each
/// keyed by their own value. Every lookup here always resolves (never null) — a null answer is what the
/// compiler reads as "no resource uses this" and lowers to <c>ParameterOutcome.KnownMiss</c>, which this
/// resolver deliberately never produces, since it has no real catalog to say a system or code is unknown.
///
/// A new instance is created per HTTP request, so ids are stable within a trace and need not persist across
/// requests. The <c>parameter</c> argument is assumed valid per the method contract (defensive null-checking
/// is not performed). Id assignment uses <see cref="short"/> to match the database surrogate id width; ids
/// wrap after 32,767 entries per instance, which is acceptable because a fresh resolver is created per
/// request and no single query should exhaust that ceiling.
/// </summary>
public sealed class InMemorySymbolResolver : ISymbolResolver
{
    private readonly ConcurrentDictionary<string, short> _searchParamIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, short> _resourceTypeIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _systemIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _quantityCodeIds = new(StringComparer.Ordinal);
    private int _nextSearchParamId;
    private int _nextResourceTypeId;
    private int _nextSystemId;
    private int _nextQuantityCodeId;

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
    {
        var key = parameter.Url?.ToString() ?? parameter.Code;
        // Cast wraps after 32,767 entries. Acceptable because a fresh resolver is created per request.
        var id = _searchParamIds.GetOrAdd(key, _ => (short)Interlocked.Increment(ref _nextSearchParamId));
        return Task.FromResult<short?>(id);
    }

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        // Cast wraps after 32,767 entries. Acceptable because a fresh resolver is created per request.
        var id = _resourceTypeIds.GetOrAdd(resourceType, _ => (short)Interlocked.Increment(ref _nextResourceTypeId));
        return Task.FromResult<short?>(id);
    }

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
    {
        var id = _systemIds.GetOrAdd(system, _ => Interlocked.Increment(ref _nextSystemId));
        return Task.FromResult<int?>(id);
    }

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
    {
        var id = _quantityCodeIds.GetOrAdd(code, _ => Interlocked.Increment(ref _nextQuantityCodeId));
        return Task.FromResult<int?>(id);
    }
}
```

- [ ] **Step 5: Fix `SearchEngineFactory` — `ReferenceSearchValueParser` needs a second argument**

In `backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs`, line 58, change:

```csharp
        var referenceParser = new ReferenceSearchValueParser(schema);
```

to:

```csharp
        var referenceParser = new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance);
```

`NullFhirBaseUriProvider` is in `Ignixa.Abstractions`, already imported at the top of this file (`using Ignixa.Abstractions;` is already present — no new `using` needed).

- [ ] **Step 6: Fix `SearchTraceMapper.ToResponse` — take the requested resource type as a fallback**

In `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs`, replace lines 15–20:

```csharp
    public static SearchTraceResponse ToResponse(SearchTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return new SearchTraceResponse(
            trace.ResourceType,
```

with:

```csharp
    public static SearchTraceResponse ToResponse(SearchTrace trace, string requestedResourceType)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return new SearchTraceResponse(
            trace.ResourceType ?? requestedResourceType,
```

`SearchTrace.ResourceType` is now `string?` at 0.6.41 (was non-nullable) — the caller already knows the resource type it asked for (the route parameter), so that's the natural fallback rather than an empty string.

- [ ] **Step 7: Fix `SearchFunctions.Trace` — the two call sites this touches**

In `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`, line 66, change the trailing positional `cancellationToken` argument to named (robust to any future parameter insertion before it, not just this one):

```csharp
                cancellationToken);
```

to:

```csharp
                cancellationToken: cancellationToken);
```

And line 81, update the now-two-argument call:

```csharp
        return new OkObjectResult(SearchTraceMapper.ToResponse(trace));
```

to:

```csharp
        return new OkObjectResult(SearchTraceMapper.ToResponse(trace, resourceType));
```

- [ ] **Step 8: Fix `SearchTraceMapperTests.cs` — update every `ToResponse` call site and the `EmittedSqlTrace` construction**

Every existing test in this file constructs its own `SearchTrace` whose first positional argument is a resource type string (all are `"Patient"` in the current file) — pass that same string as `ToResponse`'s second argument at every call site:
- `SearchTraceMapper.ToResponse(trace)` → `SearchTraceMapper.ToResponse(trace, "Patient")` (appears 5 times: `ToResponse_CompiledOutcome_MapsKindOnly`, `ToResponse_IgnoredOutcome_CarriesReasonAndSpan`, `ToResponse_FailedOutcome_CarriesStageAndMessage`, `ToResponse_PreservesCteParameterOrdinalAndKindData`, `ToResponse_ChainJoinRow_CarriesReferencedCteIndexesAndContributingOrdinals`, `ToResponse_NullPlanAndSql_MapToNull`)
- `SearchTraceMapper.ToResponse(new SearchTrace("Patient", [trace], Plan: null, Sql: null))` (in `ToResponse_DataType_MapsFromParameterTraceDirectly`) → `SearchTraceMapper.ToResponse(new SearchTrace("Patient", [trace], Plan: null, Sql: null), "Patient")`

In `ToResponse_PreservesCteParameterOrdinalAndKindData`, fix the `EmittedSqlTrace` construction (it gained a `Parameters` member before `Ranges`):

```csharp
        var sql = new EmittedSqlTrace("SELECT 1", [new SqlTextRange("cte0", SqlRangeKind.Cte, 0, 6)]);
```

to:

```csharp
        var sql = new EmittedSqlTrace("SELECT 1", Parameters: [], Ranges: [new SqlTextRange("cte0", SqlRangeKind.Cte, 0, 6)]);
```

- [ ] **Step 9: Build and test — expect fully green**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

Run: `dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity minimal`
Expected: all tests pass (575 at the time this plan was written — the exact number may have grown from other work landing on `main`; the point is zero failures, not a specific count).

- [ ] **Step 10: Commit**

```bash
git add Directory.Packages.props backend/src/Ignixa.Lab.Functions/Services/Search/InMemorySymbolResolver.cs backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs
git commit -m "Bump Ignixa.* to 0.6.41, fix the four resulting breaking changes"
```

---

## Task 2: Add `ParameterOutcome.KnownMiss` outcome support (backend + frontend)

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs:58-64`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs`
- Modify: `frontend/src/benches/search/searchTypes.ts:24`
- Modify: `frontend/src/benches/search/SearchBench.tsx:216-259`

**Interfaces:**
- Consumes: `ParameterOutcome.KnownMiss(string Reason, SourceSpan? Span)` from `Ignixa.Search.Parsing` (already imported via the existing `using Ignixa.Search.Parsing;` — no, check: `SearchTraceMapper.cs` imports `Ignixa.Search.Parsing` already at line 4).
- Produces: `ParameterOutcomeDto("KnownMiss", reason, null, span)` — a fourth wire value for the existing `Kind` string field, no DTO shape change.

- [ ] **Step 1: Write the failing backend test**

In `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs`, add (after `ToResponse_FailedOutcome_CarriesStageAndMessage`):

```csharp
    [Fact]
    public void ToResponse_KnownMissOutcome_CarriesReasonAndSpan()
    {
        // Confirmed live: a system-qualified token/quantity value the resolver reports as unknown compiles
        // to a predicate that can never match (rendered "1 = 0" in the emitted SQL) rather than failing the
        // request -- KnownMiss is how that becomes visible per-parameter instead of only as opaque SQL.
        var trace = new SearchTrace("Observation",
            [Trace(0, "code", "http://loinc.org|99999-9", new ParameterOutcome.KnownMiss("No resource uses the token system 'http://loinc.org'.", new SourceSpan(SourceOrigin.Value, 0, 24)))],
            Plan: null, Sql: null);

        var outcome = SearchTraceMapper.ToResponse(trace, "Observation").Parameters.Single().Outcome;

        outcome.Kind.Should().Be("KnownMiss");
        outcome.Reason.Should().Be("No resource uses the token system 'http://loinc.org'.");
        outcome.Stage.Should().BeNull();
        outcome.Span!.Start.Should().Be(0);
        outcome.Span.Length.Should().Be(24);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Ignixa.Lab.sln -c Release --filter "ToResponse_KnownMissOutcome_CarriesReasonAndSpan"`
Expected: `FAIL` — `SearchTraceMapper.ToOutcomeDto`'s switch has no `KnownMiss` arm, so it hits the `_ => throw new NotSupportedException(...)` default and the test errors rather than asserting a mismatch.

- [ ] **Step 3: Add the `KnownMiss` switch arm**

In `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs`, lines 58–64, change:

```csharp
    private static ParameterOutcomeDto ToOutcomeDto(ParameterOutcome outcome) => outcome switch
    {
        ParameterOutcome.Compiled => new ParameterOutcomeDto("Compiled", null, null, null),
        ParameterOutcome.Ignored ignored => new ParameterOutcomeDto("Ignored", ignored.Reason, null, ToSpanDto(ignored.Span)),
        ParameterOutcome.Failed failed => new ParameterOutcomeDto("Failed", failed.Message, failed.Stage.ToString(), ToSpanDto(failed.Span)),
        _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
    };
```

to:

```csharp
    private static ParameterOutcomeDto ToOutcomeDto(ParameterOutcome outcome) => outcome switch
    {
        ParameterOutcome.Compiled => new ParameterOutcomeDto("Compiled", null, null, null),
        // The query is well-formed and still runs -- it's just structurally incapable of returning a row
        // for this parameter (e.g. an unknown token system or quantity code), which is otherwise visible
        // only as a "1 = 0" buried in the emitted SQL.
        ParameterOutcome.KnownMiss knownMiss => new ParameterOutcomeDto("KnownMiss", knownMiss.Reason, null, ToSpanDto(knownMiss.Span)),
        ParameterOutcome.Ignored ignored => new ParameterOutcomeDto("Ignored", ignored.Reason, null, ToSpanDto(ignored.Span)),
        ParameterOutcome.Failed failed => new ParameterOutcomeDto("Failed", failed.Message, failed.Stage.ToString(), ToSpanDto(failed.Span)),
        _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
    };
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Ignixa.Lab.sln -c Release --filter "ToResponse_KnownMissOutcome_CarriesReasonAndSpan"`
Expected: `PASS`

Run full suite too: `dotnet test Ignixa.Lab.sln -c Release --verbosity minimal` — expect all green (no regressions).

- [ ] **Step 5: Add the frontend `OutcomeKind` literal**

In `frontend/src/benches/search/searchTypes.ts`, line 24, change:

```ts
export type OutcomeKind = 'Compiled' | 'Ignored' | 'Failed';
```

to:

```ts
export type OutcomeKind = 'Compiled' | 'Ignored' | 'Failed' | 'KnownMiss';
```

- [ ] **Step 6: Add the `KnownMiss` rendering state in `SearchBench.tsx`**

In `frontend/src/benches/search/SearchBench.tsx`, `SearchParamBlock` (lines 205–260), add a `knownMiss` flag alongside the existing `muted`/`failed` ones and a matching border color and inline note. Replace lines 216–218:

```tsx
  const selected = selection.ordinal === param.ordinal;
  const muted = param.outcome.kind === 'Ignored';
  const failed = param.outcome.kind === 'Failed';
```

with:

```tsx
  const selected = selection.ordinal === param.ordinal;
  const muted = param.outcome.kind === 'Ignored';
  const failed = param.outcome.kind === 'Failed';
  // Compiled, not dropped -- the query is well-formed and still runs, it's just structurally incapable of
  // returning a row for this parameter. Distinct from `muted`: not faded, since nothing was ignored here.
  const knownMiss = param.outcome.kind === 'KnownMiss';
```

Then in the same component, line 225, change the border color logic:

```tsx
        border: `1px ${muted ? 'dashed' : 'solid'} ${failed ? 'var(--fail-border)' : selected ? 'var(--accent-border)' : 'var(--border2)'}`,
```

to:

```tsx
        border: `1px ${muted ? 'dashed' : 'solid'} ${failed ? 'var(--fail-border)' : knownMiss ? 'var(--warn)' : selected ? 'var(--accent-border)' : 'var(--border2)'}`,
```

Then, lines 252–257, add the `knownMiss` note alongside the existing `muted`/`failed` ones:

```tsx
      {muted ? <span style={{ fontSize: 11, color: 'var(--text4)' }}>⚠ ignored — {param.outcome.reason}</span> : null}
      {failed ? (
        <span style={{ fontSize: 11, color: 'var(--fail)' }}>
          ✕ failed at {param.outcome.stage} — {param.outcome.reason}
        </span>
      ) : null}
```

to:

```tsx
      {muted ? <span style={{ fontSize: 11, color: 'var(--text4)' }}>⚠ ignored — {param.outcome.reason}</span> : null}
      {knownMiss ? (
        <span style={{ fontSize: 11, color: 'var(--warn)' }}>⚠ compiled — can never match — {param.outcome.reason}</span>
      ) : null}
      {failed ? (
        <span style={{ fontSize: 11, color: 'var(--fail)' }}>
          ✕ failed at {param.outcome.stage} — {param.outcome.reason}
        </span>
      ) : null}
```

- [ ] **Step 7: Build and lint the frontend**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds; lint clean (only the pre-existing unrelated `HttpMessage.tsx` warning).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs frontend/src/benches/search/searchTypes.ts frontend/src/benches/search/SearchBench.tsx
git commit -m "Add ParameterOutcome.KnownMiss outcome support"
```

---

## Task 3: Extract a shared compile-and-respond helper in `SearchFunctions.cs` (pure refactor)

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`

**Interfaces:**
- Produces: `private async Task<IActionResult> CompileAndRespondAsync(string fhirVersion, string resourceType, IReadOnlyList<QueryParameter> parameters, Expression? operationExpression, CancellationToken cancellationToken)` — Task 4 and Task 5's new route handlers call this directly.

This task changes no behavior — it only extracts the compile/map/error-handling logic `Trace` already has into a private helper, so the two new routes in Task 4/5 don't duplicate it. Every existing `SearchFunctionsTests.cs` test must keep passing unchanged, proving the refactor is behavior-preserving.

- [ ] **Step 1: Run the existing tests as a baseline**

Run: `dotnet test Ignixa.Lab.sln -c Release --filter "FullyQualifiedName~SearchFunctionsTests"`
Expected: all pass (this is the safety net for the refactor below).

- [ ] **Step 2: Extract the helper and rewrite `Trace` to use it**

Replace the whole `SearchFunctions.cs` file with:

```csharp
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Search-trace endpoints powering the Expression Benches "Search" bench. Given a FHIR search query, it
/// traces the query through parse → typed expression → lowered SQL plan → generated SQL via
/// <see cref="SearchCompiler"/>, returning the cross-referenced provenance as plain JSON (not a FHIR
/// resource — this is bench tooling, so no OperationOutcome wrapping). Supports the same FHIR version set
/// as <see cref="Services.FhirPath.SchemaProviderFactory"/> (STU3, R4, R4B, R5, R6) via
/// <see cref="SearchEngineFactory.Get"/>, which defaults an unrecognized value to R4 rather than rejecting
/// the request — same permissive fallback the rest of this app uses for FHIR version strings.
/// </summary>
public sealed class SearchFunctions(ILogger<SearchFunctions> logger, SearchEngineFactory engineFactory)
{
    [Function("SearchTrace")]
    public Task<IActionResult> Trace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{resourceType}")] HttpRequest request,
        string fhirVersion,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return Task.FromResult<IActionResult>(new BadRequestObjectResult(new { error = "A resource type is required." }));
        }

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;
        var parameters = new QueryParameterParser().Parse(rawQuery);

        return CompileAndRespondAsync(fhirVersion, resourceType, parameters, operationExpression: null, cancellationToken);
    }

    // SearchCompiler.CompileAsync never validates the top-level resourceType itself -- it only rejects an
    // unknown resource type when one appears as a chain/_has target (via SearchKeyBinder resolving a
    // ReferenceSearchParameter's target types). Given a resource type nothing recognizes, it happily
    // compiles a full plan and SQL against `dbo.Resource WHERE ResourceTypeId = @p0` for an ID that will
    // never match anything -- a confidently wrong 200, not a 400, for exactly the tool whose whole job is to
    // be trusted provenance. Reject it here instead, the same way an unknown search parameter is already
    // rejected per-parameter deeper in the pipeline. Shared by every route below (type/compartment/
    // $everything all pass the resource type they're compiling against).
    private async Task<IActionResult> CompileAndRespondAsync(
        string fhirVersion,
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        Expression? operationExpression,
        CancellationToken cancellationToken)
    {
        var engine = engineFactory.Get(fhirVersion);

        if (!engine.SearchParameters.TryGetSearchParameters(resourceType, out _))
        {
            return new BadRequestObjectResult(new { error = $"'{resourceType}' is not a supported FHIR resource type for {fhirVersion}." });
        }

        var resolver = new InMemorySymbolResolver();

        SearchTrace trace;
        try
        {
            trace = await SearchCompiler.CompileAsync(
                resourceType,
                parameters,
                engine.Builder,
                resolver,
                engine.Compartments,
                engine.SearchParameters,
                operationExpression,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SearchCompiler records Resolve/Lower/Emit failures as trace data rather than throwing; a throw
            // here is an unexpected shape (e.g. a malformed query the parser rejected outright). Surface it
            // as a 400 rather than a 500, consistent with the bench's plain-JSON error convention.
            logger.LogWarning(ex, "Search trace failed for {FhirVersion}/{ResourceType}", fhirVersion, resourceType);
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        return new OkObjectResult(SearchTraceMapper.ToResponse(trace, resourceType));
    }
}
```

Note the removed `rawQuery` from the `LogWarning` call in `CompileAndRespondAsync` — the raw query string isn't available inside the shared helper for the two new routes (compartment search still parses one, but `$everything` never has one), so the log message now names only `FhirVersion`/`ResourceType`, which is the information every call site actually has in common.

- [ ] **Step 3: Run the tests again to confirm the refactor is behavior-preserving**

Run: `dotnet test Ignixa.Lab.sln -c Release --filter "FullyQualifiedName~SearchFunctionsTests"`
Expected: identical pass count to Step 1 — same tests, same results, zero new tests needed (this task adds no new behavior).

Run the full suite too: `dotnet build Ignixa.Lab.sln -c Release --no-restore && dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity minimal`
Expected: 0 warnings, 0 errors, all tests green.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs
git commit -m "Extract shared compile-and-respond helper in SearchFunctions"
```

---

## Task 4: Add compartment search route + handler

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`

**Interfaces:**
- Consumes: `CompileAndRespondAsync` (Task 3), `Ignixa.Search.Expressions.CompartmentSearchExpression(string compartmentType, string compartmentId, ISet<string>? filteredResourceTypes = null)`, `Ignixa.Specification.ValueSets.Normative.CompartmentType` enum (`Device`, `Encounter`, `Patient`, `Practitioner`, `RelatedPerson`).
- Produces: `GET /api/search/{fhirVersion}/{compartmentType}/{compartmentId}/{resourceType}` — `resourceType` may be the literal string `*` for the wildcard (all types) case.

- [ ] **Step 1: Write the failing tests**

In `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`, add a helper for building the compartment-route request alongside the existing `BuildGetRequest`, and the test cases. Add after `CreateFunctions()`:

```csharp
    private static HttpRequest BuildCompartmentGetRequest(string queryString = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(queryString);
        return context.Request;
    }
```

Then add the test cases (place near the end of the file, before the closing brace):

```csharp
    [Fact]
    public async Task CompartmentTrace_ScopedToResourceType_CompilesToANarrowPlan_NotTheFullWildcardTraversal()
    {
        // Confirmed live: omitting the resource-type scope (or passing the true wildcard) produces the full
        // ~75-CTE compartment traversal. Scoping to one type must produce a small, correctly-narrowed plan --
        // an earlier manual probe that omitted the scope by mistake produced the huge plan and looked like a
        // library defect until the actual cause (a missing argument, not a compiler bug) was found. This test
        // pins the correct, narrow shape so that mistake can't silently regress back in.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
        // The full wildcard traversal is dozens of CTEs (one per (resourceType, search-param) pair in the
        // Patient compartment); a single-type scope is at most a handful. This is not an exact literal count
        // (which resource types have which search params can shift) -- it's an assertion that scoping did
        // something, not nothing.
        response.Plan!.Ctes.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task CompartmentTrace_WildcardResourceType_CompilesTheFullTraversal()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "example", "*", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
        // The scoped test above asserts "small"; this one asserts the wildcard case is genuinely the big
        // one, so the two tests together prove the scope argument actually does the narrowing.
        response.Plan!.Ctes.Count.Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task CompartmentTrace_CombinedWithAQueryStringParameter_NarrowsFurther()
    {
        // Confirmed live: compartment scoping and the existing query-string search terms are not mutually
        // exclusive -- a normal search parameter layers on top of the compartment scope.
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest("code=1234-5"), "R4", "Patient", "example", "Observation", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key == "code").Which.Outcome.Kind.Should().Be("Compiled");
    }

    [Fact]
    public async Task CompartmentTrace_UnknownCompartmentType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "NotACompartmentType", "example", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Subject.Value.Should().BeEquivalentTo(new { error = "'NotACompartmentType' is not a valid FHIR compartment type." });
    }

    [Fact]
    public async Task CompartmentTrace_UnknownMemberResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "example", "TotallyBogusResource", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CompartmentTrace_EmptyCompartmentId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.CompartmentTrace(BuildCompartmentGetRequest(), "R4", "Patient", "  ", "Observation", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore`
Expected: `FAILED` — `SearchFunctions` has no `CompartmentTrace` method yet.

- [ ] **Step 3: Add the `CompartmentTrace` route handler**

In `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`, add `using Ignixa.Specification.ValueSets.Normative;` to the `using` block at the top, then add the new method to the `SearchFunctions` class, right after `Trace`:

```csharp
    [Function("SearchCompartmentTrace")]
    public async Task<IActionResult> CompartmentTrace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/{compartmentType}/{compartmentId}/{resourceType}")] HttpRequest request,
        string fhirVersion,
        string compartmentType,
        string compartmentId,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(compartmentId))
        {
            return new BadRequestObjectResult(new { error = "A compartment id is required." });
        }

        if (!Enum.TryParse<CompartmentType>(compartmentType, ignoreCase: true, out _))
        {
            return new BadRequestObjectResult(new { error = $"'{compartmentType}' is not a valid FHIR compartment type." });
        }

        // "*" (Patient/{id}/*) means "every resource type in the compartment" -- null filteredResourceTypes
        // is what CompartmentSearchExpression reads as that wildcard. A specific type narrows to just it.
        // The resource type passed to the compiler for a wildcard is the compartment root itself (there is
        // no single member type to name); for a scoped search it's the actual member type being searched.
        var wildcard = resourceType == "*";
        var filteredResourceTypes = wildcard ? null : new HashSet<string> { resourceType };
        var compileResourceType = wildcard ? compartmentType : resourceType;

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;
        var parameters = new QueryParameterParser().Parse(rawQuery);

        var operationExpression = new CompartmentSearchExpression(compartmentType, compartmentId, filteredResourceTypes);

        return await CompileAndRespondAsync(fhirVersion, compileResourceType, parameters, operationExpression, cancellationToken);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln -c Release --filter "FullyQualifiedName~SearchFunctionsTests"`
Expected: all pass, including the 6 new compartment tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore && dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity minimal`
Expected: 0 warnings, 0 errors, all green.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs
git commit -m "Add compartment search route and handler"
```

---

## Task 5: Add `Patient/$everything` route + handler

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`

**Interfaces:**
- Consumes: `CompileAndRespondAsync` (Task 3), `Ignixa.Search.Expressions.PatientEverythingExpression(string patientId, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, DateTimeOffset? sinceDate = null, ISet<string>? filteredResourceTypes = null, bool includeReferencedResources = true)`.
- Produces: `GET /api/search/{fhirVersion}/Patient/{patientId}/$everything?[_type=A,B&_since=...&start=...&end=...&includeReferencedResources=true|false]`.

- [ ] **Step 1: Write the failing tests**

In `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`, add (after the compartment tests from Task 4):

```csharp
    [Fact]
    public async Task EverythingTrace_Bare_ReturnsEmptyParametersWithNonNullPlanAndSql()
    {
        // The whole thing is one expression, not parameter-driven -- there is nothing for QueryParameterParser
        // to have parsed, so Parameters comes back empty. This is the shape the frontend's empty-parameters
        // state depends on; pin it so that dependency doesn't silently break.
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildCompartmentGetRequest(), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Parameters.Should().BeEmpty();
        response.Plan.Should().NotBeNull();
        response.Sql.Should().NotBeNull();
    }

    [Fact]
    public async Task EverythingTrace_WithTypeFilter_CompilesToASmallerPlanThanBare()
    {
        // Confirmed live: a _type filter collapses the plan from ~800 lines to a handful of CTEs and drops
        // ReferencedTypeExpansion entirely (out of scope once _type is set).
        var functions = CreateFunctions();

        var bare = await functions.EverythingTrace(BuildCompartmentGetRequest(), "R4", "example", CancellationToken.None);
        var filtered = await functions.EverythingTrace(BuildCompartmentGetRequest("_type=Observation"), "R4", "example", CancellationToken.None);

        var bareResponse = bare.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<SearchTraceResponse>().Subject;
        var filteredResponse = filtered.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<SearchTraceResponse>().Subject;
        filteredResponse.Failure.Should().BeNull();
        filteredResponse.Plan!.Ctes.Count.Should().BeLessThan(bareResponse.Plan!.Ctes.Count);
    }

    [Fact]
    public async Task EverythingTrace_WithSince_CompilesCleanly()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildCompartmentGetRequest("_since=2026-01-01T00:00:00Z"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Sql!.Sql.Should().Contain("dbo.Transactions");
    }

    [Fact]
    public async Task EverythingTrace_WithStartAndEnd_CompilesCleanly()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildCompartmentGetRequest("start=2020-01-01T00:00:00Z&end=2026-01-01T00:00:00Z"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan.Should().NotBeNull();
    }

    [Fact]
    public async Task EverythingTrace_IncludeReferencedResourcesFalse_OmitsReferencedTypeExpansionFromExplain()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildCompartmentGetRequest("includeReferencedResources=false"), "R4", "example", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.Failure.Should().BeNull();
        response.Plan!.Explain.Should().NotContain("ReferencedTypeExpansion");
    }

    [Fact]
    public async Task EverythingTrace_EmptyPatientId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildCompartmentGetRequest(), "R4", "  ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task EverythingTrace_MalformedSince_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.EverythingTrace(BuildCompartmentGetRequest("_since=not-a-date"), "R4", "example", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore`
Expected: `FAILED` — `SearchFunctions` has no `EverythingTrace` method yet.

- [ ] **Step 3: Add the `EverythingTrace` route handler**

In `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`, add the new method after `CompartmentTrace`:

```csharp
    [Function("SearchEverythingTrace")]
    public async Task<IActionResult> EverythingTrace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{fhirVersion}/Patient/{patientId}/$everything")] HttpRequest request,
        string fhirVersion,
        string patientId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return new BadRequestObjectResult(new { error = "A patient id is required." });
        }

        HashSet<string>? filteredResourceTypes = null;
        if (request.Query.TryGetValue("_type", out var typeValues) && !string.IsNullOrWhiteSpace(typeValues.ToString()))
        {
            filteredResourceTypes = typeValues.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet();
        }

        if (!TryParseOptionalDate(request, "_since", out var sinceDate, out var sinceError))
        {
            return new BadRequestObjectResult(new { error = sinceError });
        }

        if (!TryParseOptionalDate(request, "start", out var startDate, out var startError))
        {
            return new BadRequestObjectResult(new { error = startError });
        }

        if (!TryParseOptionalDate(request, "end", out var endDate, out var endError))
        {
            return new BadRequestObjectResult(new { error = endError });
        }

        var includeReferencedResources = true;
        if (request.Query.TryGetValue("includeReferencedResources", out var includeValues) && !string.IsNullOrWhiteSpace(includeValues.ToString()))
        {
            if (!bool.TryParse(includeValues.ToString(), out includeReferencedResources))
            {
                return new BadRequestObjectResult(new { error = $"'includeReferencedResources' value '{includeValues}' is not 'true' or 'false'." });
            }
        }

        var operationExpression = new PatientEverythingExpression(patientId, startDate, endDate, sinceDate, filteredResourceTypes, includeReferencedResources);

        // $everything isn't parameter-driven -- there's no query string for QueryParameterParser to parse,
        // every option above became a typed constructor argument on the expression instead. The resource
        // type the compiler compiles against is always "Patient", the operation's anchor type; this route
        // only ever accepts Patient (there is no EncounterEverythingExpression or similar in the library).
        return await CompileAndRespondAsync(fhirVersion, "Patient", parameters: [], operationExpression, cancellationToken);
    }

    private static bool TryParseOptionalDate(HttpRequest request, string queryKey, out DateTimeOffset? value, out string? error)
    {
        value = null;
        error = null;

        if (!request.Query.TryGetValue(queryKey, out var rawValues) || string.IsNullOrWhiteSpace(rawValues.ToString()))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(rawValues.ToString(), out var parsed))
        {
            error = $"'{queryKey}' value '{rawValues}' is not a valid date/time.";
            return false;
        }

        value = parsed;
        return true;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln -c Release --filter "FullyQualifiedName~SearchFunctionsTests"`
Expected: all pass, including the 7 new `$everything` tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore && dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity minimal`
Expected: 0 warnings, 0 errors, all green. This is the last backend task — confirm the full test count is a clean, final green before moving to frontend work.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs
git commit -m "Add Patient/\$everything route and handler"
```

---

## Task 6: Frontend — generalize the request shape (types, API client, hook), rewire `SearchBench.tsx` to keep working exactly as before

**Files:**
- Modify: `frontend/src/benches/search/searchTypes.ts`
- Modify: `frontend/src/benches/search/searchApi.ts`
- Modify: `frontend/src/benches/search/useSearchTrace.ts`
- Modify: `frontend/src/benches/search/SearchBench.tsx` (the `useSearchTrace` call site only, lines 424–430)

**Interfaces:**
- Produces: `SearchRequest` (a discriminated union over `mode: 'type' | 'compartment' | 'everything'`), `runSearch(request: SearchRequest, signal: AbortSignal): Promise<SearchTraceResponse>`, `useSearchTrace(request: SearchRequest | null): SearchTraceState` — Task 7 and Task 8 build the `'compartment'`/`'everything'` UI on top of this.

This task changes no visible behavior yet — type search must work identically to before, just constructed as a `{ mode: 'type', ... }` request under the hood. It's the seam Task 7/8 build on.

- [ ] **Step 1: Add `SearchMode`, `SearchRequest`, and the per-resource-type mode availability lookup to `searchTypes.ts`**

Append to the end of `frontend/src/benches/search/searchTypes.ts` (after the existing `DEFAULT_QUERY` line):

```ts
export type SearchMode = 'type' | 'compartment' | 'everything';

/** Which resource types can be a compartment root, and which support $everything -- neither is "any of the
 * bench's 3 resource types": only Patient and Encounter are among FHIR's 5 compartment types (Device,
 * Encounter, Patient, Practitioner, RelatedPerson), and $everything is Patient-only in Ignixa.Search --
 * there is no EncounterEverythingExpression or similar. Observation is a compartment member type, never a
 * root, so it never gets either extra mode. */
const SEARCH_MODES_BY_RESOURCE_TYPE: Record<ResourceType, SearchMode[]> = {
  Patient: ['type', 'compartment', 'everything'],
  Encounter: ['type', 'compartment'],
  Observation: ['type'],
};

export function searchModesFor(resourceType: ResourceType): SearchMode[] {
  return SEARCH_MODES_BY_RESOURCE_TYPE[resourceType];
}

/** The other resource types available as a compartment's member type when `resourceType` is the root, plus
 * the wildcard. Excludes the root itself -- searching a Patient's own compartment for other Patients isn't
 * a shape this bench models. */
export function compartmentMemberOptions(root: ResourceType): (ResourceType | '*')[] {
  return [...RESOURCE_TYPES.filter((type) => type !== root), '*'];
}

/** The resource types offerable as $everything's `_type` filter -- every bench resource type except Patient
 * itself, which is always the anchor and is never filtered out by `_type`. */
export function everythingTypeFilterOptions(): ResourceType[] {
  return RESOURCE_TYPES.filter((type) => type !== 'Patient');
}

export type CompartmentMemberType = ResourceType | '*';

export type SearchRequest =
  | { mode: 'type'; fhirVersion: FhirVersion; resourceType: ResourceType; query: string }
  | {
      mode: 'compartment';
      fhirVersion: FhirVersion;
      compartmentType: ResourceType;
      compartmentId: string;
      memberType: CompartmentMemberType;
      query: string;
    }
  | {
      mode: 'everything';
      fhirVersion: FhirVersion;
      patientId: string;
      typeFilter: ResourceType[];
      since: string;
      start: string;
      end: string;
      includeReferencedResources: boolean;
    };
```

- [ ] **Step 2: Rewrite `searchApi.ts` to build a URL per `SearchRequest` mode**

Replace the whole file with:

```ts
import type { SearchRequest, SearchTraceResponse } from './searchTypes';

function apiBaseUrl(): string {
  return (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
}

function buildUrl(request: SearchRequest): string {
  const base = apiBaseUrl();
  switch (request.mode) {
    case 'type': {
      const suffix = request.query.trim() ? `?${request.query.trim()}` : '';
      return `${base}/api/search/${encodeURIComponent(request.fhirVersion)}/${encodeURIComponent(request.resourceType)}${suffix}`;
    }
    case 'compartment': {
      const suffix = request.query.trim() ? `?${request.query.trim()}` : '';
      return `${base}/api/search/${encodeURIComponent(request.fhirVersion)}/${encodeURIComponent(request.compartmentType)}/${encodeURIComponent(request.compartmentId)}/${encodeURIComponent(request.memberType)}${suffix}`;
    }
    case 'everything': {
      const params = new URLSearchParams();
      if (request.typeFilter.length > 0) {
        params.set('_type', request.typeFilter.join(','));
      }
      if (request.since.trim()) {
        params.set('_since', request.since.trim());
      }
      if (request.start.trim()) {
        params.set('start', request.start.trim());
      }
      if (request.end.trim()) {
        params.set('end', request.end.trim());
      }
      if (!request.includeReferencedResources) {
        params.set('includeReferencedResources', 'false');
      }
      const suffix = params.toString() ? `?${params.toString()}` : '';
      // "$everything" is a literal route segment, not encoded -- the backend route defines it the same way.
      return `${base}/api/search/${encodeURIComponent(request.fhirVersion)}/Patient/${encodeURIComponent(request.patientId)}/$everything${suffix}`;
    }
  }
}

/** GETs the search-trace endpoint for a `SearchRequest` (type search, compartment search, or $everything).
 * Throws on non-2xx or a `{ error }` body (the backend reports bad requests that way), or on a network/abort
 * error. */
export async function runSearch(request: SearchRequest, signal: AbortSignal): Promise<SearchTraceResponse> {
  const response = await fetch(buildUrl(request), { method: 'GET', signal });

  const text = await response.text();
  let json: unknown;
  try {
    json = JSON.parse(text);
  } catch {
    throw new Error(`Request failed with status ${response.status} ${response.statusText}`);
  }

  if (!response.ok) {
    const errorBody = json as { error?: string };
    throw new Error(errorBody?.error ?? `Request failed with status ${response.status}`);
  }
  return json as SearchTraceResponse;
}
```

- [ ] **Step 3: Rewrite `useSearchTrace.ts` to accept a `SearchRequest | null`**

Replace the whole file with:

```ts
import { useEffect, useRef, useState } from 'react';
import { runSearch } from './searchApi';
import type { SearchRequest, SearchTraceResponse } from './searchTypes';
import { getErrorMessage } from '../shared/errorMessage';

const DEBOUNCE_MS = 450;

export interface SearchTraceState {
  result: SearchTraceResponse | null;
  error: string | null;
  isLoading: boolean;
}

const EMPTY: SearchTraceState = { result: null, error: null, isLoading: false };

/** Debounced, abortable search-trace runner: re-GETs ~450ms after `request` changes, cancelling any
 * still-in-flight request first. `request === null` means "not enough input to run yet" (e.g. compartment/
 * $everything mode with no id typed) -- clears to the empty state without calling the API. */
export function useSearchTrace(request: SearchRequest | null): SearchTraceState {
  const [state, setState] = useState<SearchTraceState>(EMPTY);
  const abortRef = useRef<AbortController | null>(null);
  const requestKey = request ? JSON.stringify(request) : null;

  useEffect(() => {
    if (request === null) {
      abortRef.current?.abort();
      setState(EMPTY);
      return undefined;
    }

    const timer = setTimeout(() => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      setState((prev) => ({ ...prev, isLoading: true }));

      runSearch(request, controller.signal)
        .then((result) => setState({ result, error: null, isLoading: false }))
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === 'AbortError') {
            return;
          }
          setState({ result: null, error: getErrorMessage(error), isLoading: false });
        });
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
    // `requestKey` is the stable, content-based re-run trigger; `request` itself is a fresh object every
    // render even when unchanged, so depending on it directly would re-run the effect every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey]);

  return state;
}
```

- [ ] **Step 4: Update `SearchBench.tsx`'s `useSearchTrace` call site to construct a `'type'` request**

In `frontend/src/benches/search/SearchBench.tsx`, line 430, change:

```tsx
  const { result, error, isLoading } = useSearchTrace(fhirVersion, resourceType, query);
```

to:

```tsx
  const { result, error, isLoading } = useSearchTrace({ mode: 'type', fhirVersion, resourceType, query });
```

- [ ] **Step 5: Build and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds; lint clean (only the pre-existing unrelated `HttpMessage.tsx` warning).

- [ ] **Step 6: Manual smoke check — type search still works exactly as before**

Start the backend (`func start` from `backend/src/Ignixa.Lab.Functions`) and frontend (`npm run dev` from `frontend`), open the Search bench, confirm the default query still traces successfully (parameters/plan/SQL all populate, click-to-trace lineage still works). This is the regression check for this task — the refactor must be invisible to a user of type search.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/benches/search/searchTypes.ts frontend/src/benches/search/searchApi.ts frontend/src/benches/search/useSearchTrace.ts frontend/src/benches/search/SearchBench.tsx
git commit -m "Generalize the Search bench request shape into a SearchRequest union"
```

---

## Task 7: Frontend — mode pills + compartment mode UI

**Files:**
- Modify: `frontend/src/benches/search/SearchBench.tsx`

**Interfaces:**
- Consumes: `searchModesFor`, `compartmentMemberOptions`, `SearchMode`, `CompartmentMemberType`, `SearchRequest` (all from Task 6's `searchTypes.ts`).

- [ ] **Step 1: Add mode/id/member-type state and the mode-aware `SearchRequest` construction**

In `frontend/src/benches/search/SearchBench.tsx`, update the imports (line 22–34) to add the new types/functions:

```tsx
import {
  compartmentMemberOptions,
  DEFAULT_FHIR_VERSION,
  DEFAULT_QUERY,
  DEFAULT_RESOURCE_TYPE,
  FHIR_VERSIONS,
  RESOURCE_TYPES,
  searchModesFor,
  type CompartmentMemberType,
  type FhirVersion,
  type ParameterTrace,
  type PlanExplainRow,
  type QueryPlan,
  type ResourceType,
  type SearchMode,
  type SearchRequest,
  type SqlTextRange,
} from './searchTypes';
```

`everythingTypeFilterOptions` is deliberately not imported here — it has no consumer until Task 8 adds the `$everything` controls, and an unused import fails this task's own `npm run lint` gate (Step 4). Task 8 adds it to this same import list when it adds the code that calls it.

Then, in the `SearchBench` component body (after the existing `resourceType`/`query` state, lines 425–426), add:

```tsx
  const [searchMode, setSearchMode] = useState<SearchMode>('type');
  const [compartmentId, setCompartmentId] = useState('');
  const [memberType, setMemberType] = useState<CompartmentMemberType>('*');
```

Add a `SEARCH_MODE_LABELS` lookup near the top of the file, alongside the other `*_ITEMS`/`*_LABELS` constants (after `SQL_TAB_ITEMS`, before `KIND_CHIP_COLORS`):

```tsx
const SEARCH_MODE_LABELS: Record<SearchMode, string> = {
  type: 'Type search',
  compartment: 'Compartment',
  everything: '$everything',
};
```

Add a resource-type change handler that resets `searchMode` back to `'type'` whenever the newly-selected type doesn't support the currently-active mode (e.g. switching to Observation while in Compartment mode). Add this right after the new state declarations:

```tsx
  const availableModes = searchModesFor(resourceType);
  const handleResourceTypeChange = (nextType: ResourceType) => {
    setResourceType(nextType);
    if (!searchModesFor(nextType).includes(searchMode)) {
      setSearchMode('type');
    }
  };
```

Build the `SearchRequest` passed to `useSearchTrace`, replacing the Task 6 line:

```tsx
  const { result, error, isLoading } = useSearchTrace({ mode: 'type', fhirVersion, resourceType, query });
```

with:

```tsx
  const searchRequest: SearchRequest | null = (() => {
    if (searchMode === 'type') {
      return { mode: 'type', fhirVersion, resourceType, query };
    }
    if (searchMode === 'compartment') {
      if (!compartmentId.trim()) {
        return null;
      }
      return { mode: 'compartment', fhirVersion, compartmentType: resourceType, compartmentId: compartmentId.trim(), memberType, query };
    }
    // 'everything' is handled by Task 8; until then this branch is unreachable because 'everything' can't
    // be selected without Task 8's UI, but the type checker still needs every SearchMode covered.
    return null;
  })();

  const { result, error, isLoading } = useSearchTrace(searchRequest);
```

- [ ] **Step 2: Add the Mode pill row and the compartment id/member-type controls**

In the query-config `Card` (lines 482–553), right after the existing FHIR version / Resource type pill row (lines 483–489), add a Mode row that only shows when the current resource type supports more than just `'type'`:

```tsx
        {availableModes.length > 1 ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <span style={sectionLabelStyle}>Mode</span>
            <Pills
              items={availableModes.map((mode) => ({ id: mode, label: SEARCH_MODE_LABELS[mode] }))}
              activeId={searchMode}
              onChange={setSearchMode}
            />
          </div>
        ) : null}
```

Place this immediately after the existing FHIR version / Resource type `<div>` block (which ends right before the `Search query` section at line 491).

Update the Resource type `Pills`'s `onChange` (line 488) from `setResourceType` to the new handler:

```tsx
          <Pills items={RESOURCE_TYPE_ITEMS} activeId={resourceType} onChange={setResourceType} />
```

to:

```tsx
          <Pills items={RESOURCE_TYPE_ITEMS} activeId={resourceType} onChange={handleResourceTypeChange} />
```

Add the compartment id field and member-type picker, shown only in compartment mode. Insert this right after the Mode row block just added:

```tsx
        {searchMode === 'compartment' ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <span style={sectionLabelStyle}>{resourceType} id</span>
            <input
              value={compartmentId}
              onChange={(event) => setCompartmentId(event.target.value)}
              placeholder="example"
              spellCheck={false}
              style={{
                fontFamily: monoFont,
                fontSize: 12.5,
                padding: '6px 10px',
                borderRadius: 6,
                border: '1px solid var(--border2)',
                background: 'var(--code)',
                color: 'var(--text)',
                width: 140,
              }}
            />
            <span
              onClick={() => setCompartmentId('example')}
              style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--accent)', cursor: 'pointer' }}
            >
              example
            </span>
            <div style={{ width: 1, height: 18, background: 'var(--border2)' }} />
            <span style={sectionLabelStyle}>within compartment, search</span>
            <Pills
              items={compartmentMemberOptions(resourceType).map((type) => ({ id: type, label: type === '*' ? '* all types' : type }))}
              activeId={memberType}
              onChange={setMemberType}
            />
          </div>
        ) : null}
```

- [ ] **Step 3: Update the `GET /...` breadcrumb to reflect compartment mode**

In the `Search query` section (lines 491–536), the breadcrumb `<span>` currently always reads `GET /{resourceType}?` (lines 503–514). Replace lines 503–514:

```tsx
            <span
              style={{
                fontFamily: monoFont,
                fontSize: 12.5,
                color: 'var(--text3)',
                padding: '11px 0 11px 13px',
                whiteSpace: 'nowrap',
                userSelect: 'none',
              }}
            >
              GET /{resourceType}?
            </span>
```

with:

```tsx
            <span
              style={{
                fontFamily: monoFont,
                fontSize: 12.5,
                color: 'var(--text3)',
                padding: '11px 0 11px 13px',
                whiteSpace: 'nowrap',
                userSelect: 'none',
              }}
            >
              {searchMode === 'compartment'
                ? `GET /${resourceType}/${compartmentId.trim() || '{id}'}/${memberType}?`
                : `GET /${resourceType}?`}
            </span>
```

(The `everything` case is added in Task 8, following the same pattern — this task only needs to not break for `'everything'` mode, which isn't reachable yet since Task 8 hasn't added its trigger UI.)

- [ ] **Step 4: Build and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds; lint clean.

- [ ] **Step 5: Manual verification**

With both servers running:
1. Select `Patient` as resource type — confirm a "Mode" pill row appears with `Type search`/`Compartment`/`$everything` (the `$everything` pill will do nothing useful until Task 8, that's expected).
2. Click `Compartment` — confirm the id field and "within compartment, search" pills appear, and the breadcrumb updates to `GET /Patient/{id}/...`.
3. Type `example` in the id field, pick `Observation` as the member type — confirm a trace comes back with a small plan (not the huge wildcard one).
4. Pick `* all types` — confirm the plan grows substantially (the wildcard traversal).
5. Select `Encounter` — confirm the Mode row still shows `Type search`/`Compartment` but not `$everything`.
6. Select `Observation` — confirm no Mode row appears at all (falls back silently to type search).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/benches/search/SearchBench.tsx
git commit -m "Add compartment search mode UI"
```

---

## Task 8: Frontend — `$everything` mode UI + empty-parameters state

**Files:**
- Modify: `frontend/src/benches/search/SearchBench.tsx`

**Interfaces:**
- Consumes: `everythingTypeFilterOptions` (Task 6/7's `searchTypes.ts`).

- [ ] **Step 1: Add the `everythingTypeFilterOptions` import**

In `frontend/src/benches/search/SearchBench.tsx`, add `everythingTypeFilterOptions` to the existing import from `./searchTypes` (the one Task 7 set up), alphabetically between `DEFAULT_RESOURCE_TYPE` and `FHIR_VERSIONS`:

```tsx
import {
  compartmentMemberOptions,
  DEFAULT_FHIR_VERSION,
  DEFAULT_QUERY,
  DEFAULT_RESOURCE_TYPE,
  everythingTypeFilterOptions,
  FHIR_VERSIONS,
  RESOURCE_TYPES,
  searchModesFor,
  type CompartmentMemberType,
  type FhirVersion,
  type ParameterTrace,
  type PlanExplainRow,
  type QueryPlan,
  type ResourceType,
  type SearchMode,
  type SearchRequest,
  type SqlTextRange,
} from './searchTypes';
```

- [ ] **Step 2: Add `$everything` state**

In `frontend/src/benches/search/SearchBench.tsx`, after the `memberType` state added in Task 7, add:

```tsx
  const [everythingId, setEverythingId] = useState('');
  const [typeFilter, setTypeFilter] = useState<ResourceType[]>([]);
  const [since, setSince] = useState('');
  const [everythingStart, setEverythingStart] = useState('');
  const [everythingEnd, setEverythingEnd] = useState('');
  const [includeReferencedResources, setIncludeReferencedResources] = useState(true);
```

(`everythingStart`/`everythingEnd`, not `start`/`end`, to avoid any confusion with unrelated identifiers elsewhere in the file.)

- [ ] **Step 3: Wire the `'everything'` branch of `searchRequest`**

Replace the placeholder branch Task 7 left:

```tsx
    // 'everything' is handled by Task 8; until then this branch is unreachable because 'everything' can't
    // be selected without Task 8's UI, but the type checker still needs every SearchMode covered.
    return null;
```

with:

```tsx
    if (!everythingId.trim()) {
      return null;
    }
    return {
      mode: 'everything',
      fhirVersion,
      patientId: everythingId.trim(),
      typeFilter,
      since,
      start: everythingStart,
      end: everythingEnd,
      includeReferencedResources,
    };
```

- [ ] **Step 4: Add the `$everything` controls**

Right after the compartment-mode block added in Task 7 (the `{searchMode === 'compartment' ? (...) : null}` block), add:

```tsx
        {searchMode === 'everything' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span style={sectionLabelStyle}>Patient id</span>
              <input
                value={everythingId}
                onChange={(event) => setEverythingId(event.target.value)}
                placeholder="example"
                spellCheck={false}
                style={{
                  fontFamily: monoFont,
                  fontSize: 12.5,
                  padding: '6px 10px',
                  borderRadius: 6,
                  border: '1px solid var(--border2)',
                  background: 'var(--code)',
                  color: 'var(--text)',
                  width: 140,
                }}
              />
              <span
                onClick={() => setEverythingId('example')}
                style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--accent)', cursor: 'pointer' }}
              >
                example
              </span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span style={sectionLabelStyle}>_type</span>
              {everythingTypeFilterOptions().map((type) => {
                const active = typeFilter.includes(type);
                return (
                  <span
                    key={type}
                    onClick={() =>
                      setTypeFilter((prev) => (active ? prev.filter((t) => t !== type) : [...prev, type]))
                    }
                    style={{
                      ...chipStyle(active ? 'var(--chip-vio-bg)' : 'var(--chip-gray-bg)', active ? 'var(--chip-vio-fg)' : 'var(--chip-gray2-fg)'),
                      cursor: 'pointer',
                    }}
                  >
                    {type}
                  </span>
                );
              })}
              {typeFilter.length === 0 ? <span style={{ fontSize: 11, color: 'var(--text4)' }}>(all types)</span> : null}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span style={sectionLabelStyle}>_since</span>
              <input
                value={since}
                onChange={(event) => setSince(event.target.value)}
                placeholder="2026-01-01T00:00:00Z"
                spellCheck={false}
                style={{ fontFamily: monoFont, fontSize: 11.5, padding: '5px 8px', borderRadius: 6, border: '1px solid var(--border2)', background: 'var(--code)', color: 'var(--text)', width: 190 }}
              />
              <span style={sectionLabelStyle}>start</span>
              <input
                value={everythingStart}
                onChange={(event) => setEverythingStart(event.target.value)}
                placeholder="2020-01-01T00:00:00Z"
                spellCheck={false}
                style={{ fontFamily: monoFont, fontSize: 11.5, padding: '5px 8px', borderRadius: 6, border: '1px solid var(--border2)', background: 'var(--code)', color: 'var(--text)', width: 190 }}
              />
              <span style={sectionLabelStyle}>end</span>
              <input
                value={everythingEnd}
                onChange={(event) => setEverythingEnd(event.target.value)}
                placeholder="2026-01-01T00:00:00Z"
                spellCheck={false}
                style={{ fontFamily: monoFont, fontSize: 11.5, padding: '5px 8px', borderRadius: 6, border: '1px solid var(--border2)', background: 'var(--code)', color: 'var(--text)', width: 190 }}
              />
            </div>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 11.5, color: 'var(--text3)', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={includeReferencedResources}
                onChange={(event) => setIncludeReferencedResources(event.target.checked)}
              />
              include referenced resources (Practitioner / Organization / Location / Medication)
            </label>
          </div>
        ) : null}
```

- [ ] **Step 5: Update the `GET /...` breadcrumb for `$everything`**

Extend the breadcrumb logic added in Task 7. Replace:

```tsx
              {searchMode === 'compartment'
                ? `GET /${resourceType}/${compartmentId.trim() || '{id}'}/${memberType}?`
                : `GET /${resourceType}?`}
```

with:

```tsx
              {searchMode === 'compartment'
                ? `GET /${resourceType}/${compartmentId.trim() || '{id}'}/${memberType}?`
                : searchMode === 'everything'
                  ? `GET /Patient/${everythingId.trim() || '{id}'}/$everything?`
                  : `GET /${resourceType}?`}
```

- [ ] **Step 6: Hide the query-string `Builder` in `$everything` mode**

`$everything` isn't parameter-driven — the `SearchQueryBuilder` chips (search terms, `_include`, sort, `_summary`) don't apply. In the `Builder` section (lines 538–541), change:

```tsx
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={sectionLabelStyle}>Builder</span>
          <SearchQueryBuilder resourceType={resourceType} query={query} onQueryChange={setQuery} />
        </div>
```

to:

```tsx
        {searchMode !== 'everything' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span style={sectionLabelStyle}>Builder</span>
            <SearchQueryBuilder resourceType={resourceType} query={query} onQueryChange={setQuery} />
          </div>
        ) : null}
```

Also hide the raw query textarea itself in `$everything` mode, since there's no query string to edit — wrap the whole `Search query` `<div>` (lines 491–536) the same way: change its opening `<div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>` at line 491 to a conditional render:

```tsx
        {searchMode !== 'everything' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
```

and close it with the matching `) : null}` right after that block's existing closing `</div>` (the one right before the `Builder` section).

- [ ] **Step 7: Add the empty-parameters state**

In the "Search" and "Search Expression" `Card`s (lines 575–599), the current fallback text ("No parameters parsed yet." / "No typed expression yet.") already covers `result === null`, but doesn't distinguish "no result yet" from "a real result with genuinely zero parameters" (the `$everything` case). Replace lines 576–585:

```tsx
          <span style={sectionLabelStyle}>Search</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <SearchParamBlock key={param.ordinal} param={param} selection={selection} onSelect={selectOrdinal} />
              ))}
            </div>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No parameters parsed yet.</span>
          )}
```

with:

```tsx
          <span style={sectionLabelStyle}>Search</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <SearchParamBlock key={param.ordinal} param={param} selection={selection} onSelect={selectOrdinal} />
              ))}
            </div>
          ) : result ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No parameters — this is a whole-compartment operation.</span>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No parameters parsed yet.</span>
          )}
```

Apply the identical `result ? ... : ...` split to the "Search Expression" card, lines 589–598:

```tsx
          <span style={sectionLabelStyle}>Search Expression</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <ExpressionParamBlock key={param.ordinal} param={param} selection={selection} onSelect={selectOrdinal} compact={compact} />
              ))}
            </div>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No typed expression yet.</span>
          )}
```

to:

```tsx
          <span style={sectionLabelStyle}>Search Expression</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <ExpressionParamBlock key={param.ordinal} param={param} selection={selection} onSelect={selectOrdinal} compact={compact} />
              ))}
            </div>
          ) : result ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No parameters — this is a whole-compartment operation.</span>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No typed expression yet.</span>
          )}
```

The Plan and SQL cards need no change — they already render correctly from `plan`/`emittedSql` regardless of whether `Parameters` is empty, which is exactly the point (Plan/SQL carry the whole story in this mode).

- [ ] **Step 8: Build and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds; lint clean.

- [ ] **Step 9: Manual verification**

1. Select `Patient`, switch Mode to `$everything` — confirm the query textarea and Builder chips disappear, and the id/`_type`/`_since`/start/end/checkbox controls appear.
2. Type `example` as the id, run bare (no filters) — confirm a large plan comes back, `Parameters` panel says "No parameters — this is a whole-compartment operation," Plan/SQL panels are populated.
3. Toggle on the `Observation` `_type` chip — confirm the plan visibly shrinks (fewer CTEs) and `ReferencedTypeExpansion` disappears from the Explain tab.
4. Set `_since` to a date — confirm the SQL contains a `dbo.Transactions` join.
5. Uncheck "include referenced resources" — confirm `ReferencedTypeExpansion` is absent from the Explain output.
6. Switch back to `Type search` mode — confirm the query textarea/Builder reappear and a normal trace still works.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/benches/search/SearchBench.tsx
git commit -m "Add Patient/\$everything mode UI and the empty-parameters state"
```

---

## Task 9: Final full verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend build and test**

Run: `dotnet build Ignixa.Lab.sln -c Release --no-restore && dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity minimal`
Expected: 0 warnings, 0 errors, 100% pass.

- [ ] **Step 2: Full frontend build, lint, and pure-function tests**

Run: `cd frontend && npm run build && npm run lint && node --experimental-strip-types --test src/benches/search/*.test.ts`
Expected: build succeeds; lint clean (only the pre-existing `HttpMessage.tsx` warning); all pure-function tests pass (this plan didn't touch `searchSpans.ts`/`searchLineage.ts`/`sqlHighlight.ts`/`queryBuilder.ts`, so these should be unaffected — a failure here would mean something in this plan broke a file it wasn't supposed to touch).

- [ ] **Step 3: Manual end-to-end drive-through**

With `func start` and `npm run dev` both running, open the Search bench and confirm, in one pass:
1. `Patient`, Type search: default query traces as before (unchanged from pre-plan behavior).
2. `Patient`, Compartment, id `example`, member type `Observation`: small narrow plan.
3. `Patient`, Compartment, id `example`, member type `* all types`: large wildcard plan.
4. `Patient`, `$everything`, id `example`, no filters: large plan, empty Parameters panel with the explanatory message, Plan/SQL populated.
5. `Patient`, `$everything`, id `example`, `_type=Observation`: visibly smaller plan than #4.
6. `Encounter`, Compartment available; `$everything` NOT offered in the Mode row.
7. `Observation`: no Mode row appears at all — only Type search is possible.
8. Click-to-trace lineage (clicking a parameter block, a plan row, or a SQL span) still highlights correctly across all three panes in every mode tested above.

- [ ] **Step 4: Confirm the branch is ready for a PR**

Run: `git log --oneline main..HEAD` (or the equivalent against whatever base branch this was branched from) to review the full commit list for this feature, then open a PR following this repo's established convention (every prior Search-bench change shipped as a PR, not a direct push to `main`).
