# FHIR Fakes Bench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fourth "Fakes" bench to the Expression Benches frontend that generates synthetic FHIR data (patient populations, clinical scenarios, single resources with edge-case fuzzing) via a real backend wired to the published `Ignixa.FhirFakes` library, and let the FHIRPath/FML/SQL-on-FHIR benches send/receive generated data to and from it.

**Architecture:** New Azure Functions endpoints in `Ignixa.Lab.Functions` consume `Ignixa.FhirFakes` 0.5.13 directly. A new `FakesBench.tsx` React component (real-backend tier, like FHIRPath) calls those endpoints through a plain-JSON API client. Cross-bench integration lifts a small amount of shared state into `BenchesApp.tsx`.

**Tech Stack:** C#/.NET 10, Azure Functions v4 isolated worker, `Ignixa.FhirFakes` 0.5.13, React 19 + TypeScript, Vite.

Full design rationale: `docs/superpowers/specs/2026-07-02-fhir-fakes-design.md`.

## Global Constraints

- New endpoints follow `RunFunction`/`SuitesFunction`'s style: `[Function("Name")]`, `HttpTrigger(AuthorizationLevel.Anonymous, ...)`, `JsonSerializer.DeserializeAsync` with `JsonSerializerDefaults.Web`, `BadRequestObjectResult`/`OkObjectResult`. NOT `FhirPathFunctions`'s FHIR-Parameters-wire-format style.
- Frontend API calls use a `request<T>()`-style plain-JSON client (new `frontend/src/benches/fakes/fakesApi.ts`), matching `frontend/src/api/client.ts`'s pattern — not the FHIRPath bench's Parameters-wire-format client.
- Scenario names, resource types, observation states, and edge-case categories all come from a backend discovery endpoint reflecting the real `Ignixa.FhirFakes` library at runtime — never hardcoded lists copied from the design mockup.
- Population and Scenario modes have **no** Seed control (the real `PopulationGenerator.Generate()` and predefined scenario methods take no seed parameter and have no determinism coverage). Only Resource mode gets a real Seed + Reseed control (`PatientBuilder`/`SchemaBasedFhirResourceFaker`'s seeded path is the one proven deterministic by `PatientBuilderDeterminismTests`).
- Population source is a **US state name only** (`PopulationGenerator.AvailableStates`) — the real `Generate(string state, int populationSize)` resolves a city internally; there is no city-level entry point.
- Scenario parameters are non-uniform across scenarios (e.g. `GetDiabeticPatient` has `age`/`gender`/`severity`; `GetWellnessVisit` has `age`/`gender`/`includeLipidPanel`) — the metadata endpoint reports each scenario's actual reflected parameter list; the frontend renders a specialized control only for parameters literally named `age`/`gender`/`severity`, and falls back to a generic control (checkbox for `bool`, number input for `int`/`decimal`, text input for anything else) for every other parameter name.
- Edge-case category blurb text lives in a static frontend lookup table (`edgeCaseDescriptions.ts`) keyed by category id, sourced from the design mockup's own wording — no backend or `Ignixa.FhirFakes` changes for this.
- Follow `C:\Users\brend\.claude\CLAUDE.md`'s C# conventions: PascalCase/camelCase naming, `sealed` classes unless designed for inheritance, one constructor, `ArgumentNullException.ThrowIfNull`, no service locator, boolean parameters avoided where an enum reads better.
- FHIR version strings used across the API are lowercase (`stu3`, `r4`, `r4b`, `r5`, `r6`), matching `FhirPathBench.tsx`'s existing `FhirVersion` type — the backend's `SchemaProviderFactory` methods already accept case-insensitively via `.ToUpperInvariant()`.

---

### Task 1: `Ignixa.FhirFakes` package reference + schema provider access

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/FhirPath/SchemaProviderFactory.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/SchemaProviderFactoryTests.cs` (new)

**Interfaces:**
- Produces: `SchemaProviderFactory.GetSchemaProvider(string fhirVersion) : IFhirSchemaProvider` — used by every later task that needs an `IFhirSchemaProvider` instance (all of Tasks 2–7).

- [ ] **Step 1: Add the package version pin**

In `Directory.Packages.props`, add immediately after the `Ignixa.Specification` line (currently line 19):

```xml
    <!-- Synthetic FHIR data generator (populations, clinical scenarios, single resources, edge-case fuzzing), powering the Fakes bench -->
    <PackageVersion Include="Ignixa.FhirFakes" Version="0.5.13" />
```

- [ ] **Step 2: Add the package reference**

In `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`, add immediately after the `Ignixa.Specification` line (currently line 19):

```xml
    <PackageReference Include="Ignixa.FhirFakes" />
```

- [ ] **Step 3: Restore and confirm the package resolves**

Run: `dotnet restore Ignixa.Lab.sln`
Expected: restore succeeds, no errors about `Ignixa.FhirFakes`.

- [ ] **Step 4: Write the failing test for the new schema-provider accessor**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/SchemaProviderFactoryTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Services.FhirPath;

namespace Ignixa.Lab.Functions.Tests.Services.FhirPath;

public sealed class SchemaProviderFactoryTests
{
    [Theory]
    [InlineData("R4", FhirVersion.R4)]
    [InlineData("r4", FhirVersion.R4)]
    [InlineData("STU3", FhirVersion.Stu3)]
    [InlineData("R4B", FhirVersion.R4B)]
    [InlineData("R5", FhirVersion.R5)]
    [InlineData("R6", FhirVersion.R6)]
    [InlineData("not-a-real-version", FhirVersion.R4)]
    public void GetSchemaProvider_ReturnsProviderForVersion_DefaultingToR4(string input, FhirVersion expected)
    {
        var factory = new SchemaProviderFactory();

        var provider = factory.GetSchemaProvider(input);

        provider.Version.Should().Be(expected);
    }

    [Fact]
    public void GetSchemaProvider_ExposesResourceTypeNames()
    {
        var factory = new SchemaProviderFactory();

        var provider = factory.GetSchemaProvider("R4");

        provider.ResourceTypeNames.Should().Contain("Patient");
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter SchemaProviderFactoryTests -v minimal`
Expected: FAIL with "'SchemaProviderFactory' does not contain a definition for 'GetSchemaProvider'".

- [ ] **Step 6: Add `GetSchemaProvider` to `SchemaProviderFactory`**

In `backend/src/Ignixa.Lab.Functions/Services/FhirPath/SchemaProviderFactory.cs`, add this method after the existing `GetAnalyzer` method (the `using Ignixa.Abstractions;` needed for `IFhirSchemaProvider` is already present at the top of the file):

```csharp
    /// <summary>
    /// Gets the FHIR schema provider for the specified FHIR version, typed as
    /// <see cref="IFhirSchemaProvider"/> for callers (like the Fakes services)
    /// that need its full surface rather than just the <see cref="ISchema"/> subset.
    /// </summary>
    /// <param name="fhirVersion">The FHIR version (e.g., "R4", "R5", "STU3").</param>
    /// <returns>The schema provider for the specified version, defaults to R4 if unknown.</returns>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public IFhirSchemaProvider GetSchemaProvider(string fhirVersion) => fhirVersion.ToUpperInvariant() switch
    {
        "STU3" or "R3" => Stu3Schema.Value,
        "R4" => R4Schema.Value,
        "R4B" => R4BSchema.Value,
        "R5" => R5Schema.Value,
        "R6" => R6Schema.Value,
        _ => R4Schema.Value
    };
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test Ignixa.Lab.sln --filter SchemaProviderFactoryTests -v minimal`
Expected: PASS, 7/7 tests.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj backend/src/Ignixa.Lab.Functions/Services/FhirPath/SchemaProviderFactory.cs backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/SchemaProviderFactoryTests.cs
git commit -m "Add Ignixa.FhirFakes package reference and IFhirSchemaProvider accessor"
```

---

### Task 2: Scenario discovery

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ScenarioDiscoveryTests.cs` (new)

**Interfaces:**
- Consumes: `SchemaProviderFactory.GetSchemaProvider(string) : IFhirSchemaProvider` (Task 1).
- Produces: `ScenarioDiscovery.All() : IReadOnlyList<DiscoveredScenario>`, `ScenarioDiscovery.Find(string id) : DiscoveredScenario?`, `ScenarioDiscovery.Invoke(DiscoveredScenario, IFhirSchemaProvider, IReadOnlyDictionary<string, JsonElement>?) : ScenarioContext` — used by Task 4 (metadata endpoint) and Task 6 (scenario endpoint).
- `DiscoveredScenario { string Id; MethodInfo Method; IReadOnlyList<ParameterInfo> Parameters }` (`Parameters` excludes the leading `IFhirSchemaProvider` parameter).

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ScenarioDiscoveryTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Ignixa.Lab.Functions.Services.Fakes;

namespace Ignixa.Lab.Functions.Tests.Services.Fakes;

public sealed class ScenarioDiscoveryTests
{
    [Fact]
    public void All_ReturnsAtLeastOneScenario_IncludingDiabeticPatient()
    {
        var discovery = new ScenarioDiscovery();

        var scenarios = discovery.All();

        scenarios.Should().NotBeEmpty();
        scenarios.Select(s => s.Id).Should().Contain("DiabeticPatient");
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        var discovery = new ScenarioDiscovery();

        discovery.Find("NotARealScenario").Should().BeNull();
    }

    [Fact]
    public void Find_DiabeticPatient_ExposesAgeGenderAndSeverityParameters()
    {
        var discovery = new ScenarioDiscovery();

        var scenario = discovery.Find("DiabeticPatient");

        scenario.Should().NotBeNull();
        scenario!.Parameters.Select(p => p.Name).Should().Contain(["age", "gender", "severity"]);
    }

    [Fact]
    public void Invoke_DiabeticPatientWithNoOverrides_UsesDefaults()
    {
        var discovery = new ScenarioDiscovery();
        var factory = new Ignixa.Lab.Functions.Services.FhirPath.SchemaProviderFactory();
        var scenario = discovery.Find("DiabeticPatient")!;

        var context = discovery.Invoke(scenario, factory.GetSchemaProvider("R4"), parameters: null);

        context.Patient.Should().NotBeNull();
        context.AllResources.Should().NotBeEmpty();
    }

    [Fact]
    public void Invoke_DiabeticPatientWithAgeOverride_UsesOverride()
    {
        var discovery = new ScenarioDiscovery();
        var factory = new Ignixa.Lab.Functions.Services.FhirPath.SchemaProviderFactory();
        var scenario = discovery.Find("DiabeticPatient")!;
        var overrides = new Dictionary<string, JsonElement>
        {
            ["age"] = JsonDocument.Parse("30").RootElement,
        };

        var context = discovery.Invoke(scenario, factory.GetSchemaProvider("R4"), overrides);

        context.BirthDate.Year.Should().BeCloseTo(DateTime.UtcNow.Year - 30, 1);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter ScenarioDiscoveryTests -v minimal`
Expected: FAIL with "The type or namespace name 'ScenarioDiscovery' could not be found".

- [ ] **Step 3: Implement `ScenarioDiscovery`**

Create `backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Predefined;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>One discovered predefined scenario factory method, reflected from <c>Ignixa.FhirFakes.Scenarios.Predefined</c> by naming convention.</summary>
public sealed class DiscoveredScenario
{
    public required string Id { get; init; }
    public required MethodInfo Method { get; init; }

    /// <summary>The method's own parameters, excluding the leading <see cref="IFhirSchemaProvider"/>.</summary>
    public IReadOnlyList<ParameterInfo> Parameters => Method.GetParameters().Skip(1).ToList();
}

/// <summary>
/// Reflects the real, published <c>Ignixa.FhirFakes</c> library for its predefined
/// scenario factory methods, so the Fakes bench always reflects what the library
/// actually offers rather than a hand-maintained list that can drift out of sync.
/// A public static method in the <c>Ignixa.FhirFakes.Scenarios.Predefined</c>
/// namespace returning <see cref="ScenarioContext"/> whose first parameter is
/// <see cref="IFhirSchemaProvider"/> counts as a scenario; its "Get" prefix (if
/// any) is stripped to form the id (e.g. <c>GetDiabeticPatient</c> -&gt; <c>DiabeticPatient</c>).
/// </summary>
public sealed class ScenarioDiscovery
{
    private readonly Lazy<IReadOnlyDictionary<string, DiscoveredScenario>> _scenarios = new(Discover);

    public IReadOnlyList<DiscoveredScenario> All() => _scenarios.Value.Values.ToList();

    public DiscoveredScenario? Find(string id) =>
        _scenarios.Value.TryGetValue(id, out var scenario) ? scenario : null;

    /// <summary>
    /// Invokes a discovered scenario's factory method, using each entry in
    /// <paramref name="parameters"/> (matched by parameter name, case-insensitive)
    /// to override that parameter's own default value.
    /// </summary>
    public ScenarioContext Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var allParameters = scenario.Method.GetParameters();
        var args = new object?[allParameters.Length];
        args[0] = schemaProvider;

        for (var i = 1; i < allParameters.Length; i++)
        {
            var parameter = allParameters[i];
            args[i] = parameters != null && parameters.TryGetValue(parameter.Name!, out var overrideValue)
                ? ConvertParameter(overrideValue, parameter.ParameterType)
                : parameter.DefaultValue;
        }

        return (ScenarioContext)scenario.Method.Invoke(null, args)!;
    }

    private static object? ConvertParameter(JsonElement value, Type parameterType)
    {
        var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (underlyingType == typeof(int))
        {
            return value.GetInt32();
        }

        if (underlyingType == typeof(bool))
        {
            return value.GetBoolean();
        }

        if (underlyingType == typeof(decimal))
        {
            return value.GetDecimal();
        }

        return value.GetString();
    }

    private static IReadOnlyDictionary<string, DiscoveredScenario> Discover()
    {
        var scenarios = new Dictionary<string, DiscoveredScenario>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(DiabeticPatientScenario).Assembly;

        var scenarioTypes = assembly.GetTypes()
            .Where(type => type.Namespace == "Ignixa.FhirFakes.Scenarios.Predefined" && type.IsClass && type.IsPublic);

        foreach (var type in scenarioTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.ReturnType == typeof(ScenarioContext));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(IFhirSchemaProvider))
                {
                    continue;
                }

                var id = method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name[3..] : method.Name;
                scenarios[id] = new DiscoveredScenario { Id = id, Method = method };
            }
        }

        return scenarios;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter ScenarioDiscoveryTests -v minimal`
Expected: PASS, 5/5 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Fakes/ScenarioDiscovery.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ScenarioDiscoveryTests.cs
git commit -m "Add reflection-based scenario discovery for the Fakes bench"
```

---

### Task 3: Observation-state discovery

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ObservationStateDiscoveryTests.cs` (new)

**Interfaces:**
- Produces: `ObservationStateDiscovery.Names() : IReadOnlyList<string>`, `ObservationStateDiscovery.Create(string name) : ObservationState?` — used by Task 4 (metadata endpoint) and Task 7 (resource endpoint).

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ObservationStateDiscoveryTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Services.Fakes;

namespace Ignixa.Lab.Functions.Tests.Services.Fakes;

public sealed class ObservationStateDiscoveryTests
{
    [Fact]
    public void Names_IncludesBloodGlucoseAndBodyTemperature()
    {
        var discovery = new ObservationStateDiscovery();

        var names = discovery.Names();

        names.Should().Contain(["BloodGlucose", "BodyTemperature"]);
    }

    [Fact]
    public void Create_UnknownName_ReturnsNull()
    {
        var discovery = new ObservationStateDiscovery();

        discovery.Create("NotARealState").Should().BeNull();
    }

    [Fact]
    public void Create_BloodGlucose_ReturnsAState()
    {
        var discovery = new ObservationStateDiscovery();

        var state = discovery.Create("BloodGlucose");

        state.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter ObservationStateDiscoveryTests -v minimal`
Expected: FAIL with "The type or namespace name 'ObservationStateDiscovery' could not be found".

- [ ] **Step 3: Implement `ObservationStateDiscovery`**

Create `backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs`:

```csharp
using System.Reflection;
using Ignixa.FhirFakes.Scenarios.States;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>
/// Reflects <c>Ignixa.FhirFakes.Scenarios.States.ObservationState</c>'s own
/// public static, no-required-argument factory methods (e.g. <c>BloodGlucose()</c>,
/// <c>BodyTemperature()</c>) so the Fakes bench's observation clinical-state
/// picker always reflects what the library actually offers. This type lives in
/// the core library itself (not the CLI tool), so it's genuinely reflectable
/// from this project.
/// </summary>
public sealed class ObservationStateDiscovery
{
    private readonly Lazy<IReadOnlyDictionary<string, MethodInfo>> _states = new(Discover);

    public IReadOnlyList<string> Names() => _states.Value.Keys.ToList();

    public ObservationState? Create(string name)
    {
        if (!_states.Value.TryGetValue(name, out var method))
        {
            return null;
        }

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = parameters[i].DefaultValue;
        }

        return method.Invoke(null, args) as ObservationState;
    }

    private static IReadOnlyDictionary<string, MethodInfo> Discover()
    {
        var states = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        var observationStateType = typeof(ObservationState);

        foreach (var method in observationStateType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != observationStateType)
            {
                continue;
            }

            if (method.GetParameters().All(parameter => parameter.HasDefaultValue))
            {
                states[method.Name] = method;
            }
        }

        return states;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter ObservationStateDiscoveryTests -v minimal`
Expected: PASS, 3/3 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Fakes/ObservationStateDiscovery.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Fakes/ObservationStateDiscoveryTests.cs
git commit -m "Add reflection-based observation-state discovery for the Fakes bench"
```

---

### Task 4: Metadata endpoint

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Program.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs` (new)

**Interfaces:**
- Consumes: `SchemaProviderFactory.GetSchemaProvider` (Task 1), `ScenarioDiscovery.All` (Task 2), `ObservationStateDiscovery.Names` (Task 3), `Ignixa.FhirFakes.Population.PopulationGenerator.AvailableStates`, `Ignixa.FhirFakes.EdgeCases.EdgeCaseCatalog.CreateDefault().All()`.
- Produces: `GET /api/fakes/metadata` route, and the `FakesFunctions` class that Tasks 5–7 add more `[Function]` methods to.

- [ ] **Step 1: Write the response DTOs**

Create `backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs`:

```csharp
namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class FakesMetadataResponse
{
    public required IReadOnlyList<string> FhirVersions { get; init; }
    public required IReadOnlyList<string> PopulationStates { get; init; }
    public required IReadOnlyList<ScenarioMetadata> Scenarios { get; init; }
    public required IReadOnlyList<string> ResourceTypes { get; init; }
    public required IReadOnlyList<string> ObservationStates { get; init; }
    public required IReadOnlyList<EdgeCaseFamilyMetadata> EdgeCaseFamilies { get; init; }
}

public sealed class ScenarioMetadata
{
    public required string Id { get; init; }
    public required IReadOnlyList<ScenarioParameterMetadata> Parameters { get; init; }
}

public sealed class ScenarioParameterMetadata
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public object? DefaultValue { get; init; }
}

public sealed class EdgeCaseFamilyMetadata
{
    public required string Family { get; init; }
    public required IReadOnlyList<EdgeCaseCategoryMetadata> Categories { get; init; }
}

public sealed class EdgeCaseCategoryMetadata
{
    public required string Id { get; init; }
    public required string Intent { get; init; }
}
```

- [ ] **Step 2: Write the failing test**

Create `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Models.Fakes;
using Ignixa.Lab.Functions.Services.Fakes;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class FakesFunctionsTests
{
    private static FakesFunctions CreateFunctions() => new(
        new SchemaProviderFactory(),
        new ScenarioDiscovery(),
        new ObservationStateDiscovery());

    [Fact]
    public void GetMetadata_ReturnsPopulationStatesScenariosAndEdgeCaseFamilies()
    {
        var functions = CreateFunctions();

        var result = functions.GetMetadata(new DefaultHttpContext().Request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var metadata = ok.Value.Should().BeOfType<FakesMetadataResponse>().Subject;
        metadata.PopulationStates.Should().Contain("Massachusetts");
        metadata.Scenarios.Select(s => s.Id).Should().Contain("DiabeticPatient");
        metadata.ObservationStates.Should().Contain("BloodGlucose");
        metadata.EdgeCaseFamilies.Select(f => f.Family).Should().Contain(["Unicode", "Temporal", "StringBoundary"]);
        metadata.EdgeCaseFamilies.Select(f => f.Family).Should().NotContain(["Cardinality", "Structural"]);
        metadata.ResourceTypes.Should().Contain("Patient");
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: FAIL with "The type or namespace name 'FakesFunctions' could not be found".

- [ ] **Step 4: Implement `FakesFunctions.GetMetadata`**

Create `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`:

```csharp
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.Population;
using Ignixa.Lab.Functions.Models.Fakes;
using Ignixa.Lab.Functions.Services.Fakes;
using Ignixa.Lab.Functions.Services.FhirPath;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Synthetic FHIR data generation endpoints, powering the Expression Benches Fakes bench.</summary>
public sealed class FakesFunctions(
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery)
{
    private static readonly string[] FhirVersions = ["stu3", "r4", "r4b", "r5", "r6"];
    private static readonly string[] ActiveEdgeCaseFamilies = ["Unicode", "Temporal", "StringBoundary"];

    [Function("FakesMetadata")]
    public IActionResult GetMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "fakes/metadata")] HttpRequest request)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider("R4");
        var populationGenerator = new PopulationGenerator(schemaProvider);
        var catalog = EdgeCaseCatalog.CreateDefault();

        var response = new FakesMetadataResponse
        {
            FhirVersions = FhirVersions,
            PopulationStates = populationGenerator.AvailableStates,
            Scenarios = scenarioDiscovery.All().Select(ToScenarioMetadata).ToList(),
            ResourceTypes = schemaProvider.ResourceTypeNames.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            ObservationStates = observationStateDiscovery.Names(),
            EdgeCaseFamilies = ActiveEdgeCaseFamilies
                .Select(family => ToEdgeCaseFamilyMetadata(family, catalog))
                .Where(family => family.Categories.Count > 0)
                .ToList(),
        };

        return new OkObjectResult(response);
    }

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

    private static EdgeCaseFamilyMetadata ToEdgeCaseFamilyMetadata(string familyName, EdgeCaseCatalog catalog) => new()
    {
        Family = familyName,
        Categories = catalog.All()
            .Where(strategy => strategy.Family.ToString() == familyName)
            .Select(strategy => new EdgeCaseCategoryMetadata { Id = strategy.Category, Intent = strategy.Intent.ToString() })
            .ToList(),
    };
}
```

- [ ] **Step 5: Register the new services in `Program.cs`**

In `backend/src/Ignixa.Lab.Functions/Program.cs`, add after the existing `builder.Services.AddSingleton<FhirPathService>();` line:

```csharp
builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Fakes.ScenarioDiscovery>();
builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Fakes.ObservationStateDiscovery>();
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: PASS, 1/1 test.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes/FakesMetadataResponse.cs backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/src/Ignixa.Lab.Functions/Program.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs
git commit -m "Add GET /api/fakes/metadata endpoint"
```

---

### Task 5: Population endpoint

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/Fakes/PopulationRequest.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Program.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`

