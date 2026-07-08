# NuGet Publish Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish `IgnixaLab.TestScript.Suites` to nuget.org from a manually triggered workflow while keeping backend site deployment automatic on every `main` check-in.

**Architecture:** Keep the repo-local `0.1.0-local` package for development restore, but add a GitVersion-enabled pack path for CI release artifacts. Backend CI builds/tests the backend, packs a GitVersioned NuGet artifact, and uploads it; a separate manual `Publish` workflow promotes the latest successful backend artifact to nuget.org.

**Tech Stack:** GitHub Actions, .NET 10 SDK, NuGet, GitVersion.MsBuild 6.5.1, Azure Functions deployment workflow.

---

## File structure

- Create: `GitVersion.yml` — GitVersion configuration matching `ignixa-fhir` release tag behavior.
- Create: `.github/workflows/publish.yml` — manual nuget.org publication workflow.
- Modify: `Directory.Packages.props` — central `GitVersion.MsBuild` package version and updated suite package comments.
- Modify: `backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj` — NuGet metadata and conditional GitVersion package reference.
- Modify: `backend/pack-suites.ps1` — force local restore package version to remain `0.1.0-local`.
- Modify: `.github/workflows/backend.yml` — full-history checkout, include GitVersion changes in triggers, upload package artifact.
- Modify: `.github/workflows/backend-deploy.yml` — deploy on every `main` push and use full-history checkout for consistency.
- Modify: `backend/README.md` — document local packing, backend deploy, and manual NuGet publish flow.
- Modify: `docs/superpowers/specs/2026-07-07-nuget-publish-workflow-design.md` if implementation needs to clarify the local-versus-release package split.

## Task 1: Add GitVersion package versioning

**Files:**
- Create: `GitVersion.yml`
- Modify: `Directory.Packages.props`
- Modify: `backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj`
- Modify: `backend/pack-suites.ps1`

- [ ] **Step 1: Add root GitVersion configuration**

Create `GitVersion.yml`:

```yaml
workflow: TrunkBased/preview1
assembly-versioning-scheme: MajorMinorPatch
# Recognize release/X.Y.Z tags as version sources so the version auto-increments
# from the last release instead of repo height. Per-commit bumps via commit message:
#   +semver: major | minor | patch | none
# Do NOT add next-version alongside tag-prefix: GitVersion 6 throws "Failed to parse"
# when both are set.
tag-prefix: 'release/'
```

- [ ] **Step 2: Add the central GitVersion.MsBuild package version**

In `Directory.Packages.props`, add this package version near `Microsoft.SourceLink.GitHub`:

```xml
    <!-- GitVersion for release package semantic versioning -->
    <PackageVersion Include="GitVersion.MsBuild" Version="6.5.1" />
```

Update the `IgnixaLab.TestScript.Suites` comment to explain that `0.1.0-local` remains the local restore package version while CI produces GitVersioned artifacts for nuget.org.

- [ ] **Step 3: Update suite package metadata**

In `backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj`, keep `<Version>0.1.0-local</Version>` as the local fallback and add public metadata:

```xml
    <Authors>Ignixa Contributors</Authors>
    <Company>Ignixa Contributors</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/brendankowitz/ignixa-lab</PackageProjectUrl>
    <RepositoryUrl>https://github.com/brendankowitz/ignixa-lab</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>fhir;testscript;conformance;hl7;ignixa;healthcare;testing</PackageTags>
```

Replace the interim description with:

```xml
      Canonical FHIR TestScript suites for Ignixa Lab conformance testing,
      packaged as NuGet content for .NET backend consumers.
```

- [ ] **Step 4: Add conditional GitVersion package reference**

In `backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj`, add:

```xml
  <ItemGroup Condition="'$(UseGitVersion)' == 'true'">
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="All" />
  </ItemGroup>
```

This keeps local restore/build fast and stable while letting CI opt into GitVersion for release package artifacts.

- [ ] **Step 5: Keep local package packing fixed**

In `backend/pack-suites.ps1`, change the `dotnet pack` invocation to:

```powershell
dotnet pack $project -c Release -o $outputDir /nodeReuse:false `
    /p:UseGitVersion=false `
    /p:Version=0.1.0-local `
    /p:PackageVersion=0.1.0-local
