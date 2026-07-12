# ignixa-fhir 0.6.19 Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `ignixa-lab` onto `ignixa-fhir` 0.6.19, extend the dashboard to surface the new grouped-assertion diagnostics, migrate all five suites off the hand-rolled status-alternative workarounds onto the real `assertionAnyOfGroup`/`assertionWhenResponseStatus` extensions, delete the workarounds, and adopt `waitFor` where it closes a real gap.

**Architecture:** Eight sequential tasks across four phases (package/build → dashboard plumbing → suite-by-suite migration → cleanup → waitFor). Each suite migration is verified against two real, shared FHIR servers — not mocks — before moving to the next.

**Tech Stack:** .NET (Azure Functions isolated worker), xUnit + FluentAssertions (backend tests use `.Should()`, NOT Shouldly — different convention than the ignixa-fhir engine repo), React/TypeScript frontend, `Ignixa.Lab.sln`.

**Design doc:** `docs/superpowers/specs/2026-07-12-ignixa-fhir-0.6.19-migration-design.md` — read it first for full rationale.

## Global Constraints

- Backend build/test commands: `dotnet build Ignixa.Lab.sln -c Release` and `dotnet test Ignixa.Lab.sln` — run from the repo root.
- Backend test project: `backend/test/Ignixa.Lab.Functions.Tests`, convention is FluentAssertions (`result.Should().Be(...)`), xUnit `[Fact]`.
- Live verification target for every suite-affecting task: start the Functions host (`func start`, from `backend/src/Ignixa.Lab.Functions` — see `docs/development.md`), then `POST http://localhost:7071/api/run` with body `{"targetUrl": "<target>", "suiteIds": ["<id>"]}` against **both** `https://subscriptions.argo.run/` and `https://bkowitz-testdeploy.azurewebsites.net`. Look up exact suite IDs via `GET http://localhost:7071/api/suites` first — don't assume the ID matches the file path.
- **CRITICAL, discovered during Task 5:** `SuiteCatalog` reads suite JSON from `AppContext.BaseDirectory/testscripts`, populated from the `IgnixaLab.TestScript.Suites` NuGet package — NOT live from `backend/src/Ignixa.Lab.Suites/testscripts/*.json` on disk. That package is pinned to a fixed version (`0.1.0-local`), so NuGet's cache does NOT invalidate on re-pack — editing suite JSON and restarting `func start` alone will silently keep serving OLD content (confirmed: `GET /api/health`'s `testScriptsRevision` will show a stale commit SHA). **After editing ANY suite JSON, before live verification:** run `./backend/pack-suites.ps1` (repo root), then `dotnet restore Ignixa.Lab.sln --force`, then rebuild, then restart `func start`. Confirm `testScriptsRevision` in `/api/health` matches your just-created commit SHA before trusting any live result.
- No suite's pass/fail may regress without being explicitly triaged and explained in the task report.
- The `Subscriptions/basic.json` suite creates/updates/deletes a real `Subscription` resource on these shared servers — treat live runs against it with care (it's already idempotent per-run via a `${runId}` variable, don't change that).
- Every `assertionAnyOfGroup` migration must: use a distinct group id per logical group (never span two logical alternatives with one id), and set explicit `sourceId` on every group member (never rely on implicit `LastResponse`).
- Do not touch `TestScriptContentNormalizer` or `RunScopedDefinitionPreparer` — unrelated workarounds, out of scope.
- Do not attempt to move the 4 `MutableNode` call sites onto typed models, and do not attempt blob-content verification for `$export`/`$import` — both explicitly out of scope per the design doc.

---

### Task 1: Package bump + `MutableNode` fix + build/unit-test green

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `backend/src/Ignixa.Lab.Functions/Execution/ConformanceReportMapper.cs:162`
- Modify: `backend/src/Ignixa.Lab.Functions/Execution/RunScopedDefinitionPreparer.cs:41`
- Modify: `backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs:138`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/FhirPath/ResultFormatter.cs:221,279,308,381,405,408`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: a green build against `Ignixa.TestScript`/`Ignixa.TestScript.FhirFakes` `0.6.19-beta` and the other 6 `Ignixa.*` packages at `0.6.19` — every later task depends on this.

- [ ] **Step 1: Bump all 8 `Ignixa.*` package versions**

In `Directory.Packages.props`, change:

```xml
    <PackageVersion Include="Ignixa.TestScript" Version="0.6.7-beta" />
```
to
```xml
    <PackageVersion Include="Ignixa.TestScript" Version="0.6.19-beta" />
```

Same substitution (`0.6.7-beta` → `0.6.19-beta`) for `Ignixa.TestScript.FhirFakes`. And (`0.6.7` → `0.6.19`) for `Ignixa.FhirPath`, `Ignixa.Serialization`, `Ignixa.Specification`, `Ignixa.Validation`, `Ignixa.PackageManagement`, `Ignixa.FhirFakes`.

- [ ] **Step 2: Attempt restore/build, confirm the expected `MutableNode` compile errors**

Run: `dotnet build Ignixa.Lab.sln -c Release`
Expected: FAIL — `CS0122` ("MutableNode is inaccessible due to its protection level") at each of the 9 call sites listed above.

- [ ] **Step 3: Fix `ConformanceReportMapper.cs:162`**

Add `using Ignixa.Serialization.SourceNodes;` to the file's using block if not already present. Change:

```csharp
            Body: request.FormBody ?? request.Body?.MutableNode.ToJsonString());
