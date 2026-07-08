# Feature: TestScript suite sourcing

**Status**: Decided

## Problem

Ignixa Lab's backend executes FHIR TestScript suites against a target server. The
repo currently vendors four curated starter suites, copied into the Functions
output at build. The canonical/full suites live upstream in `ignixa-fhir` (which
also publishes the `Ignixa.TestScript` engine as a NuGet package). We need a
sustainable way for the backend to obtain the canonical suites without drifting
from upstream, and to decide where they are deployed.

## Decision

Source the canonical suites as a **versioned artifact** (NuGet content package,
or a pinned GitHub release asset) published from `ignixa-fhir`, restored into the
Functions output at build; keep the vendored starters as an offline/dev + test
fallback. Suites deploy with the **backend only** — the static frontend never
executes them.

See [ADR-2607: TestScript suite sourcing](./adr-2607-suite-sourcing.md).

## Provenance sidecars

Bundled TestScripts carry per-file FHIR R4 Provenance sidecars named
`<suite>.provenance.json` beside the executable TestScript. The sidecars are
packaged with `IgnixaLab.TestScript.Suites`, ignored by `SuiteCatalog`, and used
for auditability rather than runtime behavior. They complement the package-level
`source-revision.txt`: the revision identifies the ignixa-lab commit that was
packed, while each Provenance resource records the upstream source entities that
influenced the distilled TestScript.
