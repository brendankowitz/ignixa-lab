---
description: Build and test the .NET Functions backend
---

Build and test the Ignixa Lab backend, then report the outcome.

Run these from the repository root:

1. `dotnet build Ignixa.Lab.sln -c Release` — the solution builds with
   warnings-as-errors, so any analyzer warning is a failure.
2. `dotnet test Ignixa.Lab.sln` — run the xUnit suite.

If either step fails, summarize the failing projects/tests and the root cause.
Do not modify unrelated code. Report a concise pass/fail summary at the end.
