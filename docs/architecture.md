# Architecture

Ignixa Lab has two independent pieces: a .NET Functions **backend** that runs
TestScripts, and a React **frontend** that drives it.

```
┌─────────────────────┐        HTTP/JSON        ┌──────────────────────────────┐
│  Frontend (SPA)     │  ───────────────────▶   │  Backend (Azure Functions)   │
│  Vite + React 19    │   /api/health           │  .NET 10 isolated worker     │
│                     │   /api/suites           │                              │
│  HostForm           │   /api/run              │  HealthFunction              │
│  SuitePicker        │  ◀───────────────────   │  SuitesFunction              │
│  ResultsMatrix      │   ConformanceReport     │  RunFunction                 │
└─────────────────────┘                         │      │                       │
                                                │      ▼                       │
                                                │  TestScriptRunner            │
                                                │   ├─ TargetUrlValidator      │  SSRF guard
                                                │   ├─ SuiteCatalog            │  bundled suites
                                                │   ├─ Ignixa.TestScript       │  execution engine
                                                │   └─ ConformanceReportMapper │  → report JSON
                                                └──────────────┬───────────────┘
                                                               │ HTTP (FHIR)
                                                               ▼
                                                     Target FHIR server
```

## Backend

The backend is a **.NET 10 isolated-worker Azure Functions** app using the
ASP.NET Core integration (`FunctionsApplication.CreateBuilder` +
`ConfigureFunctionsWebApplication`). Wiring lives in `Program.cs`.

### Request flow (`POST /api/run`)

1. **`RunFunction`** deserializes the `RunRequest` body (camelCase, `Web`
   defaults) and delegates to `TestScriptRunner`.
2. **`TestScriptRunner.RunAsync`**:
   - Validates the target URL with **`TargetUrlValidator`** (see below).
   - Resolves the requested bundled suites from **`SuiteCatalog`** and parses any
     inline uploaded TestScripts.
   - Rejects empty selections and selections over `MaxSuitesPerRun`.
   - Constructs a `TestScriptEvaluator` (from `Ignixa.TestScript`) bound to an
     `HttpClient` scoped to the target, then executes each suite.
   - Maps each engine `TestScriptReport` into `ConformanceResult`s via
     **`ConformanceReportMapper`** and aggregates them into a single
     `ConformanceReport`.
3. **`RunFunction`** returns `200` with the report, or `400` with
   `{ "error": "…" }` for validation failures.

### SSRF protection

Because the target URL is user-supplied, `TargetUrlValidator` blocks requests
that could be used to probe internal infrastructure:

- Only `http`/`https` absolute URLs are accepted.
- Loopback, `localhost` (and `*.localhost`), private (`10/8`, `172.16/12`,
  `192.168/16`), link-local (`169.254/16`), and `0/8` addresses are rejected,
  including when a hostname resolves to one of those ranges, and the IPv6
  equivalents.
- This can be relaxed for local development with
  `IgnixaLab:AllowPrivateTargets=true`.

### Suite catalog

`SuiteCatalog` discovers bundled TestScripts under the `testscripts` directory
(shipped next to the worker binary). The **immediate sub-folder** of each script
is used as its **category**. Suites are parsed lazily and exposed as
`SuiteDescriptor`s through `GET /api/suites`.

## Frontend

A **Vite 8 + React 19 + TypeScript** single-page app. State is owned by two
hooks and rendered by presentational components.

- **`useConformanceRun`** — loads the suite catalog on mount and drives a run
  (`idle → running → complete | error`). `POST /api/run` currently returns a
  single report on completion; the hook is shaped so per-suite progress can be
  added later without changing the component API.
- **`useSuiteSelection`** — owns the set of selected suite IDs.
- **`lib/conformance.ts`** — pure helpers for status aggregation and grouping
  results by category.
- **Components** — `HostForm`, `SuitePicker`, `ProgressPanel`, `SummaryCards`,
  `ResultsMatrix` → `CategoryGroup` → `ResultRow` → `StepTrace`.

The `src/types/conformance.ts` interfaces mirror the backend records so a report
round-trips without transformation.

## Cross-cutting

- **Central Package Management** (`Directory.Packages.props`) keeps NuGet
  versions consistent across projects.
- **Analyzers** run with warnings-as-errors (`Directory.Build.props`); the test
  project opts out of the xUnit-idiomatic `CA1707`/`CA1861` rules.
- **Report interchange** — the report schema matches the ignixa-fhir dashboard
  artifact, so a run can feed the same visualizations.
