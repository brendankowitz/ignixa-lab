# TestScript provenance sidecars

## Context

Ignixa Lab packages its bundled FHIR TestScript suites through
`backend/src/Ignixa.Lab.Suites` as the interim `IgnixaLab.TestScript.Suites`
content package. The package already stamps a package-level
`testscripts/source-revision.txt` so the app can link bundled TestScript files
back to the exact ignixa-lab commit they were packed from.

That is useful for reproducibility of the packaged artifact, but it does not
answer where an individual distilled TestScript came from. Several current and
future suites are derived from, inspired by, or cross-checked against external
sources such as Microsoft FHIR Server tests, ignixa-fhir/ignixa-lab tests,
fhir262 cases, fhir-candle coverage, HAPI FHIR terminology behavior, API
documentation, and FHIR specification sections.

FHIR R4 `Provenance` is a good fit for this because it records the target
artifact, the activity that produced it, the responsible agent, and the source
entities used to create it. FHIR Provenance is contextual metadata rather than
an executable TestScript concern, so it should live beside the test fixtures
without changing test execution semantics.

## Goals

1. Add one valid FHIR R4 `Provenance` sidecar per distilled bundled TestScript.
2. Package the sidecars with `IgnixaLab.TestScript.Suites`, but do not expose
   them through `GET /api/suites`, the frontend, or runtime APIs yet.
3. Retrofit sidecars for the existing bundled suite set and make sidecars the
   documented expectation for newly added or materially changed suites.
4. Keep validation warning-only for now: missing, incomplete, or invalid
   provenance should be visible to maintainers but must not fail CI yet.
5. Preserve the current suite count and runner behavior.

## Non-goals

- No new public API fields, frontend provenance panel, or TestReport export
  changes in this phase.
- No attempt to prove copyright or license compatibility automatically. The
  sidecars record the declared source/license/relation so maintainers can review
  it.
- No requirement that every source link points to a line-level permalink. Use
  the most precise stable source available; a repository, issue, API page, or
  specification section is acceptable when that is the true source.

## Design

### 1. File layout

Place each sidecar next to the TestScript it describes, sharing the same base
filename:

```text
backend/src/Ignixa.Lab.Suites/testscripts/<Category>/<suite>.json
backend/src/Ignixa.Lab.Suites/testscripts/<Category>/<suite>.provenance.json
```

This keeps provenance discoverable during code review and lets the existing
suite package include the sidecars with minimal packaging changes. The package
project already packs `testscripts/**/*.json`, and the package targets already
copy `testscripts/**/*.json` into consumers' output directories.

Because sidecars are also JSON files under `testscripts/`, `SuiteCatalog` must
explicitly ignore `*.provenance.json`. Otherwise it will try to parse every
sidecar as a TestScript and log each one as an invalid suite. This filter is the
only runtime behavior change required for the sidecars-only phase.

### 2. Provenance target

The sidecar targets the distilled TestScript artifact by a stable path
identifier:

```json
{
  "identifier": {
    "system": "urn:ignixa-lab:testscripts:path",
    "value": "Microsoft/ms-convert-data.json"
  },
  "display": "Microsoft/ms-convert-data.json"
}
```

Use a path identifier rather than a commit-specific GitHub URL in the static
sidecar. The final commit hash is not known when the sidecar is authored, and
the package-level `source-revision.txt` already records the exact packed
ignixa-lab revision.

### 3. Provenance content model

Each sidecar is a FHIR R4 `Provenance` resource with these conventions:

- `resourceType`: always `Provenance`.
- `target`: the TestScript path identifier described above.
- `recorded`: when the provenance assertion was authored or last materially
  updated.
- `activity`: an Ignixa coding for TestScript distillation, for example
  `http://ignixa.io/fhir/provenance-activity|distill-testscript`.
- `agent`: the maintainer or automation responsible for the distillation or
  review. Use `Ignixa Lab maintainers` when a specific person is not useful.
- `entity[]`: one entry per upstream influence with `role: "source"` and the
  most precise stable reference available.

FHIR Provenance does not have first-class fields for the relationship strength
or source license, so use Ignixa extensions on each source entity:

| Extension | Value | Purpose |
| --- | --- | --- |
| `http://ignixa.io/fhir/StructureDefinition/provenance-source-relationship` | `direct-port`, `distilled-from`, `inspired-by`, or `spec-reference` | Describes how strongly the TestScript depends on the source. |
| `http://ignixa.io/fhir/StructureDefinition/provenance-source-license` | SPDX-like license string or source-declared license text | Records the source license seen during distillation. |
| `http://ignixa.io/fhir/StructureDefinition/provenance-source-version` | Commit SHA, tag, release, API version, or spec version | Pins the upstream source when known. |
| `http://ignixa.io/fhir/StructureDefinition/provenance-distillation-notes` | Short string | Explains what changed during distillation. |

Example shape:

```json
{
  "resourceType": "Provenance",
  "target": [
    {
      "identifier": {
        "system": "urn:ignixa-lab:testscripts:path",
        "value": "Microsoft/ms-convert-data.json"
      },
      "display": "Microsoft/ms-convert-data.json"
    }
  ],
  "recorded": "2026-07-07T00:00:00Z",
  "activity": {
    "coding": [
      {
        "system": "http://ignixa.io/fhir/provenance-activity",
        "code": "distill-testscript",
        "display": "Distill TestScript"
      }
    ]
  },
  "agent": [
    {
      "who": {
        "identifier": {
          "system": "https://github.com/brendankowitz/ignixa-lab",
          "value": "maintainers"
        },
        "display": "Ignixa Lab maintainers"
      }
    }
  ],
  "entity": [
    {
      "role": "source",
      "what": {
        "reference": "https://github.com/microsoft/fhir-server",
        "display": "Microsoft FHIR Server test coverage"
      },
      "extension": [
        {
          "url": "http://ignixa.io/fhir/StructureDefinition/provenance-source-relationship",
          "valueCode": "distilled-from"
        },
        {
          "url": "http://ignixa.io/fhir/StructureDefinition/provenance-source-license",
          "valueString": "MIT"
        },
        {
          "url": "http://ignixa.io/fhir/StructureDefinition/provenance-distillation-notes",
          "valueString": "Converted server-specific e2e behavior into black-box FHIR TestScript assertions."
        }
      ]
    }
  ]
}
```

### 4. Retrofitting existing suites

Retrofit the current bundled suite set in one pass. The level of specificity can
vary by the evidence available:

- Microsoft-derived categories and `Microsoft/*` suites should point to the
  Microsoft FHIR Server repository, and to the exact upstream test file or test
  class when known.
- Ignixa-native suites should point to the ignixa-lab or ignixa-fhir source that
  introduced the behavior.
- Terminology and Subscriptions additions should cite the surveyed source suites
  or API/spec pages that informed them, such as fhir-candle, HAPI FHIR,
  LinuxForHealth FHIR, or HL7 FHIR operation pages.
- If an existing TestScript only has source-family evidence, record that honestly
  with `relationship: "inspired-by"` or `relationship: "distilled-from"` and
  notes explaining the limits of the attribution.

Do not fabricate exact source paths. A less precise but true provenance record is
better than a false permalink.

### 5. Warning-only validation

Add a maintainer-facing provenance audit command that:

1. Scans executable TestScripts under `backend/src/Ignixa.Lab.Suites/testscripts/`.
2. Checks for a sibling `<suite>.provenance.json`.
3. Parses present sidecars as JSON and verifies a minimal FHIR Provenance shape:
   `resourceType == "Provenance"`, at least one `target`, at least one `agent`,
   and at least one `entity`.
4. Verifies the first target path identifier matches the sibling TestScript's
   relative path.
5. Emits warnings for missing, invalid, or mismatched sidecars, then exits
   successfully.

The command can be called from `backend/pack-suites.ps1` so CI logs show the
warnings without blocking. A later tightening phase can flip the same audit to
fail CI once the team is comfortable with the sidecar quality.

### 6. Documentation updates

Update suite authoring docs to say a new or materially changed bundled
TestScript should include or update its `.provenance.json` sidecar. Also update
outdated suite-source documentation while touching it: current docs still mention
older suite counts and an old `Ignixa.Lab.Functions/Suites/testscripts` path in
places, while the active suite package lives under `Ignixa.Lab.Suites`.

### 7. Tests

Add focused tests for behavior that must not regress:

- `SuiteCatalog` ignores `*.provenance.json` and keeps the bundled suite count
  unchanged.
- Packaged output contains provenance sidecars when they exist.
- The warning-only audit reports missing/invalid/mismatched sidecars but returns
  success.

No frontend tests are required in this phase because there are no frontend
contract changes.

## Risks and trade-offs

- **Many files:** one sidecar per TestScript adds file volume, but it gives the
  cleanest review surface and avoids ambiguous source/category manifests.
- **FHIR validity vs practical metadata:** license and relationship details need
  extensions. Keeping them as FHIR extensions preserves valid FHIR JSON instead
  of inventing non-FHIR fields.
- **Warning-only enforcement:** provenance quality may drift until CI becomes
  strict. This is intentional for the first phase because the existing suite set
  needs a practical retrofit path.
- **Source precision:** some existing suites may not have exact upstream file
  attribution. The design favors truthful approximate provenance over invented
  precision.

## Future path

Once sidecars are complete and warning-only validation is quiet enough, a later
phase can expose provenance through the app:

1. Add optional provenance metadata to `SuiteDescriptor` or a dedicated
   `/api/suites/{id}/provenance` endpoint.
2. Add a frontend source/provenance drawer in the suite picker.
3. Include Provenance resources in downloaded FHIR `Bundle`s alongside
   `TestReport` resources, with each Provenance targeting the exported
   TestReport or its source TestScript as appropriate.
4. Make the audit command fail CI for missing or invalid sidecars.
