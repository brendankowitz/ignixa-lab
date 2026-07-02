# FHIRPath Evaluator Merge — Implementation Design

**Date:** 2026-07-02
**Status:** Approved (brainstorming) — pending implementation plan
**Feature docs:** `docs/features/fhirpath-evaluator/` (readme + `same-function-app-merge` investigation)

## Goal

Port Brian's `fhirpath-lab-dotnet` (`FhirPathLab-DotNetEngine`, the "Ignixa" engine
behind fhirpath-lab.com's dotnet FHIRPath evaluator option) into the `ignixa-lab`
repo and solution, deployed as part of the existing `ignixa-lab` Azure Functions
app. Source: `E:\data\src\fhirpath-lab-dotnet`, ~1,831 lines across 11 files.

## Scope decisions

- **Deploy target: same Function App.** Routes are added to the existing
  `Ignixa.Lab.Functions` app rather than a second Function App. Asked as an
  explicit choice; no objection was raised, and this matches the investigation's
  top recommendation (one deploy step, one App Insights resource, traffic is
  read-only/stateless so shared blast radius is acceptable). Revisit as a
  separate investigation later if operational data says otherwise.
- **Package versions: latest aligned release, 0.5.11.** `Ignixa.FhirPath`,
  `Ignixa.Serialization`, and `Ignixa.Specification` all publish through
  `0.5.11` on nuget.org, matching the version line `Ignixa.TestScript` /
  `Ignixa.TestScript.FhirFakes` are already pinned to (`0.5.11-beta`) in this
  repo's `Directory.Packages.props`. Confirmed via `nuget.org` version listing
  before pinning — explicitly requested by the user over the source repo's
  older `0.5.6`.
- **Routes: kept exactly as they are in the source repo, unprefixed.**
  `metadata`, `$fhirpath-stu3`, `$fhirpath-r4`, `$fhirpath-r4b`, `$fhirpath-r5`,
  `$fhirpath-r6`. These are already globally unique against ignixa-lab's
  existing `health`/`suites`/`run`/`capability` routes. Keeping them identical
  is what lets fhirpath-lab.com's UI repoint at this app later with zero
  UI-side changes (a route rename would require Brian to update the UI's
  engine config, which is out of scope here).
