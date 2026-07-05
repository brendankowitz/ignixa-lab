# Consume ignixa-fhir 0.6.4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bump ignixa-lab's `Ignixa.*` package references to `0.6.4`, consolidate the Fakes bench onto the newly-public `ScenarioCatalog`/`ObservationStateCatalog` APIs, fix FHIR-version-label gating in the TestScript runner, and surface Theme-aware generation and the `DailyAppointmentSchedule` workflow scenario pack in the Fakes bench UI.

**Architecture:** No new architectural layers — this replaces two hand-rolled reflection classes with direct calls to the now-public library catalogs, adds one new field to two existing request/response DTOs, adds one new Functions endpoint (`/api/fakes/workflow`) that mirrors the shape of the existing `/api/fakes/scenario` endpoint, and adds one new mode to the existing Fakes bench UI tab set.

**Tech Stack:** .NET 10 (Azure Functions isolated worker), `Ignixa.FhirFakes`/`Ignixa.TestScript` 0.6.4, xUnit + FluentAssertions, React + TypeScript (Vite).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-05-ignixa-fhir-0.6.4-upgrade-design.md`.
- Base branch: `main` (not the current `feature/terminology-subscriptions-suites`).
- Branch name: `feature/ignixa-fhir-0.6.4-upgrade`.
- Package versions (verified against NuGet.org and a scratch build against the actual 0.6.4 packages — see Task 1): `Ignixa.FhirFakes`/`Ignixa.FhirPath`/`Ignixa.Serialization`/`Ignixa.Specification` → `0.6.4`; `Ignixa.TestScript`/`Ignixa.TestScript.FhirFakes` → `0.6.4-beta`.
- `ScenarioCatalog.Invoke`/`WorkflowScenarioCatalog.Invoke` throw `Ignixa.FhirFakes.Scenarios.ScenarioInvocationException` (not `TargetInvocationException`) when a factory method itself fails.
- `TestScriptRunner`'s `ConformanceReport.FhirVersion` field must stay in numeric form (e.g. `"4.0"`), never a release label — it is compared against `ignixa-fhir`'s own `conformance/latest.json` artifact.
- Every task ends green: `dotnet build Ignixa.Lab.sln` and `dotnet test Ignixa.Lab.sln` for backend tasks; `npm run build` (in `frontend/`) for frontend tasks.

---

### Task 1: Bump `Ignixa.*` package versions to 0.6.4

**Files:**
- Modify: `Directory.Packages.props`

**Interfaces:**
- Produces: nothing new — this task only changes dependency versions. Verified (via a scratch build performed during planning, then reverted) that this change alone builds and tests clean against the current `main`-equivalent code; no source changes are required by this task.

- [ ] **Step 1: Bump the six `Ignixa.*` package versions**

In `Directory.Packages.props`, change:

```xml
    <PackageVersion Include="Ignixa.TestScript" Version="0.5.13-beta" />

    <!-- Generates fixture resources for the canonical suites' fhirfakes markers -->
    <PackageVersion Include="Ignixa.TestScript.FhirFakes" Version="0.5.13-beta" />

    <!-- FHIRPath expression evaluation engine, powering the fhirpath-evaluator endpoints -->
    <PackageVersion Include="Ignixa.FhirPath" Version="0.5.13" />
    <PackageVersion Include="Ignixa.Serialization" Version="0.5.13" />
    <PackageVersion Include="Ignixa.Specification" Version="0.5.13" />

    <!-- Synthetic FHIR data generator (populations, clinical scenarios, single resources, edge-case fuzzing), powering the Fakes bench -->
    <PackageVersion Include="Ignixa.FhirFakes" Version="0.5.13" />
```

to:

```xml
    <PackageVersion Include="Ignixa.TestScript" Version="0.6.4-beta" />

    <!-- Generates fixture resources for the canonical suites' fhirfakes markers -->
    <PackageVersion Include="Ignixa.TestScript.FhirFakes" Version="0.6.4-beta" />

    <!-- FHIRPath expression evaluation engine, powering the fhirpath-evaluator endpoints -->
    <PackageVersion Include="Ignixa.FhirPath" Version="0.6.4" />
    <PackageVersion Include="Ignixa.Serialization" Version="0.6.4" />
    <PackageVersion Include="Ignixa.Specification" Version="0.6.4" />

    <!-- Synthetic FHIR data generator (populations, clinical scenarios, single resources, edge-case fuzzing), powering the Fakes bench -->
    <PackageVersion Include="Ignixa.FhirFakes" Version="0.6.4" />
```

- [ ] **Step 2: Restore and build**

Run: `dotnet build Ignixa.Lab.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test Ignixa.Lab.sln -c Debug --no-build`
Expected: all tests pass (on `main`, with no in-flight suite-count work, there should be zero failures — if `SuiteCatalogTests` suite-count assertions fail, that's pre-existing suite content drift unrelated to this task; investigate before proceeding rather than assuming it's expected).

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props
git commit -m "Bump Ignixa.* package references to 0.6.4"
```

---

### Task 2: Fix FHIR release-label gating in TestScriptRunner

**Superseded post-merge:** after this task shipped and passed review, it was redesigned per user request — `NormalizeFhirVersionForEngine` was removed in favor of resolving the engine's `fhirVersion` from the target's own declared `CapabilityStatement.fhirVersion` (already fetched for `requiresCapability` gating), falling back to the request/default value only when unavailable. See `ResolveFhirVersion` in `TestScriptRunner.cs` and the corresponding note in the design spec's §2. The task text below reflects the original, since-replaced approach.

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Execution/TestScriptRunnerTests.cs`

**Interfaces:**
- Produces: `TestScriptRunner.NormalizeFhirVersionForEngine(string fhirVersion) -> string` (private static) — no external consumers, but its existence and mapping table matter for Task 8's manual verification.

**Context:** The 0.6.4 engine's `TestScriptEvaluator.IsVersionCompatible` parses the caller-supplied FHIR version with `SemVersion.TryParse`, which fails on release labels like `"R4"`. Without normalizing to numeric form first, every version-gated test is skipped regardless of #301's new granular matching. `RunRequest.FhirVersion` (and `IgnixaLabOptions.DefaultFhirVersion`) carry release labels (`"R4"`, `"R4B"`, etc.) from the frontend.

- [ ] **Step 1: Write the failing tests**

Add to `backend/test/Ignixa.Lab.Functions.Tests/Execution/TestScriptRunnerTests.cs` (inside the existing `TestScriptRunnerTests` class, alongside the existing `VersionGatedDefinition` fixture already used by other tests in this file — check whether it exists first; if not, add it exactly as shown):

```csharp
private static TestScriptDefinition VersionGatedDefinition(params string[] fhirVersions) => new()
{
    Metadata = new TestScriptMetadata { Name = "VersionGated" },
    Tests =
    [
        new TestPhaseDefinition
        {
            Name = "ReadPatient",
            FhirVersions = fhirVersions,
            Actions = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }]
        }
    ]
};

[Theory]
[InlineData("R4", "4.0")]
[InlineData("r4", "4.0")]
[InlineData("R4B", "4.3")]
[InlineData("R5", "5.0")]
[InlineData("STU3", "3.0")]
[InlineData("R3", "3.0")]
[InlineData("R6", "6.0")]
public async Task GivenSuiteGatedToNumericFhirVersion_WhenRunRequestsMatchingReleaseLabel_ThenTestRunsAndReportUsesNormalizedVersion(
    string requestedFhirVersion, string declaredNumericVersion)
{
    var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
    var runner = new TestScriptRunner(
        new FakeSuiteCatalog("versioned.json", VersionGatedDefinition(declaredNumericVersion)),
        new FakeEvaluatorFactory(provider),
        new CapabilityStatementFetcher(
            new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
            Options.Create(new IgnixaLabOptions())),
        Options.Create(new IgnixaLabOptions()),
        NullLogger<TestScriptRunner>.Instance);

    var outcome = await runner.RunAsync(
        new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"], FhirVersion = requestedFhirVersion },
        CancellationToken.None);

    outcome.IsValid.Should().BeTrue();
    // Regression: previously the release label ("R4") was passed straight to the
    // engine, which compares it verbatim against the suite's numeric fhirVersions
    // extension ("4.0"), so the test was always skipped.
    outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
    // The report must carry the numeric form, not the raw label: it's interchangeable
    // with the ignixa-fhir conformance/latest.json artifact, which is always numeric.
    outcome.Report.FhirVersion.Should().Be(declaredNumericVersion);
    provider.CallCount.Should().Be(1);
}

