# Ignixa Lab — Backend

A **.NET 9 isolated-worker Azure Functions** app that executes FHIR TestScript
suites against a target server and returns a conformance report.

## Endpoints

| Method | Route | Description |
| ------ | ----- | ----------- |
| `GET` | `/api/health` | Liveness + engine version. |
| `GET` | `/api/suites` | Catalog of bundled TestScript suites. |
| `POST` | `/api/run` | Run selected suites against a target server. |

See the [API reference](../docs/api.md) and
[report schema](../docs/conformance-report-schema.md) for details.

## Run locally

```bash
cd backend/src/Ignixa.Lab.Functions
cp local.settings.json.example local.settings.json   # first run only
func start                                            # http://localhost:7071
```

## Project structure

```
src/Ignixa.Lab.Functions/
  Conformance/     Report schema records (report, result, step, error, http req/res)
  Configuration/   IgnixaLabOptions (bound from the "IgnixaLab" section)
  Execution/       TestScriptRunner, TargetUrlValidator (SSRF), ConformanceReportMapper,
                   HttpEvaluatorFactory, RunOutcome
  Functions/       HealthFunction, SuitesFunction, RunFunction
  Models/          RunRequest, SuiteDescriptor
  Suites/          SuiteCatalog + testscripts/<category>/*.json
  Program.cs       Host + DI wiring

test/Ignixa.Lab.Functions.Tests/
  Execution/       TargetUrlValidatorTests, ConformanceReportMapperTests
  Suites/          SuiteCatalogTests
```

## Test

```bash
dotnet test Ignixa.Lab.sln
```

## Notes

- The engine is the `Ignixa.TestScript` NuGet package (published from
  ignixa-fhir).
- NuGet versions are centralized in `Directory.Packages.props`.
- User-supplied target URLs are validated to prevent SSRF; see
  [architecture](../docs/architecture.md#ssrf-protection).