```
to
```csharp
            Body: request.FormBody ?? ((IMutableJsonNode?)request.Body)?.MutableNode.ToJsonString());
```

- [ ] **Step 4: Fix `RunScopedDefinitionPreparer.cs:41`**

Add `using Ignixa.Serialization.SourceNodes;` if not already present. Change:

```csharp
        var json = JsonNode.Parse(resource.MutableNode.ToJsonString())!;
```
to
```csharp
        var json = JsonNode.Parse(((IMutableJsonNode)resource).MutableNode.ToJsonString())!;
```

- [ ] **Step 5: Fix `TestScriptRunner.cs:138`**

Add `using Ignixa.Serialization.SourceNodes;` if not already present. Change:

```csharp
        var declaredVersion = capabilityStatement?.MutableNode["fhirVersion"] is JsonValue value && value.TryGetValue<string>(out var declared)
```
to
```csharp
        var declaredVersion = ((IMutableJsonNode?)capabilityStatement)?.MutableNode["fhirVersion"] is JsonValue value && value.TryGetValue<string>(out var declared)
```

- [ ] **Step 6: Fix the 6 call sites in `ResultFormatter.cs`**

Add `using Ignixa.Serialization.SourceNodes;` if not already present. Each of the 6 lines wraps its receiver the same way:

Line 221:
```csharp
        outcomePart.MutableNode["resource"] = JsonNode.Parse(outcome.SerializeToString());
```
becomes
```csharp
        ((IMutableJsonNode)outcomePart).MutableNode["resource"] = JsonNode.Parse(outcome.SerializeToString());
```

Line 279:
```csharp
                resultPart.MutableNode[$"value{outputValue.InstanceType}"] = JsonNode.Parse(json);
```
becomes
```csharp
                ((IMutableJsonNode)resultPart).MutableNode[$"value{outputValue.InstanceType}"] = JsonNode.Parse(json);
```

Line 308:
```csharp
                    elementPart.MutableNode[$"value{element.InstanceType}"] = JsonNode.Parse(json);
```
becomes
```csharp
                    ((IMutableJsonNode)elementPart).MutableNode[$"value{element.InstanceType}"] = JsonNode.Parse(json);
```

Line 381:
```csharp
        part.MutableNode["resource"] = JsonNode.Parse(resource.SerializeToString());
```
becomes
```csharp
        ((IMutableJsonNode)part).MutableNode["resource"] = JsonNode.Parse(resource.SerializeToString());
```

Lines 405 and 408 (both operate on the same `param` receiver, in the same `if`/body):
```csharp
        if (param.MutableNode["extension"] is not JsonArray extensionArray)
```
becomes
```csharp
        if (((IMutableJsonNode)param).MutableNode["extension"] is not JsonArray extensionArray)
```
and
```csharp
            param.MutableNode["extension"] = extensionArray;
```
becomes
```csharp
            ((IMutableJsonNode)param).MutableNode["extension"] = extensionArray;
```

- [ ] **Step 7: Build again**

Run: `dotnet build Ignixa.Lab.sln -c Release`
Expected: SUCCESS — 0 errors. If any other error appears (e.g. from the typed-models/slicing-enum changes elsewhere in 0.6.19), read the error, fix it, and note it in your report — don't assume only these 9 sites are affected.

- [ ] **Step 8: Run the full backend unit test suite**

Run: `dotnet test Ignixa.Lab.sln`
Expected: all tests pass, no new failures relative to a pre-bump baseline run (run the same command once before Step 1 if you haven't already, to have a baseline to diff against).

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props \
        backend/src/Ignixa.Lab.Functions/Execution/ConformanceReportMapper.cs \
        backend/src/Ignixa.Lab.Functions/Execution/RunScopedDefinitionPreparer.cs \
        backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs \
        backend/src/Ignixa.Lab.Functions/Services/FhirPath/ResultFormatter.cs
git commit -m "chore: bump ignixa-fhir to 0.6.19, fix MutableNode SDK-surface break"
```

---

### Task 2: Live-suite baseline verification against both targets

**Files:** none modified — this task is pure verification.

**Interfaces:**
- Consumes: Task 1's green build.
- Produces: a documented baseline of every bundled suite's pass/fail on both live targets, pre- and post-bump, used by every later suite-touching task to detect regressions.

- [ ] **Step 1: Capture a pre-bump baseline** (skip this step if you already have one from before Task 1 started — note that in your report instead)

