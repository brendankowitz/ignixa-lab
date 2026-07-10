# TestScript provenance manifest hardening

## Context

Ignixa Lab currently keeps one FHIR R4 `Provenance` sidecar beside each bundled
TestScript. The sidecars are packaged with the suite content, ignored by
`SuiteCatalog`, and generated deterministically by
`tools/new-provenance-sidecars.ps1`.

The sidecar architecture is sound, but the generator infers source attribution
from category prefixes and a growing set of path exceptions. In particular,
unmatched files in several broad categories default to Microsoft FHIR Server.
That makes a newly authored or differently sourced suite susceptible to false
attribution unless a maintainer remembers to add another exception.

The initial warning-only audit was appropriate while retrofitting the existing
suite set. All current TestScripts now have sidecars, so structural and
classification failures can become blocking without making uncertain historical
source precision blocking.

## Goals

1. Make source attribution explicit for every bundled TestScript.
2. Remove category-based and default source inference.
3. Distinguish locally authored TestScripts from externally distilled
   TestScripts through the Provenance activity.
4. Record the date of each attribution assertion rather than applying a hidden
   global timestamp.
5. Validate the allowed activity and source-relationship vocabulary.
6. Detect missing, unclassified, orphaned, malformed, mismatched, or stale
   provenance during packaging and CI.
7. Preserve deterministic sidecar generation and current runtime/API behavior.
8. Record exact upstream revisions and source permalinks where existing
   evidence supports them without fabricating historical precision.

## Non-goals

- Publishing formal FHIR `StructureDefinition` or `CodeSystem` resources.
- Adding a full external FHIR validator dependency.
- Exposing provenance through backend APIs or the frontend.
- Automatically proving copyright or license compatibility.
- Inventing commit SHAs, tags, or exact source files that were not captured
  during the original distillation.

## Options considered

### 1. Keep path rules and add more exceptions

This is the smallest change, but correctness continues to depend on rule order
and maintainers recognizing every special case. A broad fallback can still
silently produce plausible but false provenance.

### 2. Hand-maintain sidecars without a generator

This makes each sidecar independently editable, but repeats shared source
metadata across many files and makes vocabulary, formatting, and structural
consistency harder to enforce.

### 3. Use an explicit manifest as the source of truth

This keeps one reviewable sidecar per TestScript while centralizing the
authoritative attribution decisions in a machine-validated manifest. The
generator becomes a pure manifest-to-sidecar transformation. This is the chosen
option.

## Design

### 1. Manifest location and ownership

Add:

```text
backend/src/Ignixa.Lab.Suites/tools/provenance-manifest.json
```

The manifest is authoring and build metadata. It remains outside
`testscripts/`, so `SuiteCatalog` cannot mistake it for an executable
TestScript. Generated `.provenance.json` files remain packaged beside their
targets and remain the portable FHIR representation.

The manifest is authoritative. Sidecars must never contain attribution that
cannot be reproduced from it.

### 2. Manifest model

The manifest has a version, reusable source profiles, and one explicit entry
keyed by every TestScript's suite-relative path:

```json
{
  "schemaVersion": 1,
  "profiles": {
    "fhir262-search": {
      "sources": [
        {
          "reference": "https://github.com/fhir-fi/fhir262",
          "display": "fhir262 search tests",
          "relationship": "distilled-from",
          "license": "source-declared open-source license",
          "version": "not recorded during original distillation",
          "notes": "Ported fhir262 search coverage into black-box FHIR TestScript assertions."
        },
        {
          "reference": "https://hl7.org/fhir/R4/search.html",
          "display": "FHIR R4 search",
          "relationship": "spec-reference",
          "license": "FHIR specification license",
          "version": "FHIR R4",
          "notes": "Normative search semantics used while distilling the suite."
        }
      ]
    }
  },
  "suites": {
    "Search/basic.json": {
      "profile": "fhir262-search",
      "activity": "distill-testscript",
      "recorded": "2026-07-07T00:00:00Z"
    }
  }
}
```

Each suite entry must contain:

- `profile`: the reusable source profile to resolve.
- `activity`: an allowed TestScript provenance activity.
- `recorded`: the date-time when the provenance assertion was authored or last
  materially revised.

Profiles contain the complete source entity metadata needed by the FHIR
sidecar. When exact upstream evidence is known for only one suite, that suite
may use an optional inline `sources` array instead of `profile`. Exactly one of
`profile` or `sources` must be present.

Inline sources use the same shape and validation rules as profile sources. This
allows a suite to cite an exact upstream file, class, tag, or commit without
creating a one-use profile.

### 3. Controlled vocabulary

Allowed activities:

| Code | Meaning |
| --- | --- |
| `author-testscript` | The TestScript was created in Ignixa Lab from local requirements and normative references. |
| `distill-testscript` | External test behavior or coverage was transformed into a portable black-box TestScript. |

Allowed source relationships:

| Code | Meaning |
| --- | --- |
| `authored-in` | Repository where the TestScript was originally authored. |
| `direct-port` | The TestScript closely ports a specific external test. |
| `distilled-from` | External test behavior was materially transformed or generalized. |
| `inspired-by` | The source informed coverage but was not directly transformed. |
| `spec-reference` | Normative or explanatory specification context. |

`author-testscript` is used for the locally created all-resource-type suites and
other suites whose primary origin is Ignixa Lab. `distill-testscript` is used
when upstream test coverage is a direct input. A normative specification entity
does not by itself require `distill-testscript`.

