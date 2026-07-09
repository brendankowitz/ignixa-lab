# FHIR Suite Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove known false signals from the bundled FHIR TestScript suites while preserving strict, spec-backed conformance failures.

**Architecture:** Keep the current architecture: JSON TestScripts in `backend/src/Ignixa.Lab.Suites/testscripts/` remain the source of truth and `Ignixa.Lab.Functions` continues to execute them through `Ignixa.TestScript`. Add focused backend tests that lock in reliability rules, then update suite JSON in small groups: assertion mechanics, fixture validity, capability gates, and classic Subscription hygiene.

**Tech Stack:** .NET 10 isolated Azure Functions, xUnit + FluentAssertions, Ignixa.TestScript 0.6.7-beta, FHIR R4/R4B/R5 TestScript JSON.

---

## File Structure

**Modify:**

- `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs` — add guard tests for known false-signal patterns in bundled suites.
- `backend/src/Ignixa.Lab.Suites/testscripts/Bundles/batch.json` — allow `Bundle.entry.response.status` reason phrases.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/create.json` — remove unsupported variable interpolation from ETag header asserts; classify X-Provenance as Microsoft/server-specific warning behavior.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/read.json` — remove unsupported variable interpolation from ETag header asserts; make `X-Content-Type-Options` informational; make 404-after-delete non-failing.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/vread.json` — gate vread/versioning behavior and remove unsupported variable interpolation from ETag header asserts.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/delete.json` — gate version-specific readback behavior and make 404-after-delete non-failing.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/update.json` — replace syntactically invalid media type, avoid fixed-id contamination, and add setup assertions.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/conditional-update.json` — avoid fixed-id contamination in no-match explicit-id case.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/conditional-delete.json` — gate conditional delete on advertised support.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/patch-body.json` — gate PATCH.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/patch-fhirpath.json` — gate PATCH.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/patch-json.json` — gate PATCH.
- `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/all-resource-types.json` — replace the `MessageHeader` fhirfakes marker with an inline valid R4/R4B/R5-compatible fixture.
- `backend/src/Ignixa.Lab.Suites/testscripts/Foundation/cors.json` — make CORS checks informational because CORS is not a universal FHIR conformance requirement.
- `backend/src/Ignixa.Lab.Suites/testscripts/Foundation/health.json` — make `/health/check` checks informational because this route is server-specific, not base FHIR.
- `backend/src/Ignixa.Lab.Suites/testscripts/Search/date.json` — fix invalid dateTime fixtures and query values by adding timezone offsets.
- `backend/src/Ignixa.Lab.Suites/testscripts/Operations/import-search.json` — fix invalid dateTime fixture/query values.
- `backend/src/Ignixa.Lab.Suites/testscripts/Operations/import-search-2.json` — gate `_tag`-dependent import-search tests.
- `backend/src/Ignixa.Lab.Suites/testscripts/Search/custom-search-param.json` — gate custom SearchParameter behavior.
- `backend/src/Ignixa.Lab.Suites/testscripts/Search/chaining-and-sort.json`, `Search/chaining.json`, `Search/includes.json`, `Search/joins.json` — gate non-universal include/chaining behavior where the suite relies on advertised `searchInclude` or chained references.
- `backend/src/Ignixa.Lab.Suites/testscripts/Subscriptions/basic.json` — keep classic R4/R4B behavior, add setup success assertions, tighten capability gate, and make 404-after-delete non-failing.

**Create:**

- No production source files.
- No new suite category in this pass.

**Spec references to use while implementing strict assertions:**

- FHIR R4 HTTP: `https://hl7.org/fhir/R4/http.html`
- FHIR R4 Search: `https://hl7.org/fhir/R4/search.html`
- FHIR R4 Bundle: `https://hl7.org/fhir/R4/bundle.html`
- FHIR R4 Subscription: `https://hl7.org/fhir/R4/subscription.html`
- FHIR R4 dateTime primitive: `https://hl7.org/fhir/R4/datatypes.html#dateTime`
- Subscriptions Backport for future work only: `https://hl7.org/fhir/uv/subscriptions-backport/STU1.1/`

---

### Task 1: Add bundled-suite reliability guard tests

