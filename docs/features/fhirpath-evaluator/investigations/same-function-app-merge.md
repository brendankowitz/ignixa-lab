# Investigation: Same-Function-App merge

**Feature**: fhirpath-evaluator
**Status**: In Progress
**Created**: 2026-07-02

## Approach

Port `fhirpath-lab-dotnet`'s Functions class and its 5 DI services into
`backend/src/Ignixa.Lab.Functions` as a new area (e.g. `Functions/FhirPath*`,
`Services/FhirPath/*`), so both TestScript conformance and FHIRPath evaluation
run inside the **same** deployed Azure Functions app (`ignixa-lab`), same
solution, same CI/CD pipeline. `fhirpath-lab-dotnet` as a standalone repo is
retired (or kept read-only for history); the separate `ignixafhirpath`
Function App is decommissioned once DNS/UI wiring on fhirpath-lab.com points
at the new routes.

### What moves

Per the source-repo exploration (`fhirpath-lab-dotnet`, ~1,831 lines across 11
files — a genuinely small port):

- `FunctionFhirPathTest.cs` → new `Functions/FhirPathFunctions.cs`, 6 triggers:
  `metadata`, `$fhirpath-{stu3,r4,r4b,r5,r6}` (GET+POST), plus a `Warmer` timer
  trigger (15 min).
- `Services/{FhirPathService, SchemaProviderFactory, ExpressionAnalyzer,
  ExpressionEvaluator, ResultFormatter}.cs` → registered as singletons in
  `Program.cs`, same pattern `ignixa-lab` already uses for `SuiteCatalog` /
  `HttpEvaluatorFactory`.
- `Serialization/JsonAstVisitor.cs`, `Models/FhirPath{Request,Result}.cs`,
  `Services/LightweightElementResolver.cs` → ported as-is; these have no
  external dependencies beyond the `Ignixa.*` NuGet packages.

### What changes on the way in

- **TFM**: bump the ported code to `net10.0` to match `Ignixa.Lab.Functions`
  (single-TFM project; no reason to keep a `net9.0` island in this solution).
- **Package versions**: reconcile `Ignixa.FhirPath`/`Ignixa.Serialization`/
  `Ignixa.Specification` (0.5.6) into `Directory.Packages.props` alongside the
  existing `Ignixa.TestScript` (0.5.11-beta) — same publisher, so check
  whether a newer compatible release of the FhirPath/Serialization/
  Specification trio already exists at the TestScript engine's version line
  before pinning 0.5.6.
- **CORS**: `fhirpath-lab-dotnet` has no CORS middleware in code (relied on
  Azure-portal-level Function App CORS config, which isn't tracked as code
  anywhere). `ignixa-lab`'s `CorsMiddleware` (`Middleware/CorsMiddleware.cs`)
  is origin-allowlist-based via `IgnixaLabOptions.CorsAllowedOrigins`. The
  fhirpath-lab.com UI's origin must be added to that allowlist — this becomes
  the first *tracked, code-reviewed* record of what's allowed to call these
  endpoints, which is a strict improvement.
- **SSRF guard**: `FhirPathService.LoadResourceFromUrl` (fetch-resource-by-URL)
  currently has no equivalent to `TargetUrlValidator`. Wiring it through the
  same validator `RunFunction`/`CapabilityFunction` already use closes a real
  gap — this endpoint accepts an arbitrary URL from an anonymous caller today.
- **`HttpClient` usage**: `FhirPathService` does `new HttpClient()` per call
  instead of `IHttpClientFactory` — fix to use the factory pattern
  `ignixa-lab` already has via `HttpEvaluatorFactory.HttpClientName`.
- **Rate limiting**: `ignixa-lab`'s `RateLimitMiddleware` / `EndpointClassifier`
  (ADR-2608, currently "Proposed") would need the new routes classified —
  `$fhirpath-*` does no outbound calls unless `resource` is a URL, so it's
  closer to `capability`-tier cost than `run`-tier, except when resolving a
  remote resource.
- **Route namespacing**: no collision with existing `health`/`suites`/`run`/
  `capability` routes, but `metadata` is generic enough to want a prefix
  (e.g. `fhirpath/metadata`) to avoid ever colliding with a future
  capability-statement route of `ignixa-lab`'s own. The version-specific
  `$fhirpath-*` operation routes are already unambiguous.
- **Static `Lazy<T>` per-FHIR-version engine caches** (`SchemaProviderFactory`,
  `ExpressionEvaluator`, `ExpressionAnalyzer`) can be ported unchanged — they
  don't collide with `ignixa-lab`'s existing singletons (different concern:
  FHIRPath schema/engine vs. TestScript execution engine), so no consolidation
  is required, just co-existence.
- **Deploy pipeline**: extend `backend-deploy.yml` (single job, same
  `dotnet publish` + `Azure/functions-action@v1` step) rather than duplicating
  it — one Function App, one publish artifact, one deploy step. Drop
  `fhirpath-lab-dotnet`'s separate workflow and `ignixafhirpath` OIDC secrets
  once cut over.

