---
description: Add a new bundled FHIR TestScript suite
argument-hint: <category> <suite-name>
---

Add a new bundled TestScript suite to the backend catalog for category
`$1` and suite name `$2`.

Steps:

1. Create a valid FHIR R4 TestScript JSON file under
   `backend/src/Ignixa.Lab.Functions/Suites/testscripts/$1/`. The folder name
   (`$1`) becomes the suite's category; no code change is needed for discovery
   (see `SuiteCatalog`).
2. Give the TestScript a clear `name` and `description` — these surface in
   `GET /api/suites` and the SPA suite picker.
3. Verify it loads by running the backend tests (`dotnet test Ignixa.Lab.sln`)
   and, if the host is running, `GET /api/suites`.

Follow the structure of the existing suites in the sibling category folders
(`capability`, `crud`, `search`, `validation`). Do not alter unrelated suites.
