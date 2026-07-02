# Conformance report schema

`POST /api/run` returns a `ConformanceReport`. Its JSON shape is intentionally
identical to the `conformance/latest.json` artifact consumed by the ignixa-fhir
conformance dashboard, so reports are interchangeable.

The C# records live in
`backend/src/Ignixa.Lab.Functions/Conformance/`; the TypeScript mirror is in
`frontend/src/types/conformance.ts`.

> **Note on casing.** Most fields are camelCase, but duration fields use the
> snake_case name **`duration_ms`** to match the dashboard artifact.

## Example

```json
{
  "impl": "ignixa-lab",
  "target": "https://hapi.fhir.org/baseR4",
  "fhirVersion": "4.0",
  "startedAt": "2026-07-01T12:00:00+00:00",
  "duration_ms": 1840,
  "results": [
    {
      "id": "Patient lifecycle > Create then read a Patient",
      "file": "crud/patient-lifecycle.json",
      "suite": "crud/patient-lifecycle",
      "category": "crud",
      "status": "pass",
      "duration_ms": 320,
      "error": null,
      "steps": [
        {
          "phase": "test",
          "kind": "operation",
          "status": "pass",
          "duration_ms": 210,
          "label": "Create Patient",
          "description": "POST a new Patient",
          "message": null,
          "request": null,
          "response": null
        }
      ]
    }
  ]
}
```

## `ConformanceReport`

| Field | Type | Description |
| ----- | ---- | ----------- |
| `impl` | string | Implementation identifier. Always `ignixa-lab`. |
| `target` | string | The target FHIR server base URL. |
| `fhirVersion` | string | FHIR version used for the run. |
| `startedAt` | string (date-time) | When the run started (UTC, ISO 8601). |
| `duration_ms` | number | Total wall-clock duration in milliseconds. |
| `results` | `ConformanceResult[]` | One entry per executed test case. |

## `ConformanceResult`

One result per TestScript test case within a suite.

| Field | Type | Description |
| ----- | ---- | ----------- |
| `id` | string | `"{ScriptName} > {TestName}"`, or the script name if the test case is unnamed. |
| `file` | string | Suite file path (relative to the suites directory). |
| `suite` | string | Suite ID the result belongs to. |
| `category` | string | Suite category. |
| `status` | `ConformanceStatus` | Roll-up status for the test case. |
| `duration_ms` | number | Summed duration of the case's steps. |
| `error` | `ConformanceError \| null` | The first failing assertion, when applicable. |
| `steps` | `ConformanceStep[]` | Ordered setup, test, and teardown steps. |

## `ConformanceStep`

| Field | Type | Description |
| ----- | ---- | ----------- |
| `phase` | `"setup" \| "test" \| "teardown"` | Phase the step belongs to. |
| `kind` | `"operation" \| "assertion"` | Inferred from the step wording. |
| `status` | `ConformanceStatus` | Step outcome. |
| `duration_ms` | number | Step duration in milliseconds. |
| `label` | string \| null | Short label. |
| `description` | string \| null | Longer description. |
| `message` | string \| null | Engine message (assertion detail, error text). |
| `request` | `ConformanceHttpRequest \| null` | Captured request (best-effort). |
| `response` | `ConformanceHttpResponse \| null` | Captured response (best-effort). |

## `ConformanceError`

| Field | Type | Description |
| ----- | ---- | ----------- |
| `assertion` | string \| null | The assertion (or operation) that failed. |
| `received` | string \| null | What the server returned. |

## `ConformanceHttpRequest` / `ConformanceHttpResponse`

Captured on a best-effort basis depending on what the engine exposes.

| Field | Type | Applies to |
| ----- | ---- | ---------- |
| `method` | string | request |
| `url` | string | request |
| `statusCode` | number | response |
| `headers` | object (string → string) | both |
| `body` | string \| null | both |
| `bodyParseError` | string \| null | response |

## `ConformanceStatus`

One of:

| Value | Meaning |
| ----- | ------- |
| `pass` | The test case or step passed (engine `Pass`/`Warning`). |
| `fail` | An assertion failed (engine `Fail`). |
| `error` | Execution errored (engine `Error`, or an unhandled exception). |
| `skipped` | The step or case was skipped (engine `Skip`). |
