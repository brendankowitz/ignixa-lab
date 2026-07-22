# Search Bench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fifth "Search" bench to the Expression Benches tool — live from day one — that traces a FHIR search query through parse → typed expression → lowered SQL plan → generated SQL, with click-to-trace lineage highlighting a span/IR-row/CTE/SQL-segment across all views, backed by a new real `Ignixa.Search.Sql.Tracing`-powered backend endpoint.

**Architecture:** Four phases executed back-to-back, each independently testable per this repo's `backend-check`/`frontend-check` skills: (1) bump every `Ignixa.*` package 0.6.19→0.6.23 and add the two new Search packages, wiring `artifacts/local-feed` as the source, then reach a green baseline; (2) backend — an `InMemorySymbolResolver`, a lazy R4 `SearchEngineFactory`, a `SearchTrace → SearchTraceResponse` DTO mapper, and a `SearchFunctions` endpoint; (3) frontend — types, a debounced auto-run API client, pure span-reconstruction/lineage helpers, and the `SearchBench` component; (4) tab wiring. The engine's `SearchCompiler.CompileAsync` already produces the cross-referenced provenance this UI needs — this plan maps and renders it, it does not invent it.

**Tech Stack:** .NET 10 (Azure Functions isolated worker, ASP.NET Core integration), xUnit + FluentAssertions (`result.Should().Be(...)` — NOT Shouldly, which the engine repo uses), `Ignixa.Search` (0.6.23) + `Ignixa.Search.Sql` (0.6.23-alpha), React/TypeScript + Vite (inline-style benches, oxlint), `node:test` pure-function tests.

**Design doc:** `docs/superpowers/specs/2026-07-20-search-bench-design.md` — read it first for full rationale. Precedent plan (same "bump packages then build a feature" shape): `docs/superpowers/plans/2026-07-12-ignixa-fhir-0.6.19-migration.md`.

## Global Constraints