**Interfaces:**
- Consumes: `SchemaProviderFactory.GetSchemaProvider` (Task 1), `Ignixa.FhirFakes.Population.PopulationGenerator`, `Ignixa.Serialization.SourceNodes` extension `ResourceJsonNode.SerializeToString(bool pretty = false)`.
- Produces: `FakesService.GeneratePopulation(string fhirVersion, string source, int count) : JsonObject` — a plain-JSON summary object with `patients`, `resources`, `summary` (byType/byGender/byCity/ageBuckets), used only within this task. `POST /api/fakes/population` route.

- [ ] **Step 1: Write the request DTO**

Create `backend/src/Ignixa.Lab.Functions/Models/Fakes/PopulationRequest.cs`:

```csharp
namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class PopulationRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string Source { get; init; }
    public int Count { get; init; } = 10;
}
```

- [ ] **Step 2: Write the failing test**

Add to `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs` (new `using`s: `System.Text.Json`, `Ignixa.Lab.Functions.Models.Fakes`, `Microsoft.AspNetCore.Http`, and update `CreateFunctions()`):

```csharp
    private static FakesFunctions CreateFunctions() => new(
        new SchemaProviderFactory(),
        new ScenarioDiscovery(),
        new ObservationStateDiscovery(),
        new FakesService(new SchemaProviderFactory()));

    [Fact]
    public async Task GeneratePopulation_ValidRequest_ReturnsPatientsResourcesAndSummary()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { source = "Massachusetts", count = 3 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("patients").GetArrayLength().Should().Be(3);
        doc.RootElement.GetProperty("resources").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        doc.RootElement.GetProperty("summary").GetProperty("byGender").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GeneratePopulation_MissingSource_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { count = 3 });

        var result = await functions.GeneratePopulation(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static HttpRequest BuildJsonPostRequest(object body)
    {
        var context = new DefaultHttpContext();
        var json = JsonSerializer.Serialize(body);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        return context.Request;
    }
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: FAIL to build — `FakesService` and `FakesFunctions.GeneratePopulation` don't exist yet.

- [ ] **Step 4: Implement `FakesService.GeneratePopulation`**

Create `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`:

```csharp
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>Orchestrates calls into <c>Ignixa.FhirFakes</c> and shapes the results into plain JSON for the Fakes bench endpoints.</summary>
public sealed class FakesService(SchemaProviderFactory schemaProviderFactory)
{
    public JsonObject GeneratePopulation(string fhirVersion, string source, int count)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var generator = new PopulationGenerator(schemaProvider);

