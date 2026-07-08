# Ignixa Lab — Backend

A **.NET 10 isolated-worker Azure Functions** app that executes FHIR TestScript
suites against a target server and returns a conformance report.

## Endpoints

| Method | Route | Description |
| ------ | ----- | ----------- |
| `GET` | `/api/health` | Liveness + engine version. |
| `GET` | `/api/suites` | Catalog of bundled TestScript suites. |
| `POST` | `/api/run` | Run selected suites against a target server. |
| `GET` | `/api/capability` | Fetch and normalize a target server's declared FHIR capabilities. |

See the [API reference](../docs/api.md) and
[report schema](../docs/conformance-report-schema.md) for details.

## Run locally

```bash
./backend/pack-suites.ps1                             # packs the suites package into artifacts/local-feed (see Suites below)
cd backend/src/Ignixa.Lab.Functions
cp local.settings.json.example local.settings.json    # first run only
func start                                            # http://localhost:7071
```

## Project structure

```
src/Ignixa.Lab.Functions/
  Conformance/     Report schema records (report, result, step, error, http req/res)
  Configuration/   IgnixaLabOptions (bound from the "IgnixaLab" section)
  Execution/       TestScriptRunner, TargetUrlValidator (SSRF), ConformanceReportMapper,
                   HttpEvaluatorFactory, RunOutcome, CapabilityStatementParser
  Functions/       HealthFunction, SuitesFunction, RunFunction, CapabilityFunction
  Middleware/      CorsMiddleware, RateLimitMiddleware (RateLimitPolicy,
                   EndpointClassifier, ClientIpKeyExtractor — ADR-2608)
  Models/          RunRequest, SuiteDescriptor, CapabilityResponse
  Suites/          SuiteCatalog (reads testscripts/<category>/*.json restored
                   from the IgnixaLab.TestScript.Suites package — see Suites below)
  Program.cs       Host + DI wiring

src/Ignixa.Lab.Suites/
  testscripts/     The 87 executable, non-sidecar TestScript suites (Bundles/CRUD/Foundation/
                   Microsoft/Operations/Regression/Search/Subscriptions/Validation),
                   packed into the IgnixaLab.TestScript.Suites content
                   package (ADR-2607)

test/Ignixa.Lab.Functions.Tests/
  Execution/       TargetUrlValidatorTests, ConformanceReportMapperTests,
                   CapabilityStatementParserTests
  Functions/       CapabilityFunctionTests
  Middleware/      RateLimitPolicyTests, EndpointClassifierTests,
                   ClientIpKeyExtractorTests
  Suites/          SuiteCatalogTests
```

## Test

```bash
dotnet test Ignixa.Lab.sln
```

## HTTP capture

`GET /api/run` traces each step's real HTTP request/response so the dashboard's
Request/Response tabs and capability coverage map have real data. As of the
`Ignixa.TestScript` 0.5.11-beta engine, this comes natively from each action's
`ActionResult` — `Kind` (`Operation`/`Assertion`) tells `ConformanceReportMapper`
whether a step is an operation or an assertion, and `Exchange` (when present)
carries the actual `TestRequest`/`TestResponse` the engine made, which the
mapper copies onto `ConformanceStep.Request`/`Response`. `Authorization` and
`Proxy-Authorization` header values are always redacted by the mapper before
being recorded.

## CORS

Configurable via `IgnixaLab:CorsAllowedOrigins` — a comma-separated list of
origins permitted to call the API cross-origin. Defaults to the hosted
frontend (`https://brendankowitz.github.io`), the fhirpath-lab sites
(`https://fhirpath-lab.com`, `https://dev.fhirpath-lab.com`,
`https://hackweek.fhirpath-lab.com`) plus the local Vite dev server
(`http://localhost:5173`), so both a hosted deployment and local development
work without extra configuration. Add or replace origins per environment via
app settings (Azure) or `local.settings.json` (local dev):

```json
"IgnixaLab:CorsAllowedOrigins": "https://brendankowitz.github.io,https://fhirpath-lab.com,https://dev.fhirpath-lab.com,https://hackweek.fhirpath-lab.com,http://localhost:5173"
```

Entries must match the browser's `Origin` header exactly (scheme + host +
port, no path or trailing slash).

Enforced in-process by `Middleware/CorsMiddleware`, since the isolated worker
has no Kestrel pipeline to attach `app.UseCors()` to.

## Rate limiting

Abuse protection ([ADR-2608](../docs/features/abuse-protection/adr-2608-abuse-protection.md))
runs as `Middleware/RateLimitMiddleware`, registered immediately after
`CorsMiddleware` and built on the in-box `System.Threading.RateLimiting`
primitives (no new package or Azure resource). Requests are classified by
function name, not URL parsing:

| Class | Endpoints | Per-IP limit | Global limit |
| ----- | --------- | ------------ | ------------ |
| Exempt | `Health`, CORS preflight `OPTIONS` | — | — |
| Suites | `Suites` | 30 / min (sliding) | — |
| Capability | `Capability` | 12 / min (sliding) | — |
| Run | `Run` | 4 / min **and** 20 / hour (sliding) | 100 / hour (fixed, clock-aligned) **and** max 4 concurrent |