Before Task 1's package bump, run the full suite catalog against both targets and save the raw JSON responses:
```bash
curl -s -X POST http://localhost:7071/api/run -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://subscriptions.argo.run/"}' > /tmp/baseline-argo.json
curl -s -X POST http://localhost:7071/api/run -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://bkowitz-testdeploy.azurewebsites.net"}' > /tmp/baseline-testdeploy.json
```
(Omitting `suiteIds` runs every bundled suite — confirm this against `TestScriptRunner`'s `ResolveJobs` if `request.SuiteIds` is null/empty; if it requires an explicit full list instead, get the list from `GET /api/suites` first.)

- [ ] **Step 2: Run the same two calls post-bump (after Task 1 is committed)**

```bash
curl -s -X POST http://localhost:7071/api/run -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://subscriptions.argo.run/"}' > /tmp/post-bump-argo.json
curl -s -X POST http://localhost:7071/api/run -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://bkowitz-testdeploy.azurewebsites.net"}' > /tmp/post-bump-testdeploy.json
```

- [ ] **Step 3: Diff `results[].id` + `results[].status` between baseline and post-bump for both targets**

```bash
jq '[.results[] | {id, status}]' /tmp/baseline-argo.json > /tmp/baseline-argo-status.json
jq '[.results[] | {id, status}]' /tmp/post-bump-argo.json > /tmp/post-bump-argo-status.json
diff /tmp/baseline-argo-status.json /tmp/post-bump-argo-status.json
```
(repeat for testdeploy)

- [ ] **Step 4: Triage any diff**

For each result whose `status` changed: read the corresponding `steps[].message` in the post-bump JSON. Per the design doc, the two most plausible causes are the CodeSystem/ValueSet check now running at `Compatibility` depth (not just `Full`) and the R5 `CodeableReference` shape fixes. If a diff traces to one of these, note it in your report as expected/explained — do not silently "fix" the suite to force the old result back. If a diff has no clear explanation, report it as a concern (status DONE_WITH_CONCERNS) rather than guessing.

- [ ] **Step 5: Write your report**

No commit for this task (nothing changed in the repo) — write findings to your report file per the dispatch instructions.

---

### Task 3: `ConformanceReportMapper` extension — surface `GroupId`/`Members`

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Conformance/ConformanceStep.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Conformance/ConformanceGroupMember.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Execution/ConformanceReportMapper.cs`
- Modify: `docs/conformance-report-schema.md`
- Modify: `frontend/src/types/conformance.ts`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Execution/ConformanceReportMapperTests.cs`

**Interfaces:**
- Consumes: `ActionResult.GroupId` (`string?`) and `ActionResult.Members` (`IReadOnlyList<AssertionGroupMemberResult>?`) from `Ignixa.TestScript.Reporting` (already present as of the 0.6.19 package from Task 1).
- Produces: `ConformanceStep.GroupId`/`Members`, consumed by nothing else in this plan (dashboard UI rendering of the new fields is an explicit follow-up, not part of this task).

- [ ] **Step 1: Write the failing test**

Add to `ConformanceReportMapperTests.cs` (the file already has an `Action(...)` helper building plain `ActionResult`s — this test constructs one directly since it needs `GroupId`/`Members`, which the helper doesn't support):

```csharp
    [Fact]
    public void Map_SurfacesGroupIdAndMembersOnGroupedActions()
    {
        var groupedAction = new ActionResult(
            Label: "grp",
            Description: "Deleted resource readback",
            Outcome: TestScriptOutcome.Pass,
            Message: "assertionAnyOfGroup 'grp': matched alternative 'Alternative: 404 Not Found'",
            GroupId: "grp",
            Members:
            [
                new AssertionGroupMemberResult("Preferred: 410 Gone", true, false, "Expected response 'gone' but got status 404"),
                new AssertionGroupMemberResult("Alternative: 404 Not Found", true, true, null),
            ]);

        var report = Report("GroupedTest", tests:
        [
            new TestCaseResult("case", null, [groupedAction], TestScriptOutcome.Pass),
        ]);

        var results = ConformanceReportMapper.Map(report, "suite-id", "category", "file.json");

        var step = results[0].Steps.Single(s => s.Label == "grp");
        step.GroupId.Should().Be("grp");
        step.Members.Should().NotBeNull();
        step.Members!.Should().HaveCount(2);
        step.Members![1].Passed.Should().BeTrue();
    }
```

- [ ] **Step 2: Run test to verify it fails to compile**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~ConformanceReportMapperTests"`
Expected: build errors — `ConformanceStep` has no `GroupId`/`Members` constructor parameters, `AssertionGroupMemberResult` may need a `using Ignixa.TestScript.Reporting;` already present in the test file (confirm).

- [ ] **Step 3: Create `ConformanceGroupMember.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// One member's outcome within a grouped (<c>assertionAnyOfGroup</c>) assertion step —
/// mirrors <see cref="Ignixa.TestScript.Reporting.AssertionGroupMemberResult"/> for the
/// dashboard's JSON contract.
/// </summary>
public sealed record ConformanceGroupMember(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("message")] string? Message);
```

- [ ] **Step 4: Update `ConformanceStep.cs`**

The record's last two lines currently read:

```csharp
    [property: JsonPropertyName("request")] ConformanceHttpRequest? Request,
    [property: JsonPropertyName("response")] ConformanceHttpResponse? Response);
```

Change them to:

```csharp
    [property: JsonPropertyName("request")] ConformanceHttpRequest? Request,
    [property: JsonPropertyName("response")] ConformanceHttpResponse? Response,
    [property: JsonPropertyName("group_id")] string? GroupId = null,
    [property: JsonPropertyName("members")] IReadOnlyList<ConformanceGroupMember>? Members = null);
```

Both new parameters default to `null`, so every existing `new ConformanceStep(...)` call site (in `ConformanceReportMapper.cs` and `TestScriptRunner.cs`'s synthetic error step) keeps compiling unchanged — only Step 5 below, which explicitly wants to populate them, needs to pass them.

- [ ] **Step 5: Wire `ConformanceReportMapper.MapActions`**

In `MapActions` (currently building each `steps[i] = new ConformanceStep(...)`), add:

```csharp
                GroupId: action.GroupId,
                Members: action.Members?.Select(m => new ConformanceGroupMember(m.Description, m.Applicable, m.Passed, m.Message)).ToList());
```

replacing the existing trailing `Response: ToResponse(action.Exchange?.Response));` line's closing `);` appropriately (append the two new named args before the final `)`).

- [ ] **Step 6: Run the test**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~ConformanceReportMapperTests"`
Expected: PASS, all existing tests in this file still pass too.

- [ ] **Step 7: Update `docs/conformance-report-schema.md`**

In the `## ConformanceStep` table (currently ending at the `response` row), add two rows:

```markdown
| `group_id` | string \| null | Set when this step is an aggregate result for an `assertionAnyOfGroup` — the group identifier from the TestScript. |
| `members` | `ConformanceGroupMember[] \| null` | Present alongside `group_id`: each alternative's own applicability/pass/fail, for diagnostic display. |
```

And add a new section after `## ConformanceError`:

```markdown
## `ConformanceGroupMember`

| Field | Type | Description |
| ----- | ---- | ----------- |
| `description` | string \| null | The alternative's own description text. |
| `applicable` | boolean | Whether this alternative's condition matched (always `true` for unconditional members). |
| `passed` | boolean | Whether this alternative's own criterion passed. |
| `message` | string \| null | Failure detail for this specific alternative, when applicable and not passed. |
```

- [ ] **Step 8: Update `frontend/src/types/conformance.ts`**

Add after the `ConformanceStep` interface:

```typescript
/** One alternative's outcome within a grouped (assertionAnyOfGroup) step. */
export interface ConformanceGroupMember {
  description: string | null;
  applicable: boolean;
  passed: boolean;
  message: string | null;
}
```

And add two fields to the existing `ConformanceStep` interface:

```typescript
  group_id: string | null;
  members: ConformanceGroupMember[] | null;
```

- [ ] **Step 9: Run the full backend suite once more**

Run: `dotnet test Ignixa.Lab.sln`
Expected: no regressions from this task's changes (the frontend has no test runner invoked here — this task doesn't touch frontend build; if a `frontend-check` skill/script exists in this repo, run it too and confirm the TypeScript still compiles).

- [ ] **Step 10: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Conformance/ConformanceStep.cs \
        backend/src/Ignixa.Lab.Functions/Conformance/ConformanceGroupMember.cs \
        backend/src/Ignixa.Lab.Functions/Execution/ConformanceReportMapper.cs \
        docs/conformance-report-schema.md \
        frontend/src/types/conformance.ts \
        backend/test/Ignixa.Lab.Functions.Tests/Execution/ConformanceReportMapperTests.cs
git commit -m "feat: surface assertionAnyOfGroup diagnostics (GroupId/Members) through the dashboard schema"
```

---

### Task 4: Migrate `Subscriptions/basic.json`

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Subscriptions/basic.json`

**Interfaces:**
- Consumes: Task 3's dashboard plumbing (so migrating this suite doesn't regress dashboard detail).
- Produces: nothing consumed by later tasks — suites migrate independently.

This suite's "delete accepts valid outcomes and checks immediate readback" test uses `subscription-delete-readback-v1` — the most complex of the three policies, since it spans two logical groups (the DELETE's own status, and the readback conditioned on that status).