        var patients = new JsonArray();
        var resources = new JsonArray();
        var byType = new Dictionary<string, int>();
        var byGender = new Dictionary<string, int>();
        var byCity = new Dictionary<string, int>();
        var ageBuckets = new Dictionary<string, int> { ["0-17"] = 0, ["18-34"] = 0, ["35-54"] = 0, ["55-74"] = 0, ["75+"] = 0 };

        foreach (var context in generator.Generate(source, count))
        {
            if (context.Patient != null)
            {
                patients.Add(JsonNode.Parse(context.Patient.SerializeToString()));
                Tally(byGender, GetString(context.Patient, "gender") ?? "unknown");
                var city = GetAddressCity(context.Patient);
                if (city != null)
                {
                    Tally(byCity, city);
                }

                var age = GetAge(context.Patient);
                if (age != null)
                {
                    Tally(ageBuckets, AgeBucket(age.Value));
                }
            }

            foreach (var resource in context.AllResources)
            {
                resources.Add(JsonNode.Parse(resource.SerializeToString()));
                Tally(byType, resource.ResourceType);
            }
        }

        return new JsonObject
        {
            ["patients"] = patients,
            ["resources"] = resources,
            ["summary"] = new JsonObject
            {
                ["byType"] = ToJsonObject(byType),
                ["byGender"] = ToJsonObject(byGender),
                ["byCity"] = ToJsonObject(byCity),
                ["ageBuckets"] = ToJsonObject(ageBuckets),
            },
        };
    }

    private static void Tally(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static JsonObject ToJsonObject(Dictionary<string, int> counts)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in counts)
        {
            obj[key] = value;
        }

        return obj;
    }

    private static string? GetString(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource, string propertyName)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(resource.SerializeToString());
        return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetAddressCity(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(resource.SerializeToString());
        if (!doc.RootElement.TryGetProperty("address", out var address) || address.GetArrayLength() == 0)
        {
            return null;
        }

        var first = address[0];
        return first.TryGetProperty("city", out var city) ? city.GetString() : null;
    }

    private static int? GetAge(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(resource.SerializeToString());
        if (!doc.RootElement.TryGetProperty("birthDate", out var birthDate) || birthDate.GetString() is not { } text)
        {
            return null;
        }

        return DateTime.TryParse(text[..4] + "-01-01", out var parsed) ? DateTime.UtcNow.Year - parsed.Year : null;
    }

    private static string AgeBucket(int age) => age switch
    {
        < 18 => "0-17",
        < 35 => "18-34",
        < 55 => "35-54",
        < 75 => "55-74",
        _ => "75+",
    };
}
```

- [ ] **Step 5: Add `GeneratePopulation` to `FakesFunctions`**

In `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`, change the primary constructor to add the new dependency and add the new endpoint method:

```csharp
public sealed class FakesFunctions(
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery,
    FakesService fakesService)
```

Add this method to the class (after `GetMetadata`), and add `using System.Text.Json;` at the top of the file:

```csharp
    [Function("FakesPopulation")]
    public async Task<IActionResult> GeneratePopulation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/population")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        PopulationRequest? populationRequest;
        try
        {
            populationRequest = await JsonSerializer.DeserializeAsync<PopulationRequest>(
                request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (populationRequest is null || string.IsNullOrWhiteSpace(populationRequest.Source))
        {
            return new BadRequestObjectResult(new { error = "A 'source' (US state name) is required." });
        }

        var result = fakesService.GeneratePopulation(populationRequest.FhirVersion, populationRequest.Source, populationRequest.Count);
        return new OkObjectResult(result);
    }
```

- [ ] **Step 6: Register `FakesService` in `Program.cs`**

Add after the `ScenarioDiscovery`/`ObservationStateDiscovery` registrations added in Task 4:

```csharp
builder.Services.AddSingleton<Ignixa.Lab.Functions.Services.Fakes.FakesService>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: PASS, 3/3 tests.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes/PopulationRequest.cs backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/src/Ignixa.Lab.Functions/Program.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs
git commit -m "Add POST /api/fakes/population endpoint"
```

---

### Task 6: Scenario endpoint

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/Fakes/ScenarioRequest.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`

**Interfaces:**
- Consumes: `ScenarioDiscovery.Find`/`Invoke` (Task 2), `Ignixa.FhirFakes.Scenarios.ScenarioContext.RewriteReferences`, `.ToBundle()`, `.ToBatchBundle()`.
- Produces: `FakesService.GenerateScenario(...) : JsonObject?` (null when `scenarioId` is unknown), `POST /api/fakes/scenario` route.

- [ ] **Step 1: Write the request DTO**

Create `backend/src/Ignixa.Lab.Functions/Models/Fakes/ScenarioRequest.cs`:

```csharp
using System.Text.Json;

namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class ScenarioRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string ScenarioId { get; init; }
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public string? Tag { get; init; }
    public bool ResolvedReferences { get; init; }
}
```

- [ ] **Step 2: Write the failing test**

Add to `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`:

```csharp
    [Fact]
    public async Task GenerateScenario_DiabeticPatientWithTag_StampsTagOnEveryResource()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", tag = "test-run-123" });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        var resources = doc.RootElement.GetProperty("resources");
        resources.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var resource in resources.EnumerateArray())
        {
            resource.GetProperty("meta").GetProperty("tag")[0].GetProperty("code").GetString().Should().Be("test-run-123");
        }
    }

    [Fact]
    public async Task GenerateScenario_UnknownScenarioId_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "NotARealScenario" });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateScenario_ResolvedReferencesTrue_ReturnsBatchBundle()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { scenarioId = "DiabeticPatient", resolvedReferences = true });

        var result = await functions.GenerateScenario(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("bundle").GetProperty("type").GetString().Should().Be("batch");
    }
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: FAIL to build — `FakesFunctions.GenerateScenario` doesn't exist yet.

- [ ] **Step 4: Add `GenerateScenario` to `FakesService`**

In `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`, add `ScenarioDiscovery` to the primary constructor and add the new method. Update the class declaration:

```csharp
public sealed class FakesService(SchemaProviderFactory schemaProviderFactory, ScenarioDiscovery scenarioDiscovery)
```

Add this method (needs `using Ignixa.Abstractions;`, `using Ignixa.FhirFakes.Scenarios;`, `using System.Text.Json;` at the top of the file in addition to what's already there):

`ResourceJsonNode` has no exposed in-place setter for nested properties like `meta.tag`, so tag-stamping happens entirely at the `System.Text.Json.Nodes.JsonNode` level, independently for the plain resource list and for the bundle's entries — both are separately parsed from `SerializeToString()`, so each needs its own stamp pass:

```csharp
    /// <summary>Returns null when <paramref name="scenarioId"/> doesn't match a discovered scenario.</summary>
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
        var context = scenarioDiscovery.Invoke(scenario, schemaProvider, parameters);

        if (resolvedReferences)
        {
            context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved);
        }

        var bundle = resolvedReferences ? context.ToBatchBundle() : context.ToBundle();

        var patientNode = context.Patient != null ? JsonNode.Parse(context.Patient.SerializeToString()) : null;
        var resourceNodes = context.AllResources.Select(r => JsonNode.Parse(r.SerializeToString())!).ToList();
        var bundleNode = JsonNode.Parse(bundle.SerializeToString())!.AsObject();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            if (patientNode != null)
            {
                StampTag(patientNode.AsObject(), tag);
            }

            foreach (var resourceNode in resourceNodes)
            {
                StampTag(resourceNode.AsObject(), tag);
            }

            StampBundleEntryTags(bundleNode, tag);
        }

        return new JsonObject
        {
            ["patient"] = patientNode,
            ["resources"] = new JsonArray(resourceNodes.ToArray()),
            ["bundle"] = bundleNode,
        };
    }

    private static void StampTag(JsonObject resource, string tag)
    {
        var meta = resource["meta"]?.AsObject() ?? new JsonObject();
        var tags = meta["tag"]?.AsArray() ?? new JsonArray();
        tags.Add(new JsonObject { ["system"] = "urn:ignixa:test", ["code"] = tag });
        meta["tag"] = tags;
        resource["meta"] = meta;
    }

    private static void StampBundleEntryTags(JsonObject bundle, string tag)
    {
        if (bundle["entry"] is not JsonArray entries)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry?["resource"] is JsonObject resource)
            {
                StampTag(resource, tag);
            }
        }
    }
