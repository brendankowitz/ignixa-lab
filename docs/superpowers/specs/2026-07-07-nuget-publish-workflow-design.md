# NuGet publish workflow design

## Context

`IgnixaLab.TestScript.Suites` is currently a repo-local content package produced
by `backend/pack-suites.ps1` at version `0.1.0-local`. The backend and test
projects restore it from `artifacts/local-feed` through `nuget.config`.

The goal is to publish this conformance suite package to nuget.org while keeping
backend deployment independent: every push to `main` may deploy the latest
backend site, but NuGet publication remains an explicit release action.

## Chosen approach

Use the same GitVersion-based versioning model as `brendankowitz/ignixa-fhir`:

- Add `GitVersion.yml` with trunk-based preview versioning and `release/` tag
  prefixes.
- Add `GitVersion.MsBuild` centrally so .NET pack operations derive package
  versions from Git history.
- Convert `IgnixaLab.TestScript.Suites` from the fixed `0.1.0-local` version to
  GitVersion-driven packaging metadata suitable for nuget.org.
- Update backend CI to checkout full history, build/test as today, pack the
  suites package, and upload the `.nupkg` plus `version.txt` and
  `commit-sha.txt` artifacts for later publication.
- Add a manual `Publish` workflow that downloads the latest successful backend
  package artifact from `main` and pushes it to nuget.org with `NUGET_API_KEY`.
- Make backend deployment run on every push to `main`, preserving manual
  dispatch, so the hosted backend tracks latest main independently from NuGet
  releases.

## Alternatives considered

1. Build and push directly in a one-shot manual workflow. This is simpler, but
   it can publish artifacts that did not pass the normal backend CI path.
2. Publish only from `release/*` tags. This is clean for releases, but it adds a
   tag-first release ceremony before the package has been produced and reviewed.
3. Reuse latest successful backend CI artifacts. This keeps the published
   package tied to a tested `main` build and matches the release shape used by
   `ignixa-fhir`.

## Workflow behavior

Backend CI remains the validation source for backend changes. It packs the suite
package after restore/build/test and uploads package artifacts only when the job
succeeds. The publish workflow does not rebuild the package; it promotes the
known-good package from the latest successful backend workflow run on `main`.

The publish workflow is manually triggered, requires `NUGET_API_KEY`, and uses
`dotnet nuget push --skip-duplicate` against `https://api.nuget.org/v3/index.json`.

Backend deployment stays separate from package publication. It builds and
publishes the Azure Functions app on every `main` check-in, plus manual dispatch.

## Package metadata

The NuGet package should include stable public metadata:

- package id: `IgnixaLab.TestScript.Suites`
- repository URL: `https://github.com/brendankowitz/ignixa-lab`
- project URL: `https://github.com/brendankowitz/ignixa-lab`
- license expression: `MIT`
- authors/company: Ignixa contributors
- tags describing FHIR, TestScript, conformance, and suites
- Source Link repository metadata

The package remains content-only and continues shipping:

- `testscripts/**/*.json`
- `testscripts/source-revision.txt`
- `build/IgnixaLab.TestScript.Suites.targets`

## Error handling and safety

Publishing fails loudly if the backend artifact cannot be found, if the package
artifact is missing, or if `NUGET_API_KEY` is unavailable or invalid. Duplicate
pushes are skipped by NuGet rather than treated as a new publication.

The local feed remains for developer and CI restore before the package is
published. A future cleanup can remove the local feed once all workflows and
developers restore the package from nuget.org.

## Testing

Validation should cover:

- `backend/pack-suites.ps1`
- `dotnet restore Ignixa.Lab.sln`
- `dotnet build Ignixa.Lab.sln -c Release --no-restore`
- `dotnet test Ignixa.Lab.sln -c Release --no-build`

Workflow YAML should be inspected for valid triggers, permissions, artifact
paths, and secret usage.