[Fact]
public async Task GivenSuiteGatedToIncompatibleFhirVersion_WhenRun_ThenTestIsStillSkipped()
{
    var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
    var runner = new TestScriptRunner(
        new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("5.0")),
        new FakeEvaluatorFactory(provider),
        new CapabilityStatementFetcher(
            new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
            Options.Create(new IgnixaLabOptions())),
        Options.Create(new IgnixaLabOptions()),
        NullLogger<TestScriptRunner>.Instance);

    var outcome = await runner.RunAsync(
        new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"], FhirVersion = "R4" },
        CancellationToken.None);

    outcome.IsValid.Should().BeTrue();
    outcome.Report!.Results[0].Status.Should().Be(ConformanceStatus.Skipped);
    provider.CallCount.Should().Be(0);
}
```

If `FakeSuiteCatalog`, `RecordingRequestProvider`, `FakeEvaluatorFactory`, `FixedResponseHttpClientFactory`, `CapabilityStatementWithoutReindex`, or `TargetUrl` are not already defined in this test file (they should be — this file already has other `TestScriptRunner` tests using them), read the existing tests in the file first and match their exact names/shapes rather than inventing new ones.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~TestScriptRunnerTests" -c Debug`
Expected: the new `Theory`/`Fact` tests FAIL — every case reports `Status == Skipped` because `"R4"` never matches `"4.0"`.

- [ ] **Step 3: Add the normalization**

In `backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs`, change:

```csharp
        var fhirVersion = string.IsNullOrWhiteSpace(request.FhirVersion)
            ? _options.DefaultFhirVersion
            : request.FhirVersion!;

        var (capabilityStatement, capabilityWarning) = await FetchCapabilityStatementAsync(target, cancellationToken);
```

to:

```csharp
        var fhirVersion = string.IsNullOrWhiteSpace(request.FhirVersion)
            ? _options.DefaultFhirVersion
            : request.FhirVersion!;
        var engineFhirVersion = NormalizeFhirVersionForEngine(fhirVersion);

        var (capabilityStatement, capabilityWarning) = await FetchCapabilityStatementAsync(target, cancellationToken);
```

then change:

```csharp
            results.AddRange(await ExecuteJobAsync(evaluator, job, fhirVersion, capabilityStatement, cancellationToken));
```

to:

```csharp
            results.AddRange(await ExecuteJobAsync(evaluator, job, engineFhirVersion, capabilityStatement, cancellationToken));
```

then change:

```csharp
        var report = new ConformanceReport(
            Impl: "ignixa-lab",
            Target: target.ToString(),
            FhirVersion: fhirVersion,
            StartedAt: startedAt,
```

to:

```csharp
        var report = new ConformanceReport(
            Impl: "ignixa-lab",
            Target: target.ToString(),
            // Numeric form, not the raw request label: this field must stay
            // identical in shape and value convention to the ignixa-fhir
            // conformance/latest.json artifact, which is always numeric
            // (e.g. "4.0"), never "R4".
            FhirVersion: engineFhirVersion,
            StartedAt: startedAt,
```

Then add this private static method right after `RunAsync` (before `FetchCapabilityStatementAsync`):

```csharp
    /// <summary>
    /// Maps a FHIR release label (for example <c>"R4"</c>, <c>"R4B"</c>) to the
    /// numeric major.minor form (for example <c>"4.0"</c>, <c>"4.3"</c>) used by
    /// the bundled suites' <c>http://ignixa.io/testscript/fhirVersions</c> gating
    /// extension. The 0.6.4 engine matches the requested version against a test's
    /// declared versions via <c>SemVersion.TryParse</c>, which fails on a release
    /// label — so passing one straight through causes every version-gated test
    /// to be skipped even when the label and number refer to the same release.
    /// Values already in numeric form, or not recognized as a release label,
    /// pass through unchanged.
    /// </summary>
    private static string NormalizeFhirVersionForEngine(string fhirVersion) => fhirVersion.ToUpperInvariant() switch
    {
        "STU3" or "R3" => "3.0",
        "R4" => "4.0",
        "R4B" => "4.3",
        "R5" => "5.0",
        "R6" => "6.0",
        _ => fhirVersion
    };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~TestScriptRunnerTests" -c Debug`
Expected: all pass, including pre-existing tests in this file.

- [ ] **Step 5: Run the full backend suite**

