# Ignixa Lab — Frontend

Single-page app for running FHIR TestScript conformance suites against a target
server and viewing a category-based results matrix. Built with **Vite 8**,
**React 19**, and **TypeScript**, linted with **oxlint**.

> This is the structural skeleton. Components render minimal, unstyled markup so
> the detailed visual design can be iterated on independently. The data flow,
> API client, and type contracts are complete.

## Scripts

| Command           | Description                                        |
| ----------------- | -------------------------------------------------- |
| `npm run dev`     | Start the dev server (proxies `/api` → `:7071`).   |
| `npm run build`   | Type-check (`tsc -b`) and produce a production build. |
| `npm run preview` | Serve the production build locally.                |
| `npm run lint`    | Lint with oxlint.                                  |

## Backend connection

The client calls the Functions backend at `/api/health`, `/api/suites`, and
`/api/run`. During development, `vite.config.ts` proxies `/api/*` to
`http://localhost:7071` (the default Azure Functions host), so no CORS setup is
needed. For a standalone build that targets a remote backend, set
`VITE_API_BASE_URL` (see `.env.example`).

## Structure

```
src/
  api/client.ts            Typed fetch wrapper (getHealth, getSuites, runConformance)
  types/conformance.ts     TypeScript mirror of the backend conformance schema
  lib/conformance.ts       Status aggregation + category grouping helpers
  hooks/
    useConformanceRun.ts   Loads the suite catalog and drives a run
    useSuiteSelection.ts    Manages the selected-suite set
  components/
    HostForm.tsx           Target URL input + run/cancel controls
    SuitePicker.tsx        Suite catalog grouped by category with selection
    ProgressPanel.tsx      Run status / error surface
    SummaryCards.tsx       Pass rate and per-status totals
    ResultsMatrix.tsx      Category-based results view
    CategoryGroup.tsx      Collapsible category section
    ResultRow.tsx          Expandable test-case row
    StepTrace.tsx          Per-step trace for a test case
  App.tsx                  Composes the run workflow
```

The `types/conformance.ts` interfaces intentionally match the C# records under
`backend/src/Ignixa.Lab.Functions/Conformance` (including the `duration_ms`
JSON fields), so a report is interchangeable with the ignixa-fhir conformance
dashboard artifact.
