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
   `backend/src/Ignixa.Lab.Suites/testscripts/<category>/`.
   The `<category>` folder name becomes the suite's category.
2. Update `backend/src/Ignixa.Lab.Suites/tools/provenance-manifest.json` for
   every new or materially changed TestScript. That manifest is the authoritative
   source of truth.
3. Run

   ```powershell
   pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 -Force
   ```

   to regenerate the sibling FHIR R4 Provenance sidecar named
   `<suite>.provenance.json`. The sidecar targets the TestScript's path relative
   to `testscripts/` and lists the repositories, specifications, APIs, or prior
   tests used while authoring or distilling the suite.
4. Commit the generated `.provenance.json` file alongside the TestScript. These
   sidecars are committed artifacts, not hand-authored inputs.
   If you delete or rename a TestScript, delete its old sidecar and update or
   remove the matching manifest entry before regenerating provenance.
5. Use `author-testscript` for locally created coverage and
   `distill-testscript` when external test behavior is transformed.
6. Capture the most precise stable upstream commit, tag, or release; the best
   stable file/class URL available; and an SPDX license identifier when known.
   Never replace an unknown historical revision with the upstream repository's
   current HEAD.
7. Run:

   ```powershell
   pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\new-provenance-sidecars.ps1 -Force
   pwsh -NoLogo -NoProfile -NonInteractive -File backend\src\Ignixa.Lab.Suites\tools\verify-provenance.ps1 -Strict
   .\backend\pack-suites.ps1
   ```

   `verify-provenance.ps1 -Strict` only exits non-zero for blocking structural,
   classification, stale-sidecar, or orphan-sidecar errors. Source precision
   and license advisories remain warnings.
8. The TestScript appears in `GET /api/suites` and becomes selectable in the SPA;
   provenance sidecars stay excluded from executable suite discovery and runtime
   APIs.