The existing Ignixa extension URLs remain unchanged. This phase validates their
values locally but does not publish formal conformance resources.

### 4. Generator behavior

`new-provenance-sidecars.ps1` loads and validates the manifest before writing
anything. It must:

1. Discover executable TestScripts using the existing file discovery helper.
2. Require exactly one manifest entry for every discovered TestScript.
3. Reject manifest entries whose target TestScript does not exist.
4. Resolve exactly one valid source profile or inline source list.
5. Pass the entry's activity and recorded date into
   `New-TestScriptProvenance`.
6. Generate sidecars in sorted path order with deterministic property and entity
   ordering.
7. Stop with a non-zero exit code before writing files if the manifest is
   invalid or incomplete.

There is no category fallback and no default attribution profile.

### 5. Audit behavior

Extend the audit result to distinguish blocking errors from advisory warnings.

Blocking errors:

- invalid or unsupported manifest schema;
- duplicate, missing, or orphaned suite classification;
- missing profile or simultaneous `profile` and `sources`;
- invalid activity or source relationship;
- missing required source reference, display, license, or notes;
- malformed sidecar or invalid minimal Provenance structure;
- sidecar target mismatch;
- sidecar content that does not match deterministic manifest generation.

Advisory warnings:

- an upstream repository source lacks a captured commit, tag, release, or exact
  permalink;
- a license remains recorded only as source-declared or repository license;
- historical evidence is intentionally approximate.

`verify-provenance.ps1` keeps non-blocking behavior by default so maintainers can
inspect all findings interactively. It gains `-Strict`; strict mode exits
non-zero when blocking errors exist. Warnings never make strict mode fail.

`backend/pack-suites.ps1` invokes the audit with `-Strict`, making structural,
classification, vocabulary, and stale-output failures blocking in packaging and
CI.

### 6. Accurate activity and recorded values

`New-TestScriptProvenance` accepts `Activity` and `Recorded` as required inputs.
It maps supported activity codes to deterministic displays:

- `author-testscript` -> `Author TestScript`
- `distill-testscript` -> `Distill TestScript`

The module no longer owns a global recorded timestamp. Existing sidecars retain
their original provenance assertion date during migration unless repository
history provides a more accurate material-update date.

### 7. Source precision

The migration replaces generic source versions with exact evidence only where
that evidence can be verified from repository history, source descriptions, or
an existing stable upstream reference. Unknown historical revisions are labeled
honestly rather than replaced with the upstream repository's current HEAD.

Newly distilled suites are expected to capture:

- an immutable commit, tag, or release;
- the most precise stable source URL available;
- an SPDX license identifier when known;
- concise transformation notes.

Approximate historical records remain valid but generate advisory warnings.

### 8. Runtime and packaging behavior

No API, frontend, suite descriptor, or execution behavior changes. Generated
sidecars keep the existing filename convention and FHIR target identifier.
`SuiteCatalog` continues to ignore `*.provenance.json`.

The manifest itself is not included in the suite package. The generated FHIR
sidecars and package-level `source-revision.txt` remain the portable audit
artifacts.

## Migration

1. Convert every current source profile into manifest profile data.
2. Add an explicit manifest entry for all 87 current TestScripts.
3. Assign `author-testscript` to locally authored suites and
   `distill-testscript` to externally derived suites based on current evidence.
4. Preserve known fhir262, fhir-candle, Microsoft FHIR Server, HAPI FHIR,
   LinuxForHealth, Ignixa, and FHIR specification distinctions.
5. Replace exact source metadata only when it can be substantiated.
6. Regenerate all sidecars and review the resulting attribution diff.
7. Enable strict packaging only after manifest completeness and deterministic
   agreement tests pass.

## Testing

Add focused tests proving:

- every discovered TestScript has exactly one explicit manifest entry;
- orphaned entries and unknown profiles fail validation;
- no category or default attribution fallback exists;
- unsupported activities and relationships fail validation;
- locally authored suites emit `author-testscript`;
- externally derived suites emit `distill-testscript`;
- per-suite recorded values are preserved;
- manifest generation is deterministic across directories;
- stale or manually altered sidecars are detected;
- default audit mode reports errors but exits successfully;
- strict audit mode exits non-zero for blocking errors and zero for warnings
  alone;
- packaging invokes strict audit;
- generated sidecars remain packaged but excluded from executable suite counts.

The full backend build and test suite remains the final regression gate.

## Risks and mitigations

- **Manifest size:** Explicitly listing every suite adds data, but the list is
  the audit boundary and prevents accidental source inheritance.
- **Two representations:** The manifest and sidecars could drift; deterministic
  comparison makes drift a blocking error.
- **Historical uncertainty:** Some revisions were not recorded at distillation
  time; warnings expose that debt without fabricating evidence or blocking the
  migration.
- **Vocabulary remains local:** Values are validated and documented now;
  publishing formal FHIR conformance resources remains a separate future phase.

## Success criteria

- All bundled TestScripts are explicitly classified without category fallbacks.
- Generated sidecars accurately distinguish authored and distilled activities.
- The audit detects incomplete, invalid, or stale provenance.
- Packaging fails on blocking provenance errors but not historical precision
  warnings.
- Runtime/API behavior and executable suite counts remain unchanged.