An unrecognized function name fails safe to the strictest (`Run`) tier.

Configured via `IgnixaLab:RateLimiting:*` (app settings on Azure,
`local.settings.json` locally):

| Setting | Default | Meaning |
| ------- | ------- | ------- |
| `Enabled` | `true` | Master kill switch — `false` bypasses all limiting. |
| `SuitesPerMinutePerIp` | `30` | Per-IP sliding-window limit for `/api/suites`. |
| `CapabilityPerMinutePerIp` | `12` | Per-IP sliding-window limit for `/api/capability`. |
| `RunPerMinutePerIp` | `4` | Per-IP sliding-window limit for `/api/run`, per minute. |
| `RunPerHourPerIp` | `20` | Per-IP sliding-window limit for `/api/run`, per hour. |
| `RunGlobalPerHour` | `100` | Process-wide clock-aligned hourly cap for `/api/run`. |
| `RunMaxConcurrent` | `4` | Max simultaneous `/api/run` invocations in this process. |

Client IP is taken from the **right-most** `X-Forwarded-For` entry (App Service
appends the true client IP, so only that hop is trustworthy; entries to its left
are client-supplied and ignored), with the `:port` suffix stripped and IPv6
clients collapsed to their **/64 prefix** so rotating within a delegated prefix
maps to one key. If `X-Forwarded-For` is absent it falls back to the socket peer;
an unparseable IP buckets under a single `"unknown"` key rather than being
exempted.

On rejection the middleware returns **429 Too Many Requests**, a `Retry-After`
header (whole seconds), and a JSON body matching the API's error shape:

```json
{ "error": "Rate limit exceeded for this endpoint. Retry after 42 seconds." }
```

**In-memory / per-instance limitation:** all counters live in process memory, so
under scale-out the effective limits are multiplied by the instance count (the
"global" hourly cap becomes `RunGlobalPerHour × instanceCount`). This is
acceptable only while scale-out is pinned small — operators **must** cap the App
Service plan / Flex `maximumInstanceCount` at a low number for the numbers above
to mean what they say (ADR-2608 §6). A true cross-instance cap requires the
Phase 2 shared-store counter, which is not implemented here.

## Suites

The 87 executable, non-sidecar FHIR TestScript JSON files
(`backend/src/Ignixa.Lab.Suites/testscripts/{Bundles,CRUD,Foundation,Microsoft,Operations,Regression,Search,Subscriptions,Validation}/*.json`)
are packed into a local NuGet content package, `IgnixaLab.TestScript.Suites`, by
the `Ignixa.Lab.Suites` project and consumed by `Ignixa.Lab.Functions` (and
its test project) via `PackageReference`.

This is an interim step —
see [ADR-2607](../docs/features/testscript-suite-sourcing/adr-2607-suite-sourcing.md)
— for the upstream `ignixa-fhir` suites artifact; the `PackageReference`
will be repointed there once it ships, and the local feed retired.

Most suites map to the e2e test coverage of the Microsoft FHIR Server
(`microsoft/fhir-server`), organized by category (the immediate subfolder
name under `testscripts/` — no code change needed for discovery, see
`SuiteCatalog`): `Bundles` (batch/transaction), `CRUD` (create/read/vread/
update/delete/history/patch/conditional operations), `Foundation`
(metadata/health/CORS/custom headers), `Operations` (FHIR $-operations —
$export, $everything, $expand, $docref, $member-match), `Regression`
(exception handling, version parity, reference resolution), `Search`
(search-parameter types and modifiers), and `Validation` (`$validate`).
Suites representing genuinely non-standard, Microsoft FHIR Server-proprietary
operations/modifiers/headers are prefixed `ms-` in their filename and live in
their own `Microsoft` category — kept separate from the spec-conformance
categories above so they're easy to select or exclude as a group when
testing a non-Microsoft server.

