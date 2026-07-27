---
description: Add a new bundled FHIR TestScript suite
argument-hint: <category> <suite-name>
---

Add a new bundled TestScript suite to the backend catalog for category
`$1` and suite name `$2`.

The canonical suites are sourced from the upstream `Ignixa.TestScript.Suites`
NuGet package (published from the sibling `ignixa-fhir` repo — see
ADR-2607, `docs/features/testscript-suite-sourcing/adr-2607-suite-sourcing.md`),
not from a project in this repo. Ignixa Lab no longer packs its own suites.

Steps:

1. In the `ignixa-fhir` checkout, create a valid FHIR R4 TestScript JSON file
   under `src/Core/Ignixa.TestScript.Suites/testscripts/$1/`. The folder name
   (`$1`) becomes the suite's category; no lab-side code change is needed for
   discovery (`SuiteCatalog` reads whatever the package places under
   `testscripts/` at `AppContext.BaseDirectory`).
2. Give the TestScript a clear `name` and `description` — these surface in
   `GET /api/suites` and the SPA suite picker.
3. Land that change upstream and wait for (or request) a new
   `Ignixa.TestScript.Suites` release, then bump its `PackageVersion` in this
   repo's `Directory.Packages.props`.
4. `dotnet restore Ignixa.Lab.sln && dotnet test Ignixa.Lab.sln`, and if the
   host is running, `GET /api/suites`, to verify the new suite loads.

Follow the structure of the existing suites in the sibling category folders
(`Bundles`, `CRUD`, `Search`, `Validation`). Do not alter unrelated suites.
