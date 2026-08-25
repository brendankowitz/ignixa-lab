# Ignixa FHIR 0.6.68 integration

**Status:** Proposed
**Date:** 2026-08-24
**Scope:** Upgrade the Ignixa package family used by `ignixa-lab` and adapt the lab to the release's breaking API changes and directly useful FHIRPath capabilities.

## Context

The lab currently pins the Ignixa package family to `0.6.41`, with `Ignixa.Search.Sql` at `0.6.41-alpha` and TestScript packages at `0.6.41-beta`. Release `0.6.68` is published on NuGet and replaces the old three-stage Search.Sql tracing entry point with a two-phase plan/compile API. It also changes FHIR temporal primitive values from raw strings to `FhirTemporal`, adds FHIRPath instance selectors, improves in-instance `resolve()`, and hardens several Search.Sql and FHIRPath correctness paths.

The lab's Search bench is intentionally SQL-server-free. It uses an in-memory symbol resolver to demonstrate planning and SQL emission, so it needs the new compiler seam but does not need to adopt the release's SQL Server execution or schema-deployment projects.

## Goals

- Move all direct Ignixa package references to the `0.6.68` family:
  - stable packages: `0.6.68`
  - Search.Sql: `0.6.68-alpha`
  - TestScript packages: `0.6.68-beta`
- Keep the existing Search bench response JSON contract so the frontend visualization, lineage, span, and SQL highlighting code remains compatible.
- Replace the removed `SearchCompiler.CompileAsync` and tracing types with the `ISearchSqlCompiler` two-phase flow.
- Surface plan and compilation diagnostics through the existing trace DTOs, including explicit handling for new diagnostic outcome values and failures.
- Serialize FHIRPath instance-selector nodes in the AST shape expected by the frontend.
- Preserve correct temporal primitive output when `IElement.Value` is a `FhirTemporal`, including FHIRPath evaluation results and resolver lookups.
- Add focused regression coverage for the migration and the release behaviors used by the lab.

## Non-goals

- Do not add a SQL Server connection, retry, EF data-layer, or schema-deployment dependency.
- Do not port upstream repository workflow, documentation-site, or package-readme changes.
- Do not redesign the frontend Search or FHIRPath contracts unless the `0.6.68` API makes a compatible representation impossible.
- Do not replace the lab's deterministic in-memory symbol resolver with a production storage implementation.

## Design

### Package and dependency integration

Update `Directory.Packages.props` only for Ignixa packages used by the two backend projects. Restore from NuGet, using the already downloaded release artifacts only as a local fallback while validating. Do not commit package binaries or a machine-specific feed configuration.

After the version change, restore and build before adapting code so compiler diagnostics identify the complete API delta. Any transitive package changes are accepted only when selected by the `0.6.68` package family and do not require unrelated manifest changes.

### Search compiler seam

Retain `SearchEngineFactory` as the per-FHIR-version cache for the options builder, search-parameter definitions, and compartment definitions. Construct `SearchSqlCompiler` per request with a fresh `InMemorySymbolResolver`; resolver state is intentionally request-scoped so surrogate IDs remain deterministic within one trace and do not leak between requests.

`CompileAndRespondAsync` will:

1. Parse the query parameters using the existing parser.
2. Build `SearchPlanOptions`, including the operation expression for `$everything` and compartment routes, and request full diagnostics for the Search bench.
3. Call `CreatePlanAsync` (or its options-based equivalent when the route already owns `SearchOptions`).
4. Call `SearchPlan.Compile()` and read the resulting `CompiledSearch`.
5. Map `SearchPlan.Diagnostics`, `CompiledSearch.Diagnostics`, and `SearchCompilationFailure` into the current response DTOs.

The response mapper will preserve the current top-level fields (`fhirVersion`, `resourceType`, `parameters`, `plan`, `sql`, `implicit`, and `failure`). New upstream fields such as CTE provenance and SQL ranges will be included only where they map cleanly to existing DTO fields; otherwise they remain available in backend diagnostics without inventing a frontend contract. Query parse errors and caller-invalid search values remain 400 responses, while compiler or mapper defects remain explicit 500 responses with server-side logging.

### FHIRPath AST and value handling

Add `VisitInstanceSelector` to `JsonAstVisitor`. It will emit the existing node envelope and serialize the selector's type name, optional namespace, element assignments, and child expressions as structured JSON. Existing node naming and inferred return-type behavior remain unchanged.

Audit the FHIRPath evaluator and result formatter for assumptions that primitive `IElement.Value` values are strings. Use the temporal value's canonical FHIR representation when producing JSON, display text, parameters, and trace output. Keep non-temporal primitive and complex-element behavior unchanged.

The lab's lightweight resolver already handles contained resources. Add coverage for the release's sibling Bundle/entry reference behavior and avoid duplicating upstream resolution logic where the engine now supplies it directly; the lab resolver remains responsible only for the custom external/type-checking fallback.

## Error handling

Use the new compiler's explicit `TryCreatePlan`/`TryCompile` result types where they make the request-vs-server distinction clearer. Do not catch the broad `FhirException` hierarchy as a client error. Preserve cancellation, allowlisted malformed-query errors, unsupported query-shape handling, and logged internal failures.

If an upstream diagnostic or parameter outcome is not representable, fail the mapper explicitly and return the existing internal-error response rather than silently dropping evidence.

## Testing

- Update package-sensitive Search tests and mapper tests to construct and assert the `0.6.68` plan/compile diagnostics.
- Preserve coverage for normal searches, operation expressions, malformed inputs, unsupported shapes, unknown parameters, SQL parameters, and mapper failures.
- Add an AST serialization test for an instance selector with a typed object and multiple assignments.
- Add FHIRPath regression tests for temporal primitive output and contained/Bundle `resolve()` behavior.
- Run the existing backend test project and frontend test/build commands; no new test framework or tooling is introduced.

## Rollout and compatibility

The change is a package-and-adapter migration. The frontend receives the same route shapes and response fields, so deployment does not require a coordinated frontend feature flag. If the package upgrade exposes an incompatibility that cannot be represented without changing the frontend contract, stop at the backend adapter boundary and document the required contract change rather than silently changing output.
