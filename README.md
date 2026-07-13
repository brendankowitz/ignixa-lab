<div align="center">
  <img src="frontend/public/ignixa-logo.png" alt="Ignixa Lab logo" width="200"/>
  <h1>Ignixa Lab</h1>
  <p>
    <b>A FHIR conformance runner and interactive expression toolkit, built on the Ignixa engines</b>
  </p>

[![dotnet](https://img.shields.io/badge/dotnet-10.0-512BD4)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=white)](https://react.dev/)
[![FHIR](https://img.shields.io/badge/FHIR-R4%20%7C%20R4B%20%7C%20R5%20%7C%20R6%20%7C%20STU3-orange)](https://hl7.org/fhir/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Backend](https://github.com/brendankowitz/ignixa-lab/actions/workflows/backend.yml/badge.svg)](https://github.com/brendankowitz/ignixa-lab/actions/workflows/backend.yml)
[![Frontend](https://github.com/brendankowitz/ignixa-lab/actions/workflows/frontend.yml/badge.svg)](https://github.com/brendankowitz/ignixa-lab/actions/workflows/frontend.yml)
[![Live Demo](https://img.shields.io/badge/Live%20Demo-GitHub%20Pages-222?logo=githubpages)](https://brendankowitz.github.io/ignixa-lab/)

</div>

---

> **Project Status:** Companion lab to [ignixa-fhir](https://github.com/brendankowitz/ignixa-fhir). It exercises the Ignixa engines — TestScript, FHIRPath, Fakes, Validation — against real and reference FHIR servers, and doubles as the interactive backend fhirpath-lab.com repoints at. Built heavily with AI coding agents, with manual validation and code review.

---

## Overview

**Ignixa Lab** is a two-piece app for putting the Ignixa FHIR engines to work:

- **Backend** — a .NET 10 isolated-worker Azure Functions app exposing a FHIR
  TestScript conformance runner, a FHIRPath evaluator (the same routes
  fhirpath-lab.com calls), synthetic data generation (Fakes), and resource
  validation — all via the [Ignixa Core SDK](https://github.com/brendankowitz/ignixa-fhir) packages.
- **Frontend** — a Vite + React 19 single-page app with two entry points: a
  **conformance runner** to pick suites and read results by category, and an
  **Expression Benches** playground for FHIRPath, Fakes, Validation, FML, and
  SQL-on-FHIR.

The conformance report JSON is intentionally identical to the
`conformance/latest.json` artifact used by ignixa-fhir's dashboard
(see [PR #291](https://github.com/brendankowitz/ignixa-fhir/pull/291)), so
reports are interchangeable between the two projects.

## ✨ Key Features

### 🧪 Conformance Testing

- **87 bundled FHIR TestScript suites** across 9 categories — `Bundles`,
  `CRUD`, `Foundation`, `Microsoft`, `Operations`, `Regression`, `Search`,
  `Subscriptions`, `Validation` — plus support for uploading your own
  TestScripts inline.
- **Capability-aware gating** — a suite or individual test can require a
  target's declared `CapabilityStatement` support via a `requiresCapability`
  FHIRPath extension. If `/metadata` can't be fetched, gating fails **open**
  (with a warning) rather than silently skipping coverage.
- **Category-based results matrix** with every failure traced to its
  assertion, and full HTTP request/response capture (auth headers redacted).

### 🧮 Expression Benches

An interactive playground for the engine, backed by real endpoints for
**FHIRPath**, **Fakes**, and **Validation**, plus client-side prototyping
benches for **FML** and **SQL-on-FHIR**:

- Write a FHIRPath expression, watch it parse into an AST, and evaluate it
  live against sample resources.
- Generate synthetic FHIR resources, clinical scenarios, and population packs
  with the Fakes bench.
- Validate resources against the FHIR spec.

### 🛡️ Hardened by default

- **SSRF guard** on every user-supplied target URL — blocks loopback,
  private, and link-local addresses, including when a hostname resolves to
  one.
- **Rate limiting** per endpoint (sliding-window + global caps), classified
  by function rather than URL parsing, failing safe to the strictest tier.
- **CORS** restricted by default to the hosted frontend, fhirpath-lab.com,
  and local dev — configurable per environment.

## 🚀 Live Demo

| | |
| --- | --- |
| Toolkit | [brendankowitz.github.io/ignixa-lab](https://brendankowitz.github.io/ignixa-lab/) |
| Conformance runner | [.../conformance.html](https://brendankowitz.github.io/ignixa-lab/conformance.html) |
| Expression Benches | [.../lab.html](https://brendankowitz.github.io/ignixa-lab/lab.html) |

## 🛠️ Quick Start

Prerequisites: **.NET SDK 10**, **Node.js 20+**, and the
[Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local).

**Backend**

```bash
./backend/pack-suites.ps1                             # packs the suites into artifacts/local-feed (first run + whenever suites change)
cd backend/src/Ignixa.Lab.Functions
cp local.settings.json.example local.settings.json    # first run only
func start                                             # serves http://localhost:7071
```

**Frontend**

```bash
cd frontend
npm install
npm run dev                                            # serves http://localhost:5173
```

The dev server proxies `/api/*` to the Functions host on port 7071, so the SPA
works against the local backend with no extra configuration. `index.html` is
the landing page, `conformance.html` is the TestScript runner, and `lab.html`
is Expression Benches — all served by the same dev server and cross-linked.

## 📁 Repository layout

```
.
├── backend/
│   ├── src/Ignixa.Lab.Functions/    .NET 10 Azure Functions (isolated worker)
│   │   ├── Conformance/              Report schema records (report, result, step, error, http)
│   │   ├── Configuration/            IgnixaLabOptions
│   │   ├── Execution/                Runner, SSRF guard, report mapper, evaluator factory
│   │   ├── Functions/                HTTP endpoints: Health, Suites, Run, Capability, FhirPath, Fakes, Validation
│   │   ├── Middleware/               CORS + rate limiting
│   │   ├── Models/                   Request/descriptor DTOs
│   │   ├── Suites/                   SuiteCatalog — reads testscripts restored from IgnixaLab.TestScript.Suites
│   │   └── Program.cs                Host + DI wiring
│   ├── src/Ignixa.Lab.Suites/        The 87 canonical TestScript suites, packed into a local NuGet feed
│   └── test/Ignixa.Lab.Functions.Tests/   xUnit tests
├── frontend/                         Vite + React 19 + TypeScript
│   └── src/                          conformance app (api client, types, hooks, components)
│                                      + benches/ (fhirpath, fakes, validation, fml, sof)
├── docs/                             Architecture, API, schema, and development guides
├── .github/workflows/                CI + deploy for backend and frontend
├── Directory.Build.props             Shared MSBuild settings (analyzers, warnings-as-errors)
├── Directory.Packages.props          Central Package Management (NuGet versions)
├── global.json                       Pinned .NET SDK
└── Ignixa.Lab.sln
```

## 📚 Documentation

| Guide | Contents |
| ----- | -------- |
| [Architecture](docs/architecture.md) | How the backend and frontend fit together. |
| [API reference](docs/api.md) | The `health`, `suites`, `run`, and `capability` endpoints. |
| [Conformance report schema](docs/conformance-report-schema.md) | The report JSON shape. |
| [Development](docs/development.md) | Build, test, lint, and add suites. |
| [backend/README.md](backend/README.md) | Suites, rate limiting, CORS, deploy, and capability gating in depth. |

## ✅ Testing at a glance

```bash
dotnet test Ignixa.Lab.sln          # backend (xUnit)
cd frontend && npm run build        # type-check + production build
cd frontend && npm run lint         # oxlint
```

## 🤝 Acknowledgments

Ignixa Lab is the conformance and evaluation companion to
[ignixa-fhir](https://github.com/brendankowitz/ignixa-fhir), and its FHIRPath
endpoints serve [fhirpath-lab.com](https://fhirpath-lab.com) first-class.
Most TestScript suites map to the e2e test coverage of the
[Microsoft FHIR Server](https://github.com/microsoft/fhir-server), with
additional coverage inspired by fhir-candle, HAPI FHIR, LinuxForHealth FHIR,
Health Samurai, and Helios — see [backend/README.md](backend/README.md#suites)
for the full breakdown.

## 📄 License

See [LICENSE](LICENSE).

---

<p align="center">
  <b>Ignixa Lab</b> — proving ground for the Ignixa FHIR engines.
</p>
