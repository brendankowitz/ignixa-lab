---
description: Add a new bundled FHIR TestScript suite
argument-hint: <category> <suite-name>
---

Add a new bundled TestScript suite to the backend catalog for category
`$1` and suite name `$2`.

Steps:

1. Create a valid FHIR R4 TestScript JSON file under
   `backend/src/Ignixa.Lab.Suites/testscripts/$1/`. The folder name (`$1`)
   becomes the suite's category; no code change is needed for discovery (see
   `SuiteCatalog`). This is the packaging project for the interim local
   `Ignixa.TestScript.Suites` content package — see ADR-2607
   (`docs/features/testscript-suite-sourcing/adr-2607-suite-sourcing.md`).
2. Give the TestScript a clear `name` and `description` — these surface in
   `GET /api/suites` and the SPA suite picker.
3. Repack the suites package and verify it loads: `./backend/pack-suites.ps1`,
   then `dotnet test Ignixa.Lab.sln` and, if the host is running,
   `GET /api/suites`.

Follow the structure of the existing suites in the sibling category folders
(`Bundles`, `CRUD`, `Search`, `Validation`). Do not alter unrelated suites.
