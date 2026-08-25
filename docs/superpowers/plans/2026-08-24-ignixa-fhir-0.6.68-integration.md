# Ignixa FHIR 0.6.68 integration Implementation Plan

> **Historical implementation record:** The plan was executed in PR #43. Its checkbox steps are retained to preserve the original task breakdown; final validation is recorded in [the task report](../../../.superpowers/sdd/2026-08-24-ignixa-fhir-0.6.68-integration/task-2-report.md).

**Goal:** Upgrade `ignixa-lab` to the released Ignixa FHIR `0.6.68` package family and preserve the lab's Search and FHIRPath behavior across the release's breaking API and value-model changes.

**Architecture:** Keep the lab's existing per-FHIR-version Search dependency cache and frontend response contracts. Replace the removed `SearchCompiler.CompileAsync` tracing call with a request-scoped `SearchSqlCompiler` that creates a diagnostic `SearchPlan`, compiles it, and maps the new plan/compile diagnostics into the existing DTOs. Add only the FHIRPath AST and temporal/resolution adapters required by the release; do not add the upstream SQL Server data layer.

**Tech Stack:** .NET 10 Azure Functions isolated worker, C# nullable reference types, xUnit + FluentAssertions, `Ignixa.Search` `0.6.68`, `Ignixa.Search.Sql` `0.6.68-alpha`, `Ignixa.FhirPath`/serialization/specification packages `0.6.68`, React/TypeScript frontend.

**Spec:** `docs/superpowers/specs/2026-08-24-ignixa-fhir-0.6.68-integration-design.md`

## Global Constraints

- **Package versions:** stable Ignixa packages use `0.6.68`; `Ignixa.Search.Sql` uses `0.6.68-alpha`; TestScript packages use `0.6.68-beta`.
- **Search diagnostics:** use `SearchPlanOptions.DiagnosticsLevel = SearchDiagnosticsLevel.Full` so the Search bench retains parameter, plan, and SQL provenance.
- **Search compiler:** construct `SearchSqlCompiler` with a fresh `InMemorySymbolResolver` per request; continue caching the options builder and definition managers per FHIR version.
- **Frontend compatibility:** preserve `fhirVersion`, `resourceType`, `parameters`, `plan`, `sql`, `implicit`, and `failure`, plus existing AST node naming conventions; additive failure and selector metadata remain backward-compatible.
- **Error handling:** keep malformed caller input as HTTP 400, surface expected compiler failures in the trace's structured `failure` field with logging, map unexpected compiler/mapper defects to logged HTTP 500 responses, and never catch the broad `FhirException` hierarchy as a client-error marker.
- **FHIRPath values:** handle `FhirTemporal` values for `date`, `dateTime`, `instant`, and `time` without changing non-temporal primitive or complex-resource output.
- **Scope exclusions:** do not add SQL Server, EF, retry, schema-deployment, or other upstream data-layer dependencies; do not commit package binaries or machine-specific feeds.
- **Validation:** use the existing commands `dotnet restore Ignixa.Lab.sln`, `dotnet build Ignixa.Lab.sln -c Release`, `dotnet test Ignixa.Lab.sln -c Release`, `npm run test`, and `npm run build`.

---

### Task 1: Upgrade the Ignixa package family and establish the post-restore baseline

**Files:**
- Modify: `Directory.Packages.props:10-39`
- Test/restore: `Ignixa.Lab.sln`
- Inspect only: `backend/src/Ignixa.Lab.Functions/Ignixa.Lab.Functions.csproj`, `backend/test/Ignixa.Lab.Functions.Tests/Ignixa.Lab.Functions.Tests.csproj`

**Interfaces:**
- Consumes: the existing central package-management entries and NuGet `0.6.68` packages.
- Produces: a restored solution whose direct Ignixa references resolve to the release family; later tasks consume the compiler and FHIRPath APIs from that restore.

- [ ] **Step 1: Confirm the existing package references before editing**

Run:

```powershell
Get-Content Directory.Packages.props
Get-Content backend\src\Ignixa.Lab.Functions\Ignixa.Lab.Functions.csproj
Get-Content backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj
```

Expected: the central file contains the direct Ignixa package pins already listed in the design, and the two project files consume those centrally managed packages without a second version declaration. Do not add SQL Server or internal data-layer packages.

- [ ] **Step 2: Update only the central Ignixa versions**

