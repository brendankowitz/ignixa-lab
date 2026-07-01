# API reference

The backend exposes three anonymous HTTP endpoints under `/api`. All responses
are JSON. During development the frontend reaches these through the Vite proxy
(`/api/*` → `http://localhost:7071`).

## `GET /api/health`

Liveness probe reporting service status and the TestScript engine version.

**200 OK**

```json
{
  "status": "ok",
  "engineVersion": "0.5.6-beta"
}
```

## `GET /api/suites`

Returns the catalog of bundled TestScript suites available to run.

**200 OK** — an array of suite descriptors:

```json
[
  {
    "id": "crud/patient-lifecycle",
    "name": "Patient lifecycle",
    "description": "Create, read, update, and delete a Patient resource.",
    "category": "crud",
    "fhirVersion": "4.0",
    "file": "crud/patient-lifecycle.json"
  }
]
```

| Field | Type | Description |
| ----- | ---- | ----------- |
| `id` | string | Stable identifier used in a run request. |
| `name` | string | Human-readable suite name. |
| `description` | string | Short description. |
| `category` | string | Derived from the suite's immediate sub-folder. |
| `fhirVersion` | string | FHIR version the suite targets. |
| `file` | string | Path relative to the suites directory. |

## `POST /api/run`

Executes the selected suites (and any inline uploaded TestScripts) against a
target FHIR server and returns a conformance report.

**Request body**

```json
{
  "targetUrl": "https://hapi.fhir.org/baseR4",
  "suiteIds": ["crud/patient-lifecycle", "search/patient-search"],
  "fhirVersion": "4.0",
  "uploadedTestScripts": [
    { "fileName": "my-test.json", "content": "{ …TestScript JSON… }" }
  ]
}
```

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `targetUrl` | string | yes | Absolute `http`/`https` base URL of the server under test. |
| `suiteIds` | string[] | no | IDs from `GET /api/suites`. |
| `fhirVersion` | string | no | Overrides the server default (for example `4.0`). |
| `uploadedTestScripts` | object[] | no | Inline TestScripts (`fileName`, `content`). |

At least one suite **or** one uploaded TestScript must be supplied.

**200 OK** — a [`ConformanceReport`](conformance-report-schema.md).

**400 Bad Request** — a validation failure:

```json
{ "error": "Target URL must use http or https." }
```

Common causes: missing/relative/non-http `targetUrl`; a target that resolves to
a private/loopback address (unless `AllowPrivateTargets` is enabled); an unknown
suite ID; an unparseable uploaded TestScript; an empty selection; or more than
`MaxSuitesPerRun` suites.
