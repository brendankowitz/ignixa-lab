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

## `GET /api/capability`

Fetches `{target}/metadata` from the target FHIR server and normalizes its
declared `CapabilityStatement` into the fixed resource/interaction shape the
frontend renders as a capability coverage map.

**Query parameters**

| Parameter | Required | Description |
| --------- | -------- | ----------- |
| `target` | yes | Absolute `http`/`https` base URL of the server under test (same SSRF guard as `POST /api/run`). |
| `fhirVersion` | no | Recorded on the response; defaults to the server configuration. |

**200 OK**

```json
{
  "target": "https://hapi.fhir.org/baseR4",
  "fhirVersion": "4.0",
  "resources": [
    { "type": "Patient", "interactions": ["read", "vread", "create", "update", "delete", "search", "history"] }
  ]
}
```

Raw FHIR interaction codes are mapped onto a fixed column set: `read`, `vread`,
`create`, `update`, `patch`, `delete`, `search` (from `search-type`), and
`history` (from `history-instance` and `history-type`, de-duplicated). Codes
with no mapping (e.g. `capabilities`, `transaction`, `batch`,
`history-system`) are dropped.

**400 Bad Request** — the same target-validation failure shape as `POST /api/run`:

```json
{ "error": "The target URL resolves to a private, loopback, or link-local address, which is not permitted." }
```

**502 Bad Gateway** / **504 Gateway Timeout** — the target could not be
reached, returned a non-2xx status for `/metadata`, or returned an
unparseable body:

```json
{ "error": "https://example.org returned HTTP 500 for /metadata." }
```

The frontend degrades to observed-only coverage rendering when this endpoint
fails.