- **Backend build/test:** run the `backend-check` steps from repo root, in order: `./backend/pack-suites.ps1` (packs `IgnixaLab.TestScript.Suites` into `artifacts/local-feed`; restore needs it), then `dotnet build Ignixa.Lab.sln -c Release` (warnings-as-errors — any analyzer warning fails the build), then `dotnet test Ignixa.Lab.sln`.
- **Frontend build/test:** run the `frontend-check` steps from `frontend/`: `npm install`, `npm run lint` (oxlint, must exit clean), `npm run build` (`tsc -b` + vite). Pure-function tests run via `node --test` (see `jsonPathResolver.test.ts` precedent) — they are `.test.ts` files using `node:test`/`node:assert`, NOT wired into `npm run build`; run them explicitly.
- **Package versions (verbatim):** `Ignixa.TestScript` and `Ignixa.TestScript.FhirFakes` → `0.6.23-beta`; `Ignixa.FhirPath`, `Ignixa.Serialization`, `Ignixa.Specification`, `Ignixa.Validation`, `Ignixa.PackageManagement`, `Ignixa.FhirFakes` → `0.6.23`; new `Ignixa.Search` → `0.6.23`; new `Ignixa.Search.Sql` → `0.6.23-alpha` (matching its actual prerelease tag).
- **R4 only for v1.** No FHIR-version selector in the Search bench (the mock has none). The backend factory mirrors `SchemaProviderFactory`'s lazy-per-version shape so R4B/R5/STU3 are a non-event later, but only R4 is wired.
- **Plain JSON, not FHIR wire format.** The Search endpoint returns plain `200`/`400` JSON (no `OperationOutcome` wrapping), consistent with `ValidationFunctions` — NOT the `Parameters`/`OperationOutcome` convention `FhirPathFunctions` uses. ASP.NET Core's default JSON is camelCase, so C# PascalCase DTO properties serialize as camelCase and the TS types are camelCase (same as `validationTypes.ts`).
- **Complexity badge is client-side only.** Derive the mock's cost figure from `Plan.Ctes.length` (CTE count) in the frontend. The backend emits no complexity metric — do not add one.
- **No Fakes handoff and no share-link state for Search in v1.** `SearchBench` does not call `onOpenFakes` and `shareLinks.ts` gains no `SearchShareState`. (The `'search'` id is still added to `shareLinks`' `BenchId` union and `BENCHES` array so a `?bench=search` deep link round-trips and `buildBenchShareUrl` type-checks.)
- **`SourceSpan` shape (verified against ignixa-fhir source during planning):** `public readonly record struct SourceSpan(SourceOrigin Origin, int Start, int Length)` where `enum SourceOrigin { Key, Value }`. This supersedes the design doc's provisional `{Start, Length}`. **Offsets are relative to the parameter's own `Key` or `Value` string** (per `Origin`), NOT the whole query string — the reference slice is `(span.Origin == SourceOrigin.Key ? parameter.Key : parameter.Value).Substring(span.Start, span.Length)`.
- **Do NOT add the `artifacts/nuget-internal` packages** (`Ignixa.Api`/`Ignixa.Application`/`Ignixa.DataLayer.*`/`Ignixa.Domain`) — this feature does not consume them. `SearchOptionsBuilderFactory`/`FhirVersionContext` live in `Ignixa.Application` and are the recipe this plan copies, but the plan reconstructs their construction using only `Ignixa.Search` + `Ignixa.Specification` public types, which ARE in the core feed.
- New backend types are `public` (the test project has no `InternalsVisibleTo`).

---

### Task 1: Package bump to 0.6.23 + local-feed wiring + green baseline

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`
- Populate: `artifacts/local-feed/` (copy `*.nupkg` from `artifacts/nuget-core/`)

**Interfaces:**
- Consumes: nothing.
- Produces: a green solution build+test against `Ignixa.Search` 0.6.23 / `Ignixa.Search.Sql` 0.6.23-alpha and every other `Ignixa.*` package at 0.6.23. Every later task depends on this.

- [ ] **Step 1: Capture a pre-bump test baseline**

Run the backend-check sequence once so you have a known-green starting point to diff against:

```bash
./backend/pack-suites.ps1
dotnet build Ignixa.Lab.sln -c Release
dotnet test Ignixa.Lab.sln
```

Expected: SUCCESS. Note the passing test count in your report.

- [ ] **Step 2: Copy the core `.nupkg` files into the local feed**

The 0.6.23 packages are NOT published to nuget.org — they must resolve from `artifacts/local-feed` (the existing `local-feed` source in `nuget.config`). Copy every core package (this includes transitive deps like `Ignixa.Abstractions`/`Ignixa.Models.R4` that also jumped to 0.6.23). Do NOT copy from `artifacts/nuget-internal`.

```bash
cp artifacts/nuget-core/*.nupkg artifacts/local-feed/
```

Confirm `artifacts/local-feed/Ignixa.Search.0.6.23.nupkg` and `artifacts/local-feed/Ignixa.Search.Sql.0.6.23-alpha.nupkg` now exist.

- [ ] **Step 3: Bump versions and add the two new packages in `Directory.Packages.props`**

Change these existing `PackageVersion` lines:

```xml
    <PackageVersion Include="Ignixa.TestScript" Version="0.6.19-beta" />
    <PackageVersion Include="Ignixa.TestScript.FhirFakes" Version="0.6.19-beta" />
    <PackageVersion Include="Ignixa.FhirPath" Version="0.6.19" />
    <PackageVersion Include="Ignixa.Serialization" Version="0.6.19" />
    <PackageVersion Include="Ignixa.Specification" Version="0.6.19" />
    <PackageVersion Include="Ignixa.Validation" Version="0.6.19" />
    <PackageVersion Include="Ignixa.PackageManagement" Version="0.6.19" />
    <PackageVersion Include="Ignixa.FhirFakes" Version="0.6.19" />
```

to:

```xml
    <PackageVersion Include="Ignixa.TestScript" Version="0.6.23-beta" />
    <PackageVersion Include="Ignixa.TestScript.FhirFakes" Version="0.6.23-beta" />
    <PackageVersion Include="Ignixa.FhirPath" Version="0.6.23" />
    <PackageVersion Include="Ignixa.Serialization" Version="0.6.23" />
    <PackageVersion Include="Ignixa.Specification" Version="0.6.23" />
    <PackageVersion Include="Ignixa.Validation" Version="0.6.23" />
    <PackageVersion Include="Ignixa.PackageManagement" Version="0.6.23" />
    <PackageVersion Include="Ignixa.FhirFakes" Version="0.6.23" />
```

Then, immediately after the `Ignixa.FhirFakes` line, add the two new Search packages:

```xml
    <!-- FHIR search parser/indexer, powering the Search bench's parse + typed-expression stages -->
    <PackageVersion Include="Ignixa.Search" Version="0.6.23" />

    <!-- Search-to-SQL compiler (alpha/experimental per its own README) — powers the Search bench's lowered-plan + emitted-SQL stages and the SearchCompiler tracing entry point -->
    <PackageVersion Include="Ignixa.Search.Sql" Version="0.6.23-alpha" />
```

- [ ] **Step 4: Add the two `PackageReference`s to the Functions project**

In `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`, in the first `<ItemGroup>` (after `<PackageReference Include="Ignixa.FhirFakes" />`), add:

```xml
    <PackageReference Include="Ignixa.Search" />
    <PackageReference Include="Ignixa.Search.Sql" />
```

- [ ] **Step 5: Restore + build; fix any 0.6.19→0.6.23 API breaks**

```bash
dotnet build Ignixa.Lab.sln -c Release
```

Expected: SUCCESS. The 0.6.19→0.6.23 jump may introduce breaking API changes beyond the scope of the new packages (the previous migration hit a `MutableNode` accessibility change at 0.6.19). If the build fails, read each error, fix it minimally following the upstream-established pattern, and record every fix in your report — do not assume the jump is clean. Do not proceed to Step 6 until the build is green.

- [ ] **Step 6: Run the full backend test suite**

```bash
dotnet test Ignixa.Lab.sln
```

Expected: passing count equals Step 1's baseline (no regressions). Triage any newly failing test — the engine may have changed unrelated behavior across four patch releases. Report any triaged change; do not silently paper over it.

- [ ] **Step 7: Confirm the frontend still builds (unchanged, but establishes the Phase 3 baseline)**

```bash
cd frontend && npm install && npm run lint && npm run build
```

Expected: SUCCESS.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj artifacts/local-feed
git commit -m "chore: bump ignixa-fhir to 0.6.23, add Ignixa.Search + Ignixa.Search.Sql packages"
```

(If `artifacts/local-feed` is git-ignored, note that in your report and skip staging it — the `.nupkg` copy is a local-restore step, not a committed artifact. Check `.gitignore` before assuming.)

---

### Task 2: `InMemorySymbolResolver`

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/Search/InMemorySymbolResolver.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/InMemorySymbolResolverTests.cs`

**Interfaces:**
- Consumes: `ISymbolResolver` (`Ignixa.Search.Sql.Symbols`) — two methods: `Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken)` and `Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken)`. `SearchParameterInfo` (`Ignixa.Search.Models`) exposes `.Url` (a `Uri?`) and `.Code` (`string`).
- Produces: `InMemorySymbolResolver` (a fresh instance per request), consumed by Task 5's endpoint and Task 4's mapper tests indirectly.

**Design note:** The compiler never interprets the id's value — only presence/absence — so any deterministic assignment is correct for demonstrating real plan/SQL shape. `GetSearchParamIdAsync` receives only the `SearchParameterInfo` (no separate resourceType arg), so key by the parameter's globally-unique `Url` (falling back to `Code` when `Url` is null); `GetResourceTypeIdAsync` keys by `resourceType`. This resolves the spec's "(resourceType, parameter.Url)" wording to what the actual method signature allows — `Url` is unique per search parameter, so resourceType would be redundant. Every parameter the parser recognized (a real, known search parameter) resolves; a genuinely unknown parameter never reaches the resolver as a `SearchParameterInfo` and so surfaces as `TraceStage.Resolve` via the engine's own path — nothing is silently faked.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/InMemorySymbolResolverTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class InMemorySymbolResolverTests
{
    private static SearchParameterInfo Param(string code, string url) =>
        new(code, code, SearchParamType.String, new Uri(url));

    [Fact]
    public async Task GetSearchParamIdAsync_SameParameter_ReturnsStableId()
    {
        var resolver = new InMemorySymbolResolver();
        var param = Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name");

        var first = await resolver.GetSearchParamIdAsync(param, CancellationToken.None);
        var second = await resolver.GetSearchParamIdAsync(param, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public async Task GetSearchParamIdAsync_DistinctParameters_ReturnDistinctIds()
    {
        var resolver = new InMemorySymbolResolver();
        var name = Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name");
        var gender = Param("gender", "http://hl7.org/fhir/SearchParameter/Patient-gender");

        var nameId = await resolver.GetSearchParamIdAsync(name, CancellationToken.None);
        var genderId = await resolver.GetSearchParamIdAsync(gender, CancellationToken.None);

        nameId.Should().NotBe(genderId);
    }

    [Fact]
    public async Task GetResourceTypeIdAsync_DistinctTypes_ReturnStableDistinctIds()
    {
        var resolver = new InMemorySymbolResolver();

        var patient = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        var observation = await resolver.GetResourceTypeIdAsync("Observation", CancellationToken.None);
        var patientAgain = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);

        patient.Should().NotBeNull();
        observation.Should().NotBe(patient);
        patientAgain.Should().Be(patient);
    }

    [Fact]
    public async Task SearchParamAndResourceTypeIds_AreIndependentSequences()
    {
        var resolver = new InMemorySymbolResolver();

        var typeId = await resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        var paramId = await resolver.GetSearchParamIdAsync(
            Param("name", "http://hl7.org/fhir/SearchParameter/Patient-name"), CancellationToken.None);

        // Each registry assigns from its own sequence; a shared counter is a bug.
        typeId.Should().Be(paramId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~InMemorySymbolResolverTests"`
Expected: build error — `InMemorySymbolResolver` does not exist yet.

- [ ] **Step 3: Implement `InMemorySymbolResolver`**

Create `backend/src/Ignixa.Lab.Functions/Services/Search/InMemorySymbolResolver.cs`:

```csharp
using System.Collections.Concurrent;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>
/// The Search bench has no live SQL Server (<c>Ignixa.DataLayer.SqlEntityFramework</c> is not referenced),
/// so this stands in for the compiler's only I/O seam. <see cref="ISymbolResolver"/> resolves search
/// parameters and resource types to <see cref="short"/> surrogate ids; the compiler only cares whether an
/// id is present, never its value, so any deterministic assignment produces real plan/SQL shape. Ids are
/// assigned sequentially on first sight from two independent registries. A new instance is created per
/// request, so ids are stable within a trace and need not persist across requests.
/// </summary>
public sealed class InMemorySymbolResolver : ISymbolResolver
{
    private readonly ConcurrentDictionary<string, short> _searchParamIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, short> _resourceTypeIds = new(StringComparer.Ordinal);
    private int _nextSearchParamId;
    private int _nextResourceTypeId;

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
    {
        var key = parameter.Url?.ToString() ?? parameter.Code;
        var id = _searchParamIds.GetOrAdd(key, _ => (short)Interlocked.Increment(ref _nextSearchParamId));
        return Task.FromResult<short?>(id);
    }

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        var id = _resourceTypeIds.GetOrAdd(resourceType, _ => (short)Interlocked.Increment(ref _nextResourceTypeId));
        return Task.FromResult<short?>(id);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~InMemorySymbolResolverTests"`
Expected: PASS (all four). If `SearchParameterInfo`'s constructor signature differs in the packaged 0.6.23 build (e.g. `.Url` is non-nullable, or the positional order differs from `(name, code, SearchParamType, Uri)`), the test helper `Param(...)` will fail to compile — fix it against the actual type via go-to-definition, and update this plan's snippet in your report.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Search/InMemorySymbolResolver.cs \
        backend/test/Ignixa.Lab.Functions.Tests/Services/Search/InMemorySymbolResolverTests.cs
git commit -m "feat(search): add InMemorySymbolResolver for the Search bench compiler seam"
```

---

### Task 3: `SearchEngineFactory` (lazy R4 builder + definition managers)

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchEngineFactoryTests.cs`

**Interfaces:**
- Consumes: this repo's existing `SchemaProviderFactory` (`Services/FhirPath/SchemaProviderFactory.cs`), whose `GetSchemaProvider("R4")` returns an `IFhirSchemaProvider`. Also `Ignixa.Search.Sql.Tracing.SearchCompiler`, `InMemorySymbolResolver` (Task 2), `QueryParameterParser` (`Ignixa.Search.Parsing`).
- Produces: `SearchEngineFactory.GetR4()` returning `(ISearchOptionsBuilder Builder, ISearchParameterDefinitionManager SearchParameters, ICompartmentDefinitionManager Compartments)` — the three dependencies `SearchCompiler.CompileAsync` needs beyond the resolver. Consumed by Task 5's endpoint.

**Recipe (reconstructed from `Ignixa.Application`'s `SearchOptionsBuilderFactory` + `FhirVersionContext`, using only core-feed types):**
```
schema                = schemaProviderFactory.GetSchemaProvider("R4")        // IFhirSchemaProvider
definitionManager     = new SearchParameterDefinitionManager(schema, logger) // Ignixa.Search.Definition
referenceParser       = new ReferenceSearchValueParser(schema)               // Ignixa.Search.Indexing.SearchValues
searchParamExprParser = new SearchParameterExpressionParser(referenceParser, schema) // Ignixa.Search.Expressions.Parsers
resolverDelegate      = () => definitionManager  // ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver
expressionParser      = new ExpressionParser(resolverDelegate, searchParamExprParser, schema) // Ignixa.Search.Expressions.Parsers
builder               = new SearchOptionsBuilder(expressionParser, definitionManager)          // Ignixa.Search.Parsing
compartments          = new CompartmentDefinitionManager(FhirVersion.R4)     // Ignixa.Search.Definition
```

**Verification TODO for the implementer:** these constructor signatures were read from ignixa-fhir source at planning time, not from the compiled 0.6.23 package. If any differs (parameter order, an extra logger/options arg, the nested delegate type name `SearchableSearchParameterDefinitionManagerResolver`, or `CompartmentDefinitionManager(FhirVersion)` vs another overload), the build will name the exact break — fix against go-to-definition on the packaged assembly and note the correction in your report. The Task-5 end-to-end test is the real proof this wiring is correct.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchEngineFactoryTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class SearchEngineFactoryTests
{
    [Fact]
    public void GetR4_ReturnsAllThreeDependencies()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var engine = factory.GetR4();

        engine.Builder.Should().NotBeNull();
        engine.SearchParameters.Should().NotBeNull();
        engine.Compartments.Should().NotBeNull();
    }

    [Fact]
    public void GetR4_CalledTwice_ReturnsTheSameCachedInstances()
    {
        var factory = new SearchEngineFactory(new SchemaProviderFactory());

        var first = factory.GetR4();
        var second = factory.GetR4();

        second.Builder.Should().BeSameAs(first.Builder);
        second.SearchParameters.Should().BeSameAs(first.SearchParameters);
        second.Compartments.Should().BeSameAs(first.Compartments);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~SearchEngineFactoryTests"`
Expected: build error — `SearchEngineFactory` does not exist.

- [ ] **Step 3: Implement `SearchEngineFactory`**

Create `backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>The three per-FHIR-version dependencies <see cref="Ignixa.Search.Sql.Tracing.SearchCompiler"/>
/// needs beyond the symbol resolver: the options builder, the search-parameter definition manager, and the
/// compartment definition manager.</summary>
public sealed record SearchEngine(
    ISearchOptionsBuilder Builder,
    ISearchParameterDefinitionManager SearchParameters,
    ICompartmentDefinitionManager Compartments);

/// <summary>
/// Builds and caches the Search engine dependencies per FHIR version, mirroring
/// <see cref="SchemaProviderFactory"/>'s lazy-singleton-per-version shape. Only R4 is wired today; the
/// per-version cache keeps adding R4B/R5/STU3 later a non-event. The build is expensive (loads the full
/// base search-parameter set), so it runs at most once.
/// </summary>
public sealed class SearchEngineFactory(SchemaProviderFactory schemaProviderFactory)
{
    private readonly Lazy<SearchEngine> _r4 = new(() => Build(schemaProviderFactory, "R4", FhirVersion.R4));

    /// <summary>Gets the cached R4 Search engine dependencies.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public SearchEngine GetR4() => _r4.Value;

    private static SearchEngine Build(SchemaProviderFactory schemaProviderFactory, string version, FhirVersion fhirVersion)
    {
        var schema = schemaProviderFactory.GetSchemaProvider(version);

        var definitionManager = new SearchParameterDefinitionManager(
            schema, NullLogger<SearchParameterDefinitionManager>.Instance);

        var referenceParser = new ReferenceSearchValueParser(schema);
        var searchParamExpressionParser = new SearchParameterExpressionParser(referenceParser, schema);

        ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver resolver =
            () => definitionManager;
        var expressionParser = new ExpressionParser(resolver, searchParamExpressionParser, schema);

        var builder = new SearchOptionsBuilder(expressionParser, definitionManager);
        var compartments = new CompartmentDefinitionManager(fhirVersion);

        return new SearchEngine(builder, definitionManager, compartments);
    }
}
```

- [ ] **Step 4: Build and run the test**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~SearchEngineFactoryTests"`
Expected: PASS (both). If the build fails on a constructor signature, apply the Verification TODO above.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs \
        backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchEngineFactoryTests.cs
git commit -m "feat(search): add lazy R4 SearchEngineFactory"
```

---

### Task 4: Response DTO + `SearchTrace → SearchTraceResponse` mapper

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/Search/SearchTraceResponse.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs`

**Interfaces:**
- Consumes: `Ignixa.Search.Sql.Tracing.SearchTrace` and its nested records (`QueryPlanTrace`, `EmittedSqlTrace`, `CteProvenance`, `TraceFailure`, `ImplicitParameter`), `Ignixa.Search.Parsing.ParameterTrace`/`ParameterOutcome`/`TraceStage`, `Ignixa.Search.Expressions.{SourceSpan, SourceOrigin, IrProjector, IrRow}`, `Ignixa.Search.Expressions.Parsers.SyntaxNode`, `Ignixa.Search.Sql.Ast.PlanExplainRow`, `Ignixa.Search.Sql.Builders.SqlTextRange`.
- Produces: `SearchTraceResponse` (the DTO) and `SearchTraceMapper.ToResponse(SearchTrace) : SearchTraceResponse`. Consumed by Task 5's endpoint and mirrored by Task 6's TS types.

**Source-type field reference (verified against ignixa-fhir source during planning):**
```
SearchTrace(string ResourceType, IReadOnlyList<ParameterTrace> Parameters, QueryPlanTrace? Plan, EmittedSqlTrace? Sql)
           { TraceFailure? Failure; IReadOnlyList<ImplicitParameter> Implicit; }  // Implicit defaults to []
ParameterTrace(int Ordinal, string Key, string Value, SyntaxNode? KeySyntax, SyntaxNode? ValueSyntax, Expression? Ir, ParameterOutcome Outcome)
ParameterOutcome = Compiled | Ignored(string Reason, SourceSpan? Span) | Failed(TraceStage Stage, string Message, SourceSpan? Span)
SyntaxNode(string Kind, SourceSpan Span, IReadOnlyList<SyntaxNode> Children)   // Span is NON-nullable here
SourceSpan(SourceOrigin Origin, int Start, int Length);  enum SourceOrigin { Key, Value }
IrRow(string Kind, string Text, int Depth)               // via IrProjector.Describe(Expression)
QueryPlanTrace(string Explain, IReadOnlyList<CteProvenance> Ctes, IReadOnlyList<PlanExplainRow> Rows)
CteProvenance(int CteIndex, int? ParameterOrdinal, SourceSpan? Span)
PlanExplainRow(string Label, string Body)
EmittedSqlTrace(string Sql, IReadOnlyList<SqlTextRange> Ranges)
SqlTextRange(string Label, int Start, int Length)
ImplicitParameter(string Name, string Value, string Reason)
TraceFailure(TraceStage Stage, string Message, SourceSpan? Span)
```

- [ ] **Step 1: Create the DTO records**

Create `backend/src/Ignixa.Lab.Functions/Models/Search/SearchTraceResponse.cs`:

```csharp
namespace Ignixa.Lab.Functions.Models.Search;

/// <summary>Serializable projection of <see cref="Ignixa.Search.Sql.Tracing.SearchTrace"/> for the Search
/// bench UI. Mirrors the trace field-for-field, replacing the two non-serializable pieces (the live IR
/// <c>Expression</c> graph and the plan's raw expression graph) with flattened row projections. Serialized
/// as camelCase JSON (ASP.NET Core default).</summary>
public sealed record SearchTraceResponse(
    string ResourceType,
    IReadOnlyList<ParameterTraceDto> Parameters,
    QueryPlanDto? Plan,
    EmittedSqlDto? Sql,
    IReadOnlyList<ImplicitParameterDto> Implicit,
    TraceFailureDto? Failure);

public sealed record ParameterTraceDto(
    int Ordinal,
    string Key,
    string Value,
    SyntaxNodeDto? KeySyntax,
    SyntaxNodeDto? ValueSyntax,
    IReadOnlyList<IrRowDto> Ir,
    ParameterOutcomeDto Outcome);

/// <summary>Serializable <see cref="Ignixa.Search.Expressions.Parsers.SyntaxNode"/>. Its <see cref="Span"/>
/// is non-null (the syntax scanner always spans real text).</summary>
public sealed record SyntaxNodeDto(string Kind, SpanDto Span, IReadOnlyList<SyntaxNodeDto> Children);

/// <summary>A range within one parameter's key or value string. <see cref="Origin"/> is "Key" or "Value";
/// <see cref="Start"/>/<see cref="Length"/> index into that string, NOT the whole query string.</summary>
public sealed record SpanDto(string Origin, int Start, int Length);

public sealed record IrRowDto(string Kind, string Text, int Depth);

/// <summary><see cref="Kind"/> is "Compiled" | "Ignored" | "Failed". <see cref="Reason"/> carries the
/// Ignored reason or the Failed message; <see cref="Stage"/> is set only for Failed.</summary>
public sealed record ParameterOutcomeDto(string Kind, string? Reason, string? Stage, SpanDto? Span);

public sealed record QueryPlanDto(string Explain, IReadOnlyList<PlanExplainRowDto> Rows, IReadOnlyList<CteProvenanceDto> Ctes);

public sealed record PlanExplainRowDto(string Label, string Body);

public sealed record CteProvenanceDto(int CteIndex, int? ParameterOrdinal, SpanDto? Span);

public sealed record EmittedSqlDto(string Sql, IReadOnlyList<SqlTextRangeDto> Ranges);

public sealed record SqlTextRangeDto(string Label, int Start, int Length);

public sealed record ImplicitParameterDto(string Name, string Value, string Reason);

public sealed record TraceFailureDto(string Stage, string Message, SpanDto? Span);
```

- [ ] **Step 2: Write the failing mapper tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs`. These build `SearchTrace`s directly (the mapper is pure over the trace, so no compiler run is needed):

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class SearchTraceMapperTests
{
    private static ParameterTrace Trace(int ordinal, string key, string value, ParameterOutcome outcome) =>
        new(ordinal, key, value, KeySyntax: null, ValueSyntax: null, Ir: null, outcome);

    [Fact]
    public void ToResponse_CompiledOutcome_MapsKindOnly()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())],
            Plan: null, Sql: null);

        var response = SearchTraceMapper.ToResponse(trace);

        response.ResourceType.Should().Be("Patient");
        var outcome = response.Parameters.Single().Outcome;
        outcome.Kind.Should().Be("Compiled");
        outcome.Reason.Should().BeNull();
        outcome.Stage.Should().BeNull();
    }

    [Fact]
    public void ToResponse_IgnoredOutcome_CarriesReasonAndSpan()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "birthdate:exact", "2000", new ParameterOutcome.Ignored("modifier not allowed on date", new SourceSpan(SourceOrigin.Key, 10, 5)))],
            Plan: null, Sql: null);

        var outcome = SearchTraceMapper.ToResponse(trace).Parameters.Single().Outcome;

        outcome.Kind.Should().Be("Ignored");
        outcome.Reason.Should().Be("modifier not allowed on date");
        outcome.Span!.Origin.Should().Be("Key");
        outcome.Span.Start.Should().Be(10);
        outcome.Span.Length.Should().Be(5);
    }

    [Fact]
    public void ToResponse_FailedOutcome_CarriesStageAndMessage()
    {
        var trace = new SearchTrace("Patient",
            [Trace(0, "unknown", "x", new ParameterOutcome.Failed(TraceStage.Resolve, "could not be resolved", new SourceSpan(SourceOrigin.Value, 0, 1)))],
            Plan: null, Sql: null);

        var outcome = SearchTraceMapper.ToResponse(trace).Parameters.Single().Outcome;

        outcome.Kind.Should().Be("Failed");
        outcome.Stage.Should().Be("Resolve");
        outcome.Reason.Should().Be("could not be resolved");
    }

    [Fact]
    public void ToResponse_PreservesCteParameterOrdinalUnchanged()
    {
        // The frontend joins plan rows / SQL ranges to parameters through CteProvenance.ParameterOrdinal —
        // this asserts that join key survives the mapping so the UI's lineage highlighting is trustworthy.
        var plan = new QueryPlanTrace(
            Explain: "root = ...",
            Ctes: [new CteProvenance(0, ParameterOrdinal: 7, new SourceSpan(SourceOrigin.Value, 0, 5))],
            Rows: [new PlanExplainRow("cte0", "ParamSource name")]);
        var sql = new EmittedSqlTrace("SELECT 1", [new SqlTextRange("cte0", 0, 6)]);
        var trace = new SearchTrace("Patient", [Trace(0, "name", "Smith", new ParameterOutcome.Compiled())], plan, sql)
        {
            Implicit = [new ImplicitParameter("_count", "10", "server default")],
        };

        var response = SearchTraceMapper.ToResponse(trace);

        response.Plan!.Ctes.Single().ParameterOrdinal.Should().Be(7);
        response.Plan.Rows.Single().Label.Should().Be("cte0");
        response.Sql!.Ranges.Single().Label.Should().Be("cte0");
        response.Implicit.Single().Name.Should().Be("_count");
    }

    [Fact]
    public void ToResponse_NullPlanAndSql_MapToNull()
    {
        var trace = new SearchTrace("Patient", [], Plan: null, Sql: null)
        {
            Failure = new TraceFailure(TraceStage.Resolve, "Search parameters could not be resolved: 'bogus'.", null),
        };

        var response = SearchTraceMapper.ToResponse(trace);

        response.Plan.Should().BeNull();
        response.Sql.Should().BeNull();
        response.Failure!.Stage.Should().Be("Resolve");
        response.Implicit.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~SearchTraceMapperTests"`
Expected: build error — `SearchTraceMapper` does not exist.

- [ ] **Step 4: Implement the mapper**

Create `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs`:

```csharp
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>Maps a <see cref="SearchTrace"/> to the serializable <see cref="SearchTraceResponse"/>. Thin by
/// design: <see cref="IrProjector"/> and <see cref="SyntaxNode"/> already do the flattening, so this only
/// translates shapes and projects the two non-serializable pieces.</summary>
public static class SearchTraceMapper
{
    public static SearchTraceResponse ToResponse(SearchTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return new SearchTraceResponse(
            trace.ResourceType,
            trace.Parameters.Select(ToParameterDto).ToList(),
            trace.Plan is null ? null : ToPlanDto(trace.Plan),
            trace.Sql is null ? null : new EmittedSqlDto(
                trace.Sql.Sql,
                trace.Sql.Ranges.Select(r => new SqlTextRangeDto(r.Label, r.Start, r.Length)).ToList()),
            trace.Implicit.Select(i => new ImplicitParameterDto(i.Name, i.Value, i.Reason)).ToList(),
            trace.Failure is null ? null : new TraceFailureDto(trace.Failure.Stage.ToString(), trace.Failure.Message, ToSpanDto(trace.Failure.Span)));
    }

    private static ParameterTraceDto ToParameterDto(ParameterTrace p) => new(
        p.Ordinal,
        p.Key,
        p.Value,
        p.KeySyntax is null ? null : ToSyntaxDto(p.KeySyntax),
        p.ValueSyntax is null ? null : ToSyntaxDto(p.ValueSyntax),
        DescribeIr(p.Ir),
        ToOutcomeDto(p.Outcome));

    // IrProjector.Describe is documented to throw NotSupportedException loudly on a node kind it does not
    // model. In a bench that would turn one exotic parameter into a 500 for the whole request, so degrade to
    // an empty IR list for that parameter instead — the other columns and parameters still render.
    private static IReadOnlyList<IrRowDto> DescribeIr(Expression? ir)
    {
        if (ir is null)
        {
            return [];
        }

        try
        {
            return IrProjector.Describe(ir).Select(r => new IrRowDto(r.Kind, r.Text, r.Depth)).ToList();
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private static SyntaxNodeDto ToSyntaxDto(SyntaxNode node) => new(
        node.Kind,
        ToSpanDto(node.Span),
        node.Children.Select(ToSyntaxDto).ToList());

    private static ParameterOutcomeDto ToOutcomeDto(ParameterOutcome outcome) => outcome switch
    {
        ParameterOutcome.Compiled => new ParameterOutcomeDto("Compiled", null, null, null),
        ParameterOutcome.Ignored ignored => new ParameterOutcomeDto("Ignored", ignored.Reason, null, ToSpanDto(ignored.Span)),
        ParameterOutcome.Failed failed => new ParameterOutcomeDto("Failed", failed.Message, failed.Stage.ToString(), ToSpanDto(failed.Span)),
        _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
    };

    private static QueryPlanDto ToPlanDto(QueryPlanTrace plan) => new(
        plan.Explain,
        plan.Rows.Select(r => new PlanExplainRowDto(r.Label, r.Body)).ToList(),
        plan.Ctes.Select(c => new CteProvenanceDto(c.CteIndex, c.ParameterOrdinal, ToSpanDto(c.Span))).ToList());

    private static SpanDto ToSpanDto(SourceSpan span) => new(span.Origin.ToString(), span.Start, span.Length);

    private static SpanDto? ToSpanDto(SourceSpan? span) => span is { } s ? ToSpanDto(s) : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~SearchTraceMapperTests"`
Expected: PASS (all five). If a source record's positional order differs in the packaged build, adjust the test constructors and the mapper together, and note it.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Search/SearchTraceResponse.cs \
        backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs \
        backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs
git commit -m "feat(search): add SearchTraceResponse DTO and SearchTrace mapper"
```

---

### Task 5: `SearchFunctions` endpoint + DI registration + end-to-end tests

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Program.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`

**Interfaces:**
- Consumes: `SearchEngineFactory` (Task 3), `InMemorySymbolResolver` (Task 2), `SearchTraceMapper` (Task 4), `QueryParameterParser` (`Ignixa.Search.Parsing`), `SearchCompiler.CompileAsync` (`Ignixa.Search.Sql.Tracing`).
- Produces: `GET /api/search/{resourceType}` returning `SearchTraceResponse` JSON. Consumed by Task 6's API client.

**Endpoint contract:** anonymous auth (same as `FhirPathFunctions`), `GET` + `options`, route `search/{resourceType}`. The raw query string (everything after `?`) is forwarded to the parser verbatim. Returns `200` with `SearchTraceResponse` on success, `400 { "error": ... }` on a bad request (empty/whitespace resource type, or a compiler exception that is not represented as trace data). Note: unresolved parameters, ignored parameters, and Lower/Emit failures are NOT 400s — the engine records them as data on the trace (`Failure`, per-parameter `Outcome`), so they return `200` and the UI renders them.

- [ ] **Step 1: Write the failing end-to-end tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`. These run the real compiler through the endpoint (the true proof Task 3's wiring is correct against the packaged assembly):

```csharp
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class SearchFunctionsTests
{
    private static SearchFunctions CreateFunctions() =>
        new(NullLogger<SearchFunctions>.Instance, new SearchEngineFactory(new SchemaProviderFactory()));

    private static HttpRequest BuildGetRequest(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(queryString); // e.g. "?name=Smith"
        return context.Request;
    }

    [Fact]
    public async Task Trace_PatientNameSmith_CompilesToPlanAndSql()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        response.ResourceType.Should().Be("Patient");
        response.Failure.Should().BeNull();
        response.Parameters.Should().ContainSingle(p => p.Key.StartsWith("name"));
        response.Parameters.Single().Outcome.Kind.Should().Be("Compiled");
        response.Plan.Should().NotBeNull();
        response.Sql.Should().NotBeNull();
        // The lineage join the UI depends on: a CTE attributed to the parameter, and a SQL range labelled for it.
        var cte = response.Plan!.Ctes.Should().Contain(c => c.ParameterOrdinal == 0).Subject;
        response.Sql!.Ranges.Should().Contain(r => r.Label == $"cte{cte.CteIndex}");
    }

    [Fact]
    public async Task Trace_UnknownParameter_ReportsFailureButStillReturnsOk()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?totally-bogus-param=x"), "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        // An unknown parameter is trace data, not an HTTP error. Depending on lenient handling it surfaces
        // either as Failure at Resolve, or as an Ignored/Failed parameter outcome — assert the request did
        // not 400 and did not silently pretend success.
        (response.Failure is not null
            || response.Parameters.Any(p => p.Outcome.Kind is "Failed" or "Ignored"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Trace_ChainedReference_CapturesBothSyntaxProjections()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(
            BuildGetRequest("?general-practitioner.name=Smith"), "Patient", CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<SearchTraceResponse>().Subject;
        var parameter = response.Parameters.Should().ContainSingle().Subject;
        parameter.KeySyntax.Should().NotBeNull("the chain structure lives on the key syntax");
    }

    [Fact]
    public async Task Trace_EmptyResourceType_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.Trace(BuildGetRequest("?name=Smith"), "  ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
```

**Note on the unknown-parameter test:** the exact path (page-level `Failure` vs per-parameter `Ignored`/`Failed`) depends on FHIR lenient-handling behavior in the packaged build. The assertion above deliberately accepts either so the test is robust; if you can determine the exact behavior by running it, tighten the assertion and note which path the engine took.

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~SearchFunctionsTests"`
Expected: build error — `SearchFunctions` does not exist.

- [ ] **Step 3: Implement the endpoint**

Create `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs`:

```csharp
using Ignixa.Lab.Functions.Models.Search;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// Search-trace endpoint powering the Expression Benches "Search" bench. Given a FHIR search query, it
/// traces the query through parse → typed expression → lowered SQL plan → generated SQL via
/// <see cref="SearchCompiler"/>, returning the cross-referenced provenance as plain JSON (not a FHIR
/// resource — this is bench tooling, so no OperationOutcome wrapping). R4 only for v1.
/// </summary>
public sealed class SearchFunctions(ILogger<SearchFunctions> logger, SearchEngineFactory engineFactory)
{
    [Function("SearchTrace")]
    public async Task<IActionResult> Trace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "search/{resourceType}")] HttpRequest request,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return new BadRequestObjectResult(new { error = "A resource type is required." });
        }

        var rawQuery = request.QueryString.HasValue
            ? request.QueryString.Value!.TrimStart('?')
            : string.Empty;

        var parameters = new QueryParameterParser().Parse(rawQuery);
        var engine = engineFactory.GetR4();
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
                cancellationToken);
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
            logger.LogWarning(ex, "Search trace failed for {ResourceType}?{Query}", resourceType, rawQuery);
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        return new OkObjectResult(SearchTraceMapper.ToResponse(trace));
    }
}
```

- [ ] **Step 4: Register the services in `Program.cs`**

In `backend/src/Ignixa.Lab.Functions/Program.cs`, after the line `builder.Services.AddSingleton<SchemaProviderFactory>();` (the Search factory depends on it), add:

```csharp
builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Search.SearchEngineFactory>();
```

(`InMemorySymbolResolver` is constructed per-request inside the endpoint, not injected. `SearchFunctions` itself is discovered by the Functions runtime and needs no explicit registration, same as the other `*Functions` classes.)

- [ ] **Step 5: Run the endpoint tests**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~SearchFunctionsTests"`
Expected: PASS. **This is the task where a Task-3 wiring mistake surfaces** — if `Patient?name=Smith` does not compile to a plan (empty `Plan`, or an unexpected `Failure`), the definition manager is not loading base search parameters. Re-check the `SearchEngineFactory` recipe against the packaged assembly (go-to-definition on each constructor) before assuming the query is wrong. Do not weaken the test to make it pass.

- [ ] **Step 6: Full backend-check pass**

Run the full `backend-check` sequence (`./backend/pack-suites.ps1`, `dotnet build -c Release`, `dotnet test`). Expected: whole solution green, no warnings.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs \
        backend/src/Ignixa.Lab.Functions/Program.cs \
        backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs
git commit -m "feat(search): add GET /api/search/{resourceType} trace endpoint"
```

---

### Task 6: Frontend types + API client + query-string span reconstruction

**Files:**
- Create: `frontend/src/benches/search/searchTypes.ts`
- Create: `frontend/src/benches/search/searchApi.ts`
- Create: `frontend/src/benches/search/searchSpans.ts`
- Test: `frontend/src/benches/search/searchSpans.test.ts`

**Interfaces:**
- Consumes: the `GET /api/search/{resourceType}` JSON contract (camelCase mirror of Task 4's DTO).
- Produces: `SearchTraceResponse` TS type (+ nested), `runSearch(resourceType, query, signal): Promise<SearchTraceResponse>`, and `spanSegments(text, spans): Segment[]` (pure — the tested unit). Consumed by Tasks 7 and 8.

- [ ] **Step 1: Create `searchTypes.ts` (camelCase mirror of the DTO)**

```typescript
/** Mirrors backend Models/Search/SearchTraceResponse.cs (serialized camelCase). */

export type SourceOrigin = 'Key' | 'Value';

/** A range within one parameter's key or value string (per `origin`) — NOT the whole query string. */
export interface Span {
  origin: SourceOrigin;
  start: number;
  length: number;
}

export interface SyntaxNode {
  kind: string;
  span: Span;
  children: SyntaxNode[];
}

export interface IrRow {
  kind: string;
  text: string;
  depth: number;
}

export type OutcomeKind = 'Compiled' | 'Ignored' | 'Failed';

export interface ParameterOutcome {
  kind: OutcomeKind;
  reason: string | null;
  stage: string | null;
  span: Span | null;
}

export interface ParameterTrace {
  ordinal: number;
  key: string;
  value: string;
  keySyntax: SyntaxNode | null;
  valueSyntax: SyntaxNode | null;
  ir: IrRow[];
  outcome: ParameterOutcome;
}

export interface PlanExplainRow {
  label: string;
  body: string;
}

export interface CteProvenance {
  cteIndex: number;
  parameterOrdinal: number | null;
  span: Span | null;
}

export interface QueryPlan {
  explain: string;
  rows: PlanExplainRow[];
  ctes: CteProvenance[];
}

export interface SqlTextRange {
  label: string;
  start: number;
  length: number;
}

export interface EmittedSql {
  sql: string;
  ranges: SqlTextRange[];
}

export interface ImplicitParameter {
  name: string;
  value: string;
  reason: string;
}

export interface TraceFailure {
  stage: string;
  message: string;
  span: Span | null;
}

export interface SearchTraceResponse {
  resourceType: string;
  parameters: ParameterTrace[];
  plan: QueryPlan | null;
  sql: EmittedSql | null;
  implicit: ImplicitParameter[];
  failure: TraceFailure | null;
}

export const RESOURCE_TYPES = ['Patient', 'Observation', 'Encounter'] as const;
export type ResourceType = (typeof RESOURCE_TYPES)[number];

export const DEFAULT_RESOURCE_TYPE: ResourceType = 'Patient';
export const DEFAULT_QUERY = 'name=Smith&birthdate=gt2000-01-01';

/** Example query chips shown under the expression input (mirrors the mock's example chips). */
export const EXAMPLE_QUERIES: Record<ResourceType, string[]> = {
  Patient: ['name=Smith&birthdate=gt2000-01-01', 'general-practitioner.name=Jones', 'gender=male&_sort=name'],
  Observation: ['code=http://loinc.org|8480-6', 'code-value-quantity=8480-6$gt90', 'patient=Patient/123'],
  Encounter: ['status=finished', 'date=ge2024-01-01', 'subject.name=Smith'],
};
```

- [ ] **Step 2: Create `searchApi.ts`**

```typescript
import type { SearchTraceResponse } from './searchTypes';

/** GETs the search-trace endpoint for a resource type + raw query string. Throws on non-2xx or a
 * `{ error }` body (the backend reports bad requests that way), or on a network/abort error. */
export async function runSearch(
  resourceType: string,
  query: string,
  signal: AbortSignal,
): Promise<SearchTraceResponse> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const suffix = query.trim() ? `?${query.trim()}` : '';
  const response = await fetch(`${apiBaseUrl}/api/search/${encodeURIComponent(resourceType)}${suffix}`, {
    method: 'GET',
    signal,
  });

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

- [ ] **Step 3: Write the failing span-reconstruction test**

Create `frontend/src/benches/search/searchSpans.test.ts`:

```typescript
/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { spanSegments } from './searchSpans.ts';
import type { SyntaxNode } from './searchTypes.ts';

// birthdate=gt2000-01-01 → value = "gt2000-01-01"; a value-origin syntax node covering the prefix+value.
const valueSyntax: SyntaxNode = {
  kind: 'atomic',
  span: { origin: 'Value', start: 0, length: 12 },
  children: [],
};

test('a single span over the whole value yields one highlighted segment covering it', () => {
  const segments = spanSegments('gt2000-01-01', valueSyntax, 'Value');
  const highlighted = segments.filter((s) => s.node !== null);
  assert.equal(highlighted.length, 1);
  assert.equal('gt2000-01-01'.slice(highlighted[0].start, highlighted[0].start + highlighted[0].length), 'gt2000-01-01');
});

test('a nested child span produces a sub-segment inside its parent', () => {
  const nested: SyntaxNode = {
    kind: 'composite',
    span: { origin: 'Value', start: 0, length: 11 }, // "8480-6$high"
    children: [
      { kind: 'component', span: { origin: 'Value', start: 0, length: 6 }, children: [] }, // "8480-6"
      { kind: 'component', span: { origin: 'Value', start: 7, length: 4 }, children: [] }, // "high"
    ],
  };
  const segments = spanSegments('8480-6$high', nested, 'Value');
  const texts = segments.map((s) => '8480-6$high'.slice(s.start, s.start + s.length));
  // The '$' separator between the two component spans must appear as a plain (node === null) segment.
  assert.ok(texts.includes('$'));
});

test('spans whose origin does not match the requested string are skipped', () => {
  const keySpan: SyntaxNode = { kind: 'k', span: { origin: 'Key', start: 0, length: 3 }, children: [] };
  const segments = spanSegments('abc', keySpan, 'Value'); // asking for Value segments, node is Key-origin
  assert.equal(segments.filter((s) => s.node !== null).length, 0);
});
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `node --test frontend/src/benches/search/searchSpans.test.ts` (from repo root; the file imports `.ts` directly as the existing `jsonPathResolver.test.ts` does — Node's TS support / the repo's test runner already handles this precedent).
Expected: FAIL — `searchSpans.ts` does not exist.

- [ ] **Step 5: Implement `searchSpans.ts`**

```typescript
import type { SourceOrigin, SyntaxNode } from './searchTypes';

/** One slice of a parameter's key or value string. `node` is the deepest syntax node covering it (for
 * click-to-trace), or null for the plain text between/around spans. */
export interface Segment {
  start: number;
  length: number;
  node: SyntaxNode | null;
}

/** Flattens a syntax tree into non-overlapping, ordered segments over `text` (one parameter's key or value
 * string), keeping only spans whose `origin` matches `origin`. Offsets are relative to `text`. Gaps between
 * spans (e.g. the '$' in a composite, or a comma between alternatives) become plain segments with node=null,
 * so the renderer can lay the whole string out left-to-right without losing characters. */
export function spanSegments(text: string, root: SyntaxNode | null, origin: SourceOrigin): Segment[] {
  const cuts = new Set<number>([0, text.length]);
  const covering: { start: number; end: number; node: SyntaxNode }[] = [];

  const walk = (node: SyntaxNode) => {
    if (node.span.origin === origin) {
      const start = Math.max(0, node.span.start);
      const end = Math.min(text.length, node.span.start + node.span.length);
      if (end > start) {
        cuts.add(start);
        cuts.add(end);
        covering.push({ start, end, node });
      }
    }
    for (const child of node.children) {
      walk(child);
    }
  };
  if (root) {
    walk(root);
  }

  const boundaries = [...cuts].sort((a, b) => a - b);
  const segments: Segment[] = [];
  for (let i = 0; i < boundaries.length - 1; i += 1) {
    const start = boundaries[i];
    const end = boundaries[i + 1];
    if (end <= start) {
      continue;
    }
    // Deepest covering node wins (a child span sits inside its parent); ties broken by smallest range.
    let best: SyntaxNode | null = null;
    let bestWidth = Infinity;
    for (const candidate of covering) {
      if (candidate.start <= start && candidate.end >= end) {
        const width = candidate.end - candidate.start;
        if (width < bestWidth) {
          best = candidate.node;
          bestWidth = width;
        }
      }
    }
    segments.push({ start, length: end - start, node: best });
  }
  return segments;
}
```

- [ ] **Step 6: Run the test + frontend build**

Run: `node --test frontend/src/benches/search/searchSpans.test.ts`
Expected: PASS (all three).
Run (from `frontend/`): `npm run lint && npm run build`
Expected: SUCCESS.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/benches/search/searchTypes.ts \
        frontend/src/benches/search/searchApi.ts \
        frontend/src/benches/search/searchSpans.ts \
        frontend/src/benches/search/searchSpans.test.ts
git commit -m "feat(search): add frontend types, API client, and span-reconstruction helper"
```

---

### Task 7: Frontend lineage / highlight-membership logic

**Files:**
- Create: `frontend/src/benches/search/searchLineage.ts`
- Test: `frontend/src/benches/search/searchLineage.test.ts`

**Interfaces:**
- Consumes: `SearchTraceResponse` (Task 6 types).
- Produces: pure helpers driving click-to-trace — `ordinalForCteLabel(plan, label): number | null`, `isRowSelected(label, selectedOrdinal, plan): boolean`, `isRangeSelected(label, selectedOrdinal, plan): boolean`. Consumed by Task 8's component.

**Design:** A single `selectedOrdinal: number | null` drives all highlighting (spec's "Click-to-trace"). Plan rows and SQL ranges join to a parameter ordinal through their `"cte{i}"` label: `label → CteProvenance.cteIndex → CteProvenance.parameterOrdinal`. Rows/ranges with non-`cte` labels (`root`, `inc{i}`, `sort`, `page`, `countOnly`) or a null `parameterOrdinal` (`:missing`, compartment, structural CTEs) never match a selection.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/benches/search/searchLineage.test.ts`:

```typescript
/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { ordinalForCteLabel, isRowSelected, isRangeSelected } from './searchLineage.ts';
import type { QueryPlan } from './searchTypes.ts';

const plan: QueryPlan = {
  explain: '',
  rows: [
    { label: 'root', body: 'Resource Patient' },
    { label: 'cte0', body: 'ParamSource name' },
    { label: 'cte1', body: 'ParamSource gender' },
  ],
  ctes: [
    { cteIndex: 0, parameterOrdinal: 0, span: null },
    { cteIndex: 1, parameterOrdinal: 1, span: null },
  ],
};

test('a cte label resolves to its owning parameter ordinal', () => {
  assert.equal(ordinalForCteLabel(plan, 'cte1'), 1);
});

test('a non-cte label resolves to null', () => {
  assert.equal(ordinalForCteLabel(plan, 'root'), null);
});

test('a plan row is selected when its cte maps to the selected ordinal', () => {
  assert.equal(isRowSelected('cte0', 0, plan), true);
  assert.equal(isRowSelected('cte1', 0, plan), false);
  assert.equal(isRowSelected('root', 0, plan), false);
});

test('nothing is selected when selectedOrdinal is null', () => {
  assert.equal(isRowSelected('cte0', null, plan), false);
  assert.equal(isRangeSelected('cte0', null, plan), false);
});

test('a sql range is selected when its cte maps to the selected ordinal', () => {
  assert.equal(isRangeSelected('cte1', 1, plan), true);
  assert.equal(isRangeSelected('cte1', 0, plan), false);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test frontend/src/benches/search/searchLineage.test.ts`
Expected: FAIL — `searchLineage.ts` does not exist.

- [ ] **Step 3: Implement `searchLineage.ts`**

```typescript
import type { QueryPlan } from './searchTypes';

/** Resolves a plan-row / SQL-range label of the form "cte{i}" to the parameter ordinal that produced it,
 * or null for any other label (root, inc{i}, sort, page, countOnly) or an unattributed CTE. */
export function ordinalForCteLabel(plan: QueryPlan | null, label: string): number | null {
  if (!plan) {
    return null;
  }
  const match = /^cte(\d+)$/.exec(label);
  if (!match) {
    return null;
  }
  const cteIndex = Number(match[1]);
  const cte = plan.ctes.find((c) => c.cteIndex === cteIndex);
  return cte?.parameterOrdinal ?? null;
}

/** Whether a plan row with `label` belongs to the currently selected parameter ordinal. */
export function isRowSelected(label: string, selectedOrdinal: number | null, plan: QueryPlan | null): boolean {
  return selectedOrdinal !== null && ordinalForCteLabel(plan, label) === selectedOrdinal;
}

/** Whether a SQL text range with `label` belongs to the currently selected parameter ordinal. Same join as
 * plan rows — SQL ranges use the identical "cte{i}" labelling. */
export function isRangeSelected(label: string, selectedOrdinal: number | null, plan: QueryPlan | null): boolean {
  return selectedOrdinal !== null && ordinalForCteLabel(plan, label) === selectedOrdinal;
}
```

- [ ] **Step 4: Run the test + build**

Run: `node --test frontend/src/benches/search/searchLineage.test.ts`
Expected: PASS (all five).
Run (from `frontend/`): `npm run lint && npm run build`
Expected: SUCCESS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/benches/search/searchLineage.ts \
        frontend/src/benches/search/searchLineage.test.ts
git commit -m "feat(search): add click-to-trace lineage/highlight helpers"
```

---

### Task 8: `SearchBench` component + debounced auto-run hook

**Files:**
- Create: `frontend/src/benches/search/useSearchTrace.ts`
- Create: `frontend/src/benches/search/SearchBench.tsx`

**Interfaces:**
- Consumes: `runSearch` (Task 6), `spanSegments` (Task 6), `isRowSelected`/`isRangeSelected`/`ordinalForCteLabel` (Task 7), the shared bench primitives/styles (`../components/primitives`, `../components/styles`), `useIsNarrowViewport`, `getErrorMessage` (`../shared/errorMessage`).
- Produces: `SearchBench` (default export shape matching the other benches — a component taking no required props). Consumed by Task 9's `BenchesApp`.

**Layout (ports the mock's `isSearch` block):** resource-type pills (Patient/Observation/Encounter), an expression textarea with a `GET /{resourceType}?` prefix label, example chips, a complexity badge reading `Plan.Ctes.length`, a three-column trace grid, and a SQL panel with SQL/Explain tabs. The three columns and SQL panel all read the single `selectedOrdinal` state; clicking any syntax span / IR row / plan row / SQL segment sets it (plan rows and SQL segments resolve through `ordinalForCteLabel`), and clicking empty space clears it (the mock's "click-to-clear" affordance).

- [ ] **Step 1: Implement the debounced auto-run hook**

Create `frontend/src/benches/search/useSearchTrace.ts` (mirrors `useFhirPathEval`'s debounce-and-abort pattern):

```typescript
import { useEffect, useRef, useState } from 'react';
import { runSearch } from './searchApi';
import type { SearchTraceResponse } from './searchTypes';
import { getErrorMessage } from '../shared/errorMessage';

const DEBOUNCE_MS = 450;

export interface SearchTraceState {
  result: SearchTraceResponse | null;
  error: string | null;
  isLoading: boolean;
}

const EMPTY: SearchTraceState = { result: null, error: null, isLoading: false };

/** Debounced, abortable search-trace runner: re-GETs ~450ms after the last change to resourceType/query,
 * cancelling any still-in-flight request first. */
export function useSearchTrace(resourceType: string, query: string): SearchTraceState {
  const [state, setState] = useState<SearchTraceState>(EMPTY);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      setState((prev) => ({ ...prev, isLoading: true }));

      runSearch(resourceType, query, controller.signal)
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
  }, [resourceType, query]);

  return state;
}
```

- [ ] **Step 2: Implement `SearchBench.tsx`**

Create `frontend/src/benches/search/SearchBench.tsx`. Build it from the shared primitives, following `FhirPathBench.tsx`'s structure (header + `Card`s + a responsive grid). Implement, at minimum, every element below — this is the parity contract with the mock's `isSearch` block:

- **Header:** `<h1>Search</h1>`, a subtitle ("Trace a FHIR search query from parse to generated SQL."), and an `engineBadgeStyle` badge. When `result.plan` is present, render a complexity badge reading the CTE count, e.g. `` `${result.plan.ctes.length} CTEs` ``.
- **Input card:** resource-type `Pills` (`RESOURCE_TYPES`), a query textarea prefixed with a `GET /{resourceType}?` label, and example chips from `EXAMPLE_QUERIES[resourceType]` (clicking a chip sets the query). Changing the resource type or query drives `useSearchTrace`.
- **Error banner:** when `state.error !== null`, render `<ErrorBanner message={state.error} />` (matches the `fpHasError`/`sofHasError` pattern). When `result.failure !== null`, render its `stage`/`message` in the same banner style.
- **Three-column trace grid** (stacks on narrow viewports via `useIsNarrowViewport`), driven by `selectedOrdinal` state:
  - **Search column** — one block per `parameters[]`. Render the parameter's `key` string sliced by `spanSegments(param.key, param.keySyntax, 'Key')` and its `value` string by `spanSegments(param.value, param.valueSyntax, 'Value')`. Each segment whose `node !== null` is clickable and sets `selectedOrdinal = param.ordinal`; highlight it when `selectedOrdinal === param.ordinal`. When `param.outcome.kind === 'Ignored'`, render the block muted/dashed with `outcome.reason` inline (a per-parameter warning, not a page-level error). When `'Failed'`, render its `reason`/`stage` inline on that block.
  - **Search Expression column** — one block per parameter; render `param.ir[]` as an indented list (indent by `row.depth`), each row a kind-chip (`row.kind`) + `row.text`, reusing `chipStyle(...)` as `FhirPathBench`'s `AstRows` does. Clicking a row sets `selectedOrdinal = param.ordinal`; highlight the block when selected.
  - **Lowered AST column** — render `result.plan.rows[]` (each `{ label, body }`) as an indented list. A row is clickable when `ordinalForCteLabel(result.plan, row.label) !== null`, setting `selectedOrdinal` to that ordinal; highlight it when `isRowSelected(row.label, selectedOrdinal, result.plan)`.
- **SQL panel** with SQL/Explain `Pills` tabs:
  - **SQL tab** — render `result.sql.sql` in a `<pre>`, sliced at each `result.sql.ranges[]` offset into segments (use the same left-to-right cut approach as `spanSegments`, but over `sql` with `{start,length,label}` ranges). A segment covered by a `cte{i}` range is clickable (sets `selectedOrdinal` via `ordinalForCteLabel`) and highlighted when `isRangeSelected(range.label, selectedOrdinal, result.plan)`.
  - **Explain tab** — render `result.plan.explain` as plain preformatted text (no per-line interactivity; the interactive case is covered by the Lowered AST column).
- **Implicit chips (optional but in the DTO):** render `result.implicit[]` as small muted chips reading `` `${name}=${value}` `` with `reason` as a title tooltip.
- **Click-to-clear:** a click on the trace grid's empty background sets `selectedOrdinal = null`.

Use `useState<number | null>(null)` for `selectedOrdinal`. Do NOT accept or use `onOpenFakes`, a `fakesSeed`, or any share-state props — Search has no Fakes handoff or share state in v1.

- [ ] **Step 3: Lint + build**

Run (from `frontend/`): `npm run lint && npm run build`
Expected: SUCCESS. Fix any oxlint/tsc issues (unused vars, exhaustive-deps) before committing.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/benches/search/useSearchTrace.ts frontend/src/benches/search/SearchBench.tsx
git commit -m "feat(search): add SearchBench component and debounced auto-run hook"
```