**Files:**
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs`

- [ ] **Step 1: Add failing guard tests for known false-signal patterns**

Add these `using` statements at the top of `SuiteCatalogTests.cs`:

```csharp
using System.Text.Json.Nodes;
```

Add the following tests and helpers after `GetSuites_BundledCanonicalSuites_IncludeKnownIds()`:

```csharp
[Fact]
public void BundledCanonicalSuites_DoNotUseVariablePlaceholdersInHeaderExpectedValues()
{
    var violations = new List<string>();

    foreach (var (relativePath, json) in ReadBundledSuiteJson())
    {
        var root = JsonNode.Parse(json);
        VisitObjects(root, obj =>
        {
            if (!obj.TryGetPropertyValue("headerField", out _))
            {
                return;
            }

            if (obj.TryGetPropertyValue("value", out var valueNode)
                && valueNode?.GetValueKind() == System.Text.Json.JsonValueKind.String
                && valueNode.GetValue<string>().Contains("${", StringComparison.Ordinal))
            {
                var description = obj.TryGetPropertyValue("description", out var descriptionNode)
                    ? descriptionNode?.GetValue<string>()
                    : "(no description)";
                violations.Add($"{relativePath}: header assertion '{description}' uses unsupported variable interpolation in value '{valueNode.GetValue<string>()}'.");
            }
        });
    }

    violations.Should().BeEmpty();
}

[Fact]
public void BundledCanonicalSuites_DoNotUseSyntacticallyInvalidContentTypes()
{
    var violations = ReadBundledSuiteJson()
        .Where(suite => suite.Json.Contains("\"contentType\": \"Jibberish\"", StringComparison.Ordinal))
        .Select(suite => suite.RelativePath)
        .ToArray();

    violations.Should().BeEmpty("unsupported media-type tests must use a syntactically valid media type so the request reaches the target server");
}