```

- [ ] **Step 5: Add `GenerateScenario` to `FakesFunctions`**

Add `ScenarioRequest` deserialization and the endpoint method to `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`:

```csharp
    [Function("FakesScenario")]
    public async Task<IActionResult> GenerateScenario(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/scenario")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ScenarioRequest? scenarioRequest;
        try
        {
            scenarioRequest = await JsonSerializer.DeserializeAsync<ScenarioRequest>(
                request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (scenarioRequest is null || string.IsNullOrWhiteSpace(scenarioRequest.ScenarioId))
        {
            return new BadRequestObjectResult(new { error = "A 'scenarioId' is required." });
        }

        var result = fakesService.GenerateScenario(
            scenarioRequest.FhirVersion,
            scenarioRequest.ScenarioId,
            scenarioRequest.Parameters,
            scenarioRequest.Tag,
            scenarioRequest.ResolvedReferences);

        if (result is null)
        {
            return new BadRequestObjectResult(new { error = $"Unknown scenarioId '{scenarioRequest.ScenarioId}'." });
        }

        return new OkObjectResult(result);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: PASS, 6/6 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes/ScenarioRequest.cs backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs
git commit -m "Add POST /api/fakes/scenario endpoint"
```

---

### Task 7: Resource endpoint

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/Fakes/ResourceGenerationRequest.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`

**Interfaces:**
- Consumes: `ObservationStateDiscovery.Create` (Task 3), `Ignixa.FhirFakes.Builders.PatientBuilderFactory`, `Ignixa.FhirFakes.SchemaBasedFhirResourceFaker`, `Ignixa.FhirFakes.EdgeCases.EdgeCaseCatalog`/`EdgeCasePipeline`.
- Produces: `FakesService.GenerateResource(...) : JsonObject`, `POST /api/fakes/resource` route. This is the last backend task — Task 8 onward is frontend.

- [ ] **Step 1: Write the request DTO**

Create `backend/src/Ignixa.Lab.Functions/Models/Fakes/ResourceGenerationRequest.cs`:

```csharp
namespace Ignixa.Lab.Functions.Models.Fakes;

public sealed class ResourceGenerationRequest
{
    public string FhirVersion { get; init; } = "r4";
    public required string ResourceType { get; init; }
    public int Seed { get; init; } = 42;
    public string Density { get; init; } = "Minimal";
    public string? FirstName { get; init; }
    public string? FamilyName { get; init; }
    public string? City { get; init; }
    public string? ObservationState { get; init; }
    public IReadOnlyList<string>? EdgeCaseSelectors { get; init; }
    public bool IncludeInvalid { get; init; }
}
```

- [ ] **Step 2: Write the failing test**

Add to `backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs`:

```csharp
    [Fact]
    public async Task GenerateResource_PatientWithSameSeed_IsDeterministic()
    {
        var functions = CreateFunctions();
        var request1 = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1234 });
        var request2 = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1234 });

        var result1 = await functions.GenerateResource(request1, CancellationToken.None);
        var result2 = await functions.GenerateResource(request2, CancellationToken.None);

        var body1 = JsonSerializer.Serialize(result1.Should().BeOfType<OkObjectResult>().Subject.Value);
        var body2 = JsonSerializer.Serialize(result2.Should().BeOfType<OkObjectResult>().Subject.Value);
        using var doc1 = JsonDocument.Parse(body1);
        using var doc2 = JsonDocument.Parse(body2);
        doc1.RootElement.GetProperty("resource").GetProperty("id").GetString()
            .Should().Be(doc2.RootElement.GetProperty("resource").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GenerateResource_ObservationWithState_UsesRequestedState()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Observation", observationState = "BloodGlucose", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("resource").GetProperty("resourceType").GetString().Should().Be("Observation");
    }

    [Fact]
    public async Task GenerateResource_WithEdgeCaseSelectors_ReturnsAManifest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1, edgeCaseSelectors = new[] { "unicode" } });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("manifest").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GenerateResource_NoEdgeCaseSelectors_ReturnsNullManifest()
    {
        var functions = CreateFunctions();
        var request = BuildJsonPostRequest(new { resourceType = "Patient", seed = 1 });

        var result = await functions.GenerateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("manifest").ValueKind.Should().Be(JsonValueKind.Null);
    }
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test Ignixa.Lab.sln --filter FakesFunctionsTests -v minimal`
Expected: FAIL to build — `FakesFunctions.GenerateResource` doesn't exist yet.

- [ ] **Step 4: Add `GenerateResource` to `FakesService`**

In `backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs`, add `ObservationStateDiscovery` to the primary constructor:

```csharp
public sealed class FakesService(
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery)
```

Add this method (needs `using Ignixa.FhirFakes;`, `using Ignixa.FhirFakes.Builders;`, `using Ignixa.FhirFakes.EdgeCases;` at the top of the file):

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

        JsonObject? manifestJson = null;
        if (edgeCaseSelectors is { Count: > 0 })
        {
            var catalog = EdgeCaseCatalog.CreateDefault();
            var strategies = catalog.Resolve(edgeCaseSelectors, out _);
            var pipeline = new EdgeCasePipeline(seed, schemaProvider);
            var manifest = pipeline.Apply(resource, strategies, includeInvalid);
            manifestJson = new JsonObject
            {
                ["resourceId"] = manifest.ResourceId,
                ["seed"] = manifest.Seed,
                ["mutations"] = new JsonArray(manifest.Mutations.Select(m => (JsonNode)new JsonObject
                {
                    ["category"] = m.Category,
                    ["path"] = m.Path,
                    ["before"] = m.Before,
                    ["after"] = m.After,
                    ["description"] = m.Description,
                }).ToArray()),
            };
        }

        return new JsonObject
        {
            ["resource"] = JsonNode.Parse(resource.SerializeToString()),
            ["manifest"] = manifestJson,
        };
    }

    /// <summary>
    /// Instance method (not static) because the Observation-with-state path needs
    /// <c>observationStateDiscovery</c>. Patient uses <see cref="PatientBuilderFactory"/>
    /// directly; Observation with a requested clinical state goes through
    /// <see cref="ScenarioBuilder"/>.AddObservation(ObservationState) — the real,
    /// public entry point that consumes an <c>ObservationState</c> (there is no
    /// direct <c>ObservationBuilder.FromState(...)</c> — <c>ObservationBuilder</c>
    /// has its own separate fluent API unrelated to <c>ObservationState</c>) —
    /// and the resulting <see cref="ScenarioContext"/>'s single Observation is
    /// extracted from <c>AllResources</c>. Everything else uses the generic
    /// schema-driven faker.
    /// </summary>
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
        if (string.Equals(resourceType, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            var builder = PatientBuilderFactory.Create(schemaProvider, seed);
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                builder = builder.WithGivenName(firstName);
            }

            if (!string.IsNullOrWhiteSpace(familyName))
            {
                builder = builder.WithFamilyName(familyName);
            }

            if (!string.IsNullOrWhiteSpace(city))
            {
                builder = builder.WithCity(city);
            }

            return builder.Build();
        }

        if (string.Equals(resourceType, "Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(observationState))
        {
            var state = observationStateDiscovery.Create(observationState);
            if (state != null)
            {
                var context = new ScenarioBuilder(schemaProvider).AddObservation(state).Build();
                var observation = context.AllResources.FirstOrDefault(r => r.ResourceType == "Observation");
                if (observation != null)
                {
                    return observation;
                }
            }
        }

        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density };
        return faker.Generate(resourceType);
    }
```

- [ ] **Step 5: Add `GenerateResource` to `FakesFunctions`**

Add to `backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs`:

```csharp
    [Function("FakesResource")]
    public async Task<IActionResult> GenerateResource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "fakes/resource")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ResourceGenerationRequest? resourceRequest;
        try
        {
            resourceRequest = await JsonSerializer.DeserializeAsync<ResourceGenerationRequest>(
                request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid request body: {ex.Message}" });
        }

        if (resourceRequest is null || string.IsNullOrWhiteSpace(resourceRequest.ResourceType))
        {
            return new BadRequestObjectResult(new { error = "A 'resourceType' is required." });
        }

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

        return new OkObjectResult(result);
    }
```

- [ ] **Step 6: Run all backend tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln -v minimal`
Expected: PASS, all tests including the new `FakesFunctionsTests` (10 total in that class).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/Fakes/ResourceGenerationRequest.cs backend/src/Ignixa.Lab.Functions/Services/Fakes/FakesService.cs backend/src/Ignixa.Lab.Functions/Functions/FakesFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FakesFunctionsTests.cs
git commit -m "Add POST /api/fakes/resource endpoint"
```

---

### Task 8: Frontend types + API client

**Files:**
- Create: `frontend/src/benches/fakes/fakesTypes.ts`
- Create: `frontend/src/benches/fakes/fakesApi.ts`
- Create: `frontend/src/benches/fakes/edgeCaseDescriptions.ts`

**Interfaces:**
- Produces: `FakesMetadata`, `PopulationResult`, `ScenarioResult`, `ResourceResult` types and `getFakesMetadata`, `generatePopulation`, `generateScenario`, `generateResource` functions — consumed by Task 9's `FakesBench.tsx`.

- [ ] **Step 1: Write the types**

Create `frontend/src/benches/fakes/fakesTypes.ts`:

```typescript
export interface ScenarioParameterMetadata {
  name: string;
  type: string;
  defaultValue: unknown;
}

export interface ScenarioMetadata {
  id: string;
  parameters: ScenarioParameterMetadata[];
}

export interface EdgeCaseCategoryMetadata {
  id: string;
  intent: 'PreservesValidity' | 'MayViolate' | 'AlwaysInvalid';
}

export interface EdgeCaseFamilyMetadata {
  family: string;
  categories: EdgeCaseCategoryMetadata[];
}

export interface FakesMetadata {
  fhirVersions: string[];
  populationStates: string[];
  scenarios: ScenarioMetadata[];
  resourceTypes: string[];
  observationStates: string[];
  edgeCaseFamilies: EdgeCaseFamilyMetadata[];
}

export interface PopulationSummary {
  byType: Record<string, number>;
  byGender: Record<string, number>;
  byCity: Record<string, number>;
  ageBuckets: Record<string, number>;
}

export interface PopulationResult {
  patients: Record<string, unknown>[];
  resources: Record<string, unknown>[];
  summary: PopulationSummary;
}

export interface ScenarioResult {
  patient: Record<string, unknown> | null;
  resources: Record<string, unknown>[];
  bundle: Record<string, unknown>;
}

export interface MutationRecord {
  category: string;
  path: string;
  before: string | null;
  after: string | null;
  description: string;
}

export interface MutationManifest {
  resourceId: string;
  seed: number;
  mutations: MutationRecord[];
}

export interface ResourceResult {
  resource: Record<string, unknown>;
  manifest: MutationManifest | null;
}
```

- [ ] **Step 2: Write the API client**

Create `frontend/src/benches/fakes/fakesApi.ts`:

```typescript
import type { FakesMetadata, PopulationResult, ResourceResult, ScenarioResult } from './fakesTypes';

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null;
    throw new Error(body?.error ?? `Request failed with status ${response.status}`);
  }

  return (await response.json()) as T;
}