---

### Task 9: Tab wiring — make Search a live fifth tab

**Files:**
- Modify: `frontend/src/benches/BenchesApp.tsx`
- Modify: `frontend/src/lib/shareLinks.ts`

**Interfaces:**
- Consumes: `SearchBench` (Task 8).
- Produces: the Search tab, live and selectable, in the running app.

- [ ] **Step 1: Add `'search'` to `shareLinks.ts`**

In `frontend/src/lib/shareLinks.ts`, change the `BenchId` union (line ~7):

```typescript
export type BenchId = 'fhirpath' | 'validation' | 'fml' | 'sqlonfhir' | 'fakes';
```
to
```typescript
export type BenchId = 'fhirpath' | 'validation' | 'fml' | 'sqlonfhir' | 'fakes' | 'search';
```

and the `BENCHES` array (line ~75) so a `?bench=search` deep link round-trips:

```typescript
const BENCHES: BenchId[] = ['fhirpath', 'validation', 'fml', 'sqlonfhir', 'fakes'];
```
to
```typescript
const BENCHES: BenchId[] = ['fhirpath', 'validation', 'fml', 'sqlonfhir', 'fakes', 'search'];
```

(No `SearchShareState` is added — Search has no share state in v1. `BenchShareState` is unchanged.)

- [ ] **Step 2: Wire the tab in `BenchesApp.tsx`**