- [ ] **Step 1: Replace the test's `extension` array and its `action` array**

In the test named `"delete accepts valid outcomes and checks immediate readback"`, replace:

```json
      "extension": [
        { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3" },
        { "url": "http://ignixa.io/testscript/statusAlternativePolicy", "valueCode": "subscription-delete-readback-v1" }
      ],
      "action": [
        { "operation": { "type": { "code": "delete" }, "url": "Subscription/ignixa-sub-basic-${runId}", "responseId": "delete-response", "description": "DELETE the run-scoped Subscription" } },
        { "assert": { "description": "Accepted DELETE response: 200 OK for completed deletion", "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Accepted DELETE response: 202 Accepted for asynchronous deletion", "responseCode": "202", "warningOnly": true } },
        { "assert": { "description": "Accepted DELETE response: 204 No Content for completed deletion", "responseCode": "204", "warningOnly": true } },
        { "operation": { "type": { "code": "read" }, "url": "Subscription/ignixa-sub-basic-${runId}", "responseId": "after-delete-read", "description": "Immediately read the run-scoped Subscription after the accepted delete" } },
        { "assert": { "description": "Accepted alternative: 200 OK while an asynchronous delete is still pending", "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Accepted alternative: 410 Gone when the server tracks the deleted resource", "response": "gone", "warningOnly": true } },
        { "assert": { "description": "Accepted alternative: 404 Not Found when deleted resources are not tracked", "response": "notFound", "warningOnly": true } }
      ]
```