Replace the existing values with:

```xml
<PackageVersion Include="Ignixa.TestScript" Version="0.6.68-beta" />
<PackageVersion Include="Ignixa.TestScript.FhirFakes" Version="0.6.68-beta" />
<PackageVersion Include="Ignixa.FhirPath" Version="0.6.68" />
<PackageVersion Include="Ignixa.Serialization" Version="0.6.68" />
<PackageVersion Include="Ignixa.Specification" Version="0.6.68" />
<PackageVersion Include="Ignixa.Validation" Version="0.6.68" />
<PackageVersion Include="Ignixa.PackageManagement" Version="0.6.68" />
<PackageVersion Include="Ignixa.FhirFakes" Version="0.6.68" />
<PackageVersion Include="Ignixa.Search" Version="0.6.68" />
<PackageVersion Include="Ignixa.Search.Sql" Version="0.6.68-alpha" />
<PackageVersion Include="Ignixa.TestScript.Suites" Version="0.6.68-beta" />
```

Keep the existing comments and unrelated Microsoft/test package versions unchanged.

- [ ] **Step 3: Restore from NuGet and inspect the exact restored assemblies**

Run:

```powershell
dotnet restore Ignixa.Lab.sln --force-evaluate
```

Expected: restore succeeds from the configured package sources and does not require a machine-specific feed. Confirm that the generated assets contain `Ignixa.Search.Sql/0.6.68-alpha` and `Ignixa.FhirPath/0.6.68`.

Use the restored `Ignixa.Search.Sql.xml` to verify the names used by later tasks:

```text
Ignixa.Search.Sql.SearchSqlCompiler
Ignixa.Search.Sql.ISearchSqlCompiler
Ignixa.Search.Sql.SearchPlanOptions
Ignixa.Search.Sql.SearchDiagnosticsLevel.Full
Ignixa.Search.Sql.SearchPlan.TryCompile()
Ignixa.Search.Sql.SearchCompilationResult
Ignixa.Search.Sql.SearchCompilationFailure
```

Use the restored FHIRPath assembly/XML to verify `InstanceSelectorExpression.TypeName`, `.NamespacePrefix`, `.FullTypeName`, `.Elements`, `.IsEmpty`, and `ElementAssignment.ElementName`/`.ValueExpression` before implementing Task 3.

- [ ] **Step 4: Build to expose the complete API delta**

Run:

```powershell
dotnet build Ignixa.Lab.sln -c Release --no-restore
```

Expected: the build identifies the removed `SearchCompiler.CompileAsync`/tracing API and the new `IFhirPathExpressionVisitor.VisitInstanceSelector` contract. Record any additional release-specific compiler errors and fix only those coupled to this migration in the later tasks.

- [ ] **Step 5: Commit the package-only change**

```powershell
git add Directory.Packages.props
git commit -m "chore: upgrade Ignixa packages to 0.6.68"
```

The commit message must end with:

```text
Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
Copilot-Session: c8da7bfe-0fde-41ef-adad-431c69d71f32
```

---

### Task 2: Migrate Search planning/compilation and preserve the trace response

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Functions/SearchFunctions.cs:351-445`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchEngineFactory.cs:12-88`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/Search/SearchTraceMapper.cs:1-112`
- Modify: `backend/src/Ignixa.Lab.Functions/Models/Search/SearchTraceResponse.cs` only if a new diagnostic field maps directly to an existing DTO slot
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Functions/SearchFunctionsTests.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchTraceMapperTests.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/Search/SearchEngineFactoryTests.cs`

**Interfaces:**
- Consumes: `SearchEngine`'s cached `ISearchOptionsBuilder`, `ICompartmentDefinitionManager`, and `ISearchParameterDefinitionManager`; parsed `IReadOnlyList<QueryParameter>`; `Expression? operationExpression`.
- Produces: a request-scoped `ISearchSqlCompiler`/`SearchSqlCompiler`, a `SearchPlan` created with full diagnostics, a `CompiledSearch` or `SearchCompilationFailure`, and the same `SearchTraceResponse` JSON fields used by the frontend.

- [ ] **Step 1: Add failing API-level tests for the two-phase compiler**

Add coverage to `SearchFunctionsTests.cs` that proves a normal query still returns both phases and an operation route still forwards its operation expression:

```csharp
[Fact]
public async Task Trace_PatientNameSmith_UsesPlanAndCompileDiagnostics()
{
    var result = await CreateFunctions().Trace(
        BuildGetRequest("?name=Smith"), "R4", "Patient", CancellationToken.None);

    var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
        .Should().BeOfType<SearchTraceResponse>().Subject;

    response.Failure.Should().BeNull();
    response.Parameters.Should().ContainSingle(p => p.Key == "name")
        .Which.Outcome.Kind.Should().Be("Compiled");
    response.Plan.Should().NotBeNull();
    response.Plan!.Rows.Should().NotBeEmpty();
    response.Sql.Should().NotBeNull();
    response.Sql!.Parameters.Should().NotBeEmpty();
}

[Fact]
public async Task Trace_MalformedDateValue_RemainsBadRequestAfterPlanMigration()
{
    var result = await CreateFunctions().Trace(
        BuildGetRequest("?birthdate=notadate"), "R4", "Patient", CancellationToken.None);

    result.Should().BeOfType<BadRequestObjectResult>();
}
```

Keep the existing normal-search, unknown-parameter, chained-reference, operation-expression, unsupported-shape, and version-routing tests; update only assertions whose old `SearchTrace` construction no longer matches the new diagnostic types.

- [ ] **Step 2: Run the focused Search tests and capture the expected compile failures**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SearchFunctionsTests|FullyQualifiedName~SearchTraceMapperTests|FullyQualifiedName~SearchEngineFactoryTests"
```

Expected: compilation fails at old `Ignixa.Search.Sql.Tracing` references and `SearchCompiler.CompileAsync`, confirming the tests are exercising the intended migration seam.

- [ ] **Step 3: Add the compiler to the cached engine shape**

Keep `SearchEngine` responsible only for reusable per-version dependencies:

```csharp
public sealed record SearchEngine(
    ISearchOptionsBuilder Builder,
    ISearchParameterDefinitionManager SearchParameters,
    ICompartmentDefinitionManager Compartments);
```

In `CompileAndRespondAsync`, construct the compiler from the cached collaborators and a fresh resolver:

```csharp
var compiler = new SearchSqlCompiler(
    new InMemorySymbolResolver(),
    engine.Builder,
    engine.Compartments,
    engine.SearchParameters,
    TimeProvider.System);
```

Do not cache `SearchSqlCompiler` or `InMemorySymbolResolver`; the resolver's surrogate-id state is deliberately request-scoped.

- [ ] **Step 4: Replace the removed static tracing call with explicit plan and compile phases**

Build the request options and call the verified `ISearchSqlCompiler` method:

```csharp
var planOptions = new SearchPlanOptions
{
    OperationExpression = operationExpression,
    DiagnosticsLevel = SearchDiagnosticsLevel.Full,
};

var plan = await compiler.CreatePlanAsync(
    resourceType,
    parameters,
    planOptions,
    cancellationToken);

