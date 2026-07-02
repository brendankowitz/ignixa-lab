# Ignixa Lab — Backend

A **.NET 9 isolated-worker Azure Functions** app that executes FHIR TestScript
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
                   ConformanceStepCorrelator, HttpEvaluatorFactory, RunOutcome,
                   CapabilityStatementParser
  Functions/       HealthFunction, SuitesFunction, RunFunction, CapabilityFunction
  Http/            RecordingHttpHandler, IHttpExchangeScope/HttpExchangeScope,
                   HttpExchangeCollector, CapturedExchange
  Middleware/      CorsMiddleware
  Models/          RunRequest, SuiteDescriptor, CapabilityResponse
  Suites/          SuiteCatalog (reads testscripts/<category>/*.json restored
                   from the Ignixa.TestScript.Suites package — see Suites below)
  Program.cs       Host + DI wiring

src/Ignixa.Lab.Suites/
  testscripts/     The 12 canonical TestScript suites, packed into the
                   Ignixa.TestScript.Suites content package (ADR-2607)

test/Ignixa.Lab.Functions.Tests/
  Execution/       TargetUrlValidatorTests, ConformanceReportMapperTests,
                   ConformanceStepCorrelatorTests, CapabilityStatementParserTests
  Functions/       CapabilityFunctionTests
  Http/            RecordingHttpHandlerTests, HttpExchangeScopeTests
  Suites/          SuiteCatalogTests
```

## Test

```bash
dotnet test Ignixa.Lab.sln
```

## HTTP capture

`GET /api/run` traces each step's real HTTP request/response so the dashboard's
Request/Response tabs and capability coverage map have real data. Controlled
via `IgnixaLab:HttpCaptureEnabled` (default `true`) and
`IgnixaLab:HttpCaptureMaxBodyBytes` (default `65536`) — bodies larger than the
cap are truncated with a `…[truncated N bytes]` marker. `Authorization` and
`Proxy-Authorization` header values are always redacted before being recorded.
Captured via `Http/RecordingHttpHandler`, a `DelegatingHandler` on the
`fhir-target` client that records into an ambient `IHttpExchangeScope`
collector; `ConformanceReportMapper` then correlates the ordered exchanges
back onto the operation steps that produced them.

## CORS

Configurable via `IgnixaLab:CorsAllowedOrigins` — a comma-separated list of
origins permitted to call the API cross-origin. Defaults to the hosted
frontend (`https://brendankowitz.github.io`) plus the local Vite dev server
(`http://localhost:5173`), so both a hosted deployment and local development
work without extra configuration. Add or replace origins per environment via
app settings (Azure) or `local.settings.json` (local dev):

```json
"IgnixaLab:CorsAllowedOrigins": "https://brendankowitz.github.io,http://localhost:5173"
```

Enforced in-process by `Middleware/CorsMiddleware`, since the isolated worker
has no Kestrel pipeline to attach `app.UseCors()` to.

## Suites

The 12 canonical FHIR TestScript suites (`backend/src/Ignixa.Lab.Suites/testscripts/{Bundles,CRUD,Search,Validation}/*.json`)
are packed into a local NuGet content package, `Ignixa.TestScript.Suites`, by
the `Ignixa.Lab.Suites` project and consumed by `Ignixa.Lab.Functions` (and
its test project) via `PackageReference`. This is an interim step —
see [ADR-2607](../docs/features/testscript-suite-sourcing/adr-2607-suite-sourcing.md)
— for the upstream `ignixa-fhir` suites artifact; the `PackageReference`
will be repointed there once it ships, and the local feed retired.

Because restore needs the package to already exist, it must be packed before
every restore/build/test:

```bash
./backend/pack-suites.ps1                        # -> artifacts/local-feed/Ignixa.TestScript.Suites.0.1.0-local.nupkg
dotnet build Ignixa.Lab.sln -c Release
dotnet test Ignixa.Lab.sln -c Release
```

`nuget.config` adds `artifacts/local-feed` as a package source (alongside
nuget.org) and `Directory.Packages.props` pins the version. The package ships
`build/Ignixa.TestScript.Suites.targets`, which MSBuild auto-imports for any
consumer with the `PackageReference` — it copies the packaged JSONs to the
consumer's output under `testscripts/`, preserving the category subfolders
that `SuiteCatalog` reads. Bumping the suites means editing the JSON under
`Ignixa.Lab.Suites/testscripts/` and re-running `pack-suites.ps1`.

## Deploy

`.github/workflows/backend-deploy.yml` builds and publishes the Functions app
and deploys it to an Azure App Service via `Azure/functions-action@v1` on
every push to `main` touching `backend/**` (plus manual dispatch). It expects
two repo secrets, set by the maintainer — this workflow does not create or
modify any Azure resources:

- `AZURE_FUNCTIONAPP_NAME` — the Function App name.
- `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` — its publish profile.

## Notes

- The engine is the `Ignixa.TestScript` NuGet package (published from
  ignixa-fhir).
- NuGet versions are centralized in `Directory.Packages.props`.
- User-supplied target URLs are validated to prevent SSRF; see
  [architecture](../docs/architecture.md#ssrf-protection). `GET /api/capability`
  reuses the same guard before fetching `{target}/metadata`.
