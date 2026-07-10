# Feature: TestScript suite sourcing

**Status**: Decided

## Problem

Ignixa Lab's backend executes FHIR TestScript suites against a target server. The
repo currently packs 87 executable canonical suites from
`backend/src/Ignixa.Lab.Suites/testscripts/` into the interim local
`IgnixaLab.TestScript.Suites` NuGet content package. The canonical/full suites
live upstream in `ignixa-fhir` (which also publishes the `Ignixa.TestScript`
engine as a NuGet package). We need a sustainable way for the backend to consume
those suites without drifting from upstream, while preserving the current local
package flow until the upstream artifact ships.

## Decision

Use `Ignixa.Lab.Suites` to pack the current canonical suites into the local
`IgnixaLab.TestScript.Suites` NuGet content package, restored into the Functions
output at build. This remains an interim implementation of the long-term
decision to source the canonical suites as a **versioned artifact** (NuGet
content package, or a pinned GitHub release asset) published from `ignixa-fhir`.
Suites deploy with the **backend only** — the static frontend never executes
them.

See [ADR-2607: TestScript suite sourcing](./adr-2607-suite-sourcing.md).

## Provenance sidecars

Bundled TestScripts carry per-file FHIR R4 Provenance sidecars named
`<suite>.provenance.json` beside the executable TestScript. They are generated
from `backend/src/Ignixa.Lab.Suites/tools/provenance-manifest.json`, which is
the authoritative source of truth for bundled provenance. Sidecars are
committed and packaged artifacts, ignored by `SuiteCatalog`, and excluded from
runtime APIs.

Each bundled TestScript that is new or materially changed must add or update
its exact manifest entry in that file before provenance is regenerated.
Deleting or renaming a bundled TestScript must also delete its old committed
sidecar and update or remove the matching manifest entry.

Use `author-testscript` for locally created coverage and `distill-testscript`
when external test behavior is transformed. Capture the most precise stable
upstream commit, tag, or release, the best stable file/class URL available, and
an SPDX license identifier when known. Never replace an unknown historical
revision with the upstream repository's current HEAD.

`pack-suites.ps1` runs `verify-provenance.ps1 -Strict` before packaging. That
blocks structural, classification, stale-sidecar, and orphan-sidecar errors;
source precision and license advisories remain warnings.

Maintainer sequence:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 -Force
pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 -Strict
.\backend\pack-suites.ps1
```