export function getFakesMetadata(signal?: AbortSignal): Promise<FakesMetadata> {
  return request<FakesMetadata>('/api/fakes/metadata', { signal });
}

export function generatePopulation(
  body: { fhirVersion: string; source: string; count: number },
  signal?: AbortSignal,
): Promise<PopulationResult> {
  return request<PopulationResult>('/api/fakes/population', { method: 'POST', body: JSON.stringify(body), signal });
}

export function generateScenario(
  body: {
    fhirVersion: string;
    scenarioId: string;
    parameters?: Record<string, unknown>;
    tag?: string;
    resolvedReferences: boolean;
  },
  signal?: AbortSignal,
): Promise<ScenarioResult> {
  return request<ScenarioResult>('/api/fakes/scenario', { method: 'POST', body: JSON.stringify(body), signal });
}

export function generateResource(
  body: {
    fhirVersion: string;
    resourceType: string;
    seed: number;
    density: string;
    firstName?: string;
    familyName?: string;
    city?: string;
    observationState?: string;
    edgeCaseSelectors?: string[];
    includeInvalid: boolean;
  },
  signal?: AbortSignal,
): Promise<ResourceResult> {
  return request<ResourceResult>('/api/fakes/resource', { method: 'POST', body: JSON.stringify(body), signal });
}
```

- [ ] **Step 3: Write the edge-case description lookup table**

Create `frontend/src/benches/fakes/edgeCaseDescriptions.ts`:

```typescript
/**
 * Curated blurb text for edge-case categories, keyed by the real category id
 * `Ignixa.FhirFakes`'s `EdgeCaseCatalog` reports. Wording is carried over from
 * the original design mockup where the id matches; categories with no entry
 * here fall back to a humanized version of their id (see `describeEdgeCase`).
 */
const EDGE_CASE_DESCRIPTIONS: Record<string, string> = {
  'unicode.cjk': 'CJK (Chinese/Japanese/Korean) characters',
  'unicode.rtl': 'Right-to-left script (Arabic / Hebrew)',
  'unicode.combining': 'Appends combining diacritical marks',
  'unicode.emoji': 'Emoji incl. ZWJ sequences & surrogate pairs',
  'unicode.zero-width': 'Injects zero-width chars (U+200B/C/D, U+FEFF)',
  'unicode.multi-script-long': '~40-fragment Latin + CJK + RTL + Cyrillic + emoji',
  'temporal.leap-year': 'Sets the date to Feb 29 of a leap year',
  'temporal.year-boundary': 'Sets the date to Dec 31 or Jan 1',
  'temporal.far-past': 'Far-past but spec-valid date (0001-01-01)',
  'temporal.far-future': 'Far-future but spec-valid date (9999-12-31)',
  'temporal.partial-precision': 'Reduces to year-only or year-month precision',
  'string.max-length': 'Replaces text with a very long ASCII string',
  'string.injection-like': 'SQL / HTML / template-injection-like payloads',
  'string.control-chars': 'Injects C0 control characters (disallowed by grammar)',
  'string.whitespace-only': 'Sets text to whitespace-only',
  'string.empty-present': 'Sets text to empty string (invalid per spec)',
};