var compiled = plan.TryCompile();
```

Use `compiled.Succeeded` as the branch condition. On success, read:

```csharp
compiled.Compiled!.Diagnostics
plan.Diagnostics
compiled.Compiled.Sql
compiled.Compiled.Parameters
```

On failure, retain the returned `SearchCompilationFailure` and its `Stage`, `Message`, `Span`, and optional diagnostics instead of inventing a successful trace. Preserve cancellation propagation.

- [ ] **Step 5: Refactor the mapper around new diagnostics without changing the DTO contract**

Map parameter outcomes and implicit values from `SearchCompilationDiagnostics.Parameters` and `.Implicit`. Map the plan from `.PlanTrace` and SQL ranges from `.SqlTextRanges`. Project bound values with `p.Value?.ToString()` as before.

Use explicit mapping for every known `ParameterOutcome`:

```csharp
private static ParameterOutcomeDto ToOutcomeDto(ParameterOutcome outcome) => outcome switch
{
    ParameterOutcome.Compiled => new("Compiled", null, null, null),
    ParameterOutcome.KnownMiss value => new("KnownMiss", value.Reason, null, ToSpanDto(value.Span)),
    ParameterOutcome.Ignored value => new("Ignored", value.Reason, null, ToSpanDto(value.Span)),
    ParameterOutcome.Failed value => new("Failed", ParameterFailureMessage, value.Stage.ToString(), ToSpanDto(value.Span)),
    _ => throw new NotSupportedException($"Unknown ParameterOutcome: {outcome.GetType().Name}."),
};
```

Map `PlanTraceFailure` or a `SearchCompilationFailure` into the current `failure` DTO for scope/stage/safe-message/parameterCode/span, while preserving the available parameter, plan, and implicit diagnostics. Keep raw compiler messages only in server-side logs. If a future diagnostic has no representable DTO slot, throw `NotSupportedException` from the mapper and let the endpoint's mapper-only catch return the existing logged 500 response. Do not silently drop diagnostic evidence.

- [ ] **Step 6: Preserve the request error split**

Keep the endpoint catches in this order:

```csharp
catch (OperationCanceledException)
{
    throw;
}
catch (Exception ex) when (ex is BadSearchRequestException or SearchResourceNotSupportedException)
{
    logger.LogInformation(ex, "Rejected search trace for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
    return new BadRequestObjectResult(new { error = ex.Message });
}
catch (SearchCompilationException ex)
{
    compiled = SearchCompilationResult.Failed(ex.Failure);
}
catch (NotSupportedException ex)
{
    logger.LogWarning(ex, "Unsupported search shape for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
    return new BadRequestObjectResult(new { error = "This query uses a shape the SQL compiler does not support yet." });
}
catch (Exception ex)
{
    logger.LogError(ex, "Unexpected failure compiling search trace for {FhirVersion}/{ResourceType}", resolvedVersion, resourceType);
    return new ObjectResult(new { error = "The search trace could not be compiled due to an internal error." })
    {
        StatusCode = StatusCodes.Status500InternalServerError,
    };
}
```

Keep mapper failures in a separate `try` block so a missing diagnostic mapping cannot be misreported as an unsupported caller query.

- [ ] **Step 7: Update mapper fixtures to compile against 0.6.68 diagnostics**

Construct mapper inputs from `SearchCompilationDiagnostics`, `QueryPlanTrace`, `CompiledSearch`, and `SearchCompilationFailure` where the new package exposes those records. Preserve the existing assertions for:

```text
Compiled, Ignored, KnownMiss, Failed
parameter spans and IR projections
bound SQL parameter names/values
CTE parameter ordinals and contributing ordinals
plan row kinds and referenced CTE indexes
SQL text range labels/kinds
implicit parameters
explicit mapper failure for an unknown outcome
```

Add a failure fixture that asserts a `SearchCompilationFailure` maps its `Stage` to `TraceFailureDto.Stage`, its `ParameterCode` to `TraceFailureDto.ParameterCode`, and its `Span` to `TraceFailureDto.Span`.

- [ ] **Step 8: Run the focused tests and then the backend suite**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SearchFunctionsTests|FullyQualifiedName~SearchTraceMapperTests|FullyQualifiedName~SearchEngineFactoryTests"
dotnet test Ignixa.Lab.sln -c Release --no-restore
```

Expected: the focused Search tests and the complete backend suite pass, with the existing Search response fields unchanged.

- [ ] **Step 9: Commit the Search migration**

```powershell
git add backend\src\Ignixa.Lab.Functions\Functions\SearchFunctions.cs backend\src\Ignixa.Lab.Functions\Services\Search\SearchEngineFactory.cs backend\src\Ignixa.Lab.Functions\Services\Search\SearchTraceMapper.cs backend\src\Ignixa.Lab.Functions\Models\Search\SearchTraceResponse.cs backend\test\Ignixa.Lab.Functions.Tests\Functions\SearchFunctionsTests.cs backend\test\Ignixa.Lab.Functions.Tests\Services\Search\SearchTraceMapperTests.cs backend\test\Ignixa.Lab.Functions.Tests\Services\Search\SearchEngineFactoryTests.cs
git commit -m "feat: migrate Search trace to Ignixa 0.6.68 compiler API"
```

Include the required trailers from Task 1.

---

### Task 3: Serialize FHIRPath instance selectors in the existing AST contract

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Serialization/JsonAstVisitor.cs:20-245`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/ResultFormatterTests.cs`

**Interfaces:**
- Consumes: `InstanceSelectorExpression` and `ElementAssignment` from `Ignixa.FhirPath.Expressions`, plus optional `AnalysisResult.NodeTypes`.
- Produces: the existing `FpAstNode`-compatible JSON envelope with a structured instance-selector node and nested assignment value expressions.

- [ ] **Step 1: Locate the existing AST test fixture and add a failing selector test**

The existing AST serialization assertions are in `backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/ResultFormatterTests.cs`; locate them with:

```powershell
Get-ChildItem backend\test -Recurse -Filter *.cs | Select-String -Pattern "JsonAstVisitor|ParseAndAnalyze|ExpressionType"
```

Add a test that parses and serializes an object initializer:

```csharp
[Fact]
public void VisitInstanceSelector_EmitsTypeNamespaceAssignmentsAndNestedValues()
{
    var analyzer = new ExpressionAnalyzer(new SchemaProviderFactory());
    var (expression, context, error) = analyzer.ParseAndAnalyze(
        "FHIR.Identifier { system: 'http://example.org', value: 'N0001' }",
        null,
        "Patient",
        "R4");

    error.Should().BeNull();
    var root = expression!.AcceptVisitor(new JsonAstVisitor { RootTypeName = "Patient" }, context);

    root["ExpressionType"]!.GetValue<string>().Should().Be("InstanceSelectorExpression");
    root["Name"]!.GetValue<string>().Should().Be("FHIR.Identifier");
    root["TypeName"]!.GetValue<string>().Should().Be("Identifier");
    root["NamespacePrefix"]!.GetValue<string>().Should().Be("FHIR");
    root["Arguments"]!.AsArray().Should().HaveCount(2);
    root["Arguments"]![0]!["Name"]!.GetValue<string>().Should().Be("system");
    root["Arguments"]![1]!["Name"]!.GetValue<string>().Should().Be("value");
}
```

Use the final restored member names confirmed in Task 1; if the JSON contract's existing frontend node only permits `Arguments`, keep assignment metadata inside each argument node rather than adding a new top-level frontend type.

- [ ] **Step 2: Run the selector test to verify the visitor contract is missing**

Run the test's fully qualified name with the existing backend test project. Expected: the test either fails to compile because `VisitInstanceSelector` is not implemented or fails because the selector currently has no JSON representation.

- [ ] **Step 3: Implement `VisitInstanceSelector`**

Add the visitor method:

```csharp
public JsonObject VisitInstanceSelector(InstanceSelectorExpression expression, AnalysisResult? context)
{
    var node = CreateNode(expression, "InstanceSelectorExpression", expression.FullTypeName, context);
    node["TypeName"] = expression.TypeName;
    if (expression.NamespacePrefix is not null)
        node["NamespacePrefix"] = expression.NamespacePrefix;
    node["IsEmpty"] = expression.IsEmpty;
    node["Arguments"] = new JsonArray(
        expression.Elements.Select(element =>
        {
            var assignment = CreateNode(element.ValueExpression, "ElementAssignment", element.ElementName, context);
            assignment["Arguments"] = new JsonArray(
                element.ValueExpression.AcceptVisitor(this, context));
            return assignment;
        }).ToArray());
    return node;
}
```

Preserve `CreateNode` position/length/line/column behavior on the selector and nested expression. Preserve inferred `ReturnType` from `AnalysisResult.NodeTypes`; do not replace it with a hard-coded object type. Keep empty selectors valid with an empty `Arguments` array and `IsEmpty: true`.

- [ ] **Step 4: Run selector, FHIRPath, and frontend AST tests**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Ast|FullyQualifiedName~FhirPath"
npm --prefix frontend run test
```

Expected: the new selector assertion passes and existing AST node names/argument ordering remain unchanged.

- [ ] **Step 5: Commit the AST adapter**

```powershell
git add backend\src\Ignixa.Lab.Functions\Serialization\JsonAstVisitor.cs backend\test
git commit -m "feat: serialize FHIRPath instance selectors in AST output"
```

Include the required trailers from Task 1. Stage only the AST implementation and its tests, not unrelated test changes.

---

### Task 4: Audit temporal values and `resolve()` compatibility

**Files:**
- Modify: `backend/src/Ignixa.Lab.Functions/Services/FhirPath/ResultFormatter.cs:275-313,389-470,589-603`
- Modify: `backend/src/Ignixa.Lab.Functions/Services/FhirPath/LightweightElementResolver.cs:80-100` only where the restored `IElement.Value` type requires an adapter
- Modify: `backend/src/Ignixa.Lab.Functions/Services/FhirPath/ExpressionEvaluator.cs` or `backend/src/Ignixa.Lab.Functions/Services/FhirPath/FhirPathService.cs` only if the restored evaluator exposes a temporal/resolution incompatibility
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/ResultFormatterTests.cs`
- Test: `backend/test/Ignixa.Lab.Functions.Tests/Services/FhirPath/FhirPathServiceTests.cs`

**Interfaces:**
- Consumes: `IElement.Value` values that may be `Ignixa.Abstractions.FhirTemporal`, existing result formatting helpers, and the upstream FHIRPath resolver behavior.
- Produces: canonical temporal JSON/display output, unchanged non-temporal output, and regression coverage for contained and sibling Bundle/entry `resolve()` paths.

- [ ] **Step 1: Add failing temporal formatter tests**

Add tests using the existing `EvaluateAndFormat` helper:

```csharp
[Theory]
[InlineData("birthDate", "1970-01-01")]
[InlineData("Patient.birthDate", "1970-01-01")]
public void TemporalPrimitive_UsesCanonicalFhirText(string expression, string expected)
{
    var (_, json) = EvaluateAndFormat(expression, TestPatientJson, "R4");

    json.ToJsonString().Should().Contain(expected);
}

[Fact]
public void TemporalComparison_ReturnsFhirTemporalWithoutLeakingRuntimeTypeName()
{
    var (_, json) = EvaluateAndFormat(
        "birthDate = @1970-01-01",
        TestPatientJson,
        "R4");

    json.ToJsonString().Should().NotContain("FhirTemporal");
}
```

Add direct coverage for `dateTime`, `instant`, and `time` fixtures so the assertion covers all four changed primitive types. Assert the canonical FHIR lexical text in `valueDate`, `valueDateTime`, `valueInstant`, and `valueTime` fields rather than relying only on a display string.

- [ ] **Step 2: Add failing `resolve()` tests for contained and sibling Bundle entries**

Add a contained-resource test if it is not already present, and add a Bundle fixture with a Patient and an Observation entry where the Observation references `Patient/example`:

```csharp
[Fact]
public void Evaluate_BundleEntryReferenceResolve_ReturnsSiblingResource()
{
    const string bundle = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        { "resource": { "resourceType": "Patient", "id": "example" } },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "obs",
            "subject": { "reference": "Patient/example" }
          }
        }
      ]
    }
    """;

    var request = new FhirPathRequest
    {
        Resource = ResourceJsonNode.Parse(bundle),
        Expression = "entry[1].resource.subject.resolve().id",
        FhirVersion = "R4",
    };

    var result = CreateService(new ThrowingHttpClientFactory(), allowPrivateTargets: true).Evaluate(request);

    result.IsSuccess.Should().BeTrue();
    result.Results.SelectMany(group => group.OutputValues)
        .Should().ContainSingle(value => value.Value?.ToString() == "example");
}
```

`FhirPathResult.Results` is a `List<EvaluationResult>` and each `EvaluationResult.OutputValues` is a `List<IElement>`, so retain the assertion shown above. The `ThrowingHttpClientFactory` ensures a sibling match cannot be satisfied by an HTTP fetch.

- [ ] **Step 3: Run the new tests before modifying formatting/resolution code**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ResultFormatterTests|FullyQualifiedName~FhirPathServiceTests"
```

Expected: the temporal tests expose any raw `FhirTemporal.ToString()`/runtime-type serialization mismatch, while the resolve test either passes from upstream behavior or identifies the lab resolver seam that still needs adaptation.

- [ ] **Step 4: Normalize temporal values at the formatter boundary**

Keep the existing typed-value formatting boundary and make its JSON conversion preserve JSON scalar
types while adapting only the new temporal wrapper:

```csharp
private static JsonNode CreateJsonValueFromPrimitive(object value) => value switch
{
    string text => JsonValue.Create(text),
    int integer => JsonValue.Create(integer),
    long longInteger => JsonValue.Create(longInteger),
    bool boolean => JsonValue.Create(boolean),
    decimal decimalValue => JsonValue.Create(decimalValue),
    double doubleValue => JsonValue.Create(doubleValue),
    float floatValue => JsonValue.Create(floatValue),
    DateTime dateTime => JsonValue.Create(dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture)),
    DateTimeOffset dateTimeOffset => JsonValue.Create(dateTimeOffset.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture)),
    FhirTemporal temporal => JsonValue.Create(temporal.Literal),
    _ => JsonNode.Parse(JsonSerializer.Serialize(value)),
} ?? throw new InvalidOperationException("Primitive FHIRPath values must serialize to JSON.");
```

Use it for primitive JSON value fields and nested complex-element leaves. Keep existing type-specific keys (`valueDate`, `valueDateTime`, `valueInstant`, `valueTime`), display formatting, constant text formatting, and complex-child array rules unchanged. The fallback remains for Ignixa's non-JSON CLR primitive wrappers; do not convert JSON scalars through a serialize/parse round trip.

- [ ] **Step 5: Adapt resolver lookups only at the custom boundary**

Inspect `LightweightElementResolver` after restore. Keep its contained `#id` handling and external/type-checking fallback. Do not reimplement upstream sibling Bundle/entry traversal. If the release changes the value or reference shape needed by the custom code, normalize only the value used for reference comparison, preserving the existing resource-type/id checks and error behavior.

