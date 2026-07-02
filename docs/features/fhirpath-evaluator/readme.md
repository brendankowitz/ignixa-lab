# Feature: FHIRPath evaluator (fhirpath-lab absorption)

**Status**: Exploring
**Created**: 2026-07-02

## Problem Statement

Brian's `fhirpath-lab-dotnet` repo (`FhirPathLab-DotNetEngine`) is the .NET/Ignixa
execution engine behind [fhirpath-lab.com](https://fhirpath-lab.com)'s "dotnet"
FHIRPath evaluator option — a small, stateless Azure Functions app that parses
and evaluates FHIRPath expressions against a supplied FHIR resource and returns
a `Parameters` resource with the result, parsed AST, inferred type, validation
issues, and `trace()` output. It is deployed independently today (Function App
`ignixafhirpath`, its own GitHub Actions workflow, its own Azure resources).

Both this repo and `fhirpath-lab-dotnet` are part of the same "Ignixa" FHIR
tooling family and share the same hosting model (.NET isolated-worker Azure
Functions, ASP.NET Core HTTP integration). We want to evaluate whether
`fhirpath-lab-dotnet` should be absorbed into `ignixa-lab` — sharing a repo,
solution, and/or a deployed Function App — instead of remaining a fully
separate project, and if so, how.

## Constraints

- `fhirpath-lab-dotnet`'s endpoints are anonymous and are called cross-origin
  by the external fhirpath-lab.com UI (not part of either repo) — any merge
  must not break that public contract (routes, response shape, CORS).
- The response AST shape (`Serialization/JsonAstVisitor.cs`) is a de facto
  contract with the external fhirpath-lab UI and must be preserved byte-for-shape.
- `fhirpath-lab-dotnet` targets `net9.0`; `ignixa-lab`'s Functions app targets
  `net10.0` (`AzureFunctionsVersion v4` isolated worker in both).
- `fhirpath-lab-dotnet` depends on `Ignixa.FhirPath`/`Ignixa.Serialization`/
  `Ignixa.Specification` 0.5.6; `ignixa-lab` depends on `Ignixa.TestScript`
  0.5.11-beta — same publisher/ecosystem, different package family, versions
  need reconciling under Central Package Management.
- `ignixa-lab`'s `RunFunction`/`CapabilityFunction` endpoints already have
  SSRF protection (`TargetUrlValidator`) for user-supplied target URLs;
  `fhirpath-lab-dotnet`'s `LoadResourceFromUrl` resource-fetch-by-URL path has
  no equivalent guard today — a merge is an opportunity (arguably an
  obligation) to close that gap.
- No IaC exists for either app's Azure resources today (both deploy via
  `Azure/functions-action@v1` zip-deploy to a pre-existing Function App) — a
  merge decision about "same Function App vs. same repo, separate Function
  App" has no infra automation to lean on either way.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [same-function-app-merge](./investigations/same-function-app-merge.md) | In Progress | Port `fhirpath-lab-dotnet`'s Functions class into `Ignixa.Lab.Functions`, deployed as part of the single `ignixa-lab` Function App. |

Other approaches worth their own investigation before deciding:

- **Same repo/solution, separate Function App** — share CI tooling, package
  versions, and dev workflow without sharing runtime blast radius or
  cold-start pool between the internet-facing fhirpath-lab.com traffic and
  ignixa-lab's own conformance-tool traffic.
- **Keep fully separate** (status quo) — no repo merge; only worth doing if
  the two apps' package/dependency drift (`Ignixa.FhirPath` 0.5.6 vs.
  `Ignixa.TestScript` 0.5.11-beta) turns out to be cheaper to tolerate than
  the migration cost.
- **Shared library, not shared host** — extract the FHIRPath evaluation
  services into a NuGet package consumed by both a standalone
  `fhirpath-lab-dotnet`-successor and `ignixa-lab`, if a future consumer
  needs the evaluator outside of an HTTP Function context.

## Decision

*No ADR yet - investigations in progress*