with:

```json
      "extension": [
        { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3" }
      ],
      "action": [
        { "operation": { "type": { "code": "delete" }, "url": "Subscription/ignixa-sub-basic-${runId}", "responseId": "delete-response", "description": "DELETE the run-scoped Subscription" } },
        { "assert": { "sourceId": "delete-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "delete-status" }], "description": "Accepted DELETE response: 200 OK for completed deletion", "responseCode": "200", "warningOnly": true } },
        { "assert": { "sourceId": "delete-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "delete-status" }], "description": "Accepted DELETE response: 202 Accepted for asynchronous deletion", "responseCode": "202", "warningOnly": true } },
        { "assert": { "sourceId": "delete-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "delete-status" }], "description": "Accepted DELETE response: 204 No Content for completed deletion", "responseCode": "204", "warningOnly": true } },
        { "operation": { "type": { "code": "read" }, "url": "Subscription/ignixa-sub-basic-${runId}", "responseId": "after-delete-read", "description": "Immediately read the run-scoped Subscription after the accepted delete" } },
        { "assert": {
            "sourceId": "after-delete-read",
            "extension": [
              { "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "readback" },
              { "url": "http://ignixa.io/testscript/assertionWhenResponseStatus", "extension": [
                { "url": "sourceId", "valueString": "delete-response" },
                { "url": "status", "valueInteger": 202 }
              ] }
            ],
            "description": "Accepted alternative: 200 OK while an asynchronous delete is still pending",
            "responseCode": "200", "warningOnly": true } },
        { "assert": { "sourceId": "after-delete-read", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "readback" }], "description": "Accepted alternative: 410 Gone when the server tracks the deleted resource", "response": "gone", "warningOnly": true } },
        { "assert": { "sourceId": "after-delete-read", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "readback" }], "description": "Accepted alternative: 404 Not Found when deleted resources are not tracked", "response": "notFound", "warningOnly": true } }
      ]
```

