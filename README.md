# Ignixa Lab

A FHIR **TestScript conformance runner**. Point it at a FHIR server, pick one or
more test suites, run them, and read a category-based results matrix.

- **Backend** — a .NET 10 isolated-worker Azure Functions app that executes
  [FHIR TestScript](https://www.hl7.org/fhir/testscript.html) suites against a
  target server (via the `Ignixa.TestScript` engine) and returns a conformance
  report.
- **Frontend** — a Vite + React 19 single-page app to enter a target URL, select
  suites, run them, and view results grouped by category and scenario.

The report JSON is intentionally identical to the `conformance/latest.json`
artifact used by the [ignixa-fhir](https://github.com/brendankowitz/ignixa-fhir)
conformance dashboard (see [PR #291](https://github.com/brendankowitz/ignixa-fhir/pull/291)),
so reports are interchangeable between the two projects.

## Repository layout

```
.
├── backend/                     .NET 10 Azure Functions (isolated worker)
│   ├── src/Ignixa.Lab.Functions/
│   │   ├── Conformance/          Report schema records (report, result, step, error, http)
│   │   ├── Configuration/        IgnixaLabOptions
│   │   ├── Execution/            Runner, SSRF guard, report mapper, evaluator factory
│   │   ├── Functions/            HTTP endpoints: Health, Suites, Run
│   │   ├── Models/               Request/descriptor DTOs
│   │   ├── Suites/               Suite catalog + bundled TestScripts (by category)
│   │   └── Program.cs            Host + DI wiring
│   └── test/Ignixa.Lab.Functions.Tests/   xUnit tests
├── frontend/                    Vite + React 19 + TypeScript, two pages
│   └── src/                      conformance app (api client, types, hooks,
│                                  components) + benches/ (Expression Benches:
│                                  FHIRPath [real backend], FML/SQL-on-FHIR [mocked])
├── docs/                        Architecture, API, schema, and development guides
├── .github/workflows/           CI for backend and frontend
├── Directory.Build.props        Shared MSBuild settings (analyzers, warnings-as-errors)
├── Directory.Packages.props     Central Package Management (NuGet versions)
├── global.json                  Pinned .NET SDK
└── Ignixa.Lab.sln
```

## Quick start

Prerequisites: **.NET SDK 10**, **Node.js 20+**, and the
[Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local).

**Backend**

```bash
cd backend/src/Ignixa.Lab.Functions
cp local.settings.json.example local.settings.json   # first run only
func start                                            # serves http://localhost:7071
```

**Frontend**

```bash
cd frontend
npm install
npm run dev                                           # serves http://localhost:5173
```

The dev server proxies `/api/*` to the Functions host on port 7071, so the SPA
works against the local backend with no extra configuration.

The frontend is a two-page build: `/` is the TestScript conformance runner,
`/benches.html` is Expression Benches (FHIRPath, FML, SQL on FHIR). Both are
served by the same dev server and cross-link to each other in their top bars.

## Documentation

| Guide | Contents |
| ----- | -------- |
| [Architecture](docs/architecture.md) | How the backend and frontend fit together. |
| [API reference](docs/api.md) | The `health`, `suites`, and `run` endpoints. |
| [Conformance report schema](docs/conformance-report-schema.md) | The report JSON shape. |
| [Development](docs/development.md) | Build, test, lint, and add suites. |

## Testing at a glance

```bash
dotnet test Ignixa.Lab.sln          # backend (xUnit)
cd frontend && npm run build        # type-check + production build
cd frontend && npm run lint         # oxlint
```

## License

See [LICENSE](LICENSE).