In `frontend/src/benches/BenchesApp.tsx`:

Add the import (alongside the other bench imports near the top):
```typescript
import { SearchBench } from './search/SearchBench';
```

Change the local `BenchId` union (line ~14):
```typescript
type BenchId = 'fhirpath' | 'validation' | 'fml' | 'sqlonfhir' | 'fakes';
```
to
```typescript
type BenchId = 'fhirpath' | 'validation' | 'fml' | 'sqlonfhir' | 'fakes' | 'search';
```

Add the tab to `BENCH_TABS` (NOT disabled — live from day one). Place it after `fakes`:
```typescript
const BENCH_TABS: PillItem<BenchId>[] = [
  { id: 'fhirpath', label: 'FHIRPath' },
  { id: 'validation', label: 'Validation' },
  { id: 'fakes', label: 'Fakes' },
  { id: 'search', label: 'Search' },
  { id: 'sqlonfhir', label: 'SQL on FHIR', disabled: true, title: 'Not yet implemented' },
  { id: 'fml', label: 'FML', disabled: true, title: 'Not yet implemented' },
];
```

Add `'search'` to the `live engine` label condition (line ~123):
```typescript
{bench === 'fhirpath' || bench === 'validation' || bench === 'fakes' ? 'live engine' : 'mock engine · exploration'}
```
becomes
```typescript
{bench === 'fhirpath' || bench === 'validation' || bench === 'fakes' || bench === 'search' ? 'live engine' : 'mock engine · exploration'}
```