- [ ] **Step 6: Run focused FHIRPath tests and the full backend suite**

Run:

```powershell
dotnet test backend\test\Ignixa.Lab.Functions.Tests\Ignixa.Lab.Functions.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ResultFormatterTests|FullyQualifiedName~FhirPathServiceTests"
dotnet test Ignixa.Lab.sln -c Release --no-restore
```

Expected: temporal output is canonical, `resolve()` covers contained and sibling Bundle resources, and all existing FHIRPath/resource-loading tests pass.

- [ ] **Step 7: Commit the FHIRPath compatibility changes**

```powershell
git add backend\src\Ignixa.Lab.Functions\Services\FhirPath backend\test\Ignixa.Lab.Functions.Tests\Services\FhirPath
git commit -m "fix: adapt FHIRPath temporal and resolve behavior for 0.6.68"
```

Include the required trailers from Task 1.

---

### Task 5: Run the complete validation matrix and record the compatibility result

**Files:**
- Modify: none unless a validation command identifies a migration defect directly tied to Tasks 1-4
- Inspect: `frontend/src/benches/search/searchTypes.ts`, `frontend/src/benches/fhirpath/fhirPathTypes.ts`

**Interfaces:**
- Consumes: the completed backend package/compiler/FHIRPath migration.
- Produces: verified backend and frontend builds/tests with backward-compatible additive Search and FHIRPath wire metadata.

