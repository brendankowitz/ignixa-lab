# Ignixa Lab — Frontend Implementation Design

**Date:** 2026-07-01
**Status:** Approved (brainstorming) — pending implementation plan
**Source design:** `Ignixa Lab.dc.html` (claude.ai Design project `8903721c-aa03-4d16-8e8d-6ccad7d9b096`)

## Goal

Turn the existing React frontend scaffold (PR #1, branch `claude/create-net-functions-application`)
into a working three-tab conformance app — **Setup · Runner · Report** — faithful to the design
mockup, wired to the real .NET Functions backend (`/api/health`, `/api/suites`, `/api/run`).

## Scope decisions

- **Real data only.** Everything rendered comes from actual `ConformanceReport`s. Design features
  that require data the backend does not keep are **omitted** for v1, each with a clean extension
  point:
  - Pass-rate trend / "last 10 runs" sparkline (needs run history)
  - Recent CI runs (sha, commit, delta) (needs history / git integration)
  - Capability coverage map (needs CapabilityStatement fetch + coverage computation)
- **Visual fidelity: faithful.** IBM Plex Sans/Mono, rounded panels, pill chips, the design's
  light/dark variable sets.
- **Accent: violet only.** The ember/teal accent switcher and the words-vs-icons chip switcher
  (the design's "Tweaks" panel) are dropped. Theme toggle (light/dark) stays.
- **Auth control: omitted for v1.** `RunRequest` carries no auth field, so a Bearer box would do
  nothing. A code comment marks the extension point.
- **Per-suite counts / time estimates on Setup: omitted.** Not present in `SuiteDescriptor`.
- **TestScript raw tab: omitted.** The report does not carry raw script text; row detail shows
  Assertions / Request / Response only.

## Live progress approach

`POST /api/run` returns a single report at completion and has no streaming channel. Rather than
fake per-test animation, the Runner issues **one `/api/run` call per selected suite, sequentially**,
sharing one `AbortController`, and merges each returned report as it lands. This yields real,
coarse-grained live progress with no backend change:

- Suite tree: each suite goes QUEUED → spinner (in-flight) → ✓/✕ + counts as its report returns.
- Status-bar progress: `completed suites / total suites`; pass/fail/skip tallies accumulate.
- Test list: fills in suite-by-suite.
- Stop: aborts the in-flight suite and cancels the remaining queue.

Granularity is per-suite, not per-test — acceptable for the ~4 bundled suites and fully honest.

## Architecture

### App shell & theming
- `App.tsx` → tabbed shell. Top bar: logo + "Ignixa Lab / CONFORMANCE", nav (Setup · Runner ·
  Report), `host · version` readout, theme toggle (☾/☀), primary **▶ Run tests / ■ Stop** button.
  Renders one screen by active tab.
- `useTheme` hook: `light|dark`, persisted to `localStorage['ignixa-lab-theme']` (same key as the
  design). Applied as CSS variables on a wrapper element. Violet accent is a fixed constant
  (light + dark ramps taken from the design's `ACCENTS.violet`).
- Global CSS (`index.css` / a `theme.css`): IBM Plex fonts, the full light/dark variable set
  (`--bg`, `--panel`, `--panel2`, `--text`/`--text2`/`--text3`/`--text4`, `--border`/`--border2`,
  `--inset`, `--pass*`, `--fail*`, `--skip`, `--accent*`, `--grad`, `--code`) lifted from the design.

### Setup screen (`SetupScreen`)
- Endpoint input (`https://` prefix + host/path).
- FHIR version toggle: R4 / R4B / R5 / STU3.
- Suite checklist from `GET /api/suites`, grouped by category; toggle individual + all.
- Primary action: **▶ Start run · N suites** → kicks off the run and switches to the Runner tab.

### Runner screen (`RunnerScreen`)
- Status bar, three states:
  - idle: prompt to run.
  - running: spinner + "Running <suite>…" + `completed/total` progress bar + live tallies.
  - done: **RUN COMPLETE** chip + `passed/failed/skipped · duration` + "View report →".
- Left suite tree: per-suite status (spinner / ✓ / ✕) + counts; click a suite to filter the list.
  (Sparkline omitted.)
- Right list: filter box, "Failing only · N" toggle, shown-count, grouped by file/category.
  Row expands to tabs **Assertions / Request / Response**:
  - Assertions: from `ConformanceStep`s with `kind === 'assertion'`; failing ones show
    expected/actual diff + hint sourced from `result.error` / `step.message`.
  - Request / Response: from `step.request` / `step.response`.

### Report screen (`ReportScreen`)
- Header: overall conformance % (pass rate), PASS/FAIL/SKIP pills, `target · version · duration`,
  "View N failing →" (jumps to Runner with failing-only enabled).
- Suites card: per-suite pass bars (report grouped by suite).
- Omitted: trend sparkline, capability coverage map, recent CI runs.

### State & data
- `useRunConfig` hook: endpoint, fhirVersion, selected suite IDs (reuses existing
  `useSuiteSelection`).
- `useConformanceRun` (existing) reworked: `start` loops selected suite IDs, calls `runConformance`
  per suite under a shared `AbortController`, accumulates a **merged report** + a **per-suite status
  map** (`queued | running | complete | error`). Phase `idle → running → complete/error`, now with
  incremental state exposed for the tree + progress bar. Run request passes `fhirVersion`.
- `lib/conformance.ts` gains pure derivations: `groupBySuite`, `groupByFile`,
  `extractAssertions(steps)`, reusing existing `countByStatus` / `passRate` / `groupByCategory`.
- Existing placeholder components (HostForm, SuitePicker, ProgressPanel, SummaryCards,
  ResultsMatrix, CategoryGroup, ResultRow, StepTrace) are **reworked into** the screens above.

## Deployment

### Frontend → GitHub Pages (project page)
- Served from `https://brendankowitz.github.io/ignixa-lab/`, so `vite.config.ts` sets
  `base: '/ignixa-lab/'`.
- No dev proxy in production: production calls the Functions app by absolute URL via
  `VITE_API_BASE_URL` (already supported by `api/client.ts`). Set as a GitHub Actions repo
  **variable**.
- GitHub Actions workflow (`.github/workflows/pages.yml`): on push to `main` touching `frontend/**`,
  build with the Pages base + `VITE_API_BASE_URL`, then `upload-pages-artifact` + `deploy-pages`
  (official Pages actions, least-privilege `permissions: pages: write, id-token: write`).
- Tab state is component-local (not URL routes), so Pages needs no SPA 404 fallback.

### Backend → Azure App Service (like `ignixafhirpath`)
- Add a deploy workflow (`.github/workflows/backend-deploy.yml`) that builds/publishes the isolated
  Functions app and deploys to an App Service via `Azure/functions-action` (or `webapps-deploy`).
- **No changes to the Azure subscription are made by this work.** Deployment requires inputs the
  maintainer provides as GitHub secrets/vars:
  - `AZURE_FUNCTIONAPP_NAME` (the App Service / Functions app name)
  - `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` (or OIDC federated credential as an alternative)
- **CORS:** the Functions app must allow the Pages origin (`https://brendankowitz.github.io`).
  Configure allowed origins from settings so it can be set per environment (App Service CORS config
  and/or the local `Host.CORS` setting for dev). Exact mechanism decided in the plan.

## Testing / verification

- No new test framework (matches PR intent; frontend has only `tsc -b` + `oxlint`). New logic is
  concentrated in pure `lib/conformance.ts` functions, easy to reason about and to cover with vitest
  later if desired.
- Verify via: `npm run build` (tsc typecheck + vite build), `npm run lint` (oxlint), and a manual
  run of the app against a running Functions host.
- Backend unchanged in behavior; existing xUnit tests continue to pass (`backend-check`).

## Out of scope (v1)

Run history & trend, recent CI runs, capability coverage map, per-test streaming, Bearer auth,
per-suite counts/estimates on Setup, raw TestScript tab. All left as clean extension points.