/** Humanizes a category id like `string.max-length` into `Max Length` as a fallback when no curated entry exists. */
function humanize(categoryId: string): string {
  const leaf = categoryId.includes('.') ? categoryId.split('.').slice(1).join('.') : categoryId;
  return leaf
    .split(/[-.]/)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

export function describeEdgeCase(categoryId: string): string {
  return EDGE_CASE_DESCRIPTIONS[categoryId] ?? humanize(categoryId);
}
```

- [ ] **Step 4: Verify it builds**

Run: `cd frontend && npm run build`
Expected: build succeeds (these are new, unused-so-far files — `tsc` should still type-check them cleanly since they have no dependents yet; if `noUnusedLocals`-style strictness flags anything, it would only be on unused imports, and none are introduced here).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/benches/fakes/fakesTypes.ts frontend/src/benches/fakes/fakesApi.ts frontend/src/benches/fakes/edgeCaseDescriptions.ts
git commit -m "Add Fakes bench types, API client, and edge-case description lookup"
```

---

### Task 9: `FakesBench.tsx` — all three modes, wired into `BenchesApp`

**Files:**
- Create: `frontend/src/benches/fakes/FakesBench.tsx`
- Modify: `frontend/src/benches/BenchesApp.tsx`

**Interfaces:**
- Consumes: everything from Task 8 (`fakesApi.ts`, `fakesTypes.ts`, `edgeCaseDescriptions.ts`), `Card`/`ErrorBanner`/`Pills`/`PillItem` from `../components/primitives`, `engineBadgeStyle`/`monoFont`/`sectionLabelStyle`/`monoInputStyle`/`primaryButtonStyle` from `../components/styles`, `useIsNarrowViewport` from `../../hooks/useIsNarrowViewport`.
- Produces: `FakesBench` component, mounted as `BenchesApp`'s 4th tab. Task 10 adds the cross-bench send/receive wiring on top of this.

- [ ] **Step 1: Write `FakesBench.tsx`**

Create `frontend/src/benches/fakes/FakesBench.tsx`:

```tsx
import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoInputStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { describeEdgeCase } from './edgeCaseDescriptions';
import { generatePopulation, generateResource, generateScenario, getFakesMetadata } from './fakesApi';
import type { FakesMetadata, PopulationResult, ResourceResult, ScenarioResult } from './fakesTypes';

type FakesMode = 'population' | 'scenario' | 'resource';

const MODE_ITEMS: PillItem<FakesMode>[] = [
  { id: 'population', label: 'Population' },
  { id: 'scenario', label: 'Scenario' },
  { id: 'resource', label: 'Resource' },
];

const DENSITY_ITEMS: PillItem<string>[] = [
  { id: 'Minimal', label: 'Minimal' },
  { id: 'Maximum', label: 'Maximum' },
];

/** Props for {@link FakesBench}. */
export interface FakesBenchProps {
  /** Called with the generated payload when the user sends it to another bench. Omitted in Population mode, which has no single-resource payload to send. */
  onSend?: (payload: Record<string, unknown>, label: string) => void;
}

export function FakesBench({ onSend }: FakesBenchProps) {
  const stacked = useIsNarrowViewport(720);
  const [mode, setMode] = useState<FakesMode>('scenario');
  const [metadata, setMetadata] = useState<FakesMetadata | null>(null);
  const [metadataError, setMetadataError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getFakesMetadata(controller.signal)
      .then(setMetadata)
      .catch((error: Error) => setMetadataError(error.message));
    return () => controller.abort();
  }, []);

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Fakes</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Generate realistic synthetic FHIR data — populations, clinical scenarios, and edge-case fuzzing.
        </span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>ignixa-fakes 0.5</span>
      </div>

      {metadataError ? <ErrorBanner message={`Failed to load Fakes metadata: ${metadataError}`} /> : null}

      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <Pills items={MODE_ITEMS} activeId={mode} onChange={setMode} />
      </div>

      {!metadata ? (
        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)' }}>Loading…</span>
      ) : (
        <>
          {mode === 'population' ? <PopulationPanel metadata={metadata} stacked={stacked} /> : null}
          {mode === 'scenario' ? <ScenarioPanel metadata={metadata} stacked={stacked} onSend={onSend} /> : null}
          {mode === 'resource' ? <ResourcePanel metadata={metadata} stacked={stacked} onSend={onSend} /> : null}
        </>
      )}
    </div>
  );
}

function twoColumnStyle(stacked: boolean, minmax: string): CSSProperties {
  return { display: 'grid', gridTemplateColumns: stacked ? '1fr' : `${minmax} 1fr`, gap: 14, alignItems: 'start' };
}

function PopulationPanel({ metadata, stacked }: { metadata: FakesMetadata; stacked: boolean }) {
  const [source, setSource] = useState(metadata.populationStates[0] ?? 'Massachusetts');
  const [count, setCount] = useState(10);
  const [result, setResult] = useState<PopulationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generatePopulation({ fhirVersion: 'r4', source, count })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={twoColumnStyle(stacked, 'minmax(360px,38%)')}>
      <Card style={{ minWidth: 0 }}>
        <span style={sectionLabelStyle}>Source · US state</span>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 7 }}>
          {metadata.populationStates.map((state) => (
            <button
              key={state}
              type="button"
              onClick={() => setSource(state)}
              style={{
                font: 'inherit',
                fontSize: 12,
                fontWeight: 600,
                padding: '6px 12px',
                borderRadius: 99,
                cursor: 'pointer',
                background: source === state ? 'var(--chip-vio-bg)' : 'var(--panel)',
                color: source === state ? 'var(--chip-vio-fg)' : 'var(--text3)',
                border: `1px solid ${source === state ? 'var(--accent-border)' : 'var(--border2)'}`,
              }}
            >
              {state}
            </button>
          ))}
        </div>

        <span style={sectionLabelStyle}>Patient count</span>
        <input
          type="range"
          min={1}
          max={100}
          value={count}
          onChange={(event) => setCount(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />
        <span style={{ fontSize: 11, color: 'var(--text4)' }}>{count} patients (capped at 100 in this release)</span>

        <button type="button" onClick={generate} disabled={isLoading} style={primaryButtonStyle}>
          {isLoading ? 'Generating…' : '⚡ Generate cohort'}
        </button>
      </Card>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
        {error ? <ErrorBanner message={error} /> : null}
        {result ? (
          <Card>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 10 }}>
              <span style={sectionLabelStyle}>Cohort preview</span>
              <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)' }}>
                {result.patients.length} patients · {result.resources.length} resources
              </span>
            </div>
            <SummaryBars title="Resource types" counts={result.summary.byType} />
            <SummaryBars title="Gender" counts={result.summary.byGender} />
            <SummaryBars title="Age bands" counts={result.summary.ageBuckets} />
            <SummaryBars title="Top locations" counts={result.summary.byCity} />
          </Card>
        ) : (
          <Card style={{ alignItems: 'center', textAlign: 'center', padding: '48px 24px' }}>
            <span style={{ fontSize: 14, fontWeight: 700 }}>No cohort generated yet</span>
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>
              Configure the source, count, then hit Generate cohort.
            </span>
          </Card>
        )}
      </div>
    </div>
  );
}

function SummaryBars({ title, counts }: { title: string; counts: Record<string, number> }) {
  const entries = Object.entries(counts).filter(([, count]) => count > 0);
  const max = Math.max(1, ...entries.map(([, count]) => count));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }}>
      <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--text3)' }}>{title}</span>
      {entries.map(([label, count]) => (
        <div key={label} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{ fontSize: 11, color: 'var(--text2)', width: 90, flex: 'none' }}>{label}</span>
          <div style={{ flex: 1, height: 8, borderRadius: 99, background: 'var(--inset)', overflow: 'hidden' }}>
            <div style={{ height: '100%', width: `${(count / max) * 100}%`, background: 'var(--accent)', borderRadius: 99 }} />
          </div>
          <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)', width: 26, textAlign: 'right', flex: 'none' }}>
            {count}
          </span>
        </div>
      ))}
    </div>
  );
}

function ScenarioPanel({
  metadata,
  stacked,
  onSend,
}: {
  metadata: FakesMetadata;
  stacked: boolean;
  onSend?: (payload: Record<string, unknown>, label: string) => void;
}) {
  const [scenarioId, setScenarioId] = useState(metadata.scenarios[0]?.id ?? '');
  const [paramValues, setParamValues] = useState<Record<string, unknown>>({});
  const [tag, setTag] = useState('');
  const [resolvedReferences, setResolvedReferences] = useState(false);
  const [result, setResult] = useState<ScenarioResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const scenario = useMemo(() => metadata.scenarios.find((s) => s.id === scenarioId), [metadata.scenarios, scenarioId]);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generateScenario({ fhirVersion: 'r4', scenarioId, parameters: paramValues, tag: tag || undefined, resolvedReferences })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <Card>
        <span style={sectionLabelStyle}>Predefined clinical scenarios</span>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(200px,1fr))', gap: 9 }}>
          {metadata.scenarios.map((s) => (
            <div
              key={s.id}
              onClick={() => {
                setScenarioId(s.id);
                setParamValues({});
              }}
              style={{
                padding: '12px 13px',
                borderRadius: 10,
                cursor: 'pointer',
                background: scenarioId === s.id ? 'var(--chip-vio-bg)' : 'var(--panel)',
                border: `1px solid ${scenarioId === s.id ? 'var(--accent-border)' : 'var(--border)'}`,
                fontSize: 13.5,
                fontWeight: 700,
              }}
            >
              {s.id}
            </div>
          ))}
        </div>
      </Card>

      <div style={twoColumnStyle(stacked, 'minmax(300px,32%)')}>
        <Card style={{ minWidth: 0 }}>
          {scenario?.parameters.map((param) => (
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
            <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1 }}>Resolved references</span>
            <input type="checkbox" checked={resolvedReferences} onChange={(event) => setResolvedReferences(event.target.checked)} />
          </div>

          <button type="button" onClick={generate} disabled={isLoading || !scenarioId} style={primaryButtonStyle}>
            {isLoading ? 'Generating…' : '⚡ Generate scenario'}
          </button>
        </Card>

        <Card style={{ minHeight: 360, minWidth: 0 }}>
          {error ? <ErrorBanner message={error} /> : null}
          {result ? (
            <>
              <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                {result.resources.length} resources
              </span>
              <pre
                style={{
                  margin: 0,
                  padding: '12px 14px',
                  borderRadius: 8,
                  background: 'var(--code)',
                  border: '1px solid var(--border)',
                  fontFamily: monoFont,
                  fontSize: 11,
                  lineHeight: 1.55,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  maxHeight: 420,
                  overflow: 'auto',
                }}
              >
                {JSON.stringify(result.bundle, null, 2)}
              </pre>
              {onSend ? (
                <SendBar
                  onSend={(bench) => onSend({ single: result.patient, array: result.resources }[bench === 'sqlonfhir' ? 'array' : 'single'], `${scenarioId} · ${result.resources.length} resources`)}
                />
              ) : null}
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a scenario to see its resources here.</span>
          )}
        </Card>
      </div>
    </div>
  );
}

function ScenarioParameterControl({
  param,
  value,
  onChange,
}: {
  param: { name: string; type: string; defaultValue: unknown };
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  if (param.name === 'age') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Age</span>
        <input
          type="range"
          min={0}
          max={95}
          value={Number(value ?? 0)}
          onChange={(event) => onChange(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />
        <span style={{ fontSize: 11, color: 'var(--text3)' }}>{Number(value ?? 0)}</span>
      </div>
    );
  }

  if (param.name === 'gender') {
    const genderItems: PillItem<string>[] = [
      { id: 'male', label: 'Male' },
      { id: 'female', label: 'Female' },
    ];
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Gender</span>
        <Pills items={genderItems} activeId={String(value ?? 'male')} onChange={onChange} />
      </div>
    );
  }

  if (param.name === 'severity') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Severity</span>
        <input
          type="range"
          min={1}
          max={3}
          value={Number(value ?? 1)}
          onChange={(event) => onChange(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />
      </div>
    );
  }

  if (param.type === 'Boolean') {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1 }}>{param.name}</span>
        <input type="checkbox" checked={Boolean(value)} onChange={(event) => onChange(event.target.checked)} />
      </div>
    );
  }

  if (param.type === 'Int32' || param.type === 'Decimal') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>{param.name}</span>
        <input
          value={String(value ?? '')}
          onChange={(event) => onChange(Number(event.target.value))}
          style={monoInputStyle}
        />
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <span style={sectionLabelStyle}>{param.name}</span>
      <input value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} style={monoInputStyle} />
    </div>
  );
}

function ResourcePanel({
  metadata,
  stacked,
  onSend,
}: {
  metadata: FakesMetadata;
  stacked: boolean;
  onSend?: (payload: Record<string, unknown>, label: string) => void;
}) {
  const [resourceType, setResourceType] = useState('Patient');
  const [density, setDensity] = useState('Minimal');
  const [seed, setSeed] = useState(42);
  const [observationState, setObservationState] = useState(metadata.observationStates[0] ?? '');
  const [firstName, setFirstName] = useState('');
  const [familyName, setFamilyName] = useState('');
  const [edgeCaseOn, setEdgeCaseOn] = useState(true);
  const [includeInvalid, setIncludeInvalid] = useState(false);
  const [selectedFamilies, setSelectedFamilies] = useState<Record<string, boolean>>({ Unicode: true, Temporal: true });
  const [result, setResult] = useState<ResourceResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    const selectors = edgeCaseOn ? Object.keys(selectedFamilies).filter((family) => selectedFamilies[family]) : undefined;
    generateResource({
      fhirVersion: 'r4',
      resourceType,
      seed,
      density,
      firstName: firstName || undefined,
      familyName: familyName || undefined,
      observationState: resourceType === 'Observation' ? observationState : undefined,
      edgeCaseSelectors: selectors,
      includeInvalid,
    })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={twoColumnStyle(stacked, 'minmax(360px,42%)')}>
        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Resource type</span>
          <select value={resourceType} onChange={(event) => setResourceType(event.target.value)} style={monoInputStyle}>
            {metadata.resourceTypes.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>

          {resourceType === 'Observation' ? (
            <>
              <span style={sectionLabelStyle}>Clinical state</span>
              <select value={observationState} onChange={(event) => setObservationState(event.target.value)} style={monoInputStyle}>
                {metadata.observationStates.map((state) => (
                  <option key={state} value={state}>
                    {state}
                  </option>
                ))}
              </select>
            </>
          ) : null}

          {resourceType === 'Patient' ? (
            <div style={{ display: 'flex', gap: 8 }}>
              <input value={firstName} onChange={(event) => setFirstName(event.target.value)} placeholder="First name" style={{ ...monoInputStyle, flex: 1 }} />
              <input value={familyName} onChange={(event) => setFamilyName(event.target.value)} placeholder="Surname" style={{ ...monoInputStyle, flex: 1 }} />
            </div>
          ) : null}

          <span style={sectionLabelStyle}>Generation density</span>
          <Pills items={DENSITY_ITEMS} activeId={density} onChange={setDensity} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Seed</span>
            <input
              value={String(seed)}
              onChange={(event) => setSeed(Number(event.target.value) || 0)}
              style={{ ...monoInputStyle, width: 110 }}
            />
            <button type="button" onClick={() => setSeed(Math.floor(Math.random() * 100000))} style={{ ...monoInputStyle, cursor: 'pointer' }}>
              ⟳
            </button>
          </div>

          <button type="button" onClick={generate} disabled={isLoading} style={primaryButtonStyle}>
            {isLoading ? 'Generating…' : '⚡ Generate resource'}
          </button>
        </Card>

        <Card style={{ minHeight: 360, minWidth: 0 }}>
          {error ? <ErrorBanner message={error} /> : null}
          {result ? (
            <>
              <pre
                style={{
                  margin: 0,
                  padding: '12px 14px',
                  borderRadius: 8,
                  background: 'var(--code)',
                  border: '1px solid var(--border)',
                  fontFamily: monoFont,
                  fontSize: 11,
                  lineHeight: 1.55,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  maxHeight: 420,
                  overflow: 'auto',
                }}
              >
                {JSON.stringify(result.resource, null, 2)}
              </pre>
              {result.manifest ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <span style={{ fontSize: 11, color: 'var(--text4)' }}>{result.manifest.mutations.length} mutations applied</span>
                  {result.manifest.mutations.map((mutation, index) => (
                    <div key={index} style={{ display: 'flex', flexDirection: 'column', gap: 3, padding: '8px 10px', borderRadius: 8, background: 'var(--code)', border: '1px solid var(--border)' }}>
                      <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)' }}>{mutation.path}</span>
                      <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--fail)' }}>{mutation.before}</span>
                      <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--pass)' }}>{mutation.after}</span>
                    </div>
                  ))}
                </div>
              ) : null}
              {onSend ? (
                <SendBar onSend={(bench) => onSend(bench === 'sqlonfhir' ? [result.resource] : result.resource, `${resourceType} · edge-cased`)} />
              ) : null}
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a resource to see it here.</span>
          )}
        </Card>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <span style={{ fontSize: 14, fontWeight: 700, flex: 1 }}>Edge-case fuzzing</span>
          <input type="checkbox" checked={edgeCaseOn} onChange={(event) => setEdgeCaseOn(event.target.checked)} />
        </div>
        {edgeCaseOn ? (
          <>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(280px,1fr))', gap: 12 }}>
              {metadata.edgeCaseFamilies.map((family) => (
                <div key={family.family} style={{ padding: '12px 13px', borderRadius: 10, background: 'var(--code)', border: '1px solid var(--border)' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span
                      onClick={() => setSelectedFamilies((current) => ({ ...current, [family.family]: !current[family.family] }))}
                      style={{
                        fontFamily: monoFont,
                        fontSize: 11,
                        fontWeight: 600,
                        padding: '4px 11px',
                        borderRadius: 99,
                        cursor: 'pointer',
                        background: selectedFamilies[family.family] ? 'var(--chip-vio-bg)' : 'var(--inset)',
                      }}
                    >
                      {family.family}
                    </span>
                  </div>
                  {family.categories.map((category) => (
                    <div key={category.id} style={{ padding: '4px 0' }}>
                      <span style={{ fontFamily: monoFont, fontSize: 11.5, fontWeight: 600 }}>{category.id}</span>
                      <div style={{ fontSize: 10.5, color: 'var(--text3)' }}>{describeEdgeCase(category.id)}</div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1 }}>Include non-validity-preserving strategies</span>
              <input type="checkbox" checked={includeInvalid} onChange={(event) => setIncludeInvalid(event.target.checked)} />
            </div>
          </>
        ) : null}
      </Card>
    </div>
  );
}

function SendBar({ onSend }: { onSend: (bench: 'fhirpath' | 'fml' | 'sqlonfhir') => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <span style={{ fontSize: 10.5, color: 'var(--text4)', textTransform: 'uppercase', letterSpacing: '.1em' }}>Send to</span>
      <button type="button" onClick={() => onSend('fhirpath')} style={{ ...monoInputStyle, cursor: 'pointer' }}>
        FHIRPath
      </button>
      <button type="button" onClick={() => onSend('fml')} style={{ ...monoInputStyle, cursor: 'pointer' }}>
        FML
      </button>
      <button type="button" onClick={() => onSend('sqlonfhir')} style={{ ...monoInputStyle, cursor: 'pointer' }}>
        SQL on FHIR
      </button>
    </div>
  );
}
```

- [ ] **Step 2: Wire `FakesBench` into `BenchesApp.tsx`**

In `frontend/src/benches/BenchesApp.tsx`, apply these changes:

Change the imports (add the `FakesBench` import and widen `BenchId`):

```typescript
import { FakesBench } from './fakes/FakesBench';
```

Change `type BenchId = 'fhirpath' | 'fml' | 'sqlonfhir';` to:

```typescript
type BenchId = 'fhirpath' | 'fml' | 'sqlonfhir' | 'fakes';
```

Add to `BENCH_TABS`:

```typescript
const BENCH_TABS: PillItem<BenchId>[] = [
  { id: 'fhirpath', label: 'FHIRPath' },
  { id: 'fml', label: 'FML' },
  { id: 'sqlonfhir', label: 'SQL on FHIR' },
  { id: 'fakes', label: 'Fakes' },
];
```

Add to the `<main>` block:

```tsx
      <main>
        {bench === 'fhirpath' ? <FhirPathBench /> : null}
        {bench === 'fml' ? <FmlBench /> : null}
        {bench === 'sqlonfhir' ? <SofBench /> : null}
        {bench === 'fakes' ? <FakesBench /> : null}
      </main>
```

- [ ] **Step 3: Verify it builds and run it in the browser**

Run: `cd frontend && npm run build`
Expected: build succeeds.

Run: `cd frontend && npm run dev`, open the Expression Benches page, click the new "Fakes" tab. Verify: Population mode shows US state chips and generates a cohort with a summary; Scenario mode shows scenario cards, generates a scenario, and shows its bundle JSON; Resource mode generates a Patient/Observation/other resource, shows its JSON, and (with edge-case fuzzing on) shows a mutation manifest.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/benches/fakes/FakesBench.tsx frontend/src/benches/BenchesApp.tsx
git commit -m "Add FakesBench with Population/Scenario/Resource modes, wire into BenchesApp"
```

---

### Task 10: Cross-bench send/receive integration

**Files:**
- Modify: `frontend/src/benches/BenchesApp.tsx`
- Modify: `frontend/src/benches/fakes/FakesBench.tsx`
- Modify: `frontend/src/benches/fhirpath/FhirPathBench.tsx`
- Modify: `frontend/src/benches/fml/FmlBench.tsx`
- Modify: `frontend/src/benches/sof/SofBench.tsx`

**Interfaces:**
- Consumes: `FakesBench`'s `onSend` prop (Task 9).
- Produces: the "⚡ Fakes ↗" button on each of the three original benches, and the return-banner/sent-toast UX on `FakesBench`, matching the design mockup.

- [ ] **Step 1: Lift shared state into `BenchesApp.tsx`**

In `frontend/src/benches/BenchesApp.tsx`, add state for which bench Fakes should return to, and a sent-toast, plus handlers. Replace the component body:

```tsx
export function BenchesApp() {
  const theme = useTheme();
  const [bench, setBench] = useState<BenchId>('fhirpath');
  const [fakesReturnTo, setFakesReturnTo] = useState<Exclude<BenchId, 'fakes'> | null>(null);
  const [sentToast, setSentToast] = useState<{ bench: BenchId; label: string } | null>(null);
  const [fhirpathSeed, setFhirpathSeed] = useState<{ text: string } | null>(null);
  const [fmlSeed, setFmlSeed] = useState<{ text: string } | null>(null);
  const [sofSeed, setSofSeed] = useState<{ text: string } | null>(null);

  const openFakesFrom = (fromBench: Exclude<BenchId, 'fakes'>) => {
    setBench('fakes');
    setFakesReturnTo(fromBench);
  };

  const handleSend = (targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => {
    const text = JSON.stringify(payload, null, 2);
    if (targetBench === 'fhirpath') {
      setFhirpathSeed({ text });
    } else if (targetBench === 'fml') {
      setFmlSeed({ text });
    } else {
      setSofSeed({ text });
    }

    setBench(targetBench);
    setFakesReturnTo(null);
    setSentToast({ bench: targetBench, label });
    setTimeout(() => setSentToast(null), 6000);
  };

  return (
    <div style={{ ...shellStyle, ...(theme.variables as CSSProperties) }}>
      <header style={topBarStyle}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
          <div aria-hidden="true" style={{ width: 30, height: 30, borderRadius: 8, background: 'var(--grad)', flex: 'none' }} />
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            <span style={{ fontSize: 14.5, fontWeight: 700, letterSpacing: '-.01em' }}>Ignixa Lab</span>
            <span style={{ fontFamily: monoFont, fontSize: 9, letterSpacing: '.14em', color: 'var(--text3)', textTransform: 'uppercase' }}>
              Expression benches
            </span>
          </div>
        </div>

        <div style={{ marginLeft: 20 }}>
          <Pills items={BENCH_TABS} activeId={bench} onChange={setBench} />
        </div>

        <a href="./" style={{ fontSize: 12, fontWeight: 600, color: 'var(--text3)', textDecoration: 'none', padding: '6px 10px' }}>
          Conformance ↗
        </a>

        <div style={{ flex: 1 }} />

        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)' }}>
          {bench === 'fhirpath' ? 'live engine' : 'mock engine · exploration'}
        </span>

        <button
          type="button"
          onClick={theme.toggle}
          title="Toggle theme"
          style={{
            width: 32,
            height: 32,
            display: 'grid',
            placeItems: 'center',
            border: '1px solid var(--border2)',
            borderRadius: 8,
            background: 'transparent',
            color: 'var(--text3)',
            fontSize: 14,
            cursor: 'pointer',
          }}
        >
          {theme.icon}
        </button>
      </header>

      <main>
        {bench === 'fhirpath' ? <FhirPathBench onOpenFakes={() => openFakesFrom('fhirpath')} fakesSeed={fhirpathSeed} /> : null}
        {bench === 'fml' ? <FmlBench onOpenFakes={() => openFakesFrom('fml')} fakesSeed={fmlSeed} /> : null}
        {bench === 'sqlonfhir' ? <SofBench onOpenFakes={() => openFakesFrom('sqlonfhir')} fakesSeed={sofSeed} /> : null}
        {bench === 'fakes' ? (
          <FakesBench
            returnTo={fakesReturnTo}
            onDismissReturn={() => setFakesReturnTo(null)}
            onSend={(targetBench, payload, label) => handleSend(targetBench, payload, label)}
          />
        ) : null}
      </main>

      {sentToast ? (
        <div
          style={{
            position: 'fixed',
            bottom: 22,
            left: '50%',
            transform: 'translateX(-50%)',
            zIndex: 40,
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            padding: '11px 18px',
            borderRadius: 99,
            background: 'var(--text)',
            color: 'var(--bg)',
            fontSize: 12.5,
            fontWeight: 600,
            boxShadow: '0 8px 24px rgba(0,0,0,.22)',
          }}
        >
          <span style={{ color: '#4ade80' }}>✓</span> Received {sentToast.label}
        </div>
      ) : null}
    </div>
  );
}
```

- [ ] **Step 2: Update `FakesBench` to accept the return-banner and use `onSend` with the target-bench-aware signature**

In `frontend/src/benches/fakes/FakesBench.tsx`, change `FakesBenchProps` and add the return banner:

```tsx
export interface FakesBenchProps {
  returnTo?: 'fhirpath' | 'fml' | 'sqlonfhir' | null;
  onDismissReturn?: () => void;
  onSend?: (targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => void;
}

const BENCH_LABELS: Record<'fhirpath' | 'fml' | 'sqlonfhir', string> = {
  fhirpath: 'FHIRPath',
  fml: 'FML',
  sqlonfhir: 'SQL on FHIR',
};
```

Update the `FakesBench` function signature to `export function FakesBench({ returnTo, onDismissReturn, onSend }: FakesBenchProps)`, and insert this banner right after the mode-tab `<div>` block (before the `{!metadata ? ... }` block):

```tsx
      {returnTo ? (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            flexWrap: 'wrap',
            padding: '11px 16px',
            borderRadius: 10,
            background: 'var(--chip-vio-bg)',
            border: '1px solid var(--accent-border)',
          }}
        >
          <span style={{ fontSize: 13 }}>
            Generating for the <b style={{ color: 'var(--accent)' }}>{BENCH_LABELS[returnTo]}</b> bench — configure a source below, then send it straight in.
          </span>
          <div style={{ flex: 1 }} />
          <button type="button" onClick={onDismissReturn} style={{ fontSize: 12, fontWeight: 600, color: 'var(--text3)', border: 'none', background: 'transparent', cursor: 'pointer' }}>
            Dismiss
          </button>
        </div>
      ) : null}
