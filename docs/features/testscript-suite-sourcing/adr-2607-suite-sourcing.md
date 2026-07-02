# ADR-2607: TestScript suite sourcing

**Status**: Proposed
**Date**: 2026-07-01
**Feature**: testscript-suite-sourcing

## Context

Ignixa Lab's Functions backend parses and executes FHIR TestScript suites against
a target server; the frontend is a static GitHub Pages bundle that never executes
them. Today the repo vendors four curated starter suites, copied to the Functions
output at build. The canonical suites live upstream in `ignixa-fhir`, which also
publishes the execution engine as the `Ignixa.TestScript` NuGet package. We need
the backend to obtain the canonical suites in a way that pins to a known version,
stays reproducible in CI, and does not drift from upstream — without adding undue
operational friction.

## Options Considered

1. **Stay fully vendored in this repo** — copy suites into the repo as today.
   *(rejected: drifts from upstream and must be hand-synced; the repo becomes a
   stale fork of the canonical set)*
2. **Git submodule to `ignixa-fhir`** — reference upstream at a pinned commit.
   *(rejected: every checkout/CI needs `--recurse-submodules`, contributors trip
   over it, and it drags the whole upstream repo in for a subset)*
3. **Versioned artifact / NuGet content package** — `ignixa-fhir` publishes the
   suites as a pinned package (or release asset) restored into the Functions
   output at build. *(chosen)*

## Decision

Adopt option 3. `ignixa-fhir` publishes the canonical suites as a versioned
artifact — preferably a NuGet content/data package, mirroring how the engine
already ships — and the Ignixa Lab backend consumes a pinned version, restoring
the suites into the Functions output at build. The suite package version can be
tracked alongside the `Ignixa.TestScript` engine version so the two stay aligned.
The existing four vendored starters remain in-repo as an offline/dev and test
fallback. Suites are deployed with the backend only.

This wins because it gives explicit version pinning and reproducible builds, keeps
CI simple (no submodule recursion), avoids cloning the whole upstream repo for a
subset, and matches the engine's existing distribution model.

## Consequences

- The backend build gains a package reference (or download step) for the suites
  artifact; `SuiteCatalog` loads from the restored content path, falling back to
  the vendored starters when the package is absent.
- Suites are pinned to a version and travel through CI reproducibly; upgrading is
  an explicit version bump rather than a manual copy.
- Requires an upstream `ignixa-fhir` change to publish the suites artifact. Until
  that exists, the vendored starters remain the effective source — this ADR sets
  the target so the consumption seam can be built now and pointed at the package
  when it ships.
- Adds a versioning-coordination concern between the engine package and the suites
  package that upstream must manage.