```

- [ ] **Step 6: Verify local package still restores**

Run:

```powershell
./backend/pack-suites.ps1
dotnet restore Ignixa.Lab.sln
```

Expected: `artifacts/local-feed/IgnixaLab.TestScript.Suites.0.1.0-local.nupkg` exists and restore succeeds.

- [ ] **Step 7: Commit**

```bash
git add GitVersion.yml Directory.Packages.props backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj backend/pack-suites.ps1
git commit -m "Add GitVersioned suite package metadata"
```

## Task 2: Produce package artifacts in backend CI

**Files:**
- Modify: `.github/workflows/backend.yml`

- [ ] **Step 1: Use full-history checkout**

Change the checkout step to:

```yaml
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
```

- [ ] **Step 2: Add `GitVersion.yml` to backend workflow triggers**

Add `GitVersion.yml` under both `push.paths` and `pull_request.paths` so package versioning changes run backend CI.

- [ ] **Step 3: Pack the GitVersioned suites package after tests**

Add this step after `Test`:

```yaml
      - name: Pack suites package artifact
        shell: bash
        run: |
          set -euo pipefail
          output_dir="artifacts/nuget-packages"
          dotnet pack backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj \
            -c Release \
            -o "$output_dir" \
            /p:UseGitVersion=true \
            /nodeReuse:false

          package="$(find "$output_dir" -maxdepth 1 -name 'IgnixaLab.TestScript.Suites.*.nupkg' | sort | head -n 1)"
          if [ -z "$package" ]; then
            echo "::error::No IgnixaLab.TestScript.Suites package was produced"
            exit 1
          fi

          filename="$(basename "$package")"
          version="${filename#IgnixaLab.TestScript.Suites.}"
          version="${version%.nupkg}"
          echo "$version" > "$output_dir/version.txt"
          echo "${{ github.sha }}" > "$output_dir/commit-sha.txt"
          echo "Packed $filename from ${{ github.sha }}"