```

Update the `ScenarioPanel`/`ResourcePanel` invocations inside `FakesBench` to pass `onSend` through unchanged (they already accept an `onSend` callback of the simpler single-target-bench shape) — thread the outer `onSend` into each panel by wrapping it: replace both panel invocations' `onSend={onSend}` usage sites so the wrapping matches the new signature:

```tsx
          {mode === 'scenario' ? (
            <ScenarioPanel
              metadata={metadata}
              stacked={stacked}
              onSend={onSend ? (payload, label) => onSend(sendTargetRef.current, payload, label) : undefined}
            />
          ) : null}
```

This requires tracking which bench button was actually clicked inside `SendBar` and passing it up — simplify by changing `ScenarioPanel`'s and `ResourcePanel`'s `onSend` prop type to already include the target bench (matching `SendBar`'s callback shape), and forward it straight through without an intermediate ref:

```tsx
{mode === 'scenario' ? <ScenarioPanel metadata={metadata} stacked={stacked} onSend={onSend} /> : null}
{mode === 'resource' ? <ResourcePanel metadata={metadata} stacked={stacked} onSend={onSend} /> : null}
```

And in `ScenarioPanel`/`ResourcePanel`, change the `onSend` prop type from `(payload, label) => void` to `(targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => void`, and change their `SendBar` usage from:

```tsx
<SendBar onSend={(bench) => onSend({ single: result.patient, array: result.resources }[bench === 'sqlonfhir' ? 'array' : 'single'], `${scenarioId} · ${result.resources.length} resources`)} />
```

to:

```tsx
<SendBar onSend={(targetBench) => onSend(targetBench, targetBench === 'sqlonfhir' ? result.resources : (result.patient ?? {}), `${scenarioId} · ${result.resources.length} resources`)} />
```

and in `ResourcePanel`, from:

```tsx
<SendBar onSend={(bench) => onSend(bench === 'sqlonfhir' ? [result.resource] : result.resource, `${resourceType} · edge-cased`)} />
```

to:

```tsx
<SendBar onSend={(targetBench) => onSend(targetBench, targetBench === 'sqlonfhir' ? [result.resource] : result.resource, `${resourceType} · edge-cased`)} />
```

- [ ] **Step 3: Add the "⚡ Fakes ↗" button and `fakesSeed` prop to `FhirPathBench`**

In `frontend/src/benches/fhirpath/FhirPathBench.tsx`, change the component signature:

```tsx
export interface FhirPathBenchProps {
  onOpenFakes?: () => void;
  fakesSeed?: { text: string } | null;
}

export function FhirPathBench({ onOpenFakes, fakesSeed }: FhirPathBenchProps) {
```

Add a `useEffect` (import `useEffect` from `'react'` alongside the existing `useMemo, useState`) right after the existing `useState` declarations, to apply an incoming seed:

```tsx
  useEffect(() => {
    if (fakesSeed) {
      setSampleId('custom');
      setResourceText(fakesSeed.text);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fakesSeed]);
```

Add the button after the `{SAMPLE_RESOURCES.map(...)}` block, still inside the same flex row (right before that `</div>` at what's currently line 261):

```tsx
            {onOpenFakes ? (
              <button
                type="button"
                onClick={onOpenFakes}
                title="Generate a test resource with Fakes"
                style={{
                  fontSize: 11,
                  fontWeight: 600,
                  padding: '4px 11px',
                  borderRadius: 99,
                  cursor: 'pointer',
                  background: 'var(--chip-vio-bg)',
                  color: 'var(--accent)',
                  border: '1px solid var(--accent-border)',
                }}
              >
                ⚡ Fakes ↗
              </button>
            ) : null}
```

- [ ] **Step 4: Add the same button + seed prop to `FmlBench`**

In `frontend/src/benches/fml/FmlBench.tsx`, change the signature:

```tsx
export interface FmlBenchProps {
  onOpenFakes?: () => void;
  fakesSeed?: { text: string } | null;
}

export function FmlBench({ onOpenFakes, fakesSeed }: FmlBenchProps) {
```

Import `useEffect` alongside `useMemo, useState`, and add after the existing `useState` declarations:

```tsx
  useEffect(() => {
    if (fakesSeed) {
      setSourceText(fakesSeed.text);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fakesSeed]);
```

Change the "Source resource" header from:

```tsx
          <Card>
            <span style={sectionLabelStyle}>Source resource</span>
```

to:

```tsx
          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ ...sectionLabelStyle, flex: 1 }}>Source resource</span>
              {onOpenFakes ? (
                <button
                  type="button"
                  onClick={onOpenFakes}
                  title="Generate a source resource with Fakes"
                  style={{
                    fontSize: 11,
                    fontWeight: 600,
                    padding: '4px 11px',
                    borderRadius: 99,
                    cursor: 'pointer',
                    background: 'var(--chip-vio-bg)',
                    color: 'var(--accent)',
                    border: '1px solid var(--accent-border)',
                  }}
                >
                  ⚡ Fakes ↗
                </button>
              ) : null}
            </div>
```

- [ ] **Step 5: Add the same button + seed prop to `SofBench`**

In `frontend/src/benches/sof/SofBench.tsx`, change the signature:

```tsx
export interface SofBenchProps {
  onOpenFakes?: () => void;
  fakesSeed?: { text: string } | null;
}

export function SofBench({ onOpenFakes, fakesSeed }: SofBenchProps) {
```

Import `useEffect` alongside `useState`, and add after the existing `useState` declarations:

```tsx
  useEffect(() => {
    if (fakesSeed) {
      setResourcesText(fakesSeed.text);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fakesSeed]);
```

Change the "Resources · JSON array" header from:

```tsx
          <Card>
            <span style={sectionLabelStyle}>Resources · JSON array</span>
```

to:

```tsx
          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ ...sectionLabelStyle, flex: 1 }}>Resources · JSON array</span>
              {onOpenFakes ? (
                <button
                  type="button"
                  onClick={onOpenFakes}
                  title="Generate a population with Fakes"
                  style={{
                    fontSize: 11,
                    fontWeight: 600,
                    padding: '4px 11px',
                    borderRadius: 99,
                    cursor: 'pointer',
                    background: 'var(--chip-vio-bg)',
                    color: 'var(--accent)',
                    border: '1px solid var(--accent-border)',
                  }}
                >
                  ⚡ Fakes ↗
                </button>
              ) : null}
            </div>
```

- [ ] **Step 6: Verify it builds**

Run: `cd frontend && npm run build`
Expected: build succeeds.

- [ ] **Step 7: Manually verify the full round-trip in the browser**

Run: `cd frontend && npm run dev`. On the FHIRPath bench, click "⚡ Fakes ↗" — verify it switches to the Fakes bench in Scenario mode with the return banner showing "Generating for the FHIRPath bench...". Generate a scenario, click "→ Use in FHIRPath" (or the FHIRPath button in the Send-to bar), verify it switches back to FHIRPath with the generated patient in the Test resource box and a "✓ Received ..." toast. Repeat for FML (Source resource) and SQL on FHIR (Resources · JSON array, verify it receives the full resource array, not just one patient).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/benches/BenchesApp.tsx frontend/src/benches/fakes/FakesBench.tsx frontend/src/benches/fhirpath/FhirPathBench.tsx frontend/src/benches/fml/FmlBench.tsx frontend/src/benches/sof/SofBench.tsx
git commit -m "Wire Fakes bench send/receive integration into FHIRPath/FML/SQL on FHIR"
```

---

## Final Verification

- [ ] Run `dotnet test Ignixa.Lab.sln -v minimal` from the repo root — all backend tests pass.
- [ ] Run `cd frontend && npm run lint && npm run build` — both succeed.
- [ ] Manually exercise all three Fakes modes and the full send/receive round-trip with all three target benches, per Task 10 Step 7.
