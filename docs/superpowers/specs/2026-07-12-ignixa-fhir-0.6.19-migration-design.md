# Design: Migrate onto ignixa-fhir 0.6.19 (assertion alternatives + waitFor)

**Date**: 2026-07-12
**Status**: Approved for implementation
**Related**: [ignixa-fhir#324](https://github.com/brendankowitz/ignixa-fhir/issues/324), [ignixa-fhir#338](https://github.com/brendankowitz/ignixa-fhir/pull/338) (evaluator execution, merged), [ignixa-fhir#333](https://github.com/brendankowitz/ignixa-fhir/pull/333) (waitFor, merged), [release/0.6.19](https://github.com/brendankowitz/ignixa-fhir/releases/tag/release%2F0.6.19)

## Problem

`ignixa-lab` pins `Ignixa.TestScript`/`Ignixa.TestScript.FhirFakes` at `0.6.7-beta` and the other five lockstep `Ignixa.*` packages at `0.6.7`. Release `0.6.19` ships the two engine features `ignixa-lab`'s own reliability work was designed around: real evaluator execution for `assertionAnyOfGroup`/`assertionWhenResponseStatus` (not just parsing), and the `waitFor` operation extension for polling async jobs to completion. Consuming the release is the unlock for the actual point of that work — deleting `StatusAlternativeEnforcementPlan`/`WarningOnlyStatusAlternativeEnforcer` in favor of the real extensions, and closing the "job completion not covered" gap in the export/import suites — not an end in itself.

This is one initiative staged as four ordered phases, each independently testable, executed back-to-back without stopping between them.

## Phase 1: Package bump + build fix

Bump all 8 `Ignixa.*` entries in `Directory.Packages.props` together — NuGet confirms every one jumps straight from `0.6.7` to `0.6.19` with nothing published between, so this is one release train, not independently-versioned packages:

- `Ignixa.TestScript`, `Ignixa.TestScript.FhirFakes` → `0.6.19-beta`
- `Ignixa.FhirPath`, `Ignixa.Serialization`, `Ignixa.Specification`, `Ignixa.Validation`, `Ignixa.PackageManagement`, `Ignixa.FhirFakes` → `0.6.19`

`0.6.19` makes `BaseJsonNode.MutableNode` internal (hidden from the public SDK surface). Nine call sites across four files break:
`ConformanceReportMapper.cs:162`, `RunScopedDefinitionPreparer.cs:41`, `TestScriptRunner.cs:138`, `ResultFormatter.cs:221,279,308,381,405,408`. Fix per the upstream-established pattern (PR #316): wrap each access as `((IMutableJsonNode)expr).MutableNode`, adding `using Ignixa.Serialization.SourceNodes;` where missing. This is a mechanical unblock, not a redesign — but raw mutable-JSON access is an antipattern regardless of the cast wrapper, and four production call sites relying on it is existing debt this phase doesn't fix. Upstream's own "source-generated typed FHIR models" work (also shipped in 0.6.19, still in progress — "two down, many more to go") is the eventual real fix: once typed accessors exist for the fields these four call sites touch, they should move off raw JSON entirely. Flag this explicitly as a follow-up, don't attempt it now — it's a bigger, separately-motivated refactor blocked on upstream work that isn't finished.

After the build is green: full backend unit test suite, then the bundled TestScript suites against both real targets — `https://subscriptions.argo.run/` (fhir-candle) and `https://bkowitz-testdeploy.azurewebsites.net` (MS FHIR Server) — diffed against a pre-bump baseline run on the same two targets. 0.6.19 also changed unrelated engine behavior (choice-variant primitive-extension shadow cleanup, R5/R6 `CodeableReference` shape corrections, CodeSystem/ValueSet conformance checks now also running at `Compatibility` depth) that could shift pass/fail on existing suites independent of the `MutableNode` fix. Triage any suite whose result actually changed — the two most plausible sources are named above; anything else is a genuine surprise, not a rubber-stamp "must be fine."

## Phase 2: `ConformanceReportMapper` extension

`ConformanceReportMapper.MapActions` (`Execution/ConformanceReportMapper.cs:123-149`) maps `ActionResult` fields one-to-one into `ConformanceStep`, but never reads the new `ActionResult.GroupId`/`Members` (added in ignixa-fhir PR #338). Migrating any suite onto `assertionAnyOfGroup` before this lands would silently regress the dashboard: a trio of alternatives that renders as 3 steps today collapses to 1 aggregate step, and per-alternative diagnostic detail (which alternative matched, why the others didn't) disappears from view — the aggregate `Message` string summarizes, but structured detail is lost. This phase must land before Phase 4 (suite migration) for that reason.

Add `GroupId`/`Members` to `ConformanceStep` (a new `ConformanceGroupMember` DTO mirroring `AssertionGroupMemberResult`'s shape), wire `MapActions` to populate them, and update `docs/conformance-report-schema.md` plus whatever frontend TypeScript types consume `ConformanceStep` (locate during implementation — the frontend's `useConformanceRun`/report-rendering components are the likely consumers based on this repo's structure). Extend `ConformanceReportMapperTests.cs` with a case asserting a grouped `ActionResult` maps its members through.

## Phase 3: Suite migration — retire the status-alternative workarounds

`StatusAlternativeEnforcementPlan`/`WarningOnlyStatusAlternativeEnforcer` (`Execution/`) run as a post-processing pass in `TestScriptRunner.cs:223`, over the already-mapped `ConformanceResult[]`, keyed by a per-suite plan sourced from `ISuiteCatalog` (`StatusAlternativePlan` property, `ISuiteCatalog.cs:12`). Three policies exist today (`StatusAlternativeEnforcementPlan.cs`): `SubscriptionDeleteReadback` (used by `Subscriptions/basic.json` — DELETE 200/202/204 trio, then a GET readback trio conditioned on the DELETE's actual status), `DeletedResourceReadback` (used by `CRUD/delete.json` — classic 410/404 pair), and `ResponseStatusSet` (used by `CRUD/create.json`/`conditional-delete.json`/`read.json` — "400 OR 422"-style pairs after a single operation). All three are expressible with `assertionAnyOfGroup`/`assertionWhenResponseStatus` — verified against the actual engine semantics during the ignixa-fhir PR's own Fable review, which found the engine's version stricter in places (e.g. it enforces the group regardless of whether a matching prior operation is found, where the enforcer only enforces when one is).

Per that review's recommendations, migrated JSON must:
- Use a **distinct group id per logical group** (the DELETE-status trio and the readback trio are two separate groups, never one spanning both — nothing stops an author from merging them into one id today since the parser only validates within a declared group, not across the test).
- Set **explicit `sourceId`** on every group member (rather than relying on implicit `LastResponse`), so the parser's same-sourceId group validation actually catches authoring mistakes, and so a failed/skipped operation surfaces as a group `Error` rather than silently evaluating against a stale response.

Migrate one suite at a time (`Subscriptions/basic.json` first — it's the motivating cross-operation-conditional case and exercises both extensions together; then the four `CRUD/*.json` files), verifying each against both live targets before moving to the next. Only after all five are migrated: delete `StatusAlternativeEnforcementPlan.cs`, `WarningOnlyStatusAlternativeEnforcer.cs`, their test files, the `TestScriptRunner.cs:223` call site, and the `ISuiteCatalog`/per-suite plan wiring.

## Phase 4: `waitFor` adoption

`Operations/export-data.json` documents its own gap in its `description` field: "Scenarios requiring actual export completion and blob content verification are not covered." Add a `waitFor`-based test (or extend an existing kickoff test) that polls the `Content-Location` status endpoint to completion using the pattern from ignixa-fhir's own `waitFor` docs (`docs/site/docs/core-sdk/testscript.md` in that repo), then assert on the completed manifest. Check the Microsoft import/bulk suites (`Microsoft/ms-import-basic.json`, `ms-import-history-soft-delete.json`, `ms-import-rebuild-indexes.json`, `ms-bulk-delete.json`, `ms-bulk-update.json`) for the same "kickoff only, never verified to completion" pattern during implementation and adopt `waitFor` there too if present. This phase is independent of Phases 2-3 — only depends on Phase 1's package bump.

## Testing (all phases)

Every phase ends with: `dotnet build`/`dotnet test` on the backend, and a live run of the affected suite(s) against both `https://subscriptions.argo.run/` and `https://bkowitz-testdeploy.azurewebsites.net`, diffed against the pre-phase baseline. No suite's pass/fail may regress without being explicitly triaged and explained.

## Out of Scope

- Migrating `TestScriptContentNormalizer` (shorthand normalization) or `RunScopedDefinitionPreparer` (variable interpolation) — unrelated gaps from the same upstream issue #324, not addressed by this release.
- Moving the four `MutableNode` call sites onto typed models — blocked on upstream's in-progress typed-model consolidation.
- Standing up a new target FHIR server — two existing live targets are used throughout.