- **CORS: not configured in this PR.** No production origin for
  fhirpath-lab.com's UI was available to pin down; fhirpath-lab.com isn't
  cutting over to this deployment as part of this PR anyway (see "Out of
  scope" below), so this is a small, well-isolated follow-up right before that
  cutover rather than a guess baked in now. The routes remain reachable
  same-origin / via direct API calls (e.g. `curl`, Postman, the ported test
  suite) without CORS.
- **Rate-limit classification: explicit, not fail-safe-default.** Ignixa-lab's
  `EndpointClassifier` already defaults any unrecognized function name to the
  strictest (`Run`) tier, so leaving the new functions unclassified would be
  *safe* but wrong-shaped (FHIRPath evaluation is cheaper than a
  whole-suite run). Classify `metadata` and all five `$fhirpath-*` functions
  as `EndpointClass.Capability` (cheap-to-moderate, matches the existing
  `GET /api/capability` tier: a single unit of work per call). This does not
  distinguish the "remote `resource` URL fetch" sub-case (which does an
  outbound call, like `capability` already does) from the cheap inline-resource
  case — that's an accepted simplification, not a new tiering axis, consistent
  with how `Capability` already covers `/api/capability`'s single outbound
  call today.

## Ported components

New code lives under `backend/src/Ignixa.Lab.Functions/`, following the
existing `Functions/` + service-folder split (`Suites/`, `Execution/`,
`Middleware/`):

- **`Functions/FhirPathFunctions.cs`** — one Functions class (mirroring
  `FunctionFhirPathTest.cs`), constructor-injecting the services below, with:
  - `[Function("FhirPathMetadata")]` → `GET metadata`
  - `[Function("FhirPathStu3")]` → `GET/POST $fhirpath-stu3`
  - `[Function("FhirPathR4")]` → `GET/POST $fhirpath-r4`
  - `[Function("FhirPathR4B")]` → `GET/POST $fhirpath-r4b`
  - `[Function("FhirPathR5")]` → `GET/POST $fhirpath-r5`
  - `[Function("FhirPathR6")]` → `GET/POST $fhirpath-r6`
  - `[Function("FhirPathWarmer")]` → timer trigger, `0 */15 * * * *`
- **`Services/FhirPath/`** — `FhirPathService.cs`, `SchemaProviderFactory.cs`,
  `ExpressionAnalyzer.cs`, `ExpressionEvaluator.cs`, `ResultFormatter.cs`,
  `LightweightElementResolver.cs`, ported near-verbatim, namespaced
  `Ignixa.Lab.Functions.Services.FhirPath`.
- **`Serialization/JsonAstVisitor.cs`** — ported as-is; its AST-shape doc
  comments (matching fhirpath-lab UI expectations) are preserved verbatim
  since that shape is the external contract this whole port must not break.
- **`Models/FhirPathRequest.cs`, `Models/FhirPathResult.cs`** — ported as-is.

All five DI services register as singletons in `Program.cs`, same lifetime
the source repo used and consistent with `ignixa-lab`'s existing
`ISuiteCatalog`/`IEvaluatorFactory` singleton registrations.

## Fixes applied during the port

Two real gaps identified in the investigation are closed as part of this
port, not deferred:

1. **SSRF guard on resource-fetch-by-URL.** `FhirPathService`'s
   `LoadResourceFromUrl` currently makes an unguarded outbound call to any
   caller-supplied URL. It's rewired to validate through the existing
   `Execution/TargetUrlValidator`, the same guard `RunFunction` and
   `CapabilityFunction` already use, respecting
   `IgnixaLabOptions.AllowPrivateTargets` for local dev.
2. **`IHttpClientFactory` instead of `new HttpClient()`.** `FhirPathService`
   currently constructs a raw `HttpClient` per call. Switched to
   `IHttpClientFactory`, registered the same way `HttpEvaluatorFactory`
   already registers its named client in `Program.cs`.

Target framework is bumped from the source repo's `net9.0` to `net10.0` to
match `Ignixa.Lab.Functions` — a single-TFM project, no reason to special-case
one folder.

## Deploy pipeline

`backend-deploy.yml` is unchanged in structure — it already publishes and
deploys the whole `Ignixa.Lab.Functions` project, so the new functions ship
automatically with the next deploy. No new workflow file, no new Azure
resource, no new OIDC secrets.

## Testing

`FhirPathLab.Tests` (1,721 lines: `IgnixaApiTests.cs`, `OfficialTestResultGenerator.cs`,
`ResultFormatterTests.cs`) is ported into `backend/test/`, either as a sibling
of `Ignixa.Lab.Functions.Tests` or merged into it (implementation plan decides
based on how cleanly the integration-style base-URL setup fits alongside the
existing test project's conventions). `IgnixaApiTests.cs`'s hard-coded
`http://127.0.0.1:7071/api` base URL and `IGNIXA_API_URL` env var override are
preserved — they already point at "whatever Functions host is running
locally," which is exactly `ignixa-lab`'s own `func start` after the merge.

`OfficialTestResultGenerator.cs` (803 lines, generates/validates against the
official FHIRPath test suite) is ported as-is; it's a standalone
correctness check against the evaluation engine, not something that needs
adaptation to ignixa-lab's conventions.

## Out of scope for this PR

- Decommissioning the `fhirpath-lab-dotnet` repo or the `ignixafhirpath`
  Function App.
- Repointing fhirpath-lab.com's UI at the new deployment (needs Brian's
  coordination — a DNS/config change on his side, not a code change here).
- Configuring the production CORS origin (see "CORS" above).
- Resolving ADR-2608 (abuse protection) beyond classifying the new endpoints
  into the existing tiers — the ADR's own "Proposed" status is unaffected by
  this PR.

These become natural follow-up work once this deployment is verified working
standalone.

## Branch / PR mechanics

New branch off `main` (`feature/fhirpath-evaluator`). PR opened once the port
builds (`dotnet build Ignixa.Lab.sln`) and tests pass
(`dotnet test Ignixa.Lab.sln`).