```

- [ ] **Step 4: Upload package artifacts**

Add this step after packing:

```yaml
      - name: Upload suites package artifact
        uses: actions/upload-artifact@v4
        with:
          name: nuget-packages-conformance
          path: |
            artifacts/nuget-packages/*.nupkg
            artifacts/nuget-packages/version.txt
            artifacts/nuget-packages/commit-sha.txt
          retention-days: 30
```

- [ ] **Step 5: Inspect workflow YAML**

Run:

```powershell
Get-Content .github/workflows/backend.yml
```

Expected: checkout uses `fetch-depth: 0`, package artifact steps run after tests, artifact name is `nuget-packages-conformance`.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/backend.yml
git commit -m "Upload conformance suite package from backend CI"
```

## Task 3: Add manual NuGet publish workflow

**Files:**
- Create: `.github/workflows/publish.yml`

- [ ] **Step 1: Create workflow**

Create `.github/workflows/publish.yml`:

```yaml
name: Publish

on:
  workflow_dispatch:

permissions:
  contents: read
  actions: read

jobs:
  publish-nuget:
    name: Publish conformance package to NuGet
    runs-on: ubuntu-latest

    steps:
      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Download latest backend package artifact
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          set -euo pipefail
          run_id="$(gh run list \
            --workflow backend.yml \
            --branch main \
            --status success \
            --limit 1 \
            --json databaseId \
            --jq '.[0].databaseId')"

          if [ -z "$run_id" ] || [ "$run_id" = "null" ]; then
            echo "::error::No successful Backend workflow run found on main"
            exit 1
          fi

          gh run download "$run_id" \
            --name nuget-packages-conformance \
            --dir ./nuget-packages

          package_count="$(find ./nuget-packages -maxdepth 1 -name '*.nupkg' | wc -l)"
          if [ "$package_count" -ne 1 ]; then
            echo "::error::Expected exactly one .nupkg artifact, found $package_count"
            find ./nuget-packages -maxdepth 1 -type f -print
            exit 1
          fi

          echo "Package version: $(cat ./nuget-packages/version.txt)"
          echo "Package commit: $(cat ./nuget-packages/commit-sha.txt)"

      - name: Push package to NuGet.org
        env:
          NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}
        run: |
          set -euo pipefail
          if [ -z "${NUGET_API_KEY}" ]; then
            echo "::error::NUGET_API_KEY secret is required"
            exit 1
          fi

          dotnet nuget push ./nuget-packages/*.nupkg \
            --api-key "$NUGET_API_KEY" \
            --source https://api.nuget.org/v3/index.json \
            --skip-duplicate
```

- [ ] **Step 2: Inspect workflow YAML**

Run:

```powershell
Get-Content .github/workflows/publish.yml
```

Expected: workflow is manual, permissions include `actions: read`, and the push step uses `NUGET_API_KEY` with `--skip-duplicate`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/publish.yml
git commit -m "Add manual NuGet publish workflow"
```

## Task 4: Deploy backend on every main check-in

**Files:**
- Modify: `.github/workflows/backend-deploy.yml`

- [ ] **Step 1: Remove push path filters**

Change:

```yaml
on:
  push:
    branches: [main]
    paths:
      - 'backend/**'
```

to:

```yaml
on:
  push:
    branches: [main]
```

Keep `workflow_dispatch`.

- [ ] **Step 2: Use full-history checkout**

Change the checkout step to:

```yaml
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
```

- [ ] **Step 3: Inspect workflow YAML**

Run:

```powershell
Get-Content .github/workflows/backend-deploy.yml
```

Expected: deploy runs on every `main` push and keeps manual dispatch.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/backend-deploy.yml
git commit -m "Deploy backend on every main push"
```

## Task 5: Update documentation and validate

**Files:**
- Modify: `backend/README.md`
- Modify: `docs/superpowers/specs/2026-07-07-nuget-publish-workflow-design.md` if needed

- [ ] **Step 1: Update backend README suites section**

Replace the local-only wording in `backend/README.md` with text explaining:

```markdown
The same project also produces the public NuGet package artifact in Backend CI.
Local development still uses `0.1.0-local` from `artifacts/local-feed`; release
artifacts opt into GitVersion and are uploaded as the `nuget-packages-conformance`
workflow artifact.
```

- [ ] **Step 2: Add publish documentation**

In `backend/README.md`, add a short publish section:

```markdown
## Publish suite package

The manual `.github/workflows/publish.yml` workflow promotes the latest successful
Backend workflow artifact from `main` to nuget.org. It requires the repository
secret `NUGET_API_KEY`; duplicate package versions are skipped by NuGet.

Versioning comes from `GitVersion.yml`, matching the `ignixa-fhir` `release/`
tag convention. The publish workflow does not rebuild the package, so the pushed
artifact is the package that passed backend CI.
```

- [ ] **Step 3: Validate backend build and tests**

Run:

```powershell
./backend/pack-suites.ps1
dotnet restore Ignixa.Lab.sln
dotnet build Ignixa.Lab.sln -c Release --no-restore
dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity normal
```

Expected: all commands succeed.

- [ ] **Step 4: Validate GitVersion package pack path**

Run:

```powershell
dotnet pack backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj -c Release -o artifacts/nuget-packages /p:UseGitVersion=true /nodeReuse:false
Get-ChildItem artifacts/nuget-packages -Filter 'IgnixaLab.TestScript.Suites.*.nupkg'
```

Expected: a non-`0.1.0-local` package is produced.

- [ ] **Step 5: Commit**

```bash
git add backend/README.md docs/superpowers/specs/2026-07-07-nuget-publish-workflow-design.md
git commit -m "Document conformance package publishing"
```

## Task 6: Final review and PR

**Files:**
- All changed files

- [ ] **Step 1: Inspect final diff**

Run:

```powershell
git --no-pager diff --stat main...HEAD
git --no-pager diff main...HEAD -- .github/workflows backend Directory.Packages.props GitVersion.yml docs/superpowers
```

Expected: only design, plan, package metadata, workflow, script, and documentation changes are present.

- [ ] **Step 2: Run final backend verification**

Run:

```powershell
./backend/pack-suites.ps1
dotnet restore Ignixa.Lab.sln
dotnet build Ignixa.Lab.sln -c Release --no-restore
dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity normal
```

Expected: all commands succeed.

- [ ] **Step 3: Create pull request**

Run:

```bash
git push --set-upstream origin brendankowitz-nuget-publish-workflow
```

Then create a PR titled `Add NuGet publishing for conformance suites` with a body covering:

```markdown
## Summary
- add GitVersion-based package versioning for IgnixaLab.TestScript.Suites release artifacts
- upload tested suite packages from Backend CI
- add manual Publish workflow for nuget.org
- deploy backend on every main push

## Testing
- ./backend/pack-suites.ps1
- dotnet restore Ignixa.Lab.sln
- dotnet build Ignixa.Lab.sln -c Release --no-restore
- dotnet test Ignixa.Lab.sln -c Release --no-build --verbosity normal
- dotnet pack backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj -c Release -o artifacts/nuget-packages /p:UseGitVersion=true /nodeReuse:false
```