## Tradeoffs

| Pros | Cons |
|------|------|
| Genuinely small port (~1,831 LOC, no persistence, no frontend to migrate) — low mechanical risk | Both apps currently scale/cold-start independently; merging means FHIRPath evaluation traffic and TestScript run traffic share one Function App's instance pool and cold-start profile |
| One CI/CD pipeline, one set of OIDC secrets, one Application Insights resource to watch instead of two | `fhirpath-lab-dotnet`'s endpoints are anonymous and internet-facing from an external UI (fhirpath-lab.com) with no repo control over that caller — a bug or abuse pattern against `$fhirpath-*` now shares blast radius (compute, rate-limit budget, egress reputation) with the TestScript conformance tool |
| Forces two real gaps to close on the way in: SSRF guard on `LoadResourceFromUrl`, code-tracked CORS origin instead of portal-only config | Response AST shape (`JsonAstVisitor`) is a de facto contract with an external UI not in either repo — any refactor during the port must be verified against fhirpath-lab.com's actual expectations, not just this repo's tests |
| Same "Ignixa" package family already in both — natural to consolidate under one `Directory.Packages.props` | No IaC in either repo for the underlying Azure resources — merge doesn't get to lift-and-shift infra, has to be handled by hand either way, so this isn't actually a point in favor of merging, just a wash |
| `fhirpath-lab-dotnet`'s test project (`FhirPathLab.Tests`, 1,721 LOC) is integration/E2E style against a running host — same shape as how `ignixa-lab` would want to verify the ported routes | Test project needs re-pointing (base URL / route paths), not a pure copy — `IGNIXA_API_URL` / local port assumptions baked into `IgnixaApiTests.cs` |

## Alignment

- [x] Follows architectural layering rules — ported services slot into the
      existing `Functions/` + `Services/`-equivalent split `ignixa-lab`
      already uses (`Suites/`, `Execution/`, `Middleware/`); no new
      architectural layer required.
- [x] Developer Experience — single `dotnet build`/`dotnet test`/`func start`
      covers both surfaces; no second repo to clone for local dev.
- [x] Specification compliance — FHIRPath evaluation semantics are unchanged
      by the move (same `Ignixa.FhirPath` engine); only the response AST
      *shape* has an external contract to preserve, verified via the ported
      `IgnixaApiTests`/`OfficialTestResultGenerator` suite against the
      official FHIRPath test cases.
- [ ] Consistent with existing patterns — **not yet verified**: `ignixa-lab`'s
      abuse-protection rate limiting (ADR-2608) is still "Proposed," not
      "Decided," so there's no settled precedent yet for how a new anonymous,
      outbound-call-capable endpoint class should be classified. This should
      resolve before or alongside this merge, not after.

## Evidence

Full technical map of `fhirpath-lab-dotnet` (hosting model, API surface, DI
wiring, dependencies, deployment, coupling points, size) gathered via
codebase exploration on 2026-07-02 — see PR/session notes; key facts folded
into "What moves" / "What changes" above. Cross-referenced against
`ignixa-lab`'s own `docs/architecture.md`, `Program.cs`,
`Middleware/CorsMiddleware.cs`, `Execution/TargetUrlValidator.cs`,
`Configuration/IgnixaLabOptions.cs`, `Directory.Packages.props`, and
`.github/workflows/backend-deploy.yml`.

Both apps:
- Target Azure Functions v4, isolated worker, ASP.NET Core HTTP integration
  (`FunctionsApplication`/`HostBuilder` + `ConfigureFunctionsWebApplication`).
- Use anonymous HTTP triggers only, no function-key auth.
- Deploy via `Azure/functions-action@v1` with OIDC login, zip-deploy
  (`remote-build: false` in `ignixa-lab`; not specified in
  `fhirpath-lab-dotnet`, defaults apply).
- Depend on the same "Ignixa" package family at different version lines.

Divergent points that drove the tradeoffs above: `fhirpath-lab-dotnet` has no
CORS middleware, no SSRF guard on its URL-fetch path, and no
`IHttpClientFactory` usage — `ignixa-lab` has all three already, established
via ADR-2608 (abuse protection) and the existing `TargetUrlValidator`.

## Verdict

*Pending evaluation.* Mechanically low-risk (small, stateless, no persistence,
no frontend). The open question is not "can it be ported" (yes) but whether
sharing one Function App's blast radius/cold-start pool between an
internet-facing third-party UI's traffic (fhirpath-lab.com) and this repo's
own TestScript conformance tool is the right operational tradeoff versus a
lighter-weight alternative — e.g. same repo/solution but a **second, separate
Function App** (shares CI/build tooling and `Directory.Packages.props`
without sharing runtime blast radius), which is worth its own investigation
before deciding.
