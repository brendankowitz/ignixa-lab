# Development

## Prerequisites

- **.NET SDK 9** (pinned in `global.json`; roll-forward `latestFeature`).
- **Node.js 20+** and npm.
- **Azure Functions Core Tools v4** to run the backend locally (`func`).

## Backend

All commands are run from the repository root unless noted.

```bash
# Restore + build the whole solution
dotnet build Ignixa.Lab.sln -c Release

# Run the tests
dotnet test Ignixa.Lab.sln

# Run the Functions host locally
cd backend/src/Ignixa.Lab.Functions
cp local.settings.json.example local.settings.json   # first run only
func start                                            # http://localhost:7071
```

`local.settings.json` is git-ignored. The example sets
`IgnixaLab:AllowPrivateTargets=true` so you can test against a FHIR server on
localhost during development.

### Configuration (`IgnixaLab` section)

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `AllowPrivateTargets` | `false` | Permit runs against loopback/private/link-local targets. Keep `false` for hosted deployments. |
| `DefaultFhirVersion` | `4.0` | FHIR version used when a request omits one. |
| `SuitesDirectory` | *(bundled)* | Override the directory containing TestScript suites. Relative paths resolve against the app base directory. |
| `MaxSuitesPerRun` | `50` | Maximum suites (bundled + uploaded) per run. |
| `HttpTimeoutSeconds` | `100` | Per-run HTTP timeout for the FHIR client. |

### Code style

`Directory.Build.props` enables analyzers with **warnings-as-errors**. The test
project opts out of `CA1707` (underscores in names) and `CA1861` (constant array
arguments) because they conflict with idiomatic xUnit tests.

## Frontend

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173, proxies /api → :7071
npm run build      # tsc -b + vite build
npm run lint       # oxlint
npm run preview    # serve the production build
```

To target a remote backend from a standalone build, set `VITE_API_BASE_URL`
(see `frontend/.env.example`). Leave it empty to use same-origin `/api/*` paths.

## Adding a TestScript suite

1. Drop a FHIR TestScript JSON file under
   `backend/src/Ignixa.Lab.Functions/Suites/testscripts/<category>/`.
   The `<category>` folder name becomes the suite's category.
2. The file is bundled next to the worker automatically (see the `csproj`
   `None Include="Suites\testscripts\**\*.json"` item) and picked up by
   `SuiteCatalog` on the next run — no code change required.
3. It appears in `GET /api/suites` and becomes selectable in the SPA.

The bundled starter suites cover four categories: `capability`, `crud`,
`search`, and `validation`.