- [ ] **Step 2: Run the backend unit tests** (nothing in the C# tests directly exercises suite JSON content, but confirm no regression)

Run: `dotnet test Ignixa.Lab.sln`
Expected: unchanged pass count from Task 3.

- [ ] **Step 3: Verify live against both targets**

Look up the suite ID via `GET http://localhost:7071/api/suites`, then:
```bash
curl -s -X POST http://localhost:7071/api/run -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://subscriptions.argo.run/", "suiteIds": ["<subscriptions-basic-id>"]}' | jq '.results[] | {id, status, error}'
curl -s -X POST http://localhost:7071/api/run -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://bkowitz-testdeploy.azurewebsites.net", "suiteIds": ["<subscriptions-basic-id>"]}' | jq '.results[] | {id, status, error}'
```
Expected: the "delete accepts valid outcomes and checks immediate readback" result's `status` matches Task 2's baseline for this suite on both targets (or is explicably better — e.g. if the old enforcer had a bug the new group logic doesn't). If status changed for the worse, inspect `steps[]` where `group_id` is `"delete-status"` or `"readback"` and the member `message`s to understand why before proceeding.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Ignixa.Lab.Suites/testscripts/Subscriptions/basic.json
git commit -m "test: migrate Subscriptions/basic delete-readback onto assertionAnyOfGroup/assertionWhenResponseStatus"
```

---

### Task 5: Migrate `CRUD/delete.json` and `CRUD/read.json` (classic deleted-resource-readback pattern)

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/delete.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/read.json`

Both use the simple 2-member `deleted-resource-readback-v1` shorthand — no conditional needed, just a plain OR group.

- [ ] **Step 1: Migrate `CRUD/delete.json`**

In the test `"delete removes the resource; a plain read is Gone/NotFound while the original version remains vread-able"`, replace:

```json
      "extension": [{ "url": "http://ignixa.io/testscript/statusAlternativePolicy", "valueCode": "deleted-resource-readback-v1" }],
```
with: (remove the line entirely — this test has no other extension entries)

Then replace:
```json
        { "assert": { "description": "Preferred: 410 Gone for a deleted resource (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
        { "assert": { "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } },
```
with:
```json
        { "assert": { "sourceId": "after-delete-read-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "deleted-resource-readback" }], "description": "Preferred: 410 Gone for a deleted resource (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
        { "assert": { "sourceId": "after-delete-read-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "deleted-resource-readback" }], "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } },
```

(Since removing the test-level `"extension"` array entirely changes the JSON structure around it — read the file first and remove exactly that one key/value from the test object, preserving `"requiresCapability"` and everything else.)

- [ ] **Step 2: Migrate `CRUD/read.json`**

In the test `"read after delete returns 410 or 404"`, replace:
```json
      "extension": [{ "url": "http://ignixa.io/testscript/statusAlternativePolicy", "valueCode": "deleted-resource-readback-v1" }],
```
(remove the line entirely), then replace:
```json
        { "assert": { "description": "Preferred: 410 Gone for a deleted resource (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
        { "assert": { "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } }
```
with:
```json
        { "assert": { "sourceId": "after-delete-read", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "deleted-resource-readback" }], "description": "Preferred: 410 Gone for a deleted resource (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
        { "assert": { "sourceId": "after-delete-read", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "deleted-resource-readback" }], "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } }
```

- [ ] **Step 3: Run backend unit tests**

Run: `dotnet test Ignixa.Lab.sln` — expect unchanged pass count.

- [ ] **Step 4: Verify live against both targets**

Same pattern as Task 4 Step 3, for both `CRUD/delete` and `CRUD/read` suite IDs. Compare against Task 2's baseline.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Suites/testscripts/CRUD/delete.json backend/src/Ignixa.Lab.Suites/testscripts/CRUD/read.json
git commit -m "test: migrate CRUD/delete and CRUD/read deleted-resource-readback onto assertionAnyOfGroup"
```

---

### Task 6: Migrate `CRUD/create.json` and `CRUD/conditional-delete.json` (response-status-set pattern)

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/create.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/conditional-delete.json`

Five instances total of the `response-status-set-v1` structured policy across these two files — each is a plain 2-member OR group with no conditional, just needs an explicit `sourceId` and a group id added to each of its two asserts, and its `statusAlternativePolicy` extension entry removed.

- [ ] **Step 1: Migrate `CRUD/create.json`'s three instances**

Test `"create with an invalid (incomplete) resource body returns 400"`: replace
```json
      "extension": [{
        "url": "http://ignixa.io/testscript/statusAlternativePolicy",
        "extension": [
          { "url": "policy", "valueCode": "response-status-set-v1" },
          { "url": "method", "valueCode": "POST" },
          { "url": "status", "valueInteger": 400 },
          { "url": "status", "valueInteger": 422 }
        ]
      }],
      "action": [
        { "operation": { "type": { "code": "create" }, "resource": "Observation", "sourceId": "observation-empty-invalid", "responseId": "create-invalid-response", "description": "POST an empty Observation" } },
        { "assert": { "description": "Accepted validation status: 400 Bad Request", "responseCode": "400", "warningOnly": true } },
        { "assert": { "description": "Accepted validation status: 422 Unprocessable Entity", "responseCode": "422", "warningOnly": true } }
      ]
```
with
```json
      "action": [
        { "operation": { "type": { "code": "create" }, "resource": "Observation", "sourceId": "observation-empty-invalid", "responseId": "create-invalid-response", "description": "POST an empty Observation" } },
        { "assert": { "sourceId": "create-invalid-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "create-invalid-status" }], "description": "Accepted validation status: 400 Bad Request", "responseCode": "400", "warningOnly": true } },
        { "assert": { "sourceId": "create-invalid-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "create-invalid-status" }], "description": "Accepted validation status: 422 Unprocessable Entity", "responseCode": "422", "warningOnly": true } }
      ]
```

Test `"create with a malformed dateTime returns 400"`: same shape — remove the identical `statusAlternativePolicy` extension block (method POST), add `"sourceId": "create-baddate-response"` and group id `"create-baddate-status"` to its two asserts.

Test `"update with an illegal id format returns 400"`: same shape — remove the `statusAlternativePolicy` extension block (method PUT), add `"sourceId": "create-illegalid-response"` and group id `"update-illegalid-status"` to its two asserts.

(Read the file to confirm each test's exact `responseId` before editing — they're named `create-invalid-response`, `create-baddate-response`, `create-illegalid-response` per the current file; use each test's own `responseId` as the `sourceId` for its two asserts, and a distinct group id per test.)

- [ ] **Step 2: Migrate `CRUD/conditional-delete.json`'s two instances**

Test `"conditional DELETE with no search criteria..."` (400/412 pair): replace
```json
      "extension": [
        {
          "url": "http://ignixa.io/testscript/statusAlternativePolicy",
          "extension": [
            { "url": "policy", "valueCode": "response-status-set-v1" },
            { "url": "method", "valueCode": "DELETE" },
            { "url": "status", "valueInteger": 400 },
            { "url": "status", "valueInteger": 412 }
          ]
        }
      ],
      "action": [
        { "operation": { "type": { "code": "delete" }, "resource": "Patient", "responseId": "cd-nocriteria-response", "description": "Conditional DELETE /Patient with no query string" } },
        { "assert": { "description": "Primary: expect 412 Precondition Failed (warningOnly: some servers return 400 instead)", "response": "preconditionFailed", "warningOnly": true } },
        { "assert": { "description": "Alternative: 400 Bad Request is also accepted", "response": "bad", "warningOnly": true } }
      ]
```
with
```json
      "action": [
        { "operation": { "type": { "code": "delete" }, "resource": "Patient", "responseId": "cd-nocriteria-response", "description": "Conditional DELETE /Patient with no query string" } },
        { "assert": { "sourceId": "cd-nocriteria-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "cd-nocriteria-status" }], "description": "Primary: expect 412 Precondition Failed (warningOnly: some servers return 400 instead)", "response": "preconditionFailed", "warningOnly": true } },
        { "assert": { "sourceId": "cd-nocriteria-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "cd-nocriteria-status" }], "description": "Alternative: 400 Bad Request is also accepted", "response": "bad", "warningOnly": true } }
      ]
```

Test `"conditional DELETE matching exactly one Patient..."` (404/410 readback pair): replace
```json
      "extension": [
        {
          "url": "http://ignixa.io/testscript/statusAlternativePolicy",
          "extension": [
            { "url": "policy", "valueCode": "response-status-set-v1" },
            { "url": "method", "valueCode": "GET" },
            { "url": "status", "valueInteger": 404 },
            { "url": "status", "valueInteger": 410 }
          ]
        }
      ],
```
(remove the extension array entirely — confirm no other extension entries exist on this test), then replace
```json
        { "assert": { "description": "Primary: expect 410 Gone", "response": "gone", "warningOnly": true } },
        { "assert": { "description": "Alternative: 404 Not Found is also accepted", "response": "notFound", "warningOnly": true } }
```
with
```json
        { "assert": { "sourceId": "cd-onematch-read-after", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "cd-onematch-readback" }], "description": "Primary: expect 410 Gone", "response": "gone", "warningOnly": true } },
        { "assert": { "sourceId": "cd-onematch-read-after", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "cd-onematch-readback" }], "description": "Alternative: 404 Not Found is also accepted", "response": "notFound", "warningOnly": true } }
```

- [ ] **Step 3: Run backend unit tests**

Run: `dotnet test Ignixa.Lab.sln` — expect unchanged pass count.

- [ ] **Step 4: Verify live against both targets**

Same pattern as prior tasks, for `CRUD/create` and `CRUD/conditional-delete` suite IDs.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Suites/testscripts/CRUD/create.json backend/src/Ignixa.Lab.Suites/testscripts/CRUD/conditional-delete.json
git commit -m "test: migrate CRUD/create and CRUD/conditional-delete response-status-set policies onto assertionAnyOfGroup"
```

---

### Task 7: Delete the status-alternative workaround components

**Files:**
- Delete: `backend/src/Ignixa.Lab.Functions/Execution/StatusAlternativeEnforcementPlan.cs`
- Delete: `backend/src/Ignixa.Lab.Functions/Execution/WarningOnlyStatusAlternativeEnforcer.cs`
- Delete: any test files for the above two (locate via `grep -rl "StatusAlternativeEnforcementPlan\|WarningOnlyStatusAlternativeEnforcer" backend/test`)
- Modify: `backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Suites/ISuiteCatalog.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Suites/SuiteCatalog.cs`

**Interfaces:**
- Consumes: Tasks 4-6 having migrated every suite off `statusAlternativePolicy` markers.
- Produces: nothing — terminal cleanup for this phase.

- [ ] **Step 1: Confirm no suite JSON still references the marker**

Run: `grep -rl "statusAlternativePolicy" backend/src/Ignixa.Lab.Suites/testscripts`
Expected: no output. If any file still matches, STOP — a suite was missed in Tasks 4-6; report this as a concern rather than proceeding with deletion.

- [ ] **Step 2: Remove the `TestScriptRunner.cs` wiring**

In `ExecuteJobAsync` (around line 211-224), change:
```csharp
            var results = ConformanceReportMapper.Map(report, job.Id, job.Category, job.File);
            return WarningOnlyStatusAlternativeEnforcer.Apply(results, job.StatusAlternativePlan);
```
to
```csharp
            return ConformanceReportMapper.Map(report, job.Id, job.Category, job.File);
```

In `ResolveJobs` (around line 258-312): remove `entry.StatusAlternativePlan` from the `SuiteJob` constructor call (around line 271-276); remove the `StatusAlternativeEnforcementPlan statusAlternativePlan;` local and its `= StatusAlternativeEnforcementPlan.Parse(content);` assignment (around line 287-292); remove `statusAlternativePlan` from the uploaded-`SuiteJob` constructor call (around line 308).

In the `SuiteJob` record (around line 314-319): remove the `StatusAlternativeEnforcementPlan StatusAlternativePlan` parameter.

Read the file first — line numbers will have shifted from what's cited here since Tasks 4-6 don't touch this file, but earlier tasks in this plan don't either, so these should still be close to accurate; confirm before editing.

- [ ] **Step 3: Remove the `ISuiteCatalog.cs`/`SuiteCatalog.cs` wiring**

In `ISuiteCatalog.cs`, remove `StatusAlternativeEnforcementPlan StatusAlternativePlan` from the `CatalogEntry` record definition (line 12).

In `SuiteCatalog.cs`'s `LoadEntries` (around line 71-75), change:
```csharp
                entries.Add(new CatalogEntry(
                    descriptor,
                    file,
                    parseResult.Value,
                    StatusAlternativeEnforcementPlan.Parse(content)));
```
to
```csharp
                entries.Add(new CatalogEntry(
                    descriptor,
                    file,
                    parseResult.Value));
```

- [ ] **Step 4: Delete the two workaround files and their tests**

```bash
rm backend/src/Ignixa.Lab.Functions/Execution/StatusAlternativeEnforcementPlan.cs
rm backend/src/Ignixa.Lab.Functions/Execution/WarningOnlyStatusAlternativeEnforcer.cs
```
Then delete whatever test file(s) `grep -rl "StatusAlternativeEnforcementPlan\|WarningOnlyStatusAlternativeEnforcer" backend/test` found in Step 1's sibling search (run it again scoped to `backend/test` if you haven't already) — these test the classes you just deleted, so they must go too.

- [ ] **Step 5: Build and test**

Run: `dotnet build Ignixa.Lab.sln -c Release` — expect SUCCESS (no remaining references to the deleted types).
Run: `dotnet test Ignixa.Lab.sln` — expect all remaining tests pass (count will be lower than before by however many tests the two deleted test files contained — note the exact before/after count in your report).

- [ ] **Step 6: Verify live against both targets — full suite run, compare to Task 6's post-migration baseline**

Confirm no suite anywhere regressed as a side effect of removing the enforcer (nothing should — every suite that used it was migrated in Tasks 4-6 — but this is the final confirmation that removal itself changed nothing observable).

- [ ] **Step 7: Commit**

```bash
git add -A backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs \
           backend/src/Ignixa.Lab.Functions/Suites/ISuiteCatalog.cs \
           backend/src/Ignixa.Lab.Functions/Suites/SuiteCatalog.cs
git rm backend/src/Ignixa.Lab.Functions/Execution/StatusAlternativeEnforcementPlan.cs \
       backend/src/Ignixa.Lab.Functions/Execution/WarningOnlyStatusAlternativeEnforcer.cs
# also git rm whatever test file(s) Step 4 identified
git commit -m "chore: delete StatusAlternativeEnforcementPlan/WarningOnlyStatusAlternativeEnforcer, superseded by assertionAnyOfGroup"
```

---

### Task 8: Adopt `waitFor` in `Operations/export-data.json`

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Operations/export-data.json`

**Interfaces:**
- Consumes: Task 1's package bump only (independent of Tasks 3-7).
- Produces: nothing consumed elsewhere.

**Scope note:** the Microsoft import/bulk suites (`ms-import-basic.json` etc.) are explicitly NOT touched by this task — `ms-import-basic.json`'s own description states its gap is that "real end-to-end import needs real blob storage... matching the target server's configuration," an infrastructure limitation `waitFor` doesn't address (there's no job to poll without a storage account to import from in the first place). `export-data.json`'s gap is different: the job already kicks off successfully, it's just never polled to completion — that's exactly what `waitFor` fixes.

- [ ] **Step 1: Add a job-completion test using `waitFor`**

Add a new test to `export-data.json` (after `"system-level $export kick-off returns 202 Accepted..."`), polling the kickoff's own `Content-Location` to completion instead of cancelling early:

```json
    {
      "name": "system-level $export runs to completion and returns a manifest",
      "description": "Polling the Content-Location status endpoint to completion (via waitFor) must eventually return 200 with a completed export manifest, closing the gap this suite's own description previously called out as uncovered.",
      "action": [
        { "operation": { "type": { "code": "$export" }, "method": "GET", "url": "$export", "accept": "application/fhir+json", "requestHeader": [ { "field": "Prefer", "value": "respond-async" } ], "responseId": "completion-export-kickoff", "description": "GET /$export with Accept: application/fhir+json and Prefer: respond-async" } },
        { "assert": { "description": "Kick-off must return 202 Accepted", "sourceId": "completion-export-kickoff", "responseCode": "202" } },
        { "operation": { "type": { "code": "read" }, "url": "${completionExportLocation}", "responseId": "completion-export-poll", "description": "Poll the Content-Location until the job leaves 202", "extension": [{
          "url": "http://ignixa.io/testscript/waitFor",
          "extension": [
            { "url": "pollingStatusCode", "valueInteger": 202 },
            { "url": "maxAttempts", "valueInteger": 30 },
            { "url": "intervalMs", "valueInteger": 2000 }
          ]
        }] } },
        { "assert": { "description": "Completed export status check must return 200 OK with the manifest", "sourceId": "completion-export-poll", "response": "okay" } }
      ]
    }
```

Add the corresponding `variable` entry to the suite's existing top-level `variable` array (alongside `systemExportLocation` etc.):
```json
    { "name": "completionExportLocation", "sourceId": "completion-export-kickoff", "headerField": "Content-Location", "description": "Content-Location returned by the completion-verification $export kickoff" },
```

- [ ] **Step 2: Update the suite's own top-level `description` field**

Remove "Scenarios requiring actual export completion and blob content verification are not covered." from the suite's `description` (line 4) — job *completion* is now covered by the new test; blob *content* verification remains out of scope (still needs real storage access) — adjust the sentence to reflect only the remaining gap, e.g. "Blob content verification is not covered (would require access to the target's storage account)."

- [ ] **Step 3: Run backend unit tests**

Run: `dotnet test Ignixa.Lab.sln` — expect unchanged pass count (this suite's content isn't unit-tested directly).

- [ ] **Step 4: Verify live against both targets**

Run the `Operations/export-data` suite against both `https://subscriptions.argo.run/` and `https://bkowitz-testdeploy.azurewebsites.net`. This is a real async job — expect the new test to take up to ~60 seconds if the target actually needs multiple polling attempts. If either target doesn't support `$export` at all (gated by `requiresCapability` at the suite level, per the existing `extension` block), the whole suite should skip cleanly, not fail — confirm that's what happens rather than an error.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Suites/testscripts/Operations/export-data.json
git commit -m "test: adopt waitFor to verify $export job completion in Operations/export-data"
```