Run: `dotnet test Ignixa.Lab.sln -c Debug`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Execution/TestScriptRunner.cs backend/test/Ignixa.Lab.Functions.Tests/Execution/TestScriptRunnerTests.cs
git commit -m "Normalize FHIR release labels to numeric versions for TestScript gating"
```

---

### Task 3: Consolidate Fakes discovery onto ScenarioCatalog/ObservationStateCatalog

**Files:**
- Delete: `backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs`
- Delete: `backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs`
- Delete: `backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ScenarioDiscoveryTests.cs`
- Delete: `backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ObservationStateDiscoveryTests.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Program.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`

**Interfaces:**
- Consumes: `Ignixa.FhirFakes.Scenarios.ScenarioCatalog.GetAll() -> IReadOnlyList<DiscoveredScenario>`, `.Find(string) -> DiscoveredScenario?`, `.Invoke(DiscoveredScenario, IFhirSchemaProvider, IReadOnlyDictionary<string, object?>?) -> ScenarioContext` (throws `ScenarioInvocationException`); `Ignixa.FhirFakes.Scenarios.States.ObservationStateCatalog.GetNames() -> IReadOnlyList<string>`, `.TryCreate(string, out ObservationState?) -> bool`; `DiscoveredScenario.{Id, Category, Title, Description, Domain, Parameters}`; `DiscoveredScenarioParameter.{Name, Type, DefaultValue, HasDefaultValue}.TryParseValue(string, out object?, out string?) -> bool`.
- Produces: `FakesService(SchemaProviderFactory)` (two fewer constructor parameters), `FakesFunctions(ILogger<FakesFunctions>, SchemaProviderFactory, FakesService)` (two fewer constructor parameters) — Task 5 and Task 6 build on this constructor shape.

**Context:** `ScenarioCatalog.Invoke` takes `IReadOnlyDictionary<string, object?>?`, not `IReadOnlyDictionary<string, JsonElement>?` — `FakesService` must convert each override's `JsonElement` into the target parameter's CLR type using `DiscoveredScenarioParameter.TryParseValue` (invariant-culture, Min/Max-validated) before calling `Invoke`, rather than the old ad hoc `ConvertParameter` switch. This also removes `FakesService`'s home-grown parameter conversion entirely, since `TryParseValue` already covers int/decimal/bool/enum/string and reports an actionable `failureReason` on the range/type mismatches the existing tests exercise (`GenerateScenario_FractionalNumericParameter_ReturnsBadRequest`, `GenerateScenario_OversizedNumericParameter_ReturnsBadRequest`, `GenerateScenario_CapitalizedParameterKey_RoutesToTargetParameter` — verified against the actual `DiscoveredScenarioParameter.TryParseValue`/`ScenarioParameterBinder.BuildArguments` source in the 0.6.4 tag that these tests' expected behavior is preserved).

- [ ] **Step 1: Delete the reflection classes and their tests**

```bash
git rm backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs
git rm backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs
git rm backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ScenarioDiscoveryTests.cs
git rm backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ObservationStateDiscoveryTests.cs
```

- [ ] **Step 2: Update `FakesService.cs`**

Change the class declaration and constructor from:

```csharp
public sealed class FakesService(
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery)
{
```

to:

```csharp
public sealed class FakesService(SchemaProviderFactory schemaProviderFactory)
{
```

Change the `using` list at the top of the file to add:

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
```

Change `GenerateScenario` from:

```csharp
    public JsonObject? GenerateScenario(
        string fhirVersion,
        string scenarioId,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        string? tag,
        bool resolvedReferences)
    {
        var scenario = scenarioDiscovery.Find(scenarioId);
        if (scenario is null)
        {
            return null;
        }

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        ScenarioContext context;
        try
        {
            context = scenarioDiscovery.Invoke(scenario, schemaProvider, parameters);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.Reflection.TargetInvocationException
            or FormatException
            or OverflowException
            or ArgumentException)
        {
            var reason = ex is System.Reflection.TargetInvocationException tie
                ? tie.InnerException?.Message ?? tie.Message
                : ex.Message;
            throw new InvalidScenarioParametersException($"Invalid scenario parameters: {reason}", ex);
        }
```

to:

```csharp
    public JsonObject? GenerateScenario(
        string fhirVersion,
        string scenarioId,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        string? tag,
        bool resolvedReferences)
    {
        var scenario = ScenarioCatalog.Find(scenarioId);
        if (scenario is null)
        {
            return null;
        }

        var overrides = ConvertParameterOverrides(scenario, parameters);

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        ScenarioContext context;
        try
        {
            context = ScenarioCatalog.Invoke(scenario, schemaProvider, overrides);
        }
        catch (ScenarioInvocationException ex)
        {
            throw new InvalidScenarioParametersException($"Invalid scenario parameters: {ex.Message}", ex);
        }
```

Add this new private static method (near `BuildResource`, since it serves the same "convert loosely-typed HTTP input into the library's strongly-typed shape" role):

```csharp
    /// <summary>
    /// Converts the request's raw JSON parameter overrides into the strongly-typed values
    /// <see cref="ScenarioCatalog.Invoke"/> expects, using each parameter's own
    /// <see cref="DiscoveredScenarioParameter.TryParseValue(string, out object?, out string?)"/> —
    /// which already applies invariant-culture parsing and the scenario's declared Min/Max range,
    /// so this method no longer needs its own type-conversion switch. An override key with no
    /// matching parameter is ignored here and left for <see cref="ScenarioCatalog.Invoke"/>'s own
    /// binder to silently skip, matching this method's prior behavior of not pre-validating names.
    /// </summary>
    private static IReadOnlyDictionary<string, object?>? ConvertParameterOverrides(
        DiscoveredScenario scenario,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var parameterMetadata = scenario.Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, jsonValue) in parameters)
        {
            if (!parameterMetadata.TryGetValue(key, out var parameter))
            {
                continue;
            }

            if (jsonValue.ValueKind == JsonValueKind.Null)
            {
                overrides[parameter.Name] = null;
                continue;
            }

            var rawValue = jsonValue.ValueKind == JsonValueKind.String ? jsonValue.GetString()! : jsonValue.GetRawText();
            if (!parameter.TryParseValue(rawValue, out var value, out var failureReason))
            {
                throw new InvalidScenarioParametersException(
                    $"Invalid scenario parameters: parameter '{parameter.Name}' {failureReason ?? $"could not be converted from '{rawValue}'."}");
            }

            overrides[parameter.Name] = value;
        }

        return overrides;
    }
```

Change the `BuildResource` observation-state branch from:

```csharp
        if (string.Equals(resourceType, "Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(observationState))
        {
            var state = observationStateDiscovery.Create(observationState)
                ?? throw new InvalidOperationException(
                    $"Observation state '{observationState}' passed validation but ObservationStateDiscovery.Create returned null.");

            var context = new ScenarioBuilder(schemaProvider).WithPatient().AddObservation(state).Build();
```

to:

```csharp
        if (string.Equals(resourceType, "Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(observationState))
        {
            if (!ObservationStateCatalog.TryCreate(observationState, out var state))
            {
                throw new InvalidOperationException(
                    $"Observation state '{observationState}' passed validation but ObservationStateCatalog.TryCreate returned false.");
            }

            var context = new ScenarioBuilder(schemaProvider).WithPatient().AddObservation(state).Build();
```

- [ ] **Step 3: Update `FakesFunctions.cs`**

Change the class declaration from:

```csharp
public sealed class FakesFunctions(
    ILogger<FakesFunctions> logger,
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery,
    FakesService fakesService)
{
```

to:

```csharp
public sealed class FakesFunctions(
    ILogger<FakesFunctions> logger,
    SchemaProviderFactory schemaProviderFactory,
    FakesService fakesService)
{
```

Add to the `using` list:

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
```

In `GetMetadata`, change:

```csharp
            Scenarios = scenarioDiscovery.All().Select(ToScenarioMetadata).ToList(),
```

to:

```csharp
            Scenarios = ScenarioCatalog.GetAll().Select(ToScenarioMetadata).ToList(),
```

and change:

```csharp
            ObservationStates = observationStateDiscovery.Names(),
```

to:

```csharp
            ObservationStates = ObservationStateCatalog.GetNames(),
```

In `GenerateResource`, change:

```csharp
        if (!string.IsNullOrWhiteSpace(resourceRequest.ObservationState)
            && !observationStateDiscovery.Names().Contains(resourceRequest.ObservationState, StringComparer.OrdinalIgnoreCase))
```

to:

```csharp
        if (!string.IsNullOrWhiteSpace(resourceRequest.ObservationState)
            && !ObservationStateCatalog.GetNames().Contains(resourceRequest.ObservationState, StringComparer.OrdinalIgnoreCase))
```

Change `ToScenarioMetadata` from:

```csharp
    private static ScenarioMetadata ToScenarioMetadata(DiscoveredScenario scenario) => new()
    {
        Id = scenario.Id,
        Parameters = scenario.Parameters.Select(parameter => new ScenarioParameterMetadata
        {
            Name = parameter.Name!,
            Type = parameter.ParameterType.Name,
            DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
        }).ToList(),
    };
```

to:

```csharp
    private static ScenarioMetadata ToScenarioMetadata(DiscoveredScenario scenario) => new()
    {
        Id = scenario.Id,
        Category = scenario.Category,
        Domain = scenario.Domain?.ToString(),
        Parameters = scenario.Parameters.Select(parameter => new ScenarioParameterMetadata
        {
            Name = parameter.Name,
            Type = parameter.Type.Name,
            DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
        }).ToList(),
    };
```

(`Category`/`Domain` on `ScenarioMetadata` are added in Task 4 — this step's edit will not compile until Task 4 lands; that's expected since these two tasks touch the same type. Do Task 4 immediately after this step, before running the build.)

- [ ] **Step 4: Update `Program.cs`**

Remove these two lines:

```csharp
builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Fakes.ScenarioDiscovery>();
builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Fakes.ObservationStateDiscovery>();
```

(leave `builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Fakes.FakesService>();` as-is — DI resolves its new, shorter constructor automatically).

- [ ] **Step 5: Update `FakesFunctionsTests.cs`**

Change:

```csharp
    private static FakesFunctions CreateFunctions() => new(
        NullLogger<FakesFunctions>.Instance,
        new SchemaProviderFactory(),
        new ScenarioDiscovery(),
        new ObservationStateDiscovery(),
        new FakesService(new SchemaProviderFactory(), new ScenarioDiscovery(), new ObservationStateDiscovery()));
```

to:

```csharp
    private static FakesFunctions CreateFunctions() => new(
        NullLogger<FakesFunctions>.Instance,
        new SchemaProviderFactory(),
        new FakesService(new SchemaProviderFactory()));
```

Remove the now-unused `using Ignixa.Lab.Functions.Services.Fakes;` only if the file has no other reference to that namespace (it still constructs `FakesService`, which lives there, so keep the `using`).

- [ ] **Step 6: Do Task 4's `ScenarioMetadata` field additions now** (see Task 4, Step 1) so the solution compiles, then come back here.

- [ ] **Step 7: Build and run the full backend suite**

Run: `dotnet build Ignixa.Lab.sln -c Debug`
Expected: 0 errors.

Run: `dotnet test Ignixa.Lab.sln -c Debug --no-build`
Expected: every test in `FakesFunctionsTests.cs` still passes, including `GenerateScenario_FactoryRejectsArgument_ReturnsRealReasonNotReflectionBoilerplate`, `GenerateScenario_FractionalNumericParameter_ReturnsBadRequest`, `GenerateScenario_OversizedNumericParameter_ReturnsBadRequest`, `GenerateScenario_CapitalizedParameterKey_RoutesToTargetParameter`, `GenerateScenario_CapitalizedNumericParameterKey_Succeeds`.

- [ ] **Step 8: Commit**

```bash
git add -A backend/src/Ignixa.Lab.Functions/Services/Fakes backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/src/Ignixa.Lab.Functions/Program.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs
git commit -m "Replace reflection-based Fakes discovery with ScenarioCatalog/ObservationStateCatalog"
```

---

### Task 4: Surface scenario Category/Domain metadata end-to-end

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`
- Modify: `frontend/src/benches/fakes/fakesTypes.ts`
- Modify: `frontend/src/benches/fakes/scenarioDescriptions.ts`
- Modify: `frontend/src/benches/fakes/FakesBench.tsx`

**Interfaces:**
- Consumes: `ScenarioMetadata.{Category, Domain}` from Task 3.
- Produces: `describeScenario(scenarioId: string, category?: string | null) -> { group: string; label: string; blurb: string }` (signature change — both call sites in `FakesBench.tsx` are updated in this task).

- [ ] **Step 1: Add `Category`/`Domain` to the backend metadata contract**

In `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`, change:

```csharp
public sealed class ScenarioMetadata
{
    public required string Id { get; init; }
    public required IReadOnlyList<ScenarioParameterMetadata> Parameters { get; init; }
}
```

to:

```csharp
public sealed class ScenarioMetadata
{
    public required string Id { get; init; }
    /// <summary>Free-text grouping label from the library's <c>ScenarioAttribute.Category</c>, or null if unannotated.</summary>
    public string? Category { get; init; }
    /// <summary>Clinical specialty from the library's <c>ScenarioAttribute.Domain</c>, or null if undeclared.</summary>
    public string? Domain { get; init; }
    public required IReadOnlyList<ScenarioParameterMetadata> Parameters { get; init; }
}
```

- [ ] **Step 2: Mirror the fields on the frontend type**

In `frontend/src/benches/fakes/fakesTypes.ts`, change:

```typescript
export interface ScenarioMetadata {
  id: string;
  parameters: ScenarioParameterMetadata[];
}
```

to:

```typescript
export interface ScenarioMetadata {
  id: string;
  /** Free-text grouping label from the library's ScenarioAttribute.Category, or null if unannotated. */
  category: string | null;
  /** Clinical specialty from the library's ScenarioAttribute.Domain, or null if undeclared. */
  domain: string | null;
  parameters: ScenarioParameterMetadata[];
}
```

- [ ] **Step 3: Prefer the library's Category in `describeScenario`**

In `frontend/src/benches/fakes/scenarioDescriptions.ts`, change the function signature and body from:

```typescript
export function describeScenario(scenarioId: string): { group: string; label: string; blurb: string } {
  const curated = SCENARIO_DESCRIPTIONS[scenarioId];
  return {
    group: curated?.group ?? 'Scenario',
    label: humanize(scenarioId),
    blurb: curated?.blurb ?? 'Predefined clinical scenario.',
  };
}
```

to:

```typescript
export function describeScenario(
  scenarioId: string,
  category?: string | null,
): { group: string; label: string; blurb: string } {
  const curated = SCENARIO_DESCRIPTIONS[scenarioId];
  return {
    group: category ?? curated?.group ?? 'Scenario',
    label: humanize(scenarioId),
    blurb: curated?.blurb ?? 'Predefined clinical scenario.',
  };
}
```

Update the file's leading comment block to reflect that grouping now prefers the library's own `Category` over the curated map (the curated map remains the source for `blurb` text, and the `Scenario` fallback still applies when neither the library nor the curated map has a group).

- [ ] **Step 4: Pass `category` through both call sites in `FakesBench.tsx`**

Change:

```typescript
  const scenario = useMemo(() => metadata.scenarios.find((s) => s.id === scenarioId), [metadata.scenarios, scenarioId]);
  const description = describeScenario(scenarioId);

  const scenarioInfos = useMemo(
    () => metadata.scenarios.map((s) => ({ id: s.id, ...describeScenario(s.id) })),
    [metadata.scenarios],
  );
```

to:

```typescript
  const scenario = useMemo(() => metadata.scenarios.find((s) => s.id === scenarioId), [metadata.scenarios, scenarioId]);
  const description = describeScenario(scenarioId, scenario?.category);

  const scenarioInfos = useMemo(
    () => metadata.scenarios.map((s) => ({ id: s.id, ...describeScenario(s.id, s.category) })),
    [metadata.scenarios],
  );
```

- [ ] **Step 5: Build backend and frontend**

Run: `dotnet build Ignixa.Lab.sln -c Debug`
Expected: 0 errors (this unblocks Task 3's Step 7 if done in sequence).

Run (in `frontend/`): `npm run build`
Expected: builds clean, no TypeScript errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs frontend/src/benches/fakes/fakesTypes.ts frontend/src/benches/fakes/scenarioDescriptions.ts frontend/src/benches/fakes/FakesBench.tsx
git commit -m "Surface scenario Category/Domain metadata from ScenarioCatalog in the Fakes bench"
```

---

### Task 5: Theme-aware Maximum-density generation

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Models/Fakes/ResourceGenerationRequest.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`
- Modify: `frontend/src/benches/fakes/fakesTypes.ts`
- Modify: `frontend/src/benches/fakes/fakesApi.ts`
- Modify: `frontend/src/benches/fakes/FakesBench.tsx`

**Interfaces:**
- Consumes: `Ignixa.FhirFakes.ClinicalDomain` enum (`Unspecified`, `FamilyMedicine`, `InternalMedicine`, `Pediatrics`, `Cardiology`, `EmergencyMedicine`, `GeneralSurgery`, `ObstetricsGynecology`, `Psychiatry`, `Neurology`, `OrthopedicSurgery`, `Dermatology`, `Ophthalmology`, `Radiology`, `Anesthesiology`, `Pathology`, `Oncology`, `Pulmonology`, `Gastroenterology`, `Endocrinology`, `Nephrology`, `Urology`); `SchemaBasedFhirResourceFaker.Theme` (`ClinicalDomain?` settable property).
- Produces: `FakesMetadataResponse.ClinicalDomains -> IReadOnlyList<string>`; `ResourceGenerationRequest.Theme -> string?`.

- [ ] **Step 1: Write the failing backend test**

Add to `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`:

```csharp
[Fact]
public async Task GenerateResource_UnknownTheme_ReturnsBadRequest()
{
    var functions = CreateFunctions();
    var request = BuildJsonPostRequest(new { resourceType = "Patient", density = "Maximum", theme = "NotARealTheme" });

    var result = await functions.GenerateResource(request, CancellationToken.None);

    result.Should().BeOfType<BadRequestObjectResult>();
}

[Fact]
public async Task GenerateResource_MaximumDensityWithTheme_Succeeds()
{
    var functions = CreateFunctions();
    var request = BuildJsonPostRequest(new { resourceType = "Condition", density = "Maximum", theme = "Cardiology", seed = 1 });

    var result = await functions.GenerateResource(request, CancellationToken.None);

    result.Should().BeOfType<OkObjectResult>();
}
```

Also add to the `GetMetadata_...` test:

```csharp
        metadata.ClinicalDomains.Should().Contain("Cardiology");
```

(append this assertion inside `GetMetadata_ReturnsPopulationStatesScenariosAndEdgeCaseFamilies`, after the existing `metadata.LibraryVersion...` line.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~FakesFunctionsTests" -c Debug`
Expected: compile error (`theme` isn't a recognized property yet, `ClinicalDomains` doesn't exist) — that's the expected "fails" state for a request-shape addition; proceed to implement.

- [ ] **Step 3: Add `Theme` to the request model**

In `backend/src/Ignixa.Lab.Functions/Models/Fakes/ResourceGenerationRequest.cs`, add a property:

```csharp
namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class ResourceGenerationRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string ResourceType { get; init; }
    public int Seed { get; init; } = 42;
    public string Density { get; init; } = "Minimal";
    public string? Theme { get; init; }
    public string? FirstName { get; init; }
    public string? FamilyName { get; init; }
    public string? City { get; init; }
    public string? ObservationState { get; init; }
    public IReadOnlyList<string>? EdgeCaseSelectors { get; init; }
    public bool IncludeInvalid { get; init; }
}
```

- [ ] **Step 4: Add `ClinicalDomains` to the metadata response**

In `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`, add a property to `FakesMetadataResponse`:

```csharp
    public required IReadOnlyList<string> PatientCities { get; init; }
    /// <summary>Clinical specialty names usable as a Maximum-density generation Theme (see Ignixa.FhirFakes.ClinicalDomain), excluding "Unspecified".</summary>
    public required IReadOnlyList<string> ClinicalDomains { get; init; }
```

(add this line right after the existing `PatientCities` property.)

- [ ] **Step 5: Thread `Theme` through `FakesService`**

Change `GenerateResource`'s signature and `BuildResource` call in `FakesService.cs` from:

```csharp
    public JsonObject GenerateResource(
        string fhirVersion,
        string resourceType,
        int seed,
        string density,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState,
        IReadOnlyList<string>? edgeCaseSelectors,
        bool includeInvalid)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var generationDensity = Enum.TryParse<GenerationDensity>(density, ignoreCase: true, out var parsedDensity)
            ? parsedDensity
            : GenerationDensity.Minimal;

        var resource = BuildResource(schemaProvider, resourceType, seed, generationDensity, firstName, familyName, city, observationState);
```

to:

```csharp
    public JsonObject GenerateResource(
        string fhirVersion,
        string resourceType,
        int seed,
        string density,
        string? theme,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState,
        IReadOnlyList<string>? edgeCaseSelectors,
        bool includeInvalid)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var generationDensity = Enum.TryParse<GenerationDensity>(density, ignoreCase: true, out var parsedDensity)
            ? parsedDensity
            : GenerationDensity.Minimal;
        ClinicalDomain? clinicalTheme = !string.IsNullOrWhiteSpace(theme) && Enum.TryParse<ClinicalDomain>(theme, ignoreCase: true, out var parsedTheme)
            ? parsedTheme
            : null;

        var resource = BuildResource(schemaProvider, resourceType, seed, generationDensity, clinicalTheme, firstName, familyName, city, observationState);
```

Change `BuildResource`'s signature (add the `theme` parameter) and the generic-faker fallback line at the bottom from:

```csharp
    private Ignixa.Serialization.SourceNodes.ResourceJsonNode BuildResource(
        Ignixa.Abstractions.IFhirSchemaProvider schemaProvider,
        string resourceType,
        int seed,
        GenerationDensity density,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState)
    {
```

to:

```csharp
    private Ignixa.Serialization.SourceNodes.ResourceJsonNode BuildResource(
        Ignixa.Abstractions.IFhirSchemaProvider schemaProvider,
        string resourceType,
        int seed,
        GenerationDensity density,
        ClinicalDomain? theme,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState)
    {
```

and change:

```csharp
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density };
        return faker.Generate(resourceType);
```

to:

```csharp
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density, Theme = theme };
        return faker.Generate(resourceType);
```

- [ ] **Step 6: Validate `theme` and pass it through in `FakesFunctions.cs`**

In `GenerateResource`, after the existing `Density` validation block:

```csharp
        if (!Enum.TryParse<GenerationDensity>(resourceRequest.Density, ignoreCase: true, out _))
        {
            return new BadRequestObjectResult(new
            {
                error = $"Unknown density '{resourceRequest.Density}'. Supported: {string.Join(", ", Enum.GetNames<GenerationDensity>())}.",
            });
        }
```

add:

```csharp
        if (!string.IsNullOrWhiteSpace(resourceRequest.Theme) && !Enum.TryParse<ClinicalDomain>(resourceRequest.Theme, ignoreCase: true, out _))
        {
            return new BadRequestObjectResult(new
            {
                error = $"Unknown theme '{resourceRequest.Theme}'. Supported: {string.Join(", ", Enum.GetNames<ClinicalDomain>().Where(name => name != nameof(ClinicalDomain.Unspecified)))}.",
            });
        }
```

Add `using Ignixa.FhirFakes;` to the top of `FakesFunctions.cs` if not already present (it already imports `Ignixa.FhirFakes` for `EdgeCaseCatalog`/`PopulationGenerator`, so `ClinicalDomain` resolves without a new using).

Update the `fakesService.GenerateResource(...)` call:

```csharp
            var result = fakesService.GenerateResource(
                resourceRequest.FhirVersion,
                resourceRequest.ResourceType,
                resourceRequest.Seed,
                resourceRequest.Density,
                resourceRequest.FirstName,
                resourceRequest.FamilyName,
                resourceRequest.City,
                resourceRequest.ObservationState,
                resourceRequest.EdgeCaseSelectors,
                resourceRequest.IncludeInvalid);
```

to:

```csharp
            var result = fakesService.GenerateResource(
                resourceRequest.FhirVersion,
                resourceRequest.ResourceType,
                resourceRequest.Seed,
                resourceRequest.Density,
                resourceRequest.Theme,
                resourceRequest.FirstName,
                resourceRequest.FamilyName,
                resourceRequest.City,
                resourceRequest.ObservationState,
                resourceRequest.EdgeCaseSelectors,
                resourceRequest.IncludeInvalid);
```

In `GetMetadata`, add to the `FakesMetadataResponse` object initializer (after `PatientCities`):

```csharp
            ClinicalDomains = Enum.GetNames<ClinicalDomain>().Where(name => name != nameof(ClinicalDomain.Unspecified)).ToList(),
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~FakesFunctionsTests" -c Debug`
Expected: all pass, including the two new tests and the updated metadata assertion.

- [ ] **Step 8: Add the Theme field to the frontend API/types**

In `frontend/src/benches/fakes/fakesTypes.ts`, add to `FakesMetadata`:

```typescript
  /** Clinical specialty names usable as a Maximum-density generation Theme, excluding "Unspecified". */
  clinicalDomains: string[];
```

(after `patientCities`).

In `frontend/src/benches/fakes/fakesApi.ts`, add `theme?: string` to `generateResource`'s body parameter type:

```typescript
export function generateResource(
  body: {
    fhirVersion: string;
    resourceType: string;
    seed: number;
    density: string;
    theme?: string;
    firstName?: string;
    familyName?: string;
    city?: string;
    observationState?: string;
    edgeCaseSelectors?: string[];
    includeInvalid: boolean;
  },
  signal?: AbortSignal,
): Promise<ResourceResult> {
```

- [ ] **Step 9: Add the Theme selector to `ResourcePanel` in `FakesBench.tsx`**

Add state, near the existing `density` state (`const [density, setDensity] = useState(initialState?.density ?? 'Minimal');`):

```typescript
  const [theme, setTheme] = useState(initialState?.theme ?? '');
```

Add `theme` to the `onShareStateChange` payload object and its dependency array (both already list every other piece of resource-panel state — add `theme` alongside `density` in both places).

Add `theme: theme || undefined,` to the `generateResource({...})` call, alongside the existing `density,` line.

Add the selector UI right after the existing density `Pills`:

```typescript
          <span style={sectionLabelStyle}>Generation density</span>
          <Pills items={DENSITY_ITEMS} activeId={density} onChange={setDensity} />

          {density === 'Maximum' ? (
            <>
              <span style={sectionLabelStyle}>Theme · optional, keeps coded fields clinically coherent</span>
              <select value={theme} onChange={(event) => setTheme(event.target.value)} style={monoInputStyle}>
                <option value="">Random per generation</option>
                {metadata.clinicalDomains.map((domain) => (
                  <option key={domain} value={domain}>
                    {domain}
                  </option>
                ))}
              </select>
            </>
          ) : null}
```

- [ ] **Step 10: Build frontend**

Run (in `frontend/`): `npm run build`
Expected: builds clean.

- [ ] **Step 11: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs frontend/src/benches/fakes/fakesTypes.ts frontend/src/benches/fakes/fakesApi.ts frontend/src/benches/fakes/FakesBench.tsx
git commit -m "Add Theme-aware Maximum-density generation to the Fakes bench"
```

---

### Task 6: Workflow Scenario Pack backend endpoint

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Models/Fakes/WorkflowRequest.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`

**Interfaces:**
- Consumes: `Ignixa.FhirFakes.Workflow.WorkflowScenarioCatalog.{GetAll, Find, Invoke}` (same shape as `ScenarioCatalog` but returns `WorkflowScenarioResult`); `WorkflowScenarioOptions { Seed, Clock, Tag }`; `WorkflowScenarioResult { Graph, Manifest }`; `ResourceGraph.AllResources -> IReadOnlyList<ResourceJsonNode>`; `Ignixa.FhirFakes.ResourceBundleComposer.{ToTransactionBundle, ToBatchBundle}` (same composer `ScenarioContext.ToBundle`/`ToBatchBundle` already delegate to, verified against the 0.6.4 source).
- Produces: `FakesService.GenerateWorkflow(string fhirVersion, string packId, IReadOnlyDictionary<string, JsonElement>? parameters, int? seed, string? tag, bool resolvedReferences) -> JsonObject?`; `POST /api/fakes/workflow`; `FakesMetadataResponse.WorkflowPacks -> IReadOnlyList<ScenarioMetadata>`.

**Scope:** this is the MVP vertical slice from the design — it mirrors `/fakes/scenario`'s transaction/batch bundle shape. Full FHIR searchset paging (`SearchsetBundleComposer`) is explicitly out of scope; the 0.6.4 library's `WorkflowScenarioResult.Graph.AllResources` is bundled the same way `ScenarioContext.AllResources` already is.

- [ ] **Step 1: Write the failing test**

Add to `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`:

```csharp
[Fact]
public async Task GenerateWorkflow_DailyAppointmentScheduleWithTag_StampsTagOnEveryResource()
{
    var functions = CreateFunctions();
    var request = BuildJsonPostRequest(new { packId = "DailyAppointmentSchedule", tag = "test-run-456", parameters = new { appointmentCount = 2 } });

    var result = await functions.GenerateWorkflow(request, CancellationToken.None);

    var ok = result.Should().BeOfType<OkObjectResult>().Subject;
    var body = JsonSerializer.Serialize(ok.Value);
    using var doc = JsonDocument.Parse(body);
    var resources = doc.RootElement.GetProperty("resources");
    resources.GetArrayLength().Should().BeGreaterThan(0);
    foreach (var resource in resources.EnumerateArray())
    {
        resource.GetProperty("meta").GetProperty("tag")[0].GetProperty("code").GetString().Should().Be("test-run-456");
    }
}

[Fact]
public async Task GenerateWorkflow_UnknownPackId_ReturnsBadRequest()
{
    var functions = CreateFunctions();
    var request = BuildJsonPostRequest(new { packId = "NotARealPack" });

    var result = await functions.GenerateWorkflow(request, CancellationToken.None);

    result.Should().BeOfType<BadRequestObjectResult>();
}

[Fact]
public async Task GenerateWorkflow_ResolvedReferencesTrue_ReturnsBatchBundle()
{
    var functions = CreateFunctions();
    var request = BuildJsonPostRequest(new { packId = "DailyAppointmentSchedule", resolvedReferences = true, parameters = new { appointmentCount = 1 } });

    var result = await functions.GenerateWorkflow(request, CancellationToken.None);

    var ok = result.Should().BeOfType<OkObjectResult>().Subject;
    var body = JsonSerializer.Serialize(ok.Value);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("bundle").GetProperty("type").GetString().Should().Be("batch");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~FakesFunctionsTests" -c Debug`
Expected: compile error (`GenerateWorkflow` doesn't exist yet).

- [ ] **Step 3: Add the request model**

Create `backend/src/Ignixa.Lab.Functions/Models/Fakes/WorkflowRequest.cs`:

```csharp
using System.Text.Json;

namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class WorkflowRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string PackId { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }
    public int? Seed { get; init; }
    public string? Tag { get; init; }
    public bool ResolvedReferences { get; init; }
}
```

- [ ] **Step 4: Add `WorkflowPacks` to the metadata response**

In `FakesMetadataResponse.cs`, add to `FakesMetadataResponse` (after `ClinicalDomains`, added in Task 5):

```csharp
    /// <summary>Discoverable workflow scenario packs (e.g. "DailyAppointmentSchedule"), same shape as <see cref="Scenarios"/>.</summary>
    public required IReadOnlyList<ScenarioMetadata> WorkflowPacks { get; init; }
```

- [ ] **Step 5: Add `FakesService.GenerateWorkflow`**

Add `using Ignixa.FhirFakes.Workflow;` to `FakesService.cs`'s using list. Add this method (near `GenerateScenario`, reusing `ConvertParameterOverrides` from Task 3 — note it takes a `DiscoveredScenario`, and `WorkflowScenarioCatalog.Find` also returns `DiscoveredScenario`, so no changes to that helper are needed):

```csharp
    /// <summary>Returns null when <paramref name="packId"/> doesn't match a discovered workflow scenario pack.</summary>
    public JsonObject? GenerateWorkflow(
        string fhirVersion,
        string packId,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int? seed,
        string? tag,
        bool resolvedReferences)
    {
        var pack = WorkflowScenarioCatalog.Find(packId);
        if (pack is null)
        {
            return null;
        }

        var overrides = ConvertParameterOverrides(pack, parameters);

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var options = new WorkflowScenarioOptions { Seed = seed, Tag = tag };

        WorkflowScenarioResult result;
        try
        {
            result = WorkflowScenarioCatalog.Invoke(pack, schemaProvider, options, overrides);
        }
        catch (ScenarioInvocationException ex)
        {
            throw new InvalidScenarioParametersException($"Invalid scenario parameters: {ex.Message}", ex);
        }

        var resources = result.Graph.AllResources;
        var bundle = resolvedReferences
            ? ResourceBundleComposer.ToBatchBundle(resources)
            : ResourceBundleComposer.ToTransactionBundle(resources);

        var resourceNodes = resources.Select(r => JsonNode.Parse(r.SerializeToString())!).ToList();
        var bundleNode = JsonNode.Parse(bundle.SerializeToString())!.AsObject();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            foreach (var resourceNode in resourceNodes)
            {
                StampTag(resourceNode.AsObject(), tag);
            }

            StampBundleEntryTags(bundleNode, tag);
        }

        return new JsonObject
        {
            ["resources"] = new JsonArray(resourceNodes.ToArray()),
            ["bundle"] = bundleNode,
            ["resourceCountsByType"] = ToJsonObject(result.Manifest.ResourceCountsByType),
        };
    }
```

(`BundleJsonNode.SerializeToString()` — confirm this method exists on `BundleJsonNode` the same way it does on `ResourceJsonNode`; if the exact method name differs, use whatever `ScenarioContext.ToBundle()`'s callers in this same file already use to serialize a bundle to JSON — `GenerateScenario` already does `JsonNode.Parse(bundle.SerializeToString())!.AsObject()` two methods above this one, so match that exactly.)

- [ ] **Step 6: Add the `FakesWorkflow` function and metadata wiring**

In `FakesFunctions.cs`, add to `GetMetadata`'s `FakesMetadataResponse` initializer (after `ClinicalDomains`):

```csharp
            WorkflowPacks = WorkflowScenarioCatalog.GetAll().Select(ToScenarioMetadata).ToList(),
```

Add `using Ignixa.FhirFakes.Workflow;` to the top of the file.

Add the new HTTP function (place it after `GenerateScenario`, mirroring its structure exactly):

```csharp
    [Function("FakesWorkflow")]
    public async Task<IActionResult> GenerateWorkflow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/workflow")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        WorkflowRequest? workflowRequest;
        try
        {
            workflowRequest = await JsonSerializer.DeserializeAsync<WorkflowRequest>(
                request.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (workflowRequest is null || string.IsNullOrWhiteSpace(workflowRequest.PackId))
        {
            return new BadRequestObjectResult(new { error = "A 'packId' is required." });
        }

        if (!IsSupportedFhirVersion(workflowRequest.FhirVersion))
        {
            return UnsupportedFhirVersion(workflowRequest.FhirVersion);
        }

        JsonObject? result;
        try
        {
            result = fakesService.GenerateWorkflow(
                workflowRequest.FhirVersion,
                workflowRequest.PackId,
                workflowRequest.Parameters,
                workflowRequest.Seed,
                workflowRequest.Tag,
                workflowRequest.ResolvedReferences);
        }
        catch (InvalidScenarioParametersException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        if (result is null)
        {
            return new BadRequestObjectResult(new { error = $"Unknown packId '{workflowRequest.PackId}'." });
        }

        return new OkObjectResult(result);
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter "FullyQualifiedName~FakesFunctionsTests" -c Debug`
Expected: all pass, including the three new workflow tests.

- [ ] **Step 8: Run the full backend suite**

Run: `dotnet test Ignixa.Lab.sln -c Debug`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs
git commit -m "Add /api/fakes/workflow endpoint for Workflow Scenario Packs"
```

---

### Task 7: Workflow Scenario Pack frontend panel

**Files:**
- Modify: `frontend/src/benches/fakes/fakesTypes.ts`
- Modify: `frontend/src/benches/fakes/fakesApi.ts`
- Modify: `frontend/src/benches/fakes/FakesBench.tsx`

**Interfaces:**
- Consumes: `FakesMetadata.workflowPacks -> ScenarioMetadata[]` (Task 6); `ScenarioParameterControl` (existing, reused as-is); `Pills`, `Card`, `ErrorBanner`, `HighlightedJsonBlock`, `sectionLabelStyle`, `monoInputStyle`, `primaryButtonStyle`, `resultPreStyle`, `downloadJson` (all existing, from the same file/module `ScenarioPanel` already uses).
- Produces: `generateWorkflow(body, signal?) -> Promise<WorkflowResult>`; a new `'workflow'` `FakesMode`.

**Scope:** this MVP panel lists workflow packs (just `DailyAppointmentSchedule` in 0.6.4), lets the user set its parameters via the existing `ScenarioParameterControl`, generates, and shows the resulting bundle. It intentionally does not wire an `onSend`-to-other-bench action (Population/Scenario send single-patient payloads; a workflow result is a cross-patient graph that doesn't fit those benches' inputs) — that stays a follow-up if a concrete downstream need shows up.

- [ ] **Step 1: Add types**

In `frontend/src/benches/fakes/fakesTypes.ts`, add to `FakesMetadata` (after `clinicalDomains`, added in Task 5):

```typescript
  /** Discoverable workflow scenario packs (e.g. "DailyAppointmentSchedule"), same shape as `scenarios`. */
  workflowPacks: ScenarioMetadata[];
```

Add a new result type near `ScenarioResult`:

```typescript
export interface WorkflowResult {
  resources: Record<string, unknown>[];
  bundle: Record<string, unknown>;
  resourceCountsByType: Record<string, number>;
}
```

- [ ] **Step 2: Add the API function**

In `frontend/src/benches/fakes/fakesApi.ts`, add:

```typescript
export function generateWorkflow(
  body: {
    fhirVersion: string;
    packId: string;
    parameters?: Record<string, unknown>;
    seed?: number;
    tag?: string;
    resolvedReferences: boolean;
  },
  signal?: AbortSignal,
): Promise<WorkflowResult> {
  return request<WorkflowResult>('/api/fakes/workflow', { method: 'POST', body: JSON.stringify(body), signal });
}
```

Add `WorkflowResult` to the type-only import at the top of the file:

```typescript
import type { FakesMetadata, PopulationResult, ResourceResult, ScenarioResult, WorkflowResult } from './fakesTypes';
```

- [ ] **Step 3: Add the `'workflow'` mode**

In `FakesBench.tsx`, change:

```typescript
type FakesMode = 'population' | 'scenario' | 'resource';
```

to:

```typescript
type FakesMode = 'population' | 'scenario' | 'resource' | 'workflow';
```

Change:

```typescript
const MODE_ITEMS: PillItem<FakesMode>[] = [
  { id: 'population', label: 'Population' },
  { id: 'scenario', label: 'Scenario' },
  { id: 'resource', label: 'Resource' },
];
```

to:

```typescript
const MODE_ITEMS: PillItem<FakesMode>[] = [
  { id: 'population', label: 'Population' },
  { id: 'scenario', label: 'Scenario' },
  { id: 'resource', label: 'Resource' },
  { id: 'workflow', label: 'Workflow' },
];
```

Add `generateWorkflow` and `WorkflowResult` to the existing imports:

```typescript
import { generatePopulation, generateResource, generateScenario, generateWorkflow, getFakesMetadata } from './fakesApi';
import type {
  EdgeCaseFamilyMetadata,
  FakesMetadata,
  PopulationResult,
  ResourceResult,
  ScenarioResult,
  WorkflowResult,
} from './fakesTypes';
```

Add a render branch next to the existing three, in the component that switches on `mode` (alongside the existing `{mode === 'resource' ? <ResourcePanel .../> : null}` line):

```typescript
          {mode === 'workflow' ? <WorkflowPanel metadata={metadata} fhirVersion={fhirVersion} stacked={stacked} /> : null}
```

- [ ] **Step 4: Add the `WorkflowPanel` component**

Add this new function to `FakesBench.tsx` (place it after `ResourcePanel`'s closing brace, before the `ScenarioParameterControl` helper it reuses):

```typescript
function WorkflowPanel({
  metadata,
  fhirVersion,
  stacked,
}: {
  metadata: FakesMetadata;
  fhirVersion: string;
  stacked: boolean;
}) {
  const [packId, setPackId] = useState(metadata.workflowPacks[0]?.id ?? '');
  const [paramValues, setParamValues] = useState<Record<string, unknown>>({});
  const [tag, setTag] = useState('');
  const [resolvedReferences, setResolvedReferences] = useState(false);
  const [result, setResult] = useState<WorkflowResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const pack = useMemo(() => metadata.workflowPacks.find((p) => p.id === packId), [metadata.workflowPacks, packId]);

  const selectPack = (id: string) => {
    setPackId(id);
    setParamValues({});
  };

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generateWorkflow({ fhirVersion, packId, parameters: paramValues, tag: tag || undefined, resolvedReferences })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={twoColumnStyle(stacked, 'minmax(300px,32%)')}>
        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Workflow scenario pack</span>
          <Pills
            items={metadata.workflowPacks.map((p) => ({ id: p.id, label: p.id }))}
            activeId={packId}
            onChange={selectPack}
          />

          {pack?.parameters.map((param) => (
            <ScenarioParameterControl
              key={param.name}
              param={param}
              value={paramValues[param.name] ?? param.defaultValue}
              onChange={(value) => setParamValues((current) => ({ ...current, [param.name]: value }))}
            />
          ))}

          <span style={sectionLabelStyle}>Test-isolation tag · optional</span>
          <input value={tag} onChange={(event) => setTag(event.target.value)} placeholder="e.g. test-run-123" style={monoInputStyle} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, flex: 1 }}>
              <span style={{ fontSize: 12.5, fontWeight: 600 }}>Resolved references</span>
              <span style={{ fontSize: 10.5, color: 'var(--text4)' }}>batch bundle instead of transaction</span>
            </div>
            <Toggle checked={resolvedReferences} onChange={setResolvedReferences} ariaLabel="Resolved references" />
          </div>

          <button type="button" onClick={generate} disabled={isLoading || !packId} style={primaryButtonStyle}>
            {isLoading ? 'Generating…' : '⚡ Generate workflow'}
          </button>
        </Card>

        <Card style={{ minHeight: 360, minWidth: 0 }}>
          {error ? <ErrorBanner message={error} /> : null}
          {result ? (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.resources.length} resources</span>
              </div>
              <HighlightedJsonBlock text={JSON.stringify(result.bundle, null, 2)} style={{ ...resultPreStyle, maxHeight: 460 }} />
              <button
                type="button"
                onClick={() => downloadJson(`workflow-${slug(packId)}.json`, result.bundle)}
                style={{ fontSize: 12, fontWeight: 600, padding: '6px 13px', borderRadius: 7, border: '1px solid var(--border2)', color: 'var(--accent)', background: 'transparent', cursor: 'pointer', alignSelf: 'flex-start' }}
              >
                ⬇ Download bundle
              </button>
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a workflow pack to see its resources here.</span>
          )}
        </Card>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Build frontend**

Run (in `frontend/`): `npm run build`
Expected: builds clean, no TypeScript errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/benches/fakes/fakesTypes.ts frontend/src/benches/fakes/fakesApi.ts frontend/src/benches/fakes/FakesBench.tsx
git commit -m "Add Workflow Scenario Pack panel to the Fakes bench"
```

---

### Task 8: Final verification and PR #12 cleanup

**Files:** none (verification only).

- [ ] **Step 1: Full backend + frontend build and test**

Run: `dotnet build Ignixa.Lab.sln -c Release`
Run: `dotnet test Ignixa.Lab.sln -c Release`
Run (in `frontend/`): `npm run build` and `npm run lint`
Expected: all green.

- [ ] **Step 2: Manual smoke test via the `run`/`verify` skills**

Start the backend Functions host and frontend dev server (use this repo's `run` skill if one exists; otherwise the standard `func start` / `npm run dev`). Then, in a browser:
- Fakes bench → Scenario mode: confirm scenario cards now group by the library's `Category` (e.g. "Schedule" doesn't appear here since that's a workflow pack, but existing categories like "Emergency"/"Cardiometabolic" still render).
- Fakes bench → Resource mode: set Density to Maximum, pick a Theme (e.g. "Cardiology"), generate a Condition, confirm it succeeds and produces a plausible cardiology-coded resource.
- Fakes bench → Workflow mode: generate `DailyAppointmentSchedule`, confirm the bundle contains Patient/Practitioner/Encounter/Appointment resources.
- Fakes bench → Resource mode: set FHIR version to a UK-relevant flow — generate a Patient with City = a UK city if one is listed in `patientCities` (added by #300; if none is listed, this step confirms London needs no separate UI wiring since it's absent from `KnownCities.All`'s exposed list — note this finding rather than treating it as a blocker) and inspect the identifier/address shape.
- Conformance Runner: run a suite with `fhirVersions` gating (e.g. one of the bundled canonical suites) against a real or mock R4 target; confirm it executes rather than showing "Skipped" for every version-gated test.
- FHIRPath bench: evaluate `true = 'true'` and a decimal-returning expression; confirm lowercase boolean stringification and invariant-culture decimal formatting.

Document any deviation found in this step as a follow-up note in the PR description rather than silently reverting Task 2–7 changes.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin feature/ignixa-fhir-0.6.4-upgrade
gh pr create --title "Consume ignixa-fhir 0.6.4" --body "$(cat <<'EOF'
## Summary
- Bumps Ignixa.* packages to 0.6.4.
- Fixes FHIR release-label gating in TestScriptRunner (the same bug PR #12 targets — folded in here since #301's granular matching depends on it).
- Replaces the reflection-based ScenarioDiscovery/ObservationStateDiscovery with the newly-public ScenarioCatalog/ObservationStateCatalog.
- Surfaces scenario Category/Domain metadata, Theme-aware Maximum-density generation, and the DailyAppointmentSchedule Workflow Scenario Pack in the Fakes bench.

## Test plan
- [ ] dotnet test Ignixa.Lab.sln
- [ ] npm run build && npm run lint (frontend)
- [ ] Manual smoke test per docs/superpowers/plans/2026-07-05-ignixa-fhir-0.6.4-upgrade.md Task 8
EOF
)"
```

- [ ] **Step 4: Comment on ignixa-lab PR #12**

```bash
gh pr comment 12 --repo brendankowitz/ignixa-lab --body "Folded into the unified ignixa-fhir 0.6.4 upgrade branch — same release-label normalization fix, now riding on top of ignixa-fhir #301's granular version matching. Closing in favor of that branch once it merges."
```

(Do not close PR #12 directly — leave that for the user, per standing project practice of not taking merge/close actions autonomously.)

## Self-Review Notes

- **Spec coverage:** §1 Package bump → Task 1. §2 Version gating → Task 2. §3 Discovery consolidation → Task 3. Category/Domain metadata (part of §3) → Task 4. §4 Theme → Task 5. §5 Workflow packs → Task 6–7. §6 UK Core → Task 8 Step 2 (verification only, no code task, per the spec's own "no planned code changes" call). §7 FHIRPath AsString → Task 8 Step 2 (verification only, per spec). Testing section → covered by each task's own test steps plus Task 8's full-suite run.
- **Placeholder scan:** no TBD/TODO; the one deliberately-deferred item (searchset paging) is called out explicitly as out of scope with a reason, not left vague.
- **Type consistency:** `FakesService` constructor shape (`SchemaProviderFactory` only) introduced in Task 3 is relied on unchanged by Task 5/6's edits. `ConvertParameterOverrides(DiscoveredScenario, ...)` from Task 3 is reused as-is by Task 6 for `WorkflowScenarioCatalog`'s `DiscoveredScenario`-shaped packs — no signature drift. `ScenarioMetadata`/`describeScenario` signature changes in Task 4 are consumed identically by Task 3 Step 3 (ordered explicitly via Task 3 Step 6) and Task 7's reuse of `ScenarioParameterControl`.