Two additions extend coverage beyond the Microsoft FHIR Server baseline,
sourced from a survey of other open-source FHIR server test suites
(fhir-candle, HAPI FHIR, LinuxForHealth FHIR, Health Samurai, Helios):
`Operations/lookup-operation.json`, `Operations/validate-code-operation.json`,
`Operations/subsumes-operation.json`, and `Operations/translate-operation.json`
cover the HL7-spec terminology operations (`$lookup`, `$validate-code`,
`$subsumes`, ConceptMap `$translate`) modeled on HAPI FHIR's terminology test
coverage — none of these are Microsoft-proprietary, so none are `ms`-prefixed.
The new `Subscriptions` category (`Subscriptions/basic.json`) covers basic
classic (R4/R4B, criteria-based) Subscription create/read/search/update/delete
round-tripping, inspired by fhir-candle's and LinuxForHealth's Subscriptions
coverage. Both additions are gated with `requiresCapability` (see
[Capability-aware test gating](#capability-aware-test-gating) below) since
neither terminology operations nor the Subscription resource itself are
universally implemented. Full async notification delivery and R5's
topic-based Subscription model are deliberately **not** covered here — see
[ADR-2609](../docs/features/subscriptions-test-coverage/adr-2609-subscriptions-test-coverage.md).

**Deliberately not covered** — these areas of the upstream e2e suite need
infrastructure a black-box HTTP TestScript run can't provide, so no suites
were authored for them: `Rest/Audit/*` (requires inspecting a custom trace
logger), `Rest/Metric/*` (requires a custom in-proc metric handler),
`BasicAuthTests`/`TokenIntrospectionTests` (need multiple OAuth identities —
`RunRequest` has no per-request auth support), `Microsoft.Health.Fhir.
Shared.Tests.Smart` (SMART-on-FHIR OAuth proxy infrastructure),
`Microsoft.Health.Fhir.Shared.Tests.Crucible` (integration with the external
Crucible tool), `CreateAllFhirResourcesTests` (enumerates every resource
type per FHIR version — too large/low-signal for a curated suite), and
`*.Tests.E2E.CLI` (tooling only, no tests).

Because restore needs the package to already exist, it must be packed before
every restore/build/test:

```bash
./backend/pack-suites.ps1                        # -> artifacts/local-feed/IgnixaLab.TestScript.Suites.0.1.0-local.nupkg
dotnet build Ignixa.Lab.sln -c Release
dotnet test Ignixa.Lab.sln -c Release
```

`nuget.config` adds `artifacts/local-feed` as a package source (alongside
nuget.org) and `Directory.Packages.props` pins the version. The package ships
`build/IgnixaLab.TestScript.Suites.targets`, which MSBuild auto-imports for any
consumer with the `PackageReference` — it copies the packaged JSONs to the
consumer's output under `testscripts/`, preserving the category subfolders
that `SuiteCatalog` reads. Bumping the suites means editing the JSON under
`Ignixa.Lab.Suites/testscripts/` and re-running `pack-suites.ps1`.

Each distilled TestScript should have a sibling FHIR R4 Provenance sidecar named
`<suite>.provenance.json`. Sidecars are packaged with the suites but ignored by
`SuiteCatalog`; they record the source repositories, specifications, or APIs
used while distilling the executable TestScript. `pack-suites.ps1` runs the
warning-only provenance audit so missing or invalid sidecars show up in CI logs
without failing the build.

## Deploy

`.github/workflows/backend-deploy.yml` builds and publishes the Functions app
and deploys it to the `ignixa-lab` Azure Function App (Flex Consumption,
`Ingixa` resource group) via `Azure/functions-action@v1` on every push to
`main` touching `backend/**` (plus manual dispatch). It authenticates via
OIDC (`azure/login@v2`), not a stored publish profile — Flex Consumption
treats RBAC as the primary deploy path, and publish-profile auth would
require enabling SCM basic-auth publishing credentials on the app just to
work. This workflow does not create or modify any Azure resources; it only
deploys to the app that already exists.

The deploy identity is an Entra app registration (`gh-ignixa-lab-deploy`)
with a GitHub OIDC federated credential scoped to
`repo:brendankowitz/ignixa-lab:ref:refs/heads/main`, granted `Website
Contributor` on the `Ingixa` resource group. Three repo secrets carry its
identity — no credential value is ever stored:

- `AZURE_CLIENT_ID` — the app registration's client ID.
- `AZURE_TENANT_ID` — the Entra tenant ID.
- `AZURE_SUBSCRIPTION_ID` — the Azure subscription ID.

## Capability-aware test gating

Suites can gate a whole `TestScript` or an individual `test[]` entry on the
target server's declared `CapabilityStatement` via a
`http://ignixa.io/testscript/requiresCapability` extension, whose `valueString`
is a FHIRPath expression evaluated against the target's `GET /metadata`
response (fetched once per run and reused across every job). If the expression
evaluates to `false`, the gated test (or, for a metadata-level extension, every
test in the suite) is recorded as `skipped` rather than executed:

```json
{
  "extension": [{
    "url": "http://ignixa.io/testscript/requiresCapability",
    "valueString": "rest.resource.where(type='Patient').operation.where(name='everything').exists()"
  }]
}
```

If the CapabilityStatement can't be fetched or parsed, gating fails open (every
gated test still runs as if ungated) and `ConformanceReport.capabilityWarning`
explains why, rather than silently skipping potentially large parts of a run
because `/metadata` happened to be flaky. See
`Execution/CapabilityStatementFetcher.cs` and the engine's
`TestScriptEvaluator.IsCapabilityRequirementMet`.

## Notes

- The engine is the `Ignixa.TestScript` NuGet package (published from
  ignixa-fhir).
- NuGet versions are centralized in `Directory.Packages.props`.
- User-supplied target URLs are validated to prevent SSRF; see
  [architecture](../docs/architecture.md#ssrf-protection). `GET /api/capability`
  reuses the same guard before fetching `{target}/metadata`.