- [ ] **Step 1: Verify the backend package graph and solution build**

Run:

```powershell
dotnet restore Ignixa.Lab.sln --force-evaluate
dotnet build Ignixa.Lab.sln -c Release --no-restore
```

Expected: no warnings-as-errors, no unresolved `0.6.41` Ignixa package pins, and no SQL Server/data-layer package introduced.

- [ ] **Step 2: Run the complete backend suite**

Run:

```powershell
dotnet test Ignixa.Lab.sln -c Release --no-build
```

Expected: all pre-existing tests plus the new Search, selector, temporal, and resolve regressions pass.

- [ ] **Step 3: Verify frontend contracts and build**

Confirm `frontend/src/benches/search/searchTypes.ts` models the existing response fields plus additive failure metadata and `frontend/src/benches/fhirpath/fhirPathTypes.ts` preserves AST nodes through `expressionType`, `name`, `returnType`, `arguments`, and selector metadata. Then run:

```powershell
npm --prefix frontend run test
npm --prefix frontend run build
```

Expected: the frontend keeps the existing contract while consuming the additive failure and selector metadata through the existing generic node shapes.

- [ ] **Step 4: Review the final diff for scope and package drift**

Run:

```powershell
git diff --check
git --no-pager diff --stat HEAD~4..HEAD
git --no-pager log -5 --format="%h %s"
```

Confirm the diff contains only the central package bump, Search adapter/mapping/tests, FHIRPath AST/value/resolution adapters/tests, and related documentation. Remove any generated binaries or temporary local-feed files before committing.

- [ ] **Step 5: Commit any final test-only or documentation correction**

If the validation matrix required a final correction directly coupled to the migration, run `git status --short`, stage only the specific corrected paths shown there, and commit:

```powershell
git commit -m "test: finalize Ignixa 0.6.68 compatibility coverage"
```

Include the required trailers. If no correction is needed, leave the task commits intact and report the observed passing counts and unchanged frontend contracts.