[Fact]
public void BundledCanonicalSuites_DoNotAssertBundleEntryStatusWithExactReasonlessCode()
{
    var violations = new List<string>();

    foreach (var (relativePath, json) in ReadBundledSuiteJson())
    {
        var root = JsonNode.Parse(json);
        VisitObjects(root, obj =>
        {
            var expression = obj.TryGetPropertyValue("expression", out var expressionNode)
                ? expressionNode?.GetValue<string>()
                : null;
            var value = obj.TryGetPropertyValue("value", out var valueNode)
                ? valueNode?.GetValue<string>()
                : null;
            var op = obj.TryGetPropertyValue("operator", out var operatorNode)
                ? operatorNode?.GetValue<string>()
                : null;

            if (expression is not null
                && expression.EndsWith("response.status", StringComparison.Ordinal)
                && value is not null
                && value.All(char.IsDigit)
                && string.Equals(op, "equals", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{relativePath}: use startsWith('{value}') for Bundle.entry.response.status because FHIR permits a reason phrase.");
            }
        });
    }

    violations.Should().BeEmpty();
}

private static IEnumerable<(string RelativePath, string Json)> ReadBundledSuiteJson()
{
    var root = Path.Combine(AppContext.BaseDirectory, "testscripts");
    foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
    {
        yield return (Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(file));
    }
}

private static void VisitObjects(JsonNode? node, Action<JsonObject> visit)
{
    switch (node)
    {
        case JsonObject obj:
            visit(obj);
            foreach (var child in obj.Select(property => property.Value))
            {
                VisitObjects(child, visit);
            }
            break;
        case JsonArray array:
            foreach (var child in array)
            {
                VisitObjects(child, visit);
            }
            break;
    }
}
```

- [ ] **Step 2: Run the guard tests and confirm they fail**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests"
```

Expected: failure listing current violations in `CRUD/create.json`, `CRUD/read.json`, `CRUD/vread.json`, `Bundles/batch.json`, and `CRUD/update.json`.

- [ ] **Step 3: Commit the failing tests**

Run:

```powershell
git add backend\test\Ignixa.Lab.Functions.Tests\Suites\SuiteCatalogTests.cs
git commit -m "test: guard FHIR suite reliability patterns" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: Fix assertion mechanics and stricter-than-spec assertions

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Bundles/batch.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/create.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/read.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/vread.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/update.json`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs`

- [ ] **Step 1: Fix Bundle status assertion**

In `Bundles/batch.json`, replace:

```json
{ "assert": { "description": "First submission's entry must report 201 Created", "expression": "entry[0].response.status", "value": "201", "operator": "equals" } }
```

with:

```json
{ "assert": { "description": "First submission's entry must report 201 Created", "expression": "entry[0].response.status.startsWith('201')" } }
```

FHIR R4 `Bundle.entry.response.status` is a string containing the status code and may include the reason phrase, so `201 Created` is valid.

- [ ] **Step 2: Remove unsupported header-value interpolation from create/read/vread**

In `CRUD/create.json`, replace the ETag version-specific assertion:

```json
{ "assert": { "description": "ETag header must reflect the assigned version", "headerField": "ETag", "operator": "contains", "value": "${basicVersionId}" } },
```

with:

```json
{ "assert": { "description": "ETag header must be present for the assigned version", "headerField": "ETag", "operator": "notEmpty" } },
```

In `CRUD/read.json`, replace:

```json
{ "assert": { "description": "ETag header must reflect the current version", "headerField": "ETag", "operator": "contains", "value": "${obsVersionId}" } },
{ "assert": { "description": "X-Content-Type-Options security header must be present and set to nosniff", "headerField": "X-Content-Type-Options", "operator": "contains", "value": "nosniff" } }
```

with:

```json
{ "assert": { "description": "ETag header must be present for the current version", "headerField": "ETag", "operator": "notEmpty" } },
{ "assert": { "description": "X-Content-Type-Options security header should be present and set to nosniff (warningOnly: security header is not a base FHIR requirement)", "headerField": "X-Content-Type-Options", "operator": "contains", "value": "nosniff", "warningOnly": true } }
```

In `CRUD/vread.json`, replace:

```json
{ "assert": { "description": "ETag header must reflect the requested version", "headerField": "ETag", "operator": "contains", "value": "${obsVersionId}" } }
```

with:

```json
{ "assert": { "description": "ETag header must be present for the requested version", "headerField": "ETag", "operator": "notEmpty" } }
```

- [ ] **Step 3: Make 404-after-delete non-failing where FHIR permits it**

In `CRUD/read.json`, replace:

```json
{ "assert": { "description": "Primary: expect 410 Gone (FHIR R4 behavior for a deleted resource)", "response": "gone", "warningOnly": false } },
{ "assert": { "description": "Alternative: 404 Not Found is also accepted (warningOnly so 404-returning servers still pass this suite)", "response": "notFound", "warningOnly": true } }
```

with:

```json
{ "assert": { "description": "Preferred: 410 Gone for a deleted resource (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
{ "assert": { "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } }
```

In `CRUD/delete.json`, replace:

```json
{ "assert": { "description": "Primary: expect 410 Gone", "response": "gone", "warningOnly": false } },
{ "assert": { "description": "Alternative: 404 Not Found is also accepted", "response": "notFound", "warningOnly": true } },
```

with:

```json
{ "assert": { "description": "Preferred: 410 Gone for a deleted resource (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
{ "assert": { "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } },
```

- [ ] **Step 4: Replace invalid Content-Type test input**

In `CRUD/update.json`, replace the malformed Content-Type test name, description, operation, and assertion:

```json
"name": "update with a malformed Content-Type header is rejected",
"description": "PUT Patient/ignixa-update-content with an unparseable Content-Type ('Jibberish') must return 400 Bad Request rather than attempting to parse the body.",
...
{ "operation": { "type": { "code": "update" }, "url": "Patient/ignixa-update-content", "sourceId": "patient-v2", "contentType": "Jibberish", "responseId": "bad-contenttype-response", "description": "PUT Patient/ignixa-update-content with Content-Type: Jibberish" } },
{ "assert": { "description": "Server must return 400 Bad Request", "response": "bad" } }
```

with:

```json
"name": "update with an unsupported Content-Type header is rejected",
"description": "PUT Patient/ignixa-update-content with a syntactically valid but unsupported Content-Type must return 415 Unsupported Media Type.",
...
{ "operation": { "type": { "code": "update" }, "url": "Patient/ignixa-update-content", "sourceId": "patient-v2", "contentType": "application/not-fhir+json", "responseId": "bad-contenttype-response", "description": "PUT Patient/ignixa-update-content with Content-Type: application/not-fhir+json" } },
{ "assert": { "description": "Server must return 415 Unsupported Media Type", "responseCode": "415" } }
```

- [ ] **Step 5: Run guard tests and confirm this task passes**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests"
```

Expected: the three new reliability guard tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend\src\Ignixa.Lab.Suites\testscripts\Bundles\batch.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\create.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\read.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\vread.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\delete.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\update.json
git commit -m "fix: remove false FHIR suite assertion failures" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Fix invalid fixtures and contaminated fixed IDs

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Search/date.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Operations/import-search.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/all-resource-types.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/update.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/conditional-update.json`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs`

- [ ] **Step 1: Fix invalid dateTime fixture values**

In `Search/date.json`, replace:

```json
"effectiveDateTime": "1980-05-11T16:32:15"
```

with:

```json
"effectiveDateTime": "1980-05-11T16:32:15Z"
```

Replace:

```json
"effectiveDateTime": "1980-05-11T16:32:15.500"
```

with:

```json
"effectiveDateTime": "1980-05-11T16:32:15.500Z"
```

Also update the search URL and descriptions in the same file:

```json
"params": "?code=ignixa-date-test&date=1980-05-16T16:32:15.500"
```

to:

```json
"params": "?code=ignixa-date-test&date=1980-05-16T16:32:15.500Z"
```

Apply the same `Z` suffix replacements to the matching fixture and query values in `Operations/import-search.json`.

- [ ] **Step 2: Replace the MessageHeader fhirfakes marker with an inline fixture**

In `CRUD/all-resource-types.json`, replace the `messageHeader-fixture` resource body:

```json
"resource": {
  "extension": [
    {
      "url": "http://ignixa.io/testscript/fhirfakes",
      "valueCode": "MessageHeader"
    }
  ]
}
```

with this inline valid R4-compatible fixture:

```json
"resource": {
  "resourceType": "MessageHeader",
  "eventUri": "http://example.org/ignixa/testscript/message",
  "source": {
    "name": "Ignixa TestScript",
    "endpoint": "urn:ignixa:testscript:source"
  },
  "destination": [
    {
      "name": "FHIR server under test",
      "endpoint": "urn:ignixa:testscript:destination"
    }
  ]
}
```

FHIR R4 `MessageHeader` requires `event[x]` and `source`; this avoids relying on a generated fixture shape that produced invalid server input during live runs.

- [ ] **Step 3: Avoid reused fixed IDs in update upsert tests**

In `CRUD/update.json`, change the upsert fixture id and URL from `ignixa-update-newpat` to a run-unique marker that is unlikely to exist on reused public targets:

```json
"id": "ignixa-update-newpat-reliable"
```

and:

```json
"url": "Patient/ignixa-update-newpat-reliable"
```

Update the related assertion value and teardown URL to `ignixa-update-newpat-reliable`.

In `CRUD/conditional-update.json`, change the explicit-id fixture and assertion from `ignixa-cond-upd-explicit` to `ignixa-cond-upd-explicit-reliable`, and change the query marker from `CU-EXPLICIT` to `CU-EXPLICIT-RELIABLE` in the test operation and teardown.

- [ ] **Step 4: Add setup success assertions where downstream tests depend on setup**

In `CRUD/update.json` setup, after each setup operation add an assertion:

```json
{ "assert": { "description": "Setup PUT Patient/ignixa-update-content must succeed", "sourceId": "setup-create-response", "response": "okay" } }
```

```json
{ "assert": { "description": "Setup create for mismatch Patient must return 201 Created", "sourceId": "mismatch-create-response", "response": "created" } }
```

```json
{ "assert": { "description": "Setup PUT Practitioner/ignixa-update-prac1 must succeed", "sourceId": "setup-prac-response", "response": "okay" } }
```

In `Operations/everything-operation.json`, add `responseId` values to setup operations and assertions after each setup write:

```json
{ "operation": { "type": { "code": "update" }, "url": "Observation/ignixa-evx-obs", "sourceId": "obs", "responseId": "setup-obs-response", "description": "PUT Observation/ignixa-evx-obs (subject=pat)" } },
{ "assert": { "description": "Setup PUT Observation/ignixa-evx-obs must succeed", "sourceId": "setup-obs-response", "response": "okay" } }
```

Apply the same pattern to `Organization`, `Patient`, `Condition`, `Appointment`, the decoy `Patient`, and the decoy `Observation`.

- [ ] **Step 5: Run catalog tests**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests"
```

Expected: all `SuiteCatalogTests` pass and all bundled canonical suites load.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend\src\Ignixa.Lab.Suites\testscripts\Search\date.json backend\src\Ignixa.Lab.Suites\testscripts\Operations\import-search.json backend\src\Ignixa.Lab.Suites\testscripts\Operations\everything-operation.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\all-resource-types.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\update.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\conditional-update.json
git commit -m "fix: repair invalid FHIR suite fixtures" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Add capability gates for optional behavior

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/patch-body.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/patch-fhirpath.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/patch-json.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/vread.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/delete.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/history.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/conditional-delete.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Operations/import-search.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Operations/import-search-2.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Search/custom-search-param.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Search/includes.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Search/chaining.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Search/chaining-and-sort.json`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Execution/TestScriptRunnerTests.cs`

- [ ] **Step 1: Add a failing capability-gate regression test**

In `TestScriptRunnerTests.cs`, add this helper near the existing `GatedDefinition` helper:

```csharp
private static TestScriptDefinition MetadataGatedPatchDefinition() => new()
{
    Metadata = new TestScriptMetadata
    {
        Name = "PatchGated",
        RequiresCapability = "rest.resource.where(type='Patient').interaction.where(code='patch').exists()",
    },
    Tests =
    [
        new TestPhaseDefinition
        {
            Name = "PatchPatient",
            Actions = [new OperationExpression { Type = "patch", Resource = "Patient", Params = "/1" }],
        },
    ],
};
```

Add this test after `GivenSuiteRequiringUndeclaredCapability_WhenRun_ThenTestIsSkippedAndNoRequestIsSent`:

```csharp
[Fact]
public async Task GivenMetadataRequiresUndeclaredPatch_WhenRun_ThenEveryTestIsSkippedAndNoRequestIsSent()
{
    var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
    var runner = new TestScriptRunner(
        new FakeSuiteCatalog("patch-gated.json", MetadataGatedPatchDefinition()),
        new FakeEvaluatorFactory(provider),
        new CapabilityStatementFetcher(
            new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
            Options.Create(new IgnixaLabOptions())),
        Options.Create(new IgnixaLabOptions()),
        new SchemaProviderFactory(),
        NullLogger<TestScriptRunner>.Instance);

    var outcome = await runner.RunAsync(
        new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["patch-gated.json"] },
        CancellationToken.None);

    outcome.IsValid.Should().BeTrue();
    outcome.Report!.Results.Should().ContainSingle();
    outcome.Report.Results[0].Status.Should().Be(ConformanceStatus.Skipped);
    provider.CallCount.Should().Be(0);
}
```

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~MetadataRequiresUndeclaredPatch"
```

Expected: pass if metadata-level `RequiresCapability` is already wired; fail if it is not. If it fails, fix `TestScriptRunner` integration before editing suite gates.

- [ ] **Step 2: Gate PATCH suites**

Add this metadata extension to the top-level `extension` array in each PATCH suite. If a suite has no top-level `extension`, add one immediately after `"status": "active"`:

```json
{
  "url": "http://ignixa.io/testscript/requiresCapability",
  "valueString": "rest.resource.where(type='Patient').interaction.where(code='patch').exists()"
}
```

Apply to:

- `CRUD/patch-body.json`
- `CRUD/patch-fhirpath.json`
- `CRUD/patch-json.json`

- [ ] **Step 3: Gate vread, history, and versioning-dependent tests**

In `CRUD/vread.json`, add top-level `requiresCapability`:

```json
{
  "url": "http://ignixa.io/testscript/requiresCapability",
  "valueString": "rest.resource.where(type='Observation').interaction.where(code='vread').exists() and rest.resource.where(type='Observation').where(versioning.exists() and versioning != 'no-version').exists()"
}
```

In `CRUD/history.json`, add top-level `requiresCapability`:

```json
{
  "url": "http://ignixa.io/testscript/requiresCapability",
  "valueString": "rest.resource.where(type='Patient').interaction.where(code='history-instance' or code='history-type').exists()"
}
```

In `CRUD/delete.json`, add test-level `requiresCapability` to the test named `delete removes the resource; a plain read is Gone/NotFound while the original version remains vread-able`:

```json
"requiresCapability": "rest.resource.where(type='Patient').interaction.where(code='vread').exists() and rest.resource.where(type='Patient').where(versioning.exists() and versioning != 'no-version').exists()",
```

Keep the `deleted resource does not appear in subsequent searches` test ungated except for standard create/delete/search capability if it already has or receives those gates.

- [ ] **Step 4: Gate conditional delete**

In `CRUD/conditional-delete.json`, add top-level `requiresCapability`:

```json
{
  "url": "http://ignixa.io/testscript/requiresCapability",
  "valueString": "rest.resource.where(type='Patient').where(conditionalDelete.exists() and conditionalDelete != 'not-supported').exists()"
}
```

- [ ] **Step 5: Gate `_tag`-dependent import-search suites**

In both `Operations/import-search.json` and `Operations/import-search-2.json`, add top-level `requiresCapability`:

```json
{
  "url": "http://ignixa.io/testscript/requiresCapability",
  "valueString": "rest.resource.where(type='Observation').searchParam.where(name='_tag').exists() and rest.resource.where(type='Patient').searchParam.where(name='_tag').exists()"
}
```

This keeps strict import-search semantics but avoids treating targets that do not advertise `_tag` as failing those suites.

- [ ] **Step 6: Gate optional search features**

Add top-level or test-level `requiresCapability` expressions where the suite relies on non-universal search features:

For `Search/custom-search-param.json`:

```json
{
  "url": "http://ignixa.io/testscript/requiresCapability",
  "valueString": "rest.resource.where(type='SearchParameter').interaction.where(code='create' or code='update').exists()"
}
```

For `Search/includes.json`, use test-level gates on tests that require include support:

```json
"requiresCapability": "rest.resource.where(type='Observation').searchInclude.exists() or rest.resource.where(type='DiagnosticReport').searchInclude.exists()",
```

For `Search/chaining.json` and `Search/chaining-and-sort.json`, use test-level gates on chained-reference tests:

```json
"requiresCapability": "rest.resource.where(type='Observation').searchParam.where(type='reference').exists()"
```

When a test uses `_has`, add:

```json
"requiresCapability": "rest.resource.searchParam.where(name='_has').exists()"
```

- [ ] **Step 7: Run capability-gating tests**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~TestScriptRunnerTests|FullyQualifiedName~SuiteCatalogTests"
```

Expected: all targeted tests pass.

- [ ] **Step 8: Commit**

Run:

```powershell
git add backend\test\Ignixa.Lab.Functions.Tests\Execution\TestScriptRunnerTests.cs backend\src\Ignixa.Lab.Suites\testscripts\CRUD\patch-body.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\patch-fhirpath.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\patch-json.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\vread.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\delete.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\history.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\conditional-delete.json backend\src\Ignixa.Lab.Suites\testscripts\Operations\import-search.json backend\src\Ignixa.Lab.Suites\testscripts\Operations\import-search-2.json backend\src\Ignixa.Lab.Suites\testscripts\Search\custom-search-param.json backend\src\Ignixa.Lab.Suites\testscripts\Search\includes.json backend\src\Ignixa.Lab.Suites\testscripts\Search\chaining.json backend\src\Ignixa.Lab.Suites\testscripts\Search\chaining-and-sort.json
git commit -m "fix: gate optional FHIR suite capabilities" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: Reclassify non-FHIR server-specific suites as informational

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Foundation/cors.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Foundation/health.json`
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/CRUD/create.json`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs`

- [ ] **Step 1: Make CORS assertions warningOnly**

In `Foundation/cors.json`, add `"warningOnly": true` to every assertion in the suite. For example, replace:

```json
{
  "assert": {
    "description": "Preflight OPTIONS request must return HTTP 204 No Content",
    "sourceId": "cors-response",
    "response": "noContent"
  }
}
```

with:

```json
{
  "assert": {
    "description": "Preflight OPTIONS request should return HTTP 204 No Content (warningOnly: CORS is HTTP hosting behavior, not universal FHIR conformance)",
    "sourceId": "cors-response",
    "response": "noContent",
    "warningOnly": true
  }
}
```

Apply the same `warningOnly` treatment to `Access-Control-Allow-Origin`, `Access-Control-Allow-Methods`, `Access-Control-Allow-Headers`, and `Access-Control-Max-Age`.

- [ ] **Step 2: Make health endpoint checks warningOnly**

In `Foundation/health.json`, add `"warningOnly": true` to every assertion because `/health/check` is not part of base FHIR HTTP conformance.

- [ ] **Step 3: Make X-Provenance tests warningOnly**

In `CRUD/create.json`, add `"warningOnly": true` to all assertions in:

- `create with a valid X-Provenance header links a Provenance resource`
- `create with a malformed X-Provenance header returns 400`

Update each description to mention `warningOnly: X-Provenance is a Microsoft FHIR Server extension, not base FHIR`.

- [ ] **Step 4: Run suite catalog tests**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests"
```

Expected: all suite catalog tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add backend\src\Ignixa.Lab.Suites\testscripts\Foundation\cors.json backend\src\Ignixa.Lab.Suites\testscripts\Foundation\health.json backend\src\Ignixa.Lab.Suites\testscripts\CRUD\create.json
git commit -m "fix: mark server-specific checks informational" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: Tighten the classic R4 Subscription suite

**Files:**
- Modify: `backend/src/Ignixa.Lab.Suites/testscripts/Subscriptions/basic.json`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Suites/SuiteCatalogTests.cs`

- [ ] **Step 1: Tighten the top-level Subscription gate**

Replace the current gate:

```json
"valueString": "rest.resource.where(type='Subscription').exists()"
```

with:

```json
"valueString": "rest.resource.where(type='Subscription').interaction.where(code='create').exists() and rest.resource.where(type='Subscription').interaction.where(code='read').exists() and rest.resource.where(type='Subscription').interaction.where(code='update').exists() and rest.resource.where(type='Subscription').interaction.where(code='delete').exists() and rest.resource.where(type='Subscription').interaction.where(code='search-type').exists()"
```

This keeps the suite scoped to classic request/response lifecycle behavior.

- [ ] **Step 2: Add setup responseId and setup assertion**

Replace the setup operation:

```json
{ "operation": { "type": { "code": "update" }, "url": "Subscription/ignixa-sub-basic", "sourceId": "subscription-initial", "description": "PUT Subscription/ignixa-sub-basic (criteria=Patient?name=ignixa-subscriptions-basic-marker, channel=rest-hook)" } }
```

with:

```json
{ "operation": { "type": { "code": "update" }, "url": "Subscription/ignixa-sub-basic", "sourceId": "subscription-initial", "responseId": "subscription-setup-response", "description": "PUT Subscription/ignixa-sub-basic (criteria=Patient?name=ignixa-subscriptions-basic-marker, channel=rest-hook)" } },
{ "assert": { "description": "Setup PUT Subscription/ignixa-sub-basic must succeed before lifecycle assertions run", "sourceId": "subscription-setup-response", "response": "okay" } }
```

- [ ] **Step 3: Make delete readback 404/410 non-failing**

Replace:

```json
{ "assert": { "description": "Primary: expect 410 Gone", "response": "gone" } },
{ "assert": { "description": "Alternative: 404 Not Found is also accepted (warningOnly)", "response": "notFound", "warningOnly": true } }
```

with:

```json
{ "assert": { "description": "Preferred: 410 Gone for a deleted Subscription (warningOnly: FHIR also permits 404 when deleted resources are not tracked)", "response": "gone", "warningOnly": true } },
{ "assert": { "description": "Alternative: 404 Not Found is accepted when the server does not track deleted resources", "response": "notFound", "warningOnly": true } }
```

- [ ] **Step 4: Document classic-only scope in description**

Update the suite description to include:

```text
This suite is intentionally classic R4/R4B only. Backport/topic-based Subscriptions use criteria as a topic canonical plus filter extensions and require a separate suite.
```

- [ ] **Step 5: Run catalog tests**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj --filter "FullyQualifiedName~SuiteCatalogTests"
```

Expected: all suite catalog tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend\src\Ignixa.Lab.Suites\testscripts\Subscriptions\basic.json
git commit -m "fix: tighten classic subscription suite scope" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 7: Full backend verification and targeted live rerun

**Files:**
- No code changes expected.
- Artifacts: write live rerun reports under `C:\Users\brend\.copilot\session-state\4897abb2-6c42-48fb-b9d7-11feffce6ce8\files\fhir-suite-runs-reliability-rerun`

- [ ] **Step 1: Run full backend verification**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
.\backend\pack-suites.ps1
dotnet build Ignixa.Lab.sln -c Release
dotnet test Ignixa.Lab.sln
```

Expected: build succeeds and all xUnit tests pass.

- [ ] **Step 2: Start the local Functions backend**

Run in a background shell:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival\backend\src\Ignixa.Lab.Functions'
$env:FUNCTIONS_WORKER_RUNTIME='dotnet-isolated'
$env:IgnixaLab__RateLimiting__Enabled='false'
$env:IgnixaLab__MaxSuitesPerRun='100'
func start --port 7071
```

Wait until:

```powershell
Invoke-WebRequest -Uri 'http://127.0.0.1:7071/api/health'
```

returns HTTP 200.

- [ ] **Step 3: Rerun touched suites against both live targets**

Run:

```powershell
Set-Location 'C:\Users\brend\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-cautious-carnival'
$artifact='C:\Users\brend\.copilot\session-state\4897abb2-6c42-48fb-b9d7-11feffce6ce8\files\fhir-suite-runs-reliability-rerun'
New-Item -ItemType Directory -Force -Path $artifact | Out-Null
$suiteIds=@(
  'Bundles/batch.json',
  'CRUD/create.json',
  'CRUD/read.json',
  'CRUD/vread.json',
  'CRUD/delete.json',
  'CRUD/update.json',
  'CRUD/conditional-update.json',
  'CRUD/conditional-delete.json',
  'CRUD/patch-body.json',
  'CRUD/patch-fhirpath.json',
  'CRUD/patch-json.json',
  'CRUD/all-resource-types.json',
  'Foundation/cors.json',
  'Foundation/health.json',
  'Search/date.json',
  'Search/custom-search-param.json',
  'Search/chaining.json',
  'Search/chaining-and-sort.json',
  'Search/includes.json',
  'Operations/import-search.json',
  'Operations/import-search-2.json',
  'Operations/everything-operation.json',
  'Subscriptions/basic.json'
)
$targets=@(
  [pscustomobject]@{name='argo-subscriptions'; url='https://subscriptions.argo.run/fhir/r4'},
  [pscustomobject]@{name='azure-testdeploy'; url='https://bkowitz-testdeploy.azurewebsites.net'}
)
$summary=@()
foreach($target in $targets){
  $targetDir=Join-Path $artifact $target.name
  New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
  foreach($suiteId in $suiteIds){
    $safe=($suiteId -replace '[\\/:*?"<>|]','__')
    $out=Join-Path $targetDir "$safe.report.json"
    $body=@{ targetUrl=$target.url; fhirVersion='4.0.1'; suiteIds=@($suiteId) } | ConvertTo-Json -Depth 5
    $response=Invoke-WebRequest -Uri 'http://127.0.0.1:7071/api/run' -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 900 -SkipHttpErrorCheck
    Set-Content -Path $out -Value $response.Content -Encoding UTF8
    $report=$response.Content | ConvertFrom-Json
    $results=@($report.results)
    $summary += [pscustomobject]@{
      target=$target.name
      suite=$suiteId
      pass=@($results | Where-Object status -eq 'pass').Count
      fail=@($results | Where-Object status -eq 'fail').Count
      error=@($results | Where-Object status -eq 'error').Count
      skip=@($results | Where-Object status -eq 'skip').Count
      total=$results.Count
    }
  }
}
$summary | Export-Csv -NoTypeInformation -Path (Join-Path $artifact 'summary.csv')
$summary | Format-Table -AutoSize
```

Expected:

- PATCH suites skip on Argo if PATCH is not advertised.
- vread/history/version-dependent tests skip on servers advertising `versioning: no-version`.
- CORS and health no longer create hard failures.
- ETag literal `${...}` failures no longer appear.
- Invalid dateTime setup failures no longer appear.
- `Subscriptions/basic.json` either passes on classic-capable Azure or fails only with a clear target behavior issue; Argo classic failures should no longer cascade without setup evidence.

- [ ] **Step 4: Stop the local Functions backend**

Stop the specific `func start` process that was launched for this validation.

- [ ] **Step 5: Commit validation notes if a docs update is needed**

If the rerun reveals a spec-backed adjustment made during validation, update the relevant suite description or `backend/README.md` with the specific spec reason and commit it:

```powershell
git add backend\README.md backend\src\Ignixa.Lab.Suites\testscripts
git commit -m "docs: record FHIR suite reliability rationale" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

Skip this commit if no documentation changed.

