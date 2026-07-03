# FML and SQL-on-FHIR bench backends — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the FML and SQL-on-FHIR benches' client-side mock interpreters with real HTTP endpoints in `Ignixa.Lab.Functions`, backed by the `Ignixa.FhirMappingLanguage` and `Ignixa.SqlOnFhir` NuGet packages, matching the wire contracts specified in `docs/superpowers/specs/2026-07-02-fml-sof-backend-design.md`.

**Architecture:** Two new Function classes (`FmlFunctions`, `SqlOnFhirFunctions`) mirror the existing `FhirPathFunctions` → `FhirPathService` split, each backed by a dedicated orchestrator service that parses the request, drives the real NuGet-packaged evaluator, and formats a FHIR-shaped response. Both reuse `SchemaProviderFactory` and `FhirPathService.LoadResourceFromUrl` from the existing FHIRPath stack. The frontend gets two new thin API clients (`fmlApi.ts`, `sofApi.ts`) replacing `fmlEngine.ts`/`sofEngine.ts`, wired into the existing `FmlBench.tsx`/`SofBench.tsx` components with the same explicit "▶ Run" button UX (no debounce), now with loading/abort/error states.

**Tech Stack:** .NET 10 / Azure Functions isolated worker (C#), `Ignixa.FhirMappingLanguage` 0.5.13-beta, `Ignixa.SqlOnFhir` 0.5.13, `Ignixa.Serialization`/`Ignixa.Specification` 0.5.13 (already in use), xUnit + FluentAssertions; React 19 + TypeScript frontend (Vite, no test runner — `tsc -b` + `oxlint` + build is the existing gate).

**Conventions for every task below:**
- Every `git commit` step must append this trailer to the commit message (on its own paragraph, exactly as shown):
  ```
  Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
  ```
- Run backend commands from the repo root; `dotnet test Ignixa.Lab.sln` builds and runs the whole solution (fast enough to run after every backend task).
- Run frontend commands from `frontend/` (`cd frontend`).

---

## File structure

```
backend/src/Ignixa.Lab.Functions/
├── Models/
│   ├── FmlRequest.cs                     [create] Task 2
│   ├── FmlResult.cs                      [create] Task 2
│   ├── SqlOnFhirRequest.cs               [create] Task 6
│   └── SqlOnFhirResult.cs                [create] Task 6
├── Services/
│   ├── Fml/
│   │   ├── FmlService.cs                 [create] Task 3
│   │   └── FmlResultFormatter.cs         [create] Task 4
│   └── SqlOnFhir/
│       └── SqlOnFhirService.cs           [create] Task 7
├── Functions/
│   ├── FmlFunctions.cs                   [create] Task 5
│   └── SqlOnFhirFunctions.cs             [create] Task 8
├── Middleware/EndpointClassifier.cs      [modify] Tasks 5, 8
├── Program.cs                            [modify] Tasks 5, 8
└── Ignixa.Lab.Functions.csproj           [modify] Task 1

backend/test/Ignixa.Lab.Functions.Tests/
├── Services/Fml/FmlServiceTests.cs                [create] Task 3
├── Services/Fml/FmlResultFormatterTests.cs         [create] Task 4
├── Functions/FmlFunctionsTests.cs                  [create] Task 5
├── Services/SqlOnFhir/SqlOnFhirServiceTests.cs     [create] Task 7
├── Functions/SqlOnFhirFunctionsTests.cs             [create] Task 8
└── Middleware/EndpointClassifierTests.cs           [modify] Tasks 5, 8

Directory.Packages.props                            [modify] Task 1

frontend/src/benches/
├── fml/
│   ├── fmlApi.ts                         [create] Task 9
│   ├── FmlBench.tsx                      [modify] Task 10
│   └── fmlEngine.ts                      [delete] Task 10
└── sof/
    ├── sofApi.ts                         [create] Task 11
    ├── SofBench.tsx                      [modify] Task 12
    └── sofEngine.ts                      [delete] Task 12
frontend/src/benches/shared/miniFhirPath.ts [delete] Task 12
```

---

### Task 1: Add the FML and SQL-on-FHIR NuGet packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`

- [ ] **Step 1: Add package versions**

In `Directory.Packages.props`, add two lines right after the existing `Ignixa.Specification` entry:

```xml
    <PackageVersion Include="Ignixa.FhirPath" Version="0.5.13" />
    <PackageVersion Include="Ignixa.Serialization" Version="0.5.13" />
    <PackageVersion Include="Ignixa.Specification" Version="0.5.13" />

    <!-- FHIR Mapping Language (FML) parser/evaluator, powering the FML bench endpoint -->
    <PackageVersion Include="Ignixa.FhirMappingLanguage" Version="0.5.13-beta" />
    <!-- SQL-on-FHIR-v2 ViewDefinition evaluator, powering the SQL on FHIR bench endpoint -->
    <PackageVersion Include="Ignixa.SqlOnFhir" Version="0.5.13" />
```

- [ ] **Step 2: Add package references**

In `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`, add two lines right after the existing `Ignixa.Specification` reference:

```xml
    <PackageReference Include="Ignixa.FhirPath" />
    <PackageReference Include="Ignixa.Serialization" />
    <PackageReference Include="Ignixa.Specification" />
    <PackageReference Include="Ignixa.FhirMappingLanguage" />
    <PackageReference Include="Ignixa.SqlOnFhir" />
```

- [ ] **Step 3: Restore and verify the build**

Run: `dotnet build Ignixa.Lab.sln`
Expected: builds successfully (0 errors); NuGet restores both new packages from `nuget.org`.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj
git commit -m "build: add Ignixa.FhirMappingLanguage and Ignixa.SqlOnFhir package references

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: FML request/result models

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/FmlRequest.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Models/FmlResult.cs`

These are plain data holders (mirroring `FhirPathRequest.cs`/`FhirPathResult.cs`); they have no independent behavior to unit test and are exercised via `FmlServiceTests` in Task 3, so this task has no dedicated test step.

- [ ] **Step 1: Create `FmlRequest.cs`**

```csharp
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Represents the input parameters for a FHIR Mapping Language (FML) transform request.
/// </summary>
public sealed class FmlRequest
{
    /// <summary>
    /// The FML source text (a StructureMap definition) to parse and execute.
    /// </summary>
    public required string Map { get; init; }

    /// <summary>
    /// The FHIR resource to transform.
    /// </summary>
    public required ResourceJsonNode Resource { get; init; }
}
```

- [ ] **Step 2: Create `FmlResult.cs`**

```csharp
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// The structured outcome of running an FML transform, before it is formatted
/// into a FHIR Parameters response by FmlResultFormatter.
/// </summary>
public sealed class FmlResult
{
    /// <summary>
    /// The original request that produced this result.
    /// </summary>
    public required FmlRequest Request { get; init; }

    /// <summary>
    /// Top-level failure (parse error, unresolvable "uses" reference, unhandled
    /// exception) - null on success, even if <see cref="Errors"/> has entries.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Extra diagnostic context for <see cref="Error"/> (e.g. the map or resource text).
    /// </summary>
    public string? ErrorDiagnostics { get; init; }

    /// <summary>
    /// The transformed target resource. Present whenever the map executed,
    /// even if <see cref="Errors"/> is non-empty (Lenient error mode).
    /// </summary>
    public ResourceJsonNode? Output { get; init; }

    /// <summary>
    /// Log lines captured from the map's <c>log(...)</c> clauses, in execution order.
    /// </summary>
    public IReadOnlyList<string> LogLines { get; init; } = [];

    /// <summary>
    /// Per-rule execution errors collected in <see cref="ErrorMode.Lenient"/> mode.
    /// </summary>
    public IReadOnlyList<ExecutionError> Errors { get; init; } = [];

    /// <summary>
    /// Whether the request was successful (a top-level failure did not occur;
    /// individual rule errors may still be present in <see cref="Errors"/>).
    /// </summary>
    public bool IsSuccess => Error == null;
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Ignixa.Lab.sln`
Expected: builds successfully.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/FmlRequest.cs backend/src/Ignixa.Lab.Functions/Models/FmlResult.cs
git commit -m "feat: add FmlRequest/FmlResult models

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: FmlService (parse, resolve, execute)

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/Fml/FmlService.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Fml/FmlServiceTests.cs`

`FmlService.Transform` does the real work: parse the FML map text, resolve the entry group's source/target types from the map's `uses` declarations, build a target shell resource, and run the transform via `Ignixa.FhirMappingLanguage`'s `MappingEvaluator`/`MappingContext`. `MappingEvaluator` and `MappingContext` hold mutable per-execution state (recursion depth, current rule/group, source/target bindings, collected errors) with no internal locking, so both are constructed fresh inside `Transform` on every call - never as injected/singleton fields. `MappingParser` has no such state, so it's a `static readonly` field, mirroring `ExpressionAnalyzer`'s `private static readonly FhirPathParser Parser = new();` convention.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Fml/FmlServiceTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.Fml;

public sealed class FmlServiceTests
{
    // Note: FML string literals must use single quotes ('...') - the tokenizer
    // treats double-quoted text as a DelimitedIdentifier, not a StringLiteral,
    // so double-quoted map URLs/names/rule names fail to parse.
    private const string ValidMap = """
        map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'

        uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
        uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target

        group PatientToPerson(source src : Patient, target tgt : Person) {
          src.gender as vG -> tgt.gender = vG 'copy_gender';
          src.birthDate as vB -> tgt.birthDate = vB 'copy_birthDate';
        }
        """;

    private const string PatientJson = """{"resourceType":"Patient","id":"example","gender":"male","birthDate":"1974-12-25"}""";

    [Fact]
    public void Transform_ValidMapAndResource_ProducesExpectedOutput()
    {
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = ValidMap,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Output.Should().NotBeNull();
        var outputJson = result.Output!.SerializeToString();
        outputJson.Should().Contain("\"resourceType\":\"Person\"");
        outputJson.Should().Contain("\"gender\":\"male\"");
        outputJson.Should().Contain("\"birthDate\":\"1974-12-25\"");
    }

    [Fact]
    public void Transform_MalformedMap_ReturnsStructuredParseError()
    {
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = "this is not valid FML",
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Failed to parse FML map");
    }

    [Fact]
    public void Transform_UsesAliasNotDeclared_ReturnsUnsupportedModelReferenceError()
    {
        const string mapWithUndeclaredTargetAlias = """
            map 'http://ignixa.dev/StructureMap/Bad' = 'Bad'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source

            group Bad(source src : Patient, target tgt : SomeLogicalModel) {
              src.gender as vG -> tgt.gender = vG 'copy_gender';
            }
            """;
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = mapWithUndeclaredTargetAlias,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unsupported model reference");
        result.Error.Should().Contain("SomeLogicalModel");
    }

    [Fact]
    public void Transform_EntryGroupMissingSourceOrTarget_ReturnsStructuredError()
    {
        const string mapWithNoTarget = """
            map 'http://ignixa.dev/StructureMap/Bad' = 'Bad'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source

            group Bad(source src : Patient) {
              src.gender as vG -> src.gender = vG 'noop';
            }
            """;
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = mapWithNoTarget,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("single source and single target parameter");
    }

    [Fact]
    public void Transform_LogClauseInMap_CapturesLogLines()
    {
        const string mapWithLog = """
            map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target

            group PatientToPerson(source src : Patient, target tgt : Person) {
              src.gender as vG log 'copied gender' -> tgt.gender = vG 'copy_gender';
            }
            """;
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = mapWithLog,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeTrue();
        result.LogLines.Should().Contain(line => line.Contains("copied gender"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~FmlServiceTests`
Expected: FAIL to compile - `FmlService` does not exist yet.

- [ ] **Step 3: Implement `FmlService.cs`**

Create `backend/src/Ignixa.Lab.Functions/Services/Fml/FmlService.cs`:

```csharp
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Services.Fml;

/// <summary>
/// Orchestrator service for FHIR Mapping Language (FML) transforms: parses
/// the map text, resolves the entry group's source/target types from its
/// "uses" declarations, and runs the transform via
/// <c>Ignixa.FhirMappingLanguage</c>.
/// </summary>
public sealed class FmlService
{
    // MappingParser has no per-parse mutable state (confirmed against
    // ignixa-fhir source), so it's safe as a static singleton - mirrors
    // ExpressionAnalyzer's `private static readonly FhirPathParser Parser = new();`.
    private static readonly MappingParser Parser = new();

    private readonly SchemaProviderFactory _schemaFactory;

    public FmlService(SchemaProviderFactory schemaFactory)
    {
        _schemaFactory = schemaFactory;
    }

    /// <summary>
    /// Parses and executes an FML transform. <see cref="MappingEvaluator"/>
    /// and <see cref="MappingContext"/> hold mutable per-execution state, so
    /// both are constructed fresh here on every call rather than
    /// shared/injected as singletons.
    /// </summary>
    public FmlResult Transform(FmlRequest request)
    {
        MapExpression map;
        try
        {
            map = Parser.Parse(request.Map);
        }
        catch (ParseException ex)
        {
            return new FmlResult
            {
                Request = request,
                Error = $"Failed to parse FML map: {ex.Message}",
                ErrorDiagnostics = request.Map
            };
        }

        if (map.Groups.Count == 0)
        {
            return new FmlResult
            {
                Request = request,
                Error = "The map defines no groups.",
                ErrorDiagnostics = request.Map
            };
        }

        var entryGroup = map.Groups[0];
        var sourceParams = entryGroup.Parameters.Where(p => p.Mode == ParameterMode.Source).ToList();
        var targetParams = entryGroup.Parameters.Where(p => p.Mode == ParameterMode.Target).ToList();

        if (sourceParams.Count != 1 || targetParams.Count != 1)
        {
            return new FmlResult
            {
                Request = request,
                Error = $"Only a single source and single target parameter are supported on the entry " +
                        $"group '{entryGroup.Name}' (found {sourceParams.Count} source, {targetParams.Count} target).",
                ErrorDiagnostics = request.Map
            };
        }

        var sourceParam = sourceParams[0];
        var targetParam = targetParams[0];

        if (string.IsNullOrEmpty(sourceParam.Type) || string.IsNullOrEmpty(targetParam.Type))
        {
            return new FmlResult
            {
                Request = request,
                Error = $"The entry group's source and target parameters must both declare a type, e.g. " +
                        $"'group {entryGroup.Name}(source {sourceParam.Name} : Patient, target {targetParam.Name} : Bundle)'.",
                ErrorDiagnostics = request.Map
            };
        }

        var sourceUses = map.Uses.FirstOrDefault(u => u.Alias == sourceParam.Type);
        var targetUses = map.Uses.FirstOrDefault(u => u.Alias == targetParam.Type);

        if (sourceUses == null || targetUses == null)
        {
            var missingAlias = sourceUses == null ? sourceParam.Type : targetParam.Type;
            return new FmlResult
            {
                Request = request,
                Error = $"Unsupported model reference: no 'uses' declaration found for type alias '{missingAlias}'. " +
                        "Custom or logical StructureDefinitions supplied via a 'model' parameter are not yet supported.",
                ErrorDiagnostics = request.Map
            };
        }

        var targetTypeName = ExtractResourceTypeName(targetUses.Url);
        var schema = _schemaFactory.GetSchema("R4");

        try
        {
            var sourceElement = request.Resource.ToElement(schema);
            var targetResource = new ResourceJsonNode { ResourceType = targetTypeName };
            var targetElement = targetResource.ToElement(schema);

            var logLines = new List<string>();
            var context = new MappingContext
            {
                ErrorMode = ErrorMode.Lenient,
                Logger = line => logLines.Add(line)
            };
            context.SetSource(sourceParam.Name, sourceElement);
            // MappingEvaluator's required-parameter check reads context.GetTarget
            // (the IElement dictionary), not GetTargetResource, so both must be set:
            // SetTarget satisfies that check, SetTargetResource is what the
            // JsonNodeMutator actually mutates.
            context.SetTarget(targetParam.Name, targetElement);
            context.SetTargetResource(targetParam.Name, targetResource);

            var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
            var options = MappingEvaluatorOptions.Default;
            options.ErrorMode = ErrorMode.Lenient;
            var evaluator = new MappingEvaluator(options, mutator);

            evaluator.ExecuteGroup(map, entryGroup.Name, context);

            return new FmlResult
            {
                Request = request,
                Output = targetResource,
                LogLines = logLines,
                Errors = context.Errors
            };
        }
        catch (Exception ex)
        {
            return new FmlResult
            {
                Request = request,
                Error = $"Transform execution error: {ex.Message}",
                ErrorDiagnostics = request.Map
            };
        }
    }

    private static string ExtractResourceTypeName(string structureDefinitionUrl) =>
        structureDefinitionUrl.TrimEnd('/').Split('/').Last();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~FmlServiceTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Fml/FmlService.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Fml/FmlServiceTests.cs
git commit -m "feat: add FmlService orchestrating Ignixa.FhirMappingLanguage transforms

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: FmlResultFormatter (build the Parameters response)

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/Fml/FmlResultFormatter.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Fml/FmlResultFormatterTests.cs`

Formats an `FmlResult` as a FHIR `Parameters` resource matching the shape fhirpath-lab.com's `fml.vue` parses (`parameters`/`trace`/`result`/`outcome` parts). Reuses `ResultFormatter.CreateOperationOutcomeResult` (already public and static in `Ignixa.Lab.Functions.Services.FhirPath`) for hard failures, and the exact `part.SetValue("valueString", ...)` / `part.MutableNode["resource"] = JsonNode.Parse(...)` idioms already established in `ResultFormatter.cs`.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/Fml/FmlResultFormatterTests.cs`:

```csharp
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.Fml;

public sealed class FmlResultFormatterTests
{
    private static FmlRequest MakeRequest(string map = "map \"http://x\" = \"x\"") => new()
    {
        Map = map,
        Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient"}""")
    };

    [Fact]
    public void FormatResult_TopLevelFailure_ReturnsOperationOutcome()
    {
        var formatter = new FmlResultFormatter();
        var result = new FmlResult
        {
            Request = MakeRequest(),
            Error = "Failed to parse FML map: unexpected token",
            ErrorDiagnostics = "bad map text"
        };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
        json["issue"]![0]!["diagnostics"]!.GetValue<string>().Should().Be("Failed to parse FML map: unexpected token");
    }

    [Fact]
    public void FormatResult_Success_IncludesParametersTraceResultAndOutcomeParts()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person","gender":"male"}""");
        var result = new FmlResult
        {
            Request = MakeRequest("map \"http://x\" = \"x\""),
            Output = output,
            LogLines = ["copied gender"],
            Errors = []
        };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        json["resourceType"]!.GetValue<string>().Should().Be("Parameters");
        var parts = json["parameter"]!.AsArray();

        var configPart = parts.First(p => p!["name"]!.GetValue<string>() == "parameters")!;
        var nested = configPart["part"]!.AsArray();
        nested.Should().Contain(p => p!["name"]!.GetValue<string>() == "evaluator");
        nested.Should().Contain(p => p!["name"]!.GetValue<string>() == "map");

        var tracePart = parts.First(p => p!["name"]!.GetValue<string>() == "trace")!;
        tracePart["valueString"]!.GetValue<string>().Should().Be("copied gender");

        var resultPart = parts.First(p => p!["name"]!.GetValue<string>() == "result")!;
        resultPart["valueString"]!.GetValue<string>().Should().Contain("\"resourceType\": \"Person\"");

        var outcomePart = parts.First(p => p!["name"]!.GetValue<string>() == "outcome")!;
        outcomePart["resource"]!["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
        outcomePart["resource"]!["issue"]![0]!["severity"]!.GetValue<string>().Should().Be("information");
    }

    [Fact]
    public void FormatResult_WithRuleErrors_AddsErrorIssuesToOutcome()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person"}""");
        var errors = new List<ExecutionError> { new("Element not found", ruleName: "copy_gender", groupName: "PatientToPerson") };
        var result = new FmlResult
        {
            Request = MakeRequest(),
            Output = output,
            LogLines = [],
            Errors = errors
        };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        var outcomePart = json["parameter"]!.AsArray().First(p => p!["name"]!.GetValue<string>() == "outcome")!;
        var issue = outcomePart["resource"]!["issue"]![0]!;
        issue["severity"]!.GetValue<string>().Should().Be("error");
        issue["diagnostics"]!.GetValue<string>().Should().Contain("Element not found");
    }

    [Fact]
    public void FormatResult_DebugFalse_OmitsRuleErrorsFromTrace()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person"}""");
        var errors = new List<ExecutionError> { new("Element not found", ruleName: "copy_gender", groupName: "PatientToPerson") };
        var result = new FmlResult { Request = MakeRequest(), Output = output, LogLines = [], Errors = errors };

        var formatted = formatter.FormatResult(result, debug: false);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        var traceParts = json["parameter"]!.AsArray().Where(p => p!["name"]!.GetValue<string>() == "trace").ToList();
        traceParts.Should().BeEmpty();
    }

    [Fact]
    public void FormatResult_DebugTrue_AddsRuleErrorsAsTraceParts()
    {
        var formatter = new FmlResultFormatter();
        var output = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Person"}""");
        var errors = new List<ExecutionError> { new("Element not found", ruleName: "copy_gender", groupName: "PatientToPerson") };
        var result = new FmlResult { Request = MakeRequest(), Output = output, LogLines = [], Errors = errors };

        var formatted = formatter.FormatResult(result, debug: true);

        var json = JsonNode.Parse(formatted.SerializeToString())!;
        var traceParts = json["parameter"]!.AsArray().Where(p => p!["name"]!.GetValue<string>() == "trace").ToList();
        traceParts.Should().ContainSingle(p => p!["valueString"]!.GetValue<string>().Contains("Element not found"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~FmlResultFormatterTests`
Expected: FAIL to compile - `FmlResultFormatter` does not exist yet.

- [ ] **Step 3: Implement `FmlResultFormatter.cs`**

Create `backend/src/Ignixa.Lab.Functions/Services/Fml/FmlResultFormatter.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Lab.Functions.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Services.Fml;

/// <summary>
/// Formats an <see cref="FmlResult"/> as a FHIR Parameters resource matching
/// the shape fhirpath-lab.com's FML UI already parses: "parameters"/"trace"/
/// "result"/"outcome" parts.
/// </summary>
public sealed class FmlResultFormatter
{
    private const string EvaluatorName = "Ignixa .NET (FML)";

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public ResourceJsonNode FormatResult(FmlResult result, bool debug)
    {
        if (!result.IsSuccess)
        {
            // Per the design spec, the top-level failure diagnostics must be the parser/error
            // message itself (result.Error), not result.ErrorDiagnostics (which holds the raw
            // map text) - unlike the FhirPath formatter this reuses, whose ErrorDiagnostics field
            // holds a distinct diagnostic message.
            return FhirPath.ResultFormatter.CreateOperationOutcomeResult("error", "invalid", result.Error!, result.Error);
        }

        var parameters = new ParametersJsonNode();

        var configParam = new ParameterJsonNode { Name = "parameters" };
        parameters.Parameter.Add(configParam);
        AddPart(configParam, "evaluator", EvaluatorName);
        AddPart(configParam, "map", result.Request.Map);

        foreach (var logLine in result.LogLines)
        {
            AddTracePart(parameters, logLine);
        }

        if (debug)
        {
            foreach (var error in result.Errors)
            {
                AddTracePart(parameters, error.ToString() ?? error.Message);
            }
        }

        var resultParam = new ParameterJsonNode { Name = "result" };
        resultParam.SetValue("valueString", result.Output!.SerializeToString(pretty: true));
        parameters.Parameter.Add(resultParam);

        var outcomeParam = new ParameterJsonNode { Name = "outcome" };
        var outcome = BuildOutcome(result.Errors);
        outcomeParam.MutableNode["resource"] = JsonNode.Parse(outcome.SerializeToString());
        parameters.Parameter.Add(outcomeParam);

        return parameters;
    }

    private static void AddTracePart(ParametersJsonNode parameters, string message)
    {
        var part = new ParameterJsonNode { Name = "trace" };
        part.SetValue("valueString", message);
        parameters.Parameter.Add(part);
    }

    private static OperationOutcomeJsonNode BuildOutcome(IReadOnlyList<ExecutionError> errors)
    {
        var outcome = new OperationOutcomeJsonNode();

        if (errors.Count == 0)
        {
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Information,
                Code = OperationOutcomeJsonNode.IssueType.Informational,
                Details = new CodeableConceptJsonNode { Text = "Transformation completed successfully" }
            });
            return outcome;
        }

        foreach (var error in errors)
        {
            var issue = new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.Exception,
                Details = new CodeableConceptJsonNode { Text = error.Message },
                Diagnostics = error.ToString() ?? error.Message
            };
            if (!string.IsNullOrEmpty(error.ElementPath))
            {
                issue.Expression.Add(error.ElementPath);
            }
            outcome.Issue.Add(issue);
        }

        return outcome;
    }

    private static void AddPart(ParameterJsonNode parent, string name, string value)
    {
        var part = new ParameterJsonNode { Name = name };
        part.SetValue("valueString", value);
        parent.Part.Add(part);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~FmlResultFormatterTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/Fml/FmlResultFormatter.cs backend/test/Ignixa.Lab.Functions.Tests/Services/Fml/FmlResultFormatterTests.cs
git commit -m "feat: add FmlResultFormatter for FML Parameters responses

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: FmlFunctions endpoint + DI + rate-limit classification

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Functions/FmlFunctions.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/FmlFunctionsTests.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Program.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Middleware/EndpointClassifier.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Middleware/EndpointClassifierTests.cs`

Exposes `POST/GET/OPTIONS /api/StructureMap/$transform`, mirroring `FhirPathFunctions.cs`'s exact request-building conventions (`FindParameter`, `GetValueAs<T>()`, the resource-extraction try/catch, GET query-string-to-Parameters conversion, `LoadResourceFromUrl` reuse, `CreateErrorResponse`). Per the spec, hard failures (map parse errors, unresolved `uses` types, unhandled exceptions - all folded into `FmlResult.Error` by `FmlService`) return HTTP 400 with an `OperationOutcome` body; successful transforms (even with rule-level errors in `Errors`) return HTTP 200.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Functions/FmlFunctionsTests.cs`:

```csharp
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Fml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class FmlFunctionsTests
{
    // FML string literals must be single-quoted - the tokenizer treats double-quoted
    // text as a DelimitedIdentifier, not a StringLiteral (see Task 3's notes).
    private const string ValidMap = """
        map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'

        uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
        uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target

        group PatientToPerson(source src : Patient, target tgt : Person) {
          src.gender as vG -> tgt.gender = vG 'copy_gender';
        }
        """;

    [Fact]
    public async Task RunTransform_PostWithValidMapAndEmbeddedResource_ReturnsSuccessParameters()
    {
        var function = CreateFunction();
        // Note: `$$"""..."""` raw-string interpolation can't be used here - the JSON
        // body's `}}}` sequence (interpolation hole immediately followed by a literal
        // closing brace) triggers CS9007. Verbatim string concatenation avoids that.
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""map"",""valueString"":" + JsonValue.Create(ValidMap).ToJsonString() + @"},
                {""name"":""resource"",""resource"":{""resourceType"":""Patient"",""gender"":""male""}}
            ]}";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("Parameters");
    }

    [Fact]
    public async Task RunTransform_MissingMapParameter_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        const string body = """{"resourceType":"Parameters","parameter":[{"name":"resource","resource":{"resourceType":"Patient"}}]}""";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunTransform_MalformedMap_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        const string body = """
            {"resourceType":"Parameters","parameter":[
                {"name":"map","valueString":"this is not valid FML"},
                {"name":"resource","resource":{"resourceType":"Patient"}}
            ]}
            """;

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunTransform_RejectsPrivateResourceUrlTarget_WithoutMakingAnHttpCall()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""map"",""valueString"":" + JsonValue.Create(ValidMap).ToJsonString() + @"},
                {""name"":""resource"",""valueString"":""http://127.0.0.1/fhir/Patient/1""}
            ]}";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunTransform_ResourceAsRawJsonString_IsParsedAndTransformed()
    {
        var function = CreateFunction();
        var body = @"{""resourceType"":""Parameters"",""parameter"":[
                {""name"":""map"",""valueString"":" + JsonValue.Create(ValidMap).ToJsonString() + @"},
                {""name"":""resource"",""valueString"":""{\""resourceType\"":\""Patient\"",\""gender\"":\""female\""}""}
            ]}";

        var result = await function.RunTransform(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!;
        var resultPart = json["parameter"]!.AsArray().First(p => p!["name"]!.GetValue<string>() == "result")!;
        resultPart["valueString"]!.GetValue<string>().Should().Contain("\"gender\": \"female\"");
    }

    private static FmlFunctions CreateFunction()
    {
        var schemaFactory = new SchemaProviderFactory();
        var fmlService = new FmlService(schemaFactory);
        var resultFormatter = new FmlResultFormatter();

        var analyzer = new ExpressionAnalyzer(schemaFactory);
        var evaluator = new ExpressionEvaluator(schemaFactory);
        var fhirPathFormatter = new ResultFormatter();
        var options = Options.Create(new IgnixaLabOptions());
        var fhirPathService = new FhirPathService(analyzer, evaluator, fhirPathFormatter, new ThrowingHttpClientFactory(), options);

        return new FmlFunctions(NullLogger<FmlFunctions>.Instance, fmlService, resultFormatter, fhirPathService);
    }

    private static HttpRequest BuildPostRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context.Request;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The HTTP client should not have been used for this test.");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~FmlFunctionsTests`
Expected: FAIL to compile - `FmlFunctions` does not exist yet.

- [ ] **Step 3: Implement `FmlFunctions.cs`**

Create `backend/src/Ignixa.Lab.Functions/Functions/FmlFunctions.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// FHIR Mapping Language (FML) transform endpoint, compatible with the
/// request/response shape fhirpath-lab.com's "mapper_server" UI expects
/// from a StructureMap/$transform backend.
/// </summary>
public sealed class FmlFunctions(
    ILogger<FmlFunctions> logger,
    FmlService fmlService,
    FmlResultFormatter resultFormatter,
    FhirPathService fhirPathService)
{
    [Function("FmlTransform")]
    public Task<IActionResult> RunTransform(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options", Route = "StructureMap/$transform")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ProcessTransformRequest(request, cancellationToken);

    private async Task<IActionResult> ProcessTransformRequest(HttpRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("FML Transform (Ignixa)");

        var (operationParameters, parseError) = await ParseOperationParameters(request, cancellationToken);
        if (parseError != null)
        {
            return CreateErrorResponse(parseError);
        }

        var (built, error, errorDiagnostics) = await BuildFmlRequestAsync(operationParameters!, cancellationToken);
        if (error != null)
        {
            return CreateErrorResponse(error, errorDiagnostics);
        }

        var result = fmlService.Transform(built!);
        var debug = bool.TryParse(request.Query["debug"], out var debugFlag) && debugFlag;
        var formatted = resultFormatter.FormatResult(result, debug);

        return new ContentResult
        {
            Content = formatted.SerializeToString(pretty: true),
            ContentType = "application/fhir+json",
            StatusCode = result.IsSuccess ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest
        };
    }

    private static async Task<(ParametersJsonNode? Parameters, string? Error)> ParseOperationParameters(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method != "POST")
        {
            var parameters = new ParametersJsonNode();
            foreach (var key in request.Query.Keys)
            {
                var param = new ParameterJsonNode { Name = key };
                param.SetValue("valueString", request.Query[key].ToString());
                parameters.Parameter.Add(param);
            }
            return (parameters, null);
        }

        try
        {
            return (await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(request.Body, cancellationToken), null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid request body: {ex.Message}");
        }
    }

    private async Task<(FmlRequest? Request, string? Error, string? ErrorDiagnostics)> BuildFmlRequestAsync(
        ParametersJsonNode operationParameters,
        CancellationToken cancellationToken)
    {
        var mapParam = operationParameters.FindParameter("map");
        var resourceParam = operationParameters.FindParameter("resource");

        var map = mapParam?.GetValueAs<string>();
        if (string.IsNullOrEmpty(map))
        {
            return (null, "The 'map' parameter is required", null);
        }

        ResourceJsonNode? resource;
        try
        {
            resource = resourceParam?.Resource;
        }
        catch (Exception ex)
        {
            return (null, $"Invalid resource: {ex.Message}", null);
        }

        if (resource == null)
        {
            var resourceText = resourceParam?.GetValueAs<string>();
            if (string.IsNullOrEmpty(resourceText))
            {
                return (null, "The 'resource' parameter is required", null);
            }

            if (resourceText.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var (loadedResource, error) = await fhirPathService.LoadResourceFromUrl(resourceText, cancellationToken);
                if (error != null)
                {
                    return (null, error, resourceText);
                }
                resource = loadedResource;
            }
            else
            {
                try
                {
                    resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(resourceText);
                }
                catch (JsonException ex)
                {
                    return (null, $"Invalid resource JSON: {ex.Message}", resourceText);
                }
            }
        }

        return (new FmlRequest { Map = map, Resource = resource! }, null, null);
    }

    private static IActionResult CreateErrorResponse(string message, string? diagnostics = null)
    {
        var outcome = ResultFormatter.CreateOperationOutcomeResult("error", "invalid", message, diagnostics);

        return new ContentResult
        {
            Content = outcome.SerializeToString(pretty: true),
            ContentType = "application/fhir+json",
            StatusCode = (int)HttpStatusCode.BadRequest
        };
    }
}
```

- [ ] **Step 4: Register `FmlService`/`FmlResultFormatter` in DI**

In `backend/src/Ignixa.Lab.Functions/Program.cs`, add a `using` for the new namespace:

```csharp
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Lab.Functions.Suites;
```

Then find this existing block:

```csharp
builder.Services.AddSingleton<SchemaProviderFactory>();
builder.Services.AddSingleton<ExpressionAnalyzer>();
builder.Services.AddSingleton<ExpressionEvaluator>();
builder.Services.AddSingleton<ResultFormatter>();
builder.Services.AddSingleton<FhirPathService>();
```

And add two lines directly after it:

```csharp
builder.Services.AddSingleton<SchemaProviderFactory>();
builder.Services.AddSingleton<ExpressionAnalyzer>();
builder.Services.AddSingleton<ExpressionEvaluator>();
builder.Services.AddSingleton<ResultFormatter>();
builder.Services.AddSingleton<FhirPathService>();

builder.Services.AddSingleton<FmlService>();
builder.Services.AddSingleton<FmlResultFormatter>();
```

- [ ] **Step 5: Classify the new endpoint's rate-limit tier**

In `backend/src/Ignixa.Lab.Functions/Middleware/EndpointClassifier.cs`, the `Classify` method's `switch` currently reads:

```csharp
            // FHIRPath evaluation is a single unit of work per call (like
            // Capability), not a fan-out run — classify at the same tier.
            "FhirPathMetadata" or "FhirPathStu3" or "FhirPathR4" or "FhirPathR4B"
                or "FhirPathR5" or "FhirPathR6" => EndpointClass.Capability,
            // Fail safe: an unrecognized (e.g. newly added) endpoint gets the
            // strictest tier rather than silently running unlimited.
            _ => EndpointClass.Run,
```

Add a new arm for `FmlTransform` between the FHIRPath arm and the fail-safe default:

```csharp
            // FHIRPath evaluation is a single unit of work per call (like
            // Capability), not a fan-out run — classify at the same tier.
            "FhirPathMetadata" or "FhirPathStu3" or "FhirPathR4" or "FhirPathR4B"
                or "FhirPathR5" or "FhirPathR6" => EndpointClass.Capability,
            // FML transform is likewise a single unit of work per call.
            "FmlTransform" => EndpointClass.Capability,
            // Fail safe: an unrecognized (e.g. newly added) endpoint gets the
            // strictest tier rather than silently running unlimited.
            _ => EndpointClass.Run,
```

- [ ] **Step 6: Add a classifier test case**

In `backend/test/Ignixa.Lab.Functions.Tests/Middleware/EndpointClassifierTests.cs`, the `[InlineData]` rows currently end with:

```csharp
    [InlineData("FhirPathR6", EndpointClass.Capability)]
    [InlineData("SomeFutureEndpoint", EndpointClass.Run)]
```

Add a new row for `FmlTransform` right after the FHIRPath rows:

```csharp
    [InlineData("FhirPathR6", EndpointClass.Capability)]
    [InlineData("FmlTransform", EndpointClass.Capability)]
    [InlineData("SomeFutureEndpoint", EndpointClass.Run)]
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~FmlFunctionsTests|FullyQualifiedName~EndpointClassifierTests`
Expected: PASS (all FmlFunctionsTests and EndpointClassifierTests, including the new `FmlTransform` row).

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test Ignixa.Lab.sln`
Expected: PASS, 0 failures.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Functions/FmlFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/FmlFunctionsTests.cs backend/src/Ignixa.Lab.Functions/Program.cs backend/src/Ignixa.Lab.Functions/Middleware/EndpointClassifier.cs backend/test/Ignixa.Lab.Functions.Tests/Middleware/EndpointClassifierTests.cs
git commit -m "feat: add FmlFunctions StructureMap/\$transform endpoint

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: SqlOnFhir request/result models

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Models/SqlOnFhirRequest.cs`
- Create: `backend/src/Ignixa.Lab.Functions/Models/SqlOnFhirResult.cs`

Plain data holders, exercised via `SqlOnFhirServiceTests` in Task 7 - no dedicated test step, same as Task 2.

- [ ] **Step 1: Create `SqlOnFhirRequest.cs`**

```csharp
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Represents the input parameters for a SQL-on-FHIR ViewDefinition run request.
/// </summary>
public sealed class SqlOnFhirRequest
{
    /// <summary>
    /// The inline ViewDefinition resource describing the tabular projection to run.
    /// </summary>
    public required ResourceJsonNode ViewResource { get; init; }

    /// <summary>
    /// The FHIR resources to run the view against.
    /// </summary>
    public required IReadOnlyList<ResourceJsonNode> Resources { get; init; }

    /// <summary>
    /// Optional cap on the number of returned rows (the "_limit" parameter).
    /// </summary>
    public int? Limit { get; init; }
}
```

- [ ] **Step 2: Create `SqlOnFhirResult.cs`**

```csharp
namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// The structured outcome of running a SQL-on-FHIR ViewDefinition, before it
/// is serialized to a plain JSON array response by SqlOnFhirFunctions.
/// </summary>
public sealed class SqlOnFhirResult
{
    /// <summary>
    /// The original request that produced this result.
    /// </summary>
    public required SqlOnFhirRequest Request { get; init; }

    /// <summary>
    /// Failure message (malformed ViewDefinition, evaluation error) - null on success.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Extra diagnostic context for <see cref="Error"/>.
    /// </summary>
    public string? ErrorDiagnostics { get; init; }

    /// <summary>
    /// The resulting rows, one dictionary per input resource (subject to <see cref="SqlOnFhirRequest.Limit"/>).
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>
    /// Whether the evaluation succeeded.
    /// </summary>
    public bool IsSuccess => Error == null;
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Ignixa.Lab.sln`
Expected: builds successfully.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Models/SqlOnFhirRequest.cs backend/src/Ignixa.Lab.Functions/Models/SqlOnFhirResult.cs
git commit -m "feat: add SqlOnFhirRequest/SqlOnFhirResult models

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 7: SqlOnFhirService (evaluate a ViewDefinition against resources)

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Services/SqlOnFhir/SqlOnFhirService.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/SqlOnFhir/SqlOnFhirServiceTests.cs`

`SqlOnFhirEvaluator` exposes a parameterless constructor plus a `ClearCache()` method, implying it internally parses/caches the `ViewDefinition` document rather than holding per-execution mutable state - the same shape as `FhirPathParser`/`MappingParser`, both of which are already safely used as `static readonly` fields elsewhere in this codebase. `SqlOnFhirService` follows that same convention with a static evaluator instance.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Services/SqlOnFhir/SqlOnFhirServiceTests.cs`:

```csharp
using FluentAssertions;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.SqlOnFhir;

public sealed class SqlOnFhirServiceTests
{
    private const string ValidViewDefinition = """
        {
          "resourceType": "ViewDefinition",
          "status": "active",
          "resource": "Patient",
          "select": [
            { "column": [ { "name": "id", "path": "id" }, { "name": "gender", "path": "gender" } ] }
          ]
        }
        """;

    private static SqlOnFhirRequest MakeRequest(string viewDefinition, params string[] resources) => new()
    {
        ViewResource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(viewDefinition),
        Resources = resources.Select(r => JsonSourceNodeFactory.Parse<ResourceJsonNode>(r)).ToList()
    };

    [Fact]
    public void Evaluate_ValidViewDefinitionAndResource_ReturnsRow()
    {
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(ValidViewDefinition, """{"resourceType":"Patient","id":"p1","gender":"male"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeTrue();
        result.Rows.Should().ContainSingle();
        result.Rows[0]["id"]?.ToString().Should().Be("p1");
        result.Rows[0]["gender"]?.ToString().Should().Be("male");
    }

    [Fact]
    public void Evaluate_MultipleResources_ReturnsOneRowPerResource()
    {
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(
            ValidViewDefinition,
            """{"resourceType":"Patient","id":"p1","gender":"male"}""",
            """{"resourceType":"Patient","id":"p2","gender":"female"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeTrue();
        result.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_LimitLowerThanRowCount_TruncatesRows()
    {
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = new SqlOnFhirRequest
        {
            ViewResource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(ValidViewDefinition),
            Resources =
            [
                JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"p1","gender":"male"}"""),
                JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"p2","gender":"female"}""")
            ],
            Limit = 1
        };

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeTrue();
        result.Rows.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_MalformedColumnPath_ReturnsStructuredError()
    {
        const string badView = """
            {
              "resourceType": "ViewDefinition",
              "status": "active",
              "resource": "Patient",
              "select": [ { "column": [ { "name": "bad", "path": "id.." } ] } ]
            }
            """;
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(badView, """{"resourceType":"Patient","id":"p1"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("ViewDefinition evaluation error");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~SqlOnFhirServiceTests`
Expected: FAIL to compile - `SqlOnFhirService` does not exist yet.

- [ ] **Step 3: Implement `SqlOnFhirService.cs`**

Create `backend/src/Ignixa.Lab.Functions/Services/SqlOnFhir/SqlOnFhirService.cs`:

```csharp
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.SqlOnFhir.Evaluation;

namespace Ignixa.Lab.Functions.Services.SqlOnFhir;

/// <summary>
/// Orchestrator service for SQL-on-FHIR ViewDefinition evaluation via
/// <c>Ignixa.SqlOnFhir</c>.
/// </summary>
public sealed class SqlOnFhirService
{
    // SqlOnFhirEvaluator exposes a parameterless ctor and a ClearCache()
    // method, implying it internally parses/caches the ViewDefinition rather
    // than holding per-execution mutable state (the same shape as
    // MappingParser/FhirPathParser, which are safely used as static fields
    // elsewhere in this codebase) - so a shared static instance is used here
    // rather than constructing one per request.
    private static readonly SqlOnFhirEvaluator Evaluator = new();

    private readonly SchemaProviderFactory _schemaFactory;

    public SqlOnFhirService(SchemaProviderFactory schemaFactory)
    {
        _schemaFactory = schemaFactory;
    }

    public SqlOnFhirResult Evaluate(SqlOnFhirRequest request)
    {
        try
        {
            var schema = _schemaFactory.GetSchema("R4");
            var navigator = request.ViewResource.ToSourceNavigator();
            var elements = request.Resources.Select(r => r.ToElement(schema));

            var rows = Evaluator.EvaluateBatch(navigator, elements).ToList();

            if (request.Limit is { } limit && limit >= 0 && limit < rows.Count)
            {
                rows = rows.Take(limit).ToList();
            }

            return new SqlOnFhirResult { Request = request, Rows = rows! };
        }
        catch (Exception ex)
        {
            return new SqlOnFhirResult
            {
                Request = request,
                Error = $"ViewDefinition evaluation error: {ex.Message}"
            };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~SqlOnFhirServiceTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Services/SqlOnFhir/SqlOnFhirService.cs backend/test/Ignixa.Lab.Functions.Tests/Services/SqlOnFhir/SqlOnFhirServiceTests.cs
git commit -m "feat: add SqlOnFhirService orchestrating Ignixa.SqlOnFhir evaluation

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 8: SqlOnFhirFunctions endpoint + DI + rate-limit classification

**Files:**
- Create: `backend/src/Ignixa.Lab.Functions/Functions/SqlOnFhirFunctions.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/SqlOnFhirFunctionsTests.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Program.cs`
- Modify: `backend/src/Ignixa.Lab.Functions/Middleware/EndpointClassifier.cs`
- Modify: `backend/test/Ignixa.Lab.Functions.Tests/Middleware/EndpointClassifierTests.cs`

Exposes `POST /api/ViewDefinition/$viewdefinition-run` (no GET - the request needs embedded resources that don't fit a query string; declaring only `"post"`/`"options"` in the `HttpTrigger` means the Functions runtime itself returns 405 for GET, no in-function handling needed). Per spec, this endpoint is scoped down from the official operation: only `viewResource` (1..1, embedded), `resource` (0..*, embedded, at least one required), `_format` (must be absent or `"json"`), and `_limit` are supported; `viewReference`/`patient`/`group`/`source`/`_since` are all rejected with a 400 since they all presuppose a persistent FHIR data store this app doesn't have. Unlike `FmlFunctions`, there's no URL-fetch path here (the spec's request table only allows embedded resources), so this class has no dependency on `FhirPathService`.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Ignixa.Lab.Functions.Tests/Functions/SqlOnFhirFunctionsTests.cs`:

```csharp
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class SqlOnFhirFunctionsTests
{
    private const string ValidViewDefinitionJson = """
        {
          "resourceType": "ViewDefinition",
          "status": "active",
          "resource": "Patient",
          "select": [ { "column": [ { "name": "id", "path": "id" }, { "name": "gender", "path": "gender" } ] } ]
        }
        """;

    [Fact]
    public async Task RunViewDefinition_ValidRequest_ReturnsJsonArrayOfRows()
    {
        var function = CreateFunction();
        var body = $$"""
            {"resourceType":"Parameters","parameter":[
                {"name":"viewResource","resource":{{ValidViewDefinitionJson}}},
                {"name":"resource","resource":{"resourceType":"Patient","id":"p1","gender":"male"}}
            ]}
            """;

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        content.ContentType.Should().Be("application/json");
        var json = JsonNode.Parse(content.Content!)!.AsArray();
        json.Should().ContainSingle();
        json[0]!["id"]!.GetValue<string>().Should().Be("p1");
    }

    [Fact]
    public async Task RunViewDefinition_MissingViewResource_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        const string body = """{"resourceType":"Parameters","parameter":[{"name":"resource","resource":{"resourceType":"Patient","id":"p1"}}]}""";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_NoResourceParameters_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        var body = $$"""{"resourceType":"Parameters","parameter":[{"name":"viewResource","resource":{{ValidViewDefinitionJson}}}]}""";

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_UnsupportedFormat_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        var body = $$"""
            {"resourceType":"Parameters","parameter":[
                {"name":"viewResource","resource":{{ValidViewDefinitionJson}}},
                {"name":"resource","resource":{"resourceType":"Patient","id":"p1"}},
                {"name":"_format","valueCode":"csv"}
            ]}
            """;

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_UnsupportedParameter_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();
        var body = $$"""
            {"resourceType":"Parameters","parameter":[
                {"name":"viewResource","resource":{{ValidViewDefinitionJson}}},
                {"name":"resource","resource":{"resourceType":"Patient","id":"p1"}},
                {"name":"patient","valueString":"Patient/1"}
            ]}
            """;

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    [Fact]
    public async Task RunViewDefinition_LimitParameter_TruncatesRows()
    {
        var function = CreateFunction();
        var body = $$"""
            {"resourceType":"Parameters","parameter":[
                {"name":"viewResource","resource":{{ValidViewDefinitionJson}}},
                {"name":"resource","resource":{"resourceType":"Patient","id":"p1","gender":"male"}},
                {"name":"resource","resource":{"resourceType":"Patient","id":"p2","gender":"female"}},
                {"name":"_limit","valueInteger":1}
            ]}
            """;

        var result = await function.RunViewDefinition(BuildPostRequest(body), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(200);
        var json = JsonNode.Parse(content.Content!)!.AsArray();
        json.Should().ContainSingle();
    }

    [Fact]
    public async Task RunViewDefinition_MalformedPostBody_ReturnsBadRequestOperationOutcome()
    {
        var function = CreateFunction();

        var result = await function.RunViewDefinition(BuildPostRequest("{ this is not valid json"), CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        var json = JsonNode.Parse(content.Content!)!;
        json["resourceType"]!.GetValue<string>().Should().Be("OperationOutcome");
    }

    private static SqlOnFhirFunctions CreateFunction()
    {
        var schemaFactory = new SchemaProviderFactory();
        var service = new SqlOnFhirService(schemaFactory);
        return new SqlOnFhirFunctions(NullLogger<SqlOnFhirFunctions>.Instance, service);
    }

    private static HttpRequest BuildPostRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context.Request;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~SqlOnFhirFunctionsTests`
Expected: FAIL to compile - `SqlOnFhirFunctions` does not exist yet.

- [ ] **Step 3: Implement `SqlOnFhirFunctions.cs`**

Create `backend/src/Ignixa.Lab.Functions/Functions/SqlOnFhirFunctions.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>
/// SQL-on-FHIR-v2 ViewDefinition evaluation endpoint. Scoped to what a
/// stateless bench can support: inline ViewDefinition + inline resources
/// only - no server-stored views/resources, since this app has no
/// persistent FHIR data store.
/// </summary>
public sealed class SqlOnFhirFunctions(ILogger<SqlOnFhirFunctions> logger, SqlOnFhirService sqlOnFhirService)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // These all presuppose a persistent, server-side FHIR data store this
    // app doesn't have (server-stored views/resources, patient/group
    // compartment filtering, incremental sync) - reject explicitly rather
    // than silently ignoring them.
    private static readonly string[] UnsupportedParameterNames =
        ["viewReference", "patient", "group", "source", "_since"];

    [Function("SqlOnFhirViewDefinitionRun")]
    public async Task<IActionResult> RunViewDefinition(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "ViewDefinition/$viewdefinition-run")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SQL on FHIR ViewDefinition run (Ignixa)");

        ParametersJsonNode operationParameters;
        try
        {
            operationParameters = await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(request.Body, cancellationToken);
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse($"Invalid request body: {ex.Message}");
        }

        var (built, error) = BuildSqlOnFhirRequest(operationParameters);
        if (error != null)
        {
            return CreateErrorResponse(error);
        }

        var result = sqlOnFhirService.Evaluate(built!);
        if (!result.IsSuccess)
        {
            return CreateErrorResponse(result.Error!, result.ErrorDiagnostics);
        }

        return new ContentResult
        {
            Content = JsonSerializer.Serialize(result.Rows, JsonOptions),
            ContentType = "application/json",
            StatusCode = (int)HttpStatusCode.OK
        };
    }

    private static (SqlOnFhirRequest? Request, string? Error) BuildSqlOnFhirRequest(ParametersJsonNode operationParameters)
    {
        foreach (var unsupported in UnsupportedParameterNames)
        {
            if (operationParameters.FindParameter(unsupported) != null)
            {
                return (null, $"The '{unsupported}' parameter is not supported: this endpoint has no persistent FHIR data store to resolve it against.");
            }
        }

        var formatParam = operationParameters.FindParameter("_format");
        var format = formatParam?.GetValueAs<string>();
        if (!string.IsNullOrEmpty(format) && !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"Unsupported _format '{format}': only 'json' is supported.");
        }

        var viewResourceParam = operationParameters.FindParameter("viewResource");
        ResourceJsonNode? viewResource;
        try
        {
            viewResource = viewResourceParam?.Resource;
        }
        catch (Exception ex)
        {
            return (null, $"Invalid viewResource: {ex.Message}");
        }
        if (viewResource == null)
        {
            return (null, "The 'viewResource' parameter is required and must be an embedded ViewDefinition resource.");
        }

        var resources = new List<ResourceJsonNode>();
        foreach (var resourceParam in operationParameters.Parameter.Where(p => p.Name == "resource"))
        {
            ResourceJsonNode? resource;
            try
            {
                resource = resourceParam.Resource;
            }
            catch (Exception ex)
            {
                return (null, $"Invalid resource: {ex.Message}");
            }
            if (resource != null)
            {
                resources.Add(resource);
            }
        }

        if (resources.Count == 0)
        {
            return (null, "At least one 'resource' parameter is required.");
        }

        int? limit = null;
        var limitParam = operationParameters.FindParameter("_limit");
        if (limitParam != null)
        {
            limit = limitParam.GetValueAs<int>();
        }

        return (new SqlOnFhirRequest { ViewResource = viewResource, Resources = resources, Limit = limit }, null);
    }

    private static IActionResult CreateErrorResponse(string message, string? diagnostics = null)
    {
        var outcome = ResultFormatter.CreateOperationOutcomeResult("error", "invalid", message, diagnostics);

        return new ContentResult
        {
            Content = outcome.SerializeToString(pretty: true),
            ContentType = "application/fhir+json",
            StatusCode = (int)HttpStatusCode.BadRequest
        };
    }
}
```

- [ ] **Step 4: Register `SqlOnFhirService` in DI**

In `backend/src/Ignixa.Lab.Functions/Program.cs`, add a `using` for the new namespace:

```csharp
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Ignixa.Lab.Functions.Suites;
```

Then find the block added in Task 5:

```csharp
builder.Services.AddSingleton<FmlService>();
builder.Services.AddSingleton<FmlResultFormatter>();
```

And add a line after it:

```csharp
builder.Services.AddSingleton<FmlService>();
builder.Services.AddSingleton<FmlResultFormatter>();
builder.Services.AddSingleton<SqlOnFhirService>();
```

- [ ] **Step 5: Classify the new endpoint's rate-limit tier**

In `backend/src/Ignixa.Lab.Functions/Middleware/EndpointClassifier.cs`, the `switch` now reads (after Task 5's edit):

```csharp
            "FhirPathMetadata" or "FhirPathStu3" or "FhirPathR4" or "FhirPathR4B"
                or "FhirPathR5" or "FhirPathR6" => EndpointClass.Capability,
            // FML transform is likewise a single unit of work per call.
            "FmlTransform" => EndpointClass.Capability,
            // Fail safe: an unrecognized (e.g. newly added) endpoint gets the
            // strictest tier rather than silently running unlimited.
            _ => EndpointClass.Run,
```

Add `SqlOnFhirViewDefinitionRun` to the same tier:

```csharp
            "FhirPathMetadata" or "FhirPathStu3" or "FhirPathR4" or "FhirPathR4B"
                or "FhirPathR5" or "FhirPathR6" => EndpointClass.Capability,
            // FML transform and SQL-on-FHIR view evaluation are likewise a
            // single unit of work per call.
            "FmlTransform" or "SqlOnFhirViewDefinitionRun" => EndpointClass.Capability,
            // Fail safe: an unrecognized (e.g. newly added) endpoint gets the
            // strictest tier rather than silently running unlimited.
            _ => EndpointClass.Run,
```

- [ ] **Step 6: Add a classifier test case**

In `backend/test/Ignixa.Lab.Functions.Tests/Middleware/EndpointClassifierTests.cs`, the rows now read (after Task 5's edit):

```csharp
    [InlineData("FhirPathR6", EndpointClass.Capability)]
    [InlineData("FmlTransform", EndpointClass.Capability)]
    [InlineData("SomeFutureEndpoint", EndpointClass.Run)]
```

Add a row for `SqlOnFhirViewDefinitionRun`:

```csharp
    [InlineData("FhirPathR6", EndpointClass.Capability)]
    [InlineData("FmlTransform", EndpointClass.Capability)]
    [InlineData("SqlOnFhirViewDefinitionRun", EndpointClass.Capability)]
    [InlineData("SomeFutureEndpoint", EndpointClass.Run)]
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Ignixa.Lab.sln --filter FullyQualifiedName~SqlOnFhirFunctionsTests|FullyQualifiedName~EndpointClassifierTests`
Expected: PASS (all SqlOnFhirFunctionsTests and EndpointClassifierTests, including the new `SqlOnFhirViewDefinitionRun` row).

- [ ] **Step 8: Run the full backend test suite**

Run: `dotnet test Ignixa.Lab.sln`
Expected: PASS, 0 failures.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Ignixa.Lab.Functions/Functions/SqlOnFhirFunctions.cs backend/test/Ignixa.Lab.Functions.Tests/Functions/SqlOnFhirFunctionsTests.cs backend/src/Ignixa.Lab.Functions/Program.cs backend/src/Ignixa.Lab.Functions/Middleware/EndpointClassifier.cs backend/test/Ignixa.Lab.Functions.Tests/Middleware/EndpointClassifierTests.cs
git commit -m "feat: add SqlOnFhirFunctions ViewDefinition/\$viewdefinition-run endpoint

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

## Part 2: Frontend

The backend is now complete. The remaining tasks wire the FML and SQL-on-FHIR bench UIs to the two new endpoints, replacing their mock in-browser engines.

**No frontend unit tests exist in this repo** (`frontend/package.json` has no test script, and no test framework — e.g. vitest — is installed; only `tsc -b` via `npm run build` and `oxlint` via `npm run lint`). So Tasks 9-12 do not follow red/green TDD steps like the backend tasks — they follow a write → typecheck (`npm run build`) → lint (`npm run lint`) → commit cycle instead, consistent with how the rest of the frontend codebase is verified.

### Task 9: `fmlApi.ts` — FML backend client

**Files:**
- Create: `frontend/src/benches/fml/fmlApi.ts`

This mirrors `frontend/src/benches/fhirpath/fhirPathApi.ts`'s conventions exactly (same `readOperationOutcomeMessage`/throw-on-error idiom, same `import.meta.env.VITE_API_BASE_URL` base-URL handling), adapted for the FML wire shape from Task 4/5: a `Parameters` request with `map` (`valueString`) + `resource` (embedded resource), POSTed to `StructureMap/$transform?debug=true`, returning a `Parameters` response with `parameters`/`trace`/`result`/`outcome` parts (or a top-level `OperationOutcome` on hard failure, per the deliberate HTTP-400 behavior from Task 5).

- [ ] **Step 1: Create `frontend/src/benches/fml/fmlApi.ts`**

```typescript
/** A generic FHIR `Parameters.parameter[]` entry — permissive enough to cover every `value[x]`/`resource`/`part` shape the backend emits. */
export interface FhirParameter {
  name: string;
  part?: FhirParameter[];
  resource?: unknown;
  [valueKey: string]: unknown;
}

export interface FhirParameters {
  resourceType: string;
  parameter?: FhirParameter[];
}

export interface FmlRequestInput {
  mapText: string;
  resourceText: string;
}

export interface FmlEvalResult {
  error: string | null;
  evaluator: string;
  /** Pretty-printed JSON text of the transform's target resource, or `null` before any run. */
  output: string | null;
  /** `log(...)` lines captured during the transform, in emission order. */
  trace: string[];
  /** Rule-level error diagnostics from the debug `outcome` part (non-fatal — the transform still produced `output`). */
  outcomeIssues: string[];
}

const EMPTY_RESULT: FmlEvalResult = { error: null, evaluator: '', output: null, trace: [], outcomeIssues: [] };

/** Builds the FHIR `Parameters` request body for `StructureMap/$transform`. Throws if `resourceText` isn't valid JSON — callers should catch and surface it as a resource-JSON error. */
export function buildFmlRequest(input: FmlRequestInput): FhirParameters {
  return {
    resourceType: 'Parameters',
    parameter: [
      { name: 'map', valueString: input.mapText },
      { name: 'resource', resource: JSON.parse(input.resourceText) },
    ],
  };
}

/** Extracts a readable message from an `OperationOutcome` error response, if that's what the body is. */
function readOperationOutcomeMessage(body: unknown): string | null {
  const outcome = body as { resourceType?: string; issue?: { details?: { text?: string }; diagnostics?: string }[] };
  if (outcome?.resourceType !== 'OperationOutcome' || !outcome.issue?.length) {
    return null;
  }
  const issue = outcome.issue[0];
  return issue.details?.text?.trim() || issue.diagnostics?.trim() || null;
}

/** POSTs to the FML transform endpoint (with `debug=true` so rule-level errors are also surfaced via the `outcome` part) and returns the raw `Parameters` response. Throws if the body is a top-level `OperationOutcome` (a hard failure — parse error, unresolved model reference, or unhandled exception, always HTTP 400), the response is non-2xx, or on a network/abort error. */
export async function runFml(body: FhirParameters, signal: AbortSignal): Promise<FhirParameters> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const response = await fetch(`${apiBaseUrl}/api/StructureMap/$transform?debug=true`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });

  const text = await response.text();
  let json: FhirParameters;
  try {
    json = JSON.parse(text) as FhirParameters;
  } catch {
    throw new Error(`Request failed with status ${response.status} ${response.statusText}`);
  }

  const operationOutcomeMessage = readOperationOutcomeMessage(json);
  if (!response.ok || operationOutcomeMessage !== null) {
    throw new Error(operationOutcomeMessage ?? `Request failed with status ${response.status}`);
  }
  return json;
}

/** Parses the FML transform endpoint's `Parameters` response into the shape `FmlBench.tsx` renders. */
export function parseFmlResponse(response: FhirParameters): FmlEvalResult {
  const parameters = response.parameter ?? [];

  const configPart = parameters.find((parameter) => parameter.name === 'parameters');
  const evaluatorPart = configPart?.part?.find((part) => part.name === 'evaluator');
  const evaluator = (evaluatorPart?.valueString as string) ?? '';

  const trace = parameters
    .filter((parameter) => parameter.name === 'trace')
    .map((parameter) => (parameter.valueString as string) ?? '');

  const resultPart = parameters.find((parameter) => parameter.name === 'result');
  const output = typeof resultPart?.valueString === 'string' ? resultPart.valueString : null;

  const outcomePart = parameters.find((parameter) => parameter.name === 'outcome');
  const outcomeResource = outcomePart?.resource as
    | { issue?: { severity?: string; diagnostics?: string; details?: { text?: string } }[] }
    | undefined;
  const outcomeIssues = (outcomeResource?.issue ?? [])
    .filter((issue) => issue.severity === 'error')
    .map((issue) => issue.diagnostics?.trim() || issue.details?.text?.trim() || 'Unknown rule error');

  return { ...EMPTY_RESULT, evaluator, output, trace, outcomeIssues };
}
```

- [ ] **Step 2: Typecheck**

Run (from `frontend/`): `npm run build`
Expected: succeeds with no TypeScript errors. `fmlApi.ts` isn't imported anywhere yet, so this only confirms the new file itself compiles standalone.

- [ ] **Step 3: Lint**

Run (from `frontend/`): `npm run lint`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/benches/fml/fmlApi.ts
git commit -m "feat: add fmlApi.ts FML backend client

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 10: Rewire `FmlBench.tsx` to the real backend

**Files:**
- Modify: `frontend/src/benches/fml/FmlBench.tsx` (full rewrite)
- Delete: `frontend/src/benches/fml/fmlEngine.ts`

Current `FmlBench.tsx` calls the synchronous mock `runFml(mapText, sourceText)` from `fmlEngine.ts` directly in `useState`'s initializer and in the button's `onClick`. It renders three tabs: `output` / `diff` / `log` (the `log` tab shows `result.log: FmlLogRow[]` — a per-rule table with rule name/group/src/tgt/val/status columns that only the mock interpreter can produce, since it processes each rule as a discrete regex match).

The real backend has no equivalent of that per-rule log table — `Ignixa.FhirMappingLanguage`'s `MappingEvaluator` doesn't report a rule-by-rule trace unless the map explicitly calls `log(...)`. So the `log` tab becomes a `trace` tab showing the `log(...)` lines captured in `FmlEvalResult.trace` (one line per entry, no columns) instead of the mock's per-rule table. This matches the prior segment's design decision recorded in the plan's `Architecture` section.

This task replaces the mock call with `buildFmlRequest`/`runFml`/`parseFmlResponse` from `fmlApi.ts`, adds an explicit-run (no debounce) abortable request lifecycle matching the `▶ Run map` button (button click aborts any in-flight request first, matching the pattern in `useFhirPathEval.ts` but triggered by click instead of a debounce timer), and an `isLoading` indicator in the engine badge.

- [ ] **Step 1: Replace `frontend/src/benches/fml/FmlBench.tsx` in full**

```typescript
import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { HighlightedTextarea } from '../components/HighlightedTextarea';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { getErrorMessage } from '../shared/errorMessage';
import { diffLines } from './diffLines';
import { buildFmlRequest, parseFmlResponse, runFml, type FmlEvalResult } from './fmlApi';
import { DEFAULT_EXPECTED_TEXT, DEFAULT_MAP_TEXT, DEFAULT_SOURCE_TEXT } from './fmlFixtures';
import { highlightFml } from './fmlHighlight';

type FmlTab = 'output' | 'diff' | 'trace';

const TAB_ITEMS: PillItem<FmlTab>[] = [
  { id: 'output', label: 'Output' },
  { id: 'diff', label: 'Diff vs expected' },
  { id: 'trace', label: 'Trace' },
];

const EMPTY_RESULT: FmlEvalResult = { error: null, evaluator: '', output: null, trace: [], outcomeIssues: [] };

/** Reads the `"MapName"` string out of a map's declaration line (`map "url" = "MapName"`), for display next to the editor. Returns '' if the map has no declaration line yet. */
function extractMapName(mapText: string): string {
  const match = mapText.match(/map\s+"[^"]*"\s*=\s*"([^"]+)"/);
  return match ? match[1] : '';
}

export function FmlBench() {
  const stacked = useIsNarrowViewport(720);
  const twoColumnStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stacked ? '1fr' : 'minmax(420px,52%) 1fr',
    gap: 14,
    alignItems: 'start',
  };

  const [mapText, setMapText] = useState(DEFAULT_MAP_TEXT);
  const [sourceText, setSourceText] = useState(DEFAULT_SOURCE_TEXT);
  const [expectedText, setExpectedText] = useState(DEFAULT_EXPECTED_TEXT);
  const [tab, setTab] = useState<FmlTab>('output');
  const [result, setResult] = useState<FmlEvalResult>(EMPTY_RESULT);
  const [isLoading, setIsLoading] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);

  // Abort any in-flight request if the bench unmounts mid-run.
  useEffect(() => () => abortControllerRef.current?.abort(), []);

  const highlightedLines = useMemo(() => highlightFml(mapText), [mapText]);
  const mapName = useMemo(() => extractMapName(mapText), [mapText]);
  const diffRows = useMemo(() => (result.output ? diffLines(result.output, expectedText) : []), [result.output, expectedText]);

  const runMap = () => {
    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;
    setIsLoading(true);

    let body;
    try {
      body = buildFmlRequest({ mapText, resourceText: sourceText });
    } catch (error) {
      setIsLoading(false);
      setResult({ ...EMPTY_RESULT, error: `Source JSON — ${getErrorMessage(error)}` });
      return;
    }

    runFml(body, controller.signal)
      .then((response) => setResult(parseFmlResponse(response)))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return;
        }
        setResult({ ...EMPTY_RESULT, error: getErrorMessage(error) });
      })
      .finally(() => {
        if (abortControllerRef.current === controller) {
          setIsLoading(false);
        }
      });
  };

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>FHIR Mapping Language</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Author a StructureMap in FML and debug it rule by rule.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{result.evaluator || (isLoading ? 'transforming…' : 'ignixa-lab')}</span>
        <button type="button" onClick={runMap} style={primaryButtonStyle} disabled={isLoading}>
          ▶ Run map
        </button>
      </div>

      <div style={twoColumnStyle}>
        <Card style={{ minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Map source · .fml</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{mapName}</span>
          </div>
          <HighlightedTextarea value={mapText} onChange={setMapText} lines={highlightedLines} style={{ height: 300 }} />
        </Card>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
          <Card>
            <span style={sectionLabelStyle}>Source resource</span>
            <textarea
              value={sourceText}
              onChange={(event) => setSourceText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 160, fontSize: 11.5 }}
            />
          </Card>

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <Pills items={TAB_ITEMS} activeId={tab} onChange={setTab} />
              <div style={{ flex: 1 }} />
              <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                {result.output ? `${result.trace.length} trace ${result.trace.length === 1 ? 'entry' : 'entries'}` : ''}
              </span>
            </div>

            {result.error ? <ErrorBanner message={result.error} /> : null}
            {!result.error && result.outcomeIssues.length > 0 ? (
              <ErrorBanner message={`${result.outcomeIssues.length} rule error(s): ${result.outcomeIssues.join('; ')}`} />
            ) : null}

            {!result.error && tab === 'output' ? (
              <pre
                style={{
                  margin: 0,
                  padding: '12px 14px',
                  borderRadius: 8,
                  background: 'var(--code)',
                  border: '1px solid var(--border)',
                  fontFamily: monoFont,
                  fontSize: 11.5,
                  lineHeight: 1.6,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  color: 'var(--text)',
                  maxHeight: 420,
                  overflow: 'auto',
                }}
              >
                {result.output ?? '—'}
              </pre>
            ) : null}

            {!result.error && tab === 'diff' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <textarea
                  value={expectedText}
                  onChange={(event) => setExpectedText(event.target.value)}
                  spellCheck={false}
                  style={{ ...monoTextareaStyle, height: 120, fontSize: 11 }}
                />
                <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'auto', maxHeight: 300 }}>
                  {diffRows.map((row, index) => (
                    <div
                      key={index}
                      style={{
                        display: 'flex',
                        gap: 10,
                        padding: '1px 12px',
                        background: row.sign === '+' ? 'var(--pass-bg)' : row.sign === '−' ? 'var(--fail-bg)' : 'transparent',
                      }}
                    >
                      <span
                        style={{
                          fontFamily: monoFont,
                          fontSize: 11,
                          width: 10,
                          flex: 'none',
                          color: row.sign === '+' ? 'var(--pass)' : row.sign === '−' ? 'var(--fail)' : 'var(--chip-gray-fg)',
                        }}
                      >
                        {row.sign}
                      </span>
                      <span style={{ fontFamily: monoFont, fontSize: 11, whiteSpace: 'pre', color: 'var(--text2)' }}>{row.text}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            {!result.error && tab === 'trace' ? (
              result.trace.length > 0 ? (
                result.trace.map((line, index) => (
                  <div
                    key={index}
                    style={{ display: 'flex', gap: 12, padding: '8px 10px', borderBottom: '1px solid var(--border)' }}
                  >
                    <span
                      style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)', width: 18, textAlign: 'right', flex: 'none' }}
                    >
                      {index + 1}
                    </span>
                    <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text2)', flex: 1, minWidth: 0, wordBreak: 'break-word' }}>
                      {line}
                    </span>
                  </div>
                ))
              ) : (
                <span style={{ fontSize: 11, color: 'var(--text4)' }}>
                  No trace output — add <span style={{ fontFamily: monoFont }}>log(...)</span> anywhere in a rule to log a checkpoint.
                </span>
              )
            ) : null}
          </Card>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Delete the mock engine**

```bash
rm frontend/src/benches/fml/fmlEngine.ts
```

- [ ] **Step 3: Typecheck**

Run (from `frontend/`): `npm run build`
Expected: succeeds with no TypeScript errors — in particular, no remaining import of `fmlEngine.ts` or `FmlLogRow`/`FmlRunResult` anywhere else in the frontend.

- [ ] **Step 4: Lint**

Run (from `frontend/`): `npm run lint`
Expected: no errors.

- [ ] **Step 5: Manual smoke check**

Run (from `frontend/`): `npm run dev`, open the app, select the FML tab, click "▶ Run map" with the default fixture map/resource loaded. Expected: the "Output" tab shows a pretty-printed `Person` resource JSON (matching the shape of `DEFAULT_EXPECTED_TEXT` in `fmlFixtures.ts`), the engine badge shows a real evaluator name (not "mock engine"), and the "Diff vs expected" tab shows mostly-matching lines.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/benches/fml/FmlBench.tsx
git rm frontend/src/benches/fml/fmlEngine.ts
git commit -m "feat: wire FmlBench.tsx to the real StructureMap/\$transform backend

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

**Bugs found during a real live smoke test (not caught by any prior review — none of these files were in Task 10's declared list):**

After the implementer's commit passed both reviews, the controller ran `func start` locally and POSTed the actual `DEFAULT_MAP_TEXT`/`PATIENT_EXAMPLE` fixtures to the real `StructureMap/$transform` endpoint (rather than trusting the implementer's "couldn't test against a live backend" report). This surfaced three real bugs, all fixed directly by the controller and committed separately from Task 10's own commit:

1. **`fmlFixtures.ts`'s `DEFAULT_MAP_TEXT` used double-quoted FML string literals** (`map "url" = "Name"`, `"copy_gender"`, etc.) — the same tokenizer bug found 3 times earlier in Tasks 3/5 (`Ignixa.FhirMappingLanguage` treats `"..."` as a `DelimitedIdentifier`, not a string literal), but this was a 4th occurrence in a file no task's file list covered. Fixed by converting to single-quoted literals throughout.
2. **`fmlFixtures.ts`'s `DEFAULT_EXPECTED_TEXT` didn't match the real engine's output** — it was hand-authored against the old mock engine's naive semantics (accumulating every repeated-source value into an array). The real engine respects actual FHIR `Person` cardinalities: rules that fire once per repeating source item (`name.family`, `name.given`, `telecom.value`) overwrite the single target node each time, so only the *last* value survives as a scalar; `identifier` is the one exception since `Person.identifier` is itself list-typed. Fixed by updating the expected fixture to the real, verified output so the "Diff vs expected" tab shows a clean match on the default example.
3. **`parseFmlResponse()` (in `fmlApi.ts`) didn't normalize line endings** — the backend pretty-prints JSON with `\r\n`, but `diffLines()` splits strictly on `\n`, so *every* successful transform's output would show every line as changed vs. the LF-only expected text, regardless of whether the content actually matched. This is a generic bug (not specific to the default fixture) that would have affected every use of the Diff tab. Fixed by normalizing `\r\n` → `\n` when extracting `output` from the response.
4. **`FmlBench.tsx`'s `extractMapName` regex only matched double-quoted map declarations** (`map\s+"[^"]*"\s*=\s*"([^"]+)"`, copied from this plan's own now-corrected example syntax above). Once the map text fixture was corrected to valid single-quoted FML, this regex stopped matching, so the map-name label next to the editor went silently blank for any map the real backend can actually parse. Fixed by rewriting the regex to match single quotes.

This is why running a real live smoke test against the actual backend (not just "build succeeds, dev server starts with no console errors") mattered here: none of these four bugs live in a file Task 10's spec or either reviewer's diff covered, so a per-task review — however thorough — could not have caught them.

### Task 11: `sofApi.ts` — SQL-on-FHIR backend client

**Files:**
- Create: `frontend/src/benches/sof/sofApi.ts`

This mirrors the same conventions as `fmlApi.ts` (Task 9), adapted for the SQL-on-FHIR wire shape from Task 6/8: a `Parameters` request with an embedded `viewResource` plus zero-or-more repeated `resource` parts, POSTed to `ViewDefinition/$viewdefinition-run`, returning either a plain JSON array of row objects (success) or an `OperationOutcome` (failure, always non-2xx per Task 8).

`SofBench.tsx` currently takes a single "Resources · JSON array" textarea holding either one resource object or a JSON array of resources (see `sofEngine.ts`'s `const resourceList = Array.isArray(resources) ? resources : [resources];`). `buildSofRequest` reproduces that same flexibility, expanding whichever shape the user typed into one `resource` parameter per item.

- [ ] **Step 1: Create `frontend/src/benches/sof/sofApi.ts`**

```typescript
/** A generic FHIR `Parameters.parameter[]` entry — permissive enough to cover every `resource`/`valueString` shape used by this endpoint. */
export interface FhirParameter {
  name: string;
  resource?: unknown;
  [valueKey: string]: unknown;
}

export interface FhirParameters {
  resourceType: string;
  parameter?: FhirParameter[];
}

export type SofCellValue = string | number | boolean | null;

export interface SofEvalResult {
  error: string | null;
  columns: string[];
  rows: Record<string, SofCellValue>[];
  meta: string;
}

export interface SofRequestInput {
  viewDefinitionText: string;
  resourcesText: string;
}

const EMPTY_RESULT: SofEvalResult = { error: null, columns: [], rows: [], meta: '' };

/** Builds the FHIR `Parameters` request body for `ViewDefinition/$viewdefinition-run`. `resourcesText` may be a single resource object or a JSON array of resources — either way, each one becomes its own `resource` parameter. Throws if either JSON text field is invalid — callers should catch and surface it as a JSON error. */
export function buildSofRequest(input: SofRequestInput): FhirParameters {
  const viewResource = JSON.parse(input.viewDefinitionText);
  const resourcesValue: unknown = JSON.parse(input.resourcesText);
  const resourceList = Array.isArray(resourcesValue) ? resourcesValue : [resourcesValue];

  return {
    resourceType: 'Parameters',
    parameter: [
      { name: 'viewResource', resource: viewResource },
      ...resourceList.map((resource) => ({ name: 'resource', resource })),
    ],
  };
}

/** Extracts a readable message from an `OperationOutcome` error response, if that's what the body is. */
function readOperationOutcomeMessage(body: unknown): string | null {
  const outcome = body as { resourceType?: string; issue?: { details?: { text?: string }; diagnostics?: string }[] };
  if (outcome?.resourceType !== 'OperationOutcome' || !outcome.issue?.length) {
    return null;
  }
  const issue = outcome.issue[0];
  return issue.details?.text?.trim() || issue.diagnostics?.trim() || null;
}

/** POSTs to the SQL-on-FHIR view runner and returns the raw JSON-array response. Throws if the body is an `OperationOutcome` (the backend reports every error this way, always with a non-2xx status), the response is non-2xx, or on a network/abort error. */
export async function runSof(body: FhirParameters, signal: AbortSignal): Promise<unknown[]> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const response = await fetch(`${apiBaseUrl}/api/ViewDefinition/$viewdefinition-run`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });

  const text = await response.text();
  let json: unknown;
  try {
    json = text ? JSON.parse(text) : [];
  } catch {
    throw new Error(`Request failed with status ${response.status} ${response.statusText}`);
  }

  const operationOutcomeMessage = readOperationOutcomeMessage(json);
  if (!response.ok || operationOutcomeMessage !== null) {
    throw new Error(operationOutcomeMessage ?? `Request failed with status ${response.status}`);
  }
  return Array.isArray(json) ? json : [];
}

/** Parses the SQL-on-FHIR runner's JSON-array response into the table shape `SofBench.tsx` renders — column order is the union of keys in first-seen order across all rows. */
export function parseSofResponse(rows: unknown[]): SofEvalResult {
  const columns: string[] = [];
  const tableRows: Record<string, SofCellValue>[] = [];

  for (const row of rows) {
    const record = (row ?? {}) as Record<string, unknown>;
    const tableRow: Record<string, SofCellValue> = {};
    for (const key of Object.keys(record)) {
      if (!columns.includes(key)) columns.push(key);
      const value = record[key];
      tableRow[key] = typeof value === 'object' && value !== null ? JSON.stringify(value) : (value as SofCellValue);
    }
    tableRows.push(tableRow);
  }

  const meta = `${tableRows.length} ${tableRows.length === 1 ? 'row' : 'rows'} · ${columns.length} cols`;
  return { ...EMPTY_RESULT, columns, rows: tableRows, meta };
}
```

- [ ] **Step 2: Typecheck**

Run (from `frontend/`): `npm run build`
Expected: succeeds with no TypeScript errors.

- [ ] **Step 3: Lint**

Run (from `frontend/`): `npm run lint`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/benches/sof/sofApi.ts
git commit -m "feat: add sofApi.ts SQL-on-FHIR backend client

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 12: Rewire `SofBench.tsx` to the real backend

**Files:**
- Modify: `frontend/src/benches/sof/SofBench.tsx` (full rewrite)
- Modify: `frontend/src/benches/sof/sofFixtures.ts:3-27` (add `resourceType: 'ViewDefinition'` to the default fixture)
- Delete: `frontend/src/benches/sof/sofEngine.ts`
- Delete: `frontend/src/benches/shared/miniFhirPath.ts`

`sofFixtures.ts`'s `DEFAULT_VIEW_DEFINITION_TEXT` currently has no `resourceType` field (the mock engine in `sofEngine.ts` never looked at it). Now that this JSON is sent to the backend as an embedded FHIR `resource` part, it should be a well-formed `ViewDefinition` resource, so this task adds the missing `resourceType` field.

`miniFhirPath.ts` (in `frontend/src/benches/shared/`) is a hand-rolled subset-FHIRPath evaluator used only by the two mock engines being replaced (`fmlEngine.ts`, already deleted in Task 10, and `sofEngine.ts`, deleted in this task) — confirmed via `grep -r miniFhirPath frontend/src` returning only those two files. It has no other callers, so it's deleted here too.

- [ ] **Step 1: Add `resourceType` to the default ViewDefinition fixture**

In `frontend/src/benches/sof/sofFixtures.ts`, change:

```typescript
export const DEFAULT_VIEW_DEFINITION_TEXT = JSON.stringify(
  {
    resource: 'Patient',
    status: 'active',
    name: 'patient_demographics',
```

to:

```typescript
export const DEFAULT_VIEW_DEFINITION_TEXT = JSON.stringify(
  {
    resourceType: 'ViewDefinition',
    resource: 'Patient',
    status: 'active',
    name: 'patient_demographics',
```

- [ ] **Step 2: Replace `frontend/src/benches/sof/SofBench.tsx` in full**

```typescript
import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { Card, ErrorBanner } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { getErrorMessage } from '../shared/errorMessage';
import { buildSofRequest, parseSofResponse, runSof, type SofEvalResult } from './sofApi';
import { DEFAULT_RESOURCES_TEXT, DEFAULT_VIEW_DEFINITION_TEXT } from './sofFixtures';

const EMPTY_RESULT: SofEvalResult = { error: null, columns: [], rows: [], meta: '' };

export function SofBench() {
  const stacked = useIsNarrowViewport(720);
  const twoColumnStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stacked ? '1fr' : 'minmax(380px,44%) 1fr',
    gap: 14,
    alignItems: 'start',
  };

  const [viewDefinitionText, setViewDefinitionText] = useState(DEFAULT_VIEW_DEFINITION_TEXT);
  const [resourcesText, setResourcesText] = useState(DEFAULT_RESOURCES_TEXT);
  const [result, setResult] = useState<SofEvalResult>(EMPTY_RESULT);
  const [isLoading, setIsLoading] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);

  // Abort any in-flight request if the bench unmounts mid-run.
  useEffect(() => () => abortControllerRef.current?.abort(), []);

  const runView = () => {
    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;
    setIsLoading(true);

    let body;
    try {
      body = buildSofRequest({ viewDefinitionText, resourcesText });
    } catch (error) {
      setIsLoading(false);
      setResult({ ...EMPTY_RESULT, error: getErrorMessage(error) });
      return;
    }

    runSof(body, controller.signal)
      .then((rows) => setResult(parseSofResponse(rows)))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return;
        }
        setResult({ ...EMPTY_RESULT, error: getErrorMessage(error) });
      })
      .finally(() => {
        if (abortControllerRef.current === controller) {
          setIsLoading(false);
        }
      });
  };

  const gridColumns = result.columns.length ? `repeat(${result.columns.length}, minmax(110px, 1fr))` : '1fr';

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>SQL on FHIR</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Run a ViewDefinition over resources and inspect the flattened table.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{isLoading ? 'running…' : 'ignixa-lab'}</span>
        <button type="button" onClick={runView} style={primaryButtonStyle} disabled={isLoading}>
          ▶ Run view
        </button>
      </div>

      <div style={twoColumnStyle}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
          <Card>
            <span style={sectionLabelStyle}>ViewDefinition</span>
            <textarea
              value={viewDefinitionText}
              onChange={(event) => setViewDefinitionText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 320, fontSize: 11.5 }}
            />
          </Card>
          <Card>
            <span style={sectionLabelStyle}>Resources · JSON array</span>
            <textarea
              value={resourcesText}
              onChange={(event) => setResourcesText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 220, fontSize: 11.5 }}
            />
          </Card>
        </div>

        <Card style={{ minHeight: 400, minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Result table</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.meta}</span>
          </div>

          {result.error ? <ErrorBanner message={result.error} /> : null}

          <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'auto' }}>
            <div style={{ display: 'grid', gridTemplateColumns: gridColumns, background: 'var(--panel2)', borderBottom: '1px solid var(--border)' }}>
              {result.columns.map((column) => (
                <span
                  key={column}
                  style={{
                    padding: '8px 12px',
                    fontFamily: monoFont,
                    fontSize: 10,
                    fontWeight: 600,
                    letterSpacing: '.08em',
                    textTransform: 'uppercase',
                    color: 'var(--text2)',
                    borderRight: '1px solid var(--border)',
                  }}
                >
                  {column}
                </span>
              ))}
            </div>
            {result.rows.map((row, rowIndex) => (
              <div key={rowIndex} style={{ display: 'grid', gridTemplateColumns: gridColumns, borderBottom: '1px solid var(--border)' }}>
                {result.columns.map((column) => {
                  const value = row[column];
                  return (
                    <span
                      key={column}
                      style={{
                        padding: '7px 12px',
                        fontFamily: monoFont,
                        fontSize: 11.5,
                        color: value === null || value === undefined ? 'var(--text4)' : 'var(--text)',
                        borderRight: '1px solid var(--border)',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                      }}
                    >
                      {value === null || value === undefined ? '∅' : String(value)}
                    </span>
                  );
                })}
              </div>
            ))}
          </div>
          <span style={{ fontSize: 11, color: 'var(--text4)' }}>
            Columns come from <span style={{ fontFamily: monoFont }}>select[].column[].path</span> (FHIRPath);{' '}
            <span style={{ fontFamily: monoFont }}>forEach</span> unnests one row per item.
          </span>
        </Card>
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Delete the mock engine and its shared FHIRPath subset evaluator**

```bash
rm frontend/src/benches/sof/sofEngine.ts
rm frontend/src/benches/shared/miniFhirPath.ts
```

- [ ] **Step 4: Typecheck**

Run (from `frontend/`): `npm run build`
Expected: succeeds with no TypeScript errors — in particular, no remaining import of `sofEngine.ts`, `miniFhirPath.ts`, or `SofRunResult` anywhere else in the frontend.

- [ ] **Step 5: Lint**

Run (from `frontend/`): `npm run lint`
Expected: no errors.

- [ ] **Step 6: Manual smoke check**

Run (from `frontend/`): `npm run dev`, open the app, select the SQL on FHIR tab, click "▶ Run view" with the default fixture ViewDefinition/resources loaded. Expected: the result table shows one row per official `name` entry across the three fixture patients, with `id`/`gender`/`birth_date`/`family`/`given` columns populated.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/benches/sof/SofBench.tsx frontend/src/benches/sof/sofFixtures.ts
git rm frontend/src/benches/sof/sofEngine.ts frontend/src/benches/shared/miniFhirPath.ts
git commit -m "feat: wire SofBench.tsx to the real ViewDefinition/\$viewdefinition-run backend

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 13: Final verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full backend test suite**

Run (from repo root): `dotnet test Ignixa.Lab.sln`
Expected: PASS, 0 failures — includes every test added in Tasks 1-8 plus all pre-existing tests (confirms nothing in `EndpointClassifier`, `Program.cs`, or shared services regressed).

- [ ] **Step 2: Run the full frontend build**

Run (from `frontend/`): `npm run build`
Expected: succeeds — `tsc -b` reports no type errors across the whole frontend, then `vite build` completes.

- [ ] **Step 3: Run the frontend linter**

Run (from `frontend/`): `npm run lint`
Expected: no errors.

- [ ] **Step 4: Start both servers and manually verify end-to-end**

Run (from repo root): start the backend (`func start` or the repo's existing backend dev-run command, from `backend/src/Ignixa.Lab.Functions/`) and the frontend (`npm run dev`, from `frontend/`). Open the app in a browser:
- FML tab: verify "▶ Run map" produces a `Person` output resource and a non-mock evaluator badge (Task 10's smoke check).
- SQL on FHIR tab: verify "▶ Run view" produces the expected 3-row demographics table (Task 12's smoke check).
- Try an invalid map (e.g. delete the closing `}` from the `group` block) and confirm the error banner shows a parse error instead of a silent failure.
- Try an invalid ViewDefinition (e.g. set `"select": "not-an-array"`) and confirm the error banner shows a validation error instead of a silent failure.

Expected: all four checks pass with no console errors in the browser dev tools.

- [ ] **Step 5: Commit (if Step 4 required any fixes)**

If Step 4 uncovered any issues and required fixes, commit them now with a descriptive message and the standard trailer. If no fixes were needed, there is nothing to commit for this task.

**A 5th bug found during Task 13's own manual verification:**

Step 4's invalid-ViewDefinition check (`"select": "not-an-array"`) was expected to produce a validation-error banner. Instead, POSTing this exact payload to the real `ViewDefinition/$viewdefinition-run` endpoint returned **HTTP 200** with a silent `[{}]` (one empty-object row per input resource) — `Ignixa.SqlOnFhir`'s evaluator doesn't check that `select` is an array before processing it, so a malformed `select` was silently treated as "no select items" rather than raising an error. None of the 179 pre-existing backend tests covered this exact scenario (the existing `Evaluate_MalformedColumnPath_ReturnsStructuredError` test covers a malformed column *path*, not a malformed `select` *type*).

Fixed in `SqlOnFhirService.Evaluate()` (`backend/src/Ignixa.Lab.Functions/Services/SqlOnFhir/SqlOnFhirService.cs`): before invoking the evaluator, check `request.ViewResource.MutableNode["select"]` — if present and not a `System.Text.Json.Nodes.JsonArray`, return a structured `SqlOnFhirResult.Error` (mirroring the existing error-reporting pathway used for evaluator exceptions) instead of proceeding. Added a new test, `Evaluate_SelectIsNotAnArray_ReturnsStructuredError`, following the existing `MakeRequest`/test pattern. Re-ran the full suite (180/180 passing) and re-verified live against the real backend: the same payload now returns HTTP 400 with a proper `OperationOutcome` ("ViewDefinition evaluation error: 'select' must be an array."), and the valid-ViewDefinition path was re-checked to confirm no regression.

This is the 5th genuine bug found via live testing this session (following the 4 documented after Task 10) — again, one that no implementer's self-review or reviewer subagent's diff-level analysis could have caught, since it required actually exercising the real evaluator with malformed input rather than just the shapes each task's spec anticipated.

**3 more bugs found by a final whole-branch code reviewer subagent (after all 13 tasks were "done"):**

Per the subagent-driven-development skill, a final code-reviewer subagent reviewed the entire branch diff (not just per-task diffs) after Task 13 completed. It decompiled the actual `Ignixa.SqlOnFhir`/`Ignixa.Serialization` NuGet DLLs (via `ilspycmd`) to verify its claims rather than guessing from signatures, and found 3 more genuine issues, all independently re-verified (by decompiling the same DLLs myself, then fixing and testing) before being fixed:

6. **`SqlOnFhirService` held `SqlOnFhirEvaluator` as a shared `static readonly` field, but `SqlOnFhirEvaluator` is not stateless.** Decompiling `Ignixa.SqlOnFhir.dll` showed `EvaluateBatch` caches compiled `ViewDefinitionExpression`s in a plain, unsynchronized `Dictionary<string, ViewDefinitionExpression>`, mutated via unsynchronized `TryGetValue` + indexer-set on every call — a thread-safety hazard under Azure Functions' concurrent request dispatch. Worse, the cache key includes `viewDefinitionNode.GetHashCode()`, and decompiling `Ignixa.Serialization.dll` confirmed the navigator type returned by `ResourceJsonNode.ToSourceNavigator()` doesn't override `GetHashCode()`, so every request (which always parses a fresh `ResourceJsonNode`) produces a distinct identity-based hash — the "cache" never hits its own entries, it just leaks one never-evicted entry per request forever. Fixed by constructing a fresh `SqlOnFhirEvaluator` per call in `Evaluate()` instead of sharing one, removing both the thread-safety hazard and the leak. Added `Evaluate_CalledConcurrently_DoesNotThrow` as a light regression test.
7. **A malformed `_limit` parameter silently truncated results to an empty array instead of erroring.** `ParameterJsonNode.GetValueAs<int>()` (decompiled) wraps its conversion in `try { ... } catch { return default(T); }`, so any non-integer `_limit` (a numeric string, a decimal, a boolean, or a mistyped value key) silently became `0`, which `SqlOnFhirService.Evaluate`'s existing `limit >= 0 && limit < rows.Count` check then used to truncate every result set to `[]` — a plausible-looking but silently wrong "no rows" HTTP 200 instead of a 400 validation error. Fixed in `SqlOnFhirFunctions.BuildSqlOnFhirRequest` by validating the raw `JsonValue` via `TryGetValue<int>` before accepting `_limit`, rejecting anything that doesn't round-trip as a real integer. Added a `[Theory]` test covering a numeric string, a decimal, and a boolean.
8. **A `resource` parameter with no embedded resource was silently dropped from evaluation instead of rejected.** `ParameterJsonNode.Resource` (decompiled) returns `null` (no exception) whenever the `resource` JSON key is simply absent (e.g. the parameter was sent as `valueString` instead). The prior code only added to the resource list `if (resource != null)`, silently skipping malformed entries with no error — so a request mixing valid and malformed `resource` parameters ran the ViewDefinition against fewer resources than the caller sent, with an HTTP 200 and no indication anything was dropped. This was also inconsistent with the parallel FML endpoint (`FmlFunctions`), which errors clearly when its analogous `resource` parameter can't be resolved. Fixed by rejecting immediately with a clear 400 error when a `resource` parameter has no embedded resource, mirroring the existing `viewResource`-required check. Added `RunViewDefinition_ResourceParameterNotEmbedded_ReturnsBadRequestOperationOutcome`.

All 3 fixes were re-verified live against the real running backend (correct HTTP 400s for the malformed cases, no regression on the valid-request path) and the full suite was re-run (185/185 passing). This reinforces the pattern already seen with bugs 1-5: neither an implementer's self-review nor a per-task spec/quality reviewer scoped to that task's declared files could plausibly have caught any of these 8 bugs — it took either a live smoke test against the real backend, or (for these last 3) a reviewer willing to decompile the actual third-party library code rather than trust its public API signatures at face value.

---

## Self-Review

**Spec coverage** (against `docs/superpowers/specs/2026-07-02-fml-sof-backend-design.md`):
- FML request/response wire shapes (`map`, `resource`, `parameters`/`trace`/`result`/`outcome` parts, `?debug=true`) → Tasks 2-5.
- FML hard-failure HTTP 400 + top-level `OperationOutcome` → Task 5.
- SQL-on-FHIR request/response wire shapes (`viewResource`, repeated `resource`, plain JSON array, `_format`/`_limit`, unsupported-parameter rejection) → Tasks 6-8.
- `EndpointClassifier` rate-limit tiering for both new endpoints → Tasks 5 and 8.
- NuGet package wiring for `Ignixa.FhirMappingLanguage`/`Ignixa.SqlOnFhir` → Task 1.
- Frontend FML bench wired to the real endpoint, mock engine removed → Tasks 9-10.
- Frontend SQL-on-FHIR bench wired to the real endpoint, mock engine and shared mini-FHIRPath evaluator removed → Tasks 11-12.
- End-to-end verification → Task 13.

No gaps found — every section of the spec has a corresponding task.

**Placeholder scan:** searched the plan for "TBD", "TODO", "similar to Task", "add appropriate", "handle edge cases" — none found. Every step shows complete code or an exact command with expected output.

**Type/signature consistency:** cross-checked property names across tasks (grepped every construction site and usage site in the finished document, not just skimmed):
- `FmlRequest` (Task 2): `Map`, `Resource`. `FmlResult` (Task 2): `Request`, `Error`, `ErrorDiagnostics`, `Output`, `LogLines`, `Errors`, `IsSuccess`. All used identically (same names, no typos) in `FmlService` (Task 3), `FmlResultFormatterTests`/`FmlResultFormatter` (Task 4), and `FmlFunctionsTests`/`FmlFunctions` (Task 5).
- `SqlOnFhirRequest` (Task 6): `ViewResource`, `Resources`, `Limit`. `SqlOnFhirResult` (Task 6): `Request`, `Error`, `ErrorDiagnostics`, `Rows`, `IsSuccess`. All used identically in `SqlOnFhirServiceTests`/`SqlOnFhirService` (Task 7) and `SqlOnFhirFunctionsTests`/`SqlOnFhirFunctions` (Task 8).
- Frontend `FmlEvalResult` (Task 9): `error`, `evaluator`, `output`, `trace`, `outcomeIssues` - used identically in `FmlBench.tsx` (Task 10). `SofEvalResult` (Task 11): `error`, `columns`, `rows`, `meta` - used identically in `SofBench.tsx` (Task 12).
- No mismatches found. (Caught and fixed one mismatch during this review: an earlier draft of this checklist itself described the C# property names incorrectly, e.g. writing `MapText`/`ResourceJson`/`Trace` instead of the actual `Map`/`Resource`/`LogLines` - the code in Tasks 2-8 was always correct and consistent; only this prose summary needed correcting.)

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-02-fml-sof-backend.md`.** Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**

---