Render `<SearchBench />` in the `<main>` block, alongside the other benches (Search takes no props — no Fakes handoff, no share state):
```typescript
{bench === 'search' ? <SearchBench /> : null}
```

- [ ] **Step 3: Lint + build**

Run (from `frontend/`): `npm run lint && npm run build`
Expected: SUCCESS. If tsc complains that `'search'` is not assignable to `shareLinks`' `BenchId` in `buildBenchShareUrl(bench, ...)`, Step 1 was missed — the two `BenchId` unions must both include `'search'`.

- [ ] **Step 4: Run all frontend pure-function tests once more**

```bash
node --test frontend/src/benches/search/searchSpans.test.ts
node --test frontend/src/benches/search/searchLineage.test.ts
```
Expected: all PASS.

- [ ] **Step 5: Manual smoke check (optional but recommended)**

Start the backend (`func start` from `backend/src/Ignixa.Lab.Functions`, after `backend-check` is green) and the frontend (`npm run dev`), open the Search tab, type `name=Smith&birthdate=gt2000-01-01`, and confirm: the three columns populate, clicking a value span highlights the matching IR row / plan row / SQL segment, and clicking empty space clears the selection. Note any parity gap against the mock in your report.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/benches/BenchesApp.tsx frontend/src/lib/shareLinks.ts
git commit -m "feat(search): wire Search as a live fifth Expression Benches tab"
```

---

## Self-Review

**Spec coverage:**
- Package bump 0.6.19→0.6.23 + two new packages + local-feed → Task 1. ✓
- `SearchFunctions` endpoint (`GET /api/search/{resourceType}`, anonymous, raw query, R4-only, plain JSON) → Task 5. ✓
- `InMemorySymbolResolver : ISymbolResolver` (sequential ids, distinct per registry) → Task 2. ✓
- `SearchEngineFactory` (lazy-per-version, R4 builder + definition + compartment managers) → Task 3. ✓
- `SearchTraceResponse` DTO field-for-field with `SyntaxNode`/`IrRow` projections and `Outcome` union → Task 4. ✓
- Backend tests: resolver, mapper (one per Outcome variant + `Ctes[].ParameterOrdinal` survives + null plan), endpoint end-to-end (name=Smith, chained, unknown) → Tasks 2/4/5. ✓
- Frontend `search/` split (`searchTypes.ts`, `searchApi.ts`, `SearchBench.tsx`), debounced auto-run + abort → Tasks 6/8. ✓
- Three-column grid, span reconstruction (per-parameter Key/Value, recursing children), IR indent, plan-row `cte{i}` join, SQL slicing, Explain tab, complexity from `Ctes.length`, Ignored inline, Failure banner → Tasks 6/7/8. ✓
- Frontend pure-function tests (span reconstruction + ordinal-matching), following the `jsonPathResolver.test.ts` precedent → Tasks 6/7. ✓
- Tab wiring (`'search'` in `BenchId`, `BENCH_TABS` not disabled, `live engine` label, render) + no Fakes/share for v1 → Task 9. ✓
- Out of scope (other FHIR versions, live SQL Server, Fakes handoff, share links, enabling FML/SoF, production data path) — none added. ✓

**Open items flagged for the implementer (verify against the compiled 0.6.23 packages, not planning-time source):**
1. **Constructor signatures** used in `SearchEngineFactory` (Task 3) — `SearchParameterDefinitionManager`, `ReferenceSearchValueParser`, `SearchParameterExpressionParser`, `ExpressionParser`, `SearchOptionsBuilder`, `CompartmentDefinitionManager`, and the nested delegate type `ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver`. The build will name any break; the Task-5 end-to-end test is the proof of correctness.
2. **`SearchParameterInfo` constructor** used in the Task-2 test helper (`(name, code, SearchParamType, Uri)` and the `.Url`/`.Code` members).
3. **Source record positional order** for the Task-4 mapper tests (`SearchTrace`, `ParameterTrace`, `QueryPlanTrace`, `CteProvenance`, `SqlTextRange`, `PlanExplainRow`, `TraceFailure`, `ImplicitParameter`) — read from source at planning time; adjust test constructors + mapper together if the packaged shape differs.
4. **Unknown-parameter behavior** (Task 5) — page-level `Failure` vs per-parameter `Ignored`/`Failed`; the test accepts either. Tighten once observed.
5. **`SourceSpan` = `{Origin, Start, Length}`** (already resolved and baked into the DTO/types — the design doc's provisional `{Start, Length}` is superseded).

**Type consistency:** DTO property names (Task 4) ↔ TS interface fields (Task 6) match field-for-field in camelCase. `spanSegments`/`Segment` (Task 6) and `ordinalForCteLabel`/`isRowSelected`/`isRangeSelected` (Task 7) signatures match their Task-8 call sites. `SearchEngine` tuple/record shape (Task 3) matches the `engine.Builder`/`engine.SearchParameters`/`engine.Compartments` access in Task 5.
