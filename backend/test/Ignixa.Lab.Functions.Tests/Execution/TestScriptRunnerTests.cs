using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Suites;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Execution;

/// <summary>
/// Covers the requiresCapability wiring specifically: that <see cref="TestScriptRunner"/>
/// fetches the target's CapabilityStatement once per run and forwards it to the engine,
/// and that a fetch/parse failure fails open (with a report-level warning) rather than
/// blocking the run. General suite-execution behavior is exercised by the engine's own
/// (ignixa-fhir) TestScriptEvaluator tests, not duplicated here.
/// </summary>
public sealed class TestScriptRunnerTests
{
    private const string TargetUrl = "https://fhir.example.org/base";

    private const string CapabilityStatementWithoutReindex = """
        {
          "resourceType": "CapabilityStatement",
          "status": "active",
          "rest": [{ "mode": "server", "resource": [{ "type": "Patient", "interaction": [{"code": "read"}] }] }]
        }
        """;

    /// <summary>A CapabilityStatement declaring a specific, patch-precise <c>fhirVersion</c> — the shape a real server returns.</summary>
    private static string CapabilityStatementWithFhirVersion(string fhirVersion) => $$"""
        {
          "resourceType": "CapabilityStatement",
          "status": "active",
          "fhirVersion": "{{fhirVersion}}",
          "rest": [{ "mode": "server", "resource": [{ "type": "Patient", "interaction": [{"code": "read"}] }] }]
        }
        """;

    /// <summary>A CapabilityStatement declaring <c>fhirVersion</c> as an empty string — present but not usable.</summary>
    private const string CapabilityStatementWithEmptyFhirVersion = """
        {
          "resourceType": "CapabilityStatement",
          "status": "active",
          "fhirVersion": "",
          "rest": [{ "mode": "server", "resource": [{ "type": "Patient", "interaction": [{"code": "read"}] }] }]
        }
        """;

    /// <summary>A CapabilityStatement declaring <c>fhirVersion</c> as a non-string JSON value — malformed relative to the spec.</summary>
    private const string CapabilityStatementWithNonStringFhirVersion = """
        {
          "resourceType": "CapabilityStatement",
          "status": "active",
          "fhirVersion": 4.0,
          "rest": [{ "mode": "server", "resource": [{ "type": "Patient", "interaction": [{"code": "read"}] }] }]
        }
        """;

    private static TestScriptDefinition GatedDefinition(string requiresCapability) => new()
    {
        Metadata = new TestScriptMetadata { Name = "Gated" },
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "SystemReindex",
                RequiresCapability = requiresCapability,
                Actions = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1/$reindex" }]
            }
        ]
    };

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

    private static TestScriptDefinition FhirFakesCreateDefinition(string resourceType, params string[] fhirVersions) => new()
    {
        Metadata = new TestScriptMetadata { Name = "FhirFakesCreate" },
        Fixtures =
        [
            new FixtureDefinition
            {
                Id = "resource-fixture",
                Autocreate = false,
                Autodelete = false,
                Resource = ResourceJsonNode.Parse($$"""
                    {
                      "extension": [
                        {
                          "url": "http://ignixa.io/testscript/fhirfakes",
                          "valueCode": "{{resourceType}}"
                        }
                      ]
                    }
                    """),
            },
        ],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = $"Create{resourceType}",
                FhirVersions = fhirVersions,
                RequiresCapability = $"rest.resource.where(type='{resourceType}').interaction.where(code='create').exists()",
                Actions = [new OperationExpression { Type = "create", Resource = resourceType, SourceId = "resource-fixture" }],
            },
        ],
    };

    private static TestScriptDefinition WarningOnlyDeletedResourceStatusAlternativesDefinition() => new()
    {
        Metadata = new TestScriptMetadata { Name = "DeletedResourceStatusAlternatives" },
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "ReadAfterDelete",
                Actions =
                [
                    new OperationExpression { Type = "delete", Url = "Patient/deleted", ResponseId = "delete-response" },
                    new AssertExpression
                    {
                        Description = "Accepted DELETE response: 200 OK for completed deletion",
                        Criteria = new ResponseCodeCriteria("200"),
                        WarningOnly = true,
                    },
                    new AssertExpression
                    {
                        Description = "Accepted DELETE response: 202 Accepted for asynchronous deletion",
                        Criteria = new ResponseCodeCriteria("202"),
                        WarningOnly = true,
                    },
                    new AssertExpression
                    {
                        Description = "Accepted DELETE response: 204 No Content for completed deletion",
                        Criteria = new ResponseCodeCriteria("204"),
                        WarningOnly = true,
                    },
                    new OperationExpression { Type = "read", Url = "Patient/deleted", ResponseId = "after-delete-read" },
                    new AssertExpression
                    {
                        Description = "Accepted alternative: 200 OK while an asynchronous delete is still pending",
                        Criteria = new ResponseCodeCriteria("200"),
                        WarningOnly = true,
                    },
                    new AssertExpression
                    {
                        Description = "Accepted alternative: 410 Gone when the server tracks the deleted resource",
                        Criteria = new ResponseStatusCriteria("gone"),
                        WarningOnly = true,
                    },
                    new AssertExpression
                    {
                        Description = "Accepted alternative: 404 Not Found when deleted resources are not tracked",
                        Criteria = new ResponseStatusCriteria("notFound"),
                        WarningOnly = true,
                    },
                ],
            },
        ],
    };

    private static TestScriptDefinition UnrelatedWarningOnlyStatusAlternativesDefinition() => new()
    {
        Metadata = new TestScriptMetadata { Name = "UnrelatedStatusAlternatives" },
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "InformationalHeadBehavior",
                Actions =
                [
                    new OperationExpression { Type = "read", Url = "metadata" },
                    new AssertExpression
                    {
                        Description = "Alternative A: server aliases HEAD to GET and returns 200 (informational: not a FHIR spec requirement)",
                        Criteria = new ResponseStatusCriteria("okay"),
                        WarningOnly = true,
                    },
                    new AssertExpression
                    {
                        Description = "Alternative B: server rejects HEAD with 405 Method Not Allowed (informational: also acceptable)",
                        Criteria = new ResponseCodeCriteria("405"),
                        WarningOnly = true,
                    },
                ],
            },
        ],
    };

    private static StatusAlternativeEnforcementPlan SubscriptionDeleteReadbackPlan() =>
        new(new Dictionary<string, StatusAlternativePolicy>
        {
            ["DeletedResourceStatusAlternatives > ReadAfterDelete"] =
                StatusAlternativePolicy.SubscriptionDeleteReadback,
        });

    private static TestScriptDefinition WarningOnlyCreateStatusAlternativesDefinition() => new()
    {
        Metadata = new TestScriptMetadata { Name = "CreateStatusAlternatives" },
        Fixtures =
        [
            new FixtureDefinition
            {
                Id = "invalid-patient",
                Autocreate = false,
                Autodelete = false,
                Resource = ResourceJsonNode.Parse("""{"resourceType":"Patient"}"""),
            },
        ],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "InvalidCreate",
                Actions =
                [
                    new OperationExpression
                    {
                        Type = "create",
                        Resource = "Patient",
                        SourceId = "invalid-patient",
                    },
                    new AssertExpression
                    {
                        Description = "Accepted validation status: 400 Bad Request",
                        Criteria = new ResponseCodeCriteria("400"),
                        WarningOnly = true,
                    },
                    new AssertExpression
                    {
                        Description = "Accepted validation status: 422 Unprocessable Entity",
                        Criteria = new ResponseCodeCriteria("422"),
                        WarningOnly = true,
                    },
                ],
            },
        ],
    };

    private static StatusAlternativeEnforcementPlan AllowedCreateStatusesPlan(string method = "POST") =>
        StatusAlternativeEnforcementPlan.Parse($$"""
            {
              "resourceType": "TestScript",
              "name": "CreateStatusAlternatives",
              "status": "active",
              "test": [{
                "name": "InvalidCreate",
                "extension": [{
                  "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                  "extension": [
                    { "url": "policy", "valueCode": "response-status-set-v1" },
                    { "url": "method", "valueCode": "{{method}}" },
                    { "url": "status", "valueInteger": 400 },
                    { "url": "status", "valueInteger": 422 }
                  ]
                }],
                "action": []
              }]
            }
            """);

    private static TestScriptDefinition WarningOnlyMethodStatusAlternativesDefinition(
        string metadataName,
        string testName,
        string operationType,
        int firstStatus,
        int secondStatus)
    {
        var operation = operationType == "delete"
            ? new OperationExpression { Type = "delete", Resource = "Patient" }
            : new OperationExpression { Type = "read", Url = "Patient/conditional-delete-target" };
        return new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = metadataName },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = testName,
                    Actions =
                    [
                        operation,
                        new AssertExpression
                        {
                            Description = $"Accepted response status: {firstStatus}",
                            Criteria = new ResponseCodeCriteria(firstStatus.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)),
                            WarningOnly = true,
                        },
                        new AssertExpression
                        {
                            Description = $"Accepted response status: {secondStatus}",
                            Criteria = new ResponseCodeCriteria(secondStatus.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)),
                            WarningOnly = true,
                        },
                    ],
                },
            ],
        };
    }

    private static StatusAlternativeEnforcementPlan MethodStatusAlternativesPlan(
        string metadataName,
        string testName,
        string method,
        int firstStatus,
        int secondStatus) =>
        StatusAlternativeEnforcementPlan.Parse($$"""
            {
              "resourceType": "TestScript",
              "name": "{{metadataName}}",
              "status": "active",
              "test": [{
                "name": "{{testName}}",
                "extension": [{
                  "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                  "extension": [
                    { "url": "policy", "valueCode": "response-status-set-v1" },
                    { "url": "method", "valueCode": "{{method}}" },
                    { "url": "status", "valueInteger": {{firstStatus}} },
                    { "url": "status", "valueInteger": {{secondStatus}} }
                  ]
                }],
                "action": []
              }]
            }
            """);

    [Fact]
    public async Task GivenSuiteRequiringUndeclaredCapability_WhenRun_ThenTestIsSkippedAndNoRequestIsSent()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("gated.json", GatedDefinition("rest.operation.where(name='reindex').exists()")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["gated.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle();
        outcome.Report.Results[0].Status.Should().Be(ConformanceStatus.Skipped);
        outcome.Report.CapabilityWarning.Should().BeNull();
        provider.CallCount.Should().Be(0);
    }

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

    [Theory]
    [InlineData("""{"resourceType":"TestScript","name":"Bad","status":"active","test":[{"name":"bad","requiresCapability":"direct","extension":{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"direct"},"action":[]}]}""")]
    [InlineData("""{"resourceType":"TestScript","name":"Bad","status":"active","test":[{"name":"bad","requiresCapability":"direct","extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"different"}],"action":[]}]}""")]
    public async Task GivenUploadedMalformedOrConflictingCapabilityMetadata_WhenRun_ThenRequestIsInvalid(string content)
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("unused.json", GatedDefinition("true")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest
            {
                TargetUrl = TargetUrl,
                UploadedTestScripts = [new UploadedTestScript { FileName = "bad.json", Content = content }],
            },
            CancellationToken.None);

        outcome.IsValid.Should().BeFalse();
        provider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GivenSuiteRequiringDeclaredCapability_WhenRun_ThenTestRuns()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("gated.json", GatedDefinition("rest.resource.where(type='Patient').interaction.where(code='read').exists()")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["gated.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenCapabilityFetchFails_WhenRun_ThenReportHasWarningAndGatedTestStillRuns()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("gated.json", GatedDefinition("rest.operation.where(name='reindex').exists()")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new ThrowingHttpClientFactory(),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["gated.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.CapabilityWarning.Should().NotBeNullOrEmpty();
        // Fail-open: with no capability statement available, the gate can't be evaluated, so the test still runs.
        outcome.Report.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        provider.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("4.0.1", "4.0")]
    [InlineData("4.0", "4.0")]
    [InlineData("4.3.0", "4.3")]
    [InlineData("5.0.0", "5.0")]
    [InlineData("3.0.2", "3.0")]
    [InlineData("6.0.0-ballot3", "6.0")]
    public async Task GivenSuiteGatedToMajorMinorFhirVersion_WhenTargetDeclaresMatchingPatchVersion_ThenTestRunsAndReportUsesDeclaredVersion(
        string declaredCapabilityVersion, string suiteGatedVersion)
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition(suiteGatedVersion)),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithFhirVersion(declaredCapabilityVersion)),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        // The engine's granular fhirVersions matching (major/minor/patch/wildcard specs)
        // matches the suite's major.minor spec against the target's real, patch-precise
        // declared version — this is the whole point of gating against the target's own
        // CapabilityStatement instead of a UI-selected release label.
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        // The report must carry the target's own declared version verbatim (patch included,
        // and any prerelease/build metadata), not a normalized/truncated guess: it's
        // interchangeable with the ignixa-fhir conformance/latest.json artifact.
        outcome.Report.FhirVersion.Should().Be(declaredCapabilityVersion);
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenSuiteGatedToIncompatibleFhirVersion_WhenTargetDeclaresADifferentVersion_ThenTestIsSkipped()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("5.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithFhirVersion("4.0.1")),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().Be(ConformanceStatus.Skipped);
        provider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GivenCapabilityStatementOmitsFhirVersion_WhenRun_ThenReportFallsBackToRequestFhirVersion()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("4.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                // No "fhirVersion" field at all — some malformed/legacy server response.
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"], FhirVersion = "4.0" },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        outcome.Report.FhirVersion.Should().Be("4.0");
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenCapabilityStatementHasEmptyFhirVersion_WhenRun_ThenReportFallsBackToRequestFhirVersion()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("4.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithEmptyFhirVersion),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"], FhirVersion = "4.0" },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        outcome.Report.FhirVersion.Should().Be("4.0");
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenCapabilityStatementHasNonStringFhirVersion_WhenRun_ThenReportFallsBackToRequestFhirVersion()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("4.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithNonStringFhirVersion),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"], FhirVersion = "4.0" },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        outcome.Report.FhirVersion.Should().Be("4.0");
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenCapabilityFetchFails_WhenRun_ThenReportFallsBackToDefaultFhirVersion()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("4.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new ThrowingHttpClientFactory(),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions { DefaultFhirVersion = "4.0" }),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.CapabilityWarning.Should().NotBeNullOrEmpty();
        outcome.Report.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        outcome.Report.FhirVersion.Should().Be("4.0");
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenCapabilityStatementDeclaresNonSemverFhirVersion_WhenRun_ThenReportFallsBackToRequestFhirVersion()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 200 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("versioned.json", VersionGatedDefinition("4.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                // A non-conformant server declaring a release label instead of real semver —
                // ResolveFhirVersion must not trust this verbatim (the report's FhirVersion
                // contract requires real semver, and the engine's matching can't reason about
                // "R4"), so it falls back to the request's own (already-semver) FhirVersion.
                new FixedResponseHttpClientFactory(CapabilityStatementWithFhirVersion("R4")),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["versioned.json"], FhirVersion = "4.0.1" },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Skipped);
        outcome.Report.FhirVersion.Should().Be("4.0.1");
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenR5OnlyFhirFakesFixture_WhenTargetDeclaresR5_ThenRunnerUsesR5Schema()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 201 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("r5-only.json", FhirFakesCreateDefinition("InventoryItem", "5.0")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory("""
                    {
                      "resourceType": "CapabilityStatement",
                      "status": "active",
                      "fhirVersion": "5.0.0",
                      "rest": [{ "mode": "server", "resource": [{ "type": "InventoryItem", "interaction": [{"code": "create"}] }] }]
                    }
                    """),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["r5-only.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results[0].Status.Should().NotBe(ConformanceStatus.Error);
        provider.CallCount.Should().Be(1);
        var request = provider.Requests.Should().ContainSingle().Which;
        request.Body.Should().NotBeNull();
        request.Body!.ResourceType.Should().Be("InventoryItem");
    }

    [Fact]
    public async Task GivenUnparseableFhirVersion_WhenRun_ThenRunnerFallsBackToR4Schema()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 201 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("unparseable-version.json", FhirFakesCreateDefinition("InventoryItem")),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                // No fhirVersion declared, so ResolveFhirVersion falls back to the
                // request's FhirVersion below, which is not valid semver.
                new FixedResponseHttpClientFactory("""
                    {
                      "resourceType": "CapabilityStatement",
                      "status": "active",
                      "rest": [{ "mode": "server", "resource": [{ "type": "InventoryItem", "interaction": [{"code": "create"}] }] }]
                    }
                    """),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["unparseable-version.json"], FhirVersion = "not-a-version" },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        // InventoryItem is R5-only, so fixture generation only fails this way
        // if the unparseable fhirVersion fell back to the R4 schema, not to
        // some other (or no) schema.
        var setupStep = outcome.Report!.Results[0].Steps.Should().ContainSingle(step => step.Phase == "setup").Which;
        setupStep.Status.Should().Be(ConformanceStatus.Error);
        setupStep.Message.Should().Contain("not valid for FHIR version R4");
    }

    [Fact]
    public async Task GivenWarningOnlyDeletedResourceStatusAlternatives_WhenTargetReturnsUnexpectedStatus_ThenRunFails()
    {
        var provider = new RecordingRequestProvider(
            new TestResponse { StatusCode = 202 },
            new TestResponse { StatusCode = 500 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog(
                "deleted-resource-status.json",
                WarningOnlyDeletedResourceStatusAlternativesDefinition(),
                SubscriptionDeleteReadbackPlan()),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["deleted-resource-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        var result = outcome.Report!.Results.Should().ContainSingle().Which;
        result.Status.Should().Be(ConformanceStatus.Fail);
        result.Error!.Received.Should().Contain("200").And.Contain("410").And.Contain("404").And.Contain("500");
    }

    [Theory]
    [InlineData(202, 200)]
    [InlineData(200, 404)]
    [InlineData(204, 410)]
    [InlineData(202, 404)]
    [InlineData(202, 410)]
    public async Task GivenCorrelatedDeleteAndReadbackStatuses_WhenValid_ThenRunPasses(int deleteStatus, int readStatus)
    {
        var provider = new RecordingRequestProvider(
            new TestResponse { StatusCode = deleteStatus },
            new TestResponse { StatusCode = readStatus });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog(
                "deleted-resource-status.json",
                WarningOnlyDeletedResourceStatusAlternativesDefinition(),
                SubscriptionDeleteReadbackPlan()),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["deleted-resource-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Theory]
    [InlineData(201, 404)]
    [InlineData(200, 200)]
    [InlineData(204, 200)]
    public async Task GivenCorrelatedDeleteAndReadbackStatuses_WhenInvalid_ThenRunFails(int deleteStatus, int readStatus)
    {
        var provider = new RecordingRequestProvider(
            new TestResponse { StatusCode = deleteStatus },
            new TestResponse { StatusCode = readStatus });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog(
                "deleted-resource-status.json",
                WarningOnlyDeletedResourceStatusAlternativesDefinition(),
                SubscriptionDeleteReadbackPlan()),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["deleted-resource-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
    }

    [Fact]
    public async Task GivenUnrelatedWarningOnlyStatusAlternatives_WhenTargetReturnsOutsideAlternatives_ThenRunStillPasses()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 202 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("unrelated-status.json", UnrelatedWarningOnlyStatusAlternativesDefinition()),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["unrelated-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Fact]
    public async Task GivenUploadedScriptWithStructuredPolicy_WhenStatusIsUnexpected_ThenRunFails()
    {
        var provider = new RecordingRequestProvider(
            new TestResponse { StatusCode = 202 },
            new TestResponse { StatusCode = 500 });
        var runner = new TestScriptRunner(
            new FakeSuiteCatalog("unused.json", UnrelatedWarningOnlyStatusAlternativesDefinition()),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);
        var content = """
            {
              "resourceType": "TestScript",
              "name": "UploadedMarked",
              "status": "active",
              "test": [{
                "name": "delete readback",
                "extension": [{
                  "url": "http://ignixa.io/testscript/statusAlternativePolicy",
                  "valueCode": "subscription-delete-readback-v1"
                }],
                "action": [
                  { "operation": { "type": { "code": "delete" }, "url": "Patient/deleted" } },
                  { "assert": { "description": "Accepted DELETE response: 200 OK for completed deletion", "responseCode": "200", "warningOnly": true } },
                  { "assert": { "description": "Accepted DELETE response: 202 Accepted for asynchronous deletion", "responseCode": "202", "warningOnly": true } },
                  { "assert": { "description": "Accepted DELETE response: 204 No Content for completed deletion", "responseCode": "204", "warningOnly": true } },
                  { "operation": { "type": { "code": "read" }, "url": "Patient/deleted" } },
                  { "assert": { "description": "Accepted alternative: 200 OK while an asynchronous delete is still pending", "responseCode": "200", "warningOnly": true } },
                  { "assert": { "description": "Accepted alternative: 410 Gone when the server tracks the deleted resource", "response": "gone", "warningOnly": true } },
                  { "assert": { "description": "Accepted alternative: 404 Not Found when deleted resources are not tracked", "response": "notFound", "warningOnly": true } }
                ]
              }]
            }
            """;

        var outcome = await runner.RunAsync(
            new RunRequest
            {
                TargetUrl = TargetUrl,
                UploadedTestScripts = [new UploadedTestScript { FileName = "uploaded-copy.json", Content = content }],
            },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    public async Task GivenStructuredAllowedStatusSet_WhenTargetReturnsAllowedStatus_ThenRunPasses(int statusCode)
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = statusCode });
        var runner = CreateStatusAlternativeRunner(provider, AllowedCreateStatusesPlan());

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["create-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task GivenStructuredAllowedStatusSet_WhenTargetReturnsOtherStatus_ThenRunFails(int statusCode)
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = statusCode });
        var runner = CreateStatusAlternativeRunner(provider, AllowedCreateStatusesPlan());

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["create-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        var result = outcome.Report!.Results.Should().ContainSingle().Which;
        result.Status.Should().Be(ConformanceStatus.Fail);
        result.Error!.Received.Should().Contain("400").And.Contain("422")
            .And.Contain(statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task GivenStructuredAllowedStatusSetForDifferentMethod_WhenRun_ThenPolicyFailsClosed()
    {
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = 400 });
        var runner = CreateStatusAlternativeRunner(provider, AllowedCreateStatusesPlan("PUT"));

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["create-status.json"] },
            CancellationToken.None);

        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Fail);
    }

    [Theory]
    [InlineData("delete", "DELETE", 400, 412, 400)]
    [InlineData("delete", "DELETE", 400, 412, 412)]
    [InlineData("read", "GET", 404, 410, 404)]
    [InlineData("read", "GET", 404, 410, 410)]
    public async Task GivenConditionalDeleteStatusSet_WhenTargetReturnsAllowedAlternative_ThenRunPasses(
        string operationType,
        string method,
        int firstAllowed,
        int secondAllowed,
        int actualStatus)
    {
        const string metadataName = "ConditionalDeleteStatusAlternatives";
        const string testName = "ConditionalDeletePolicy";
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = actualStatus });
        var runner = CreateMethodStatusAlternativeRunner(
            provider,
            WarningOnlyMethodStatusAlternativesDefinition(
                metadataName,
                testName,
                operationType,
                firstAllowed,
                secondAllowed),
            MethodStatusAlternativesPlan(metadataName, testName, method, firstAllowed, secondAllowed));

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["conditional-delete-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Report!.Results.Should().ContainSingle().Which.Status.Should().Be(ConformanceStatus.Pass);
    }

    [Theory]
    [InlineData("delete", "DELETE", 400, 412, 204)]
    [InlineData("read", "GET", 404, 410, 200)]
    [InlineData("read", "GET", 404, 410, 500)]
    public async Task GivenConditionalDeleteStatusSet_WhenTargetReturnsDisallowedStatus_ThenRunFails(
        string operationType,
        string method,
        int firstAllowed,
        int secondAllowed,
        int actualStatus)
    {
        const string metadataName = "ConditionalDeleteStatusAlternatives";
        const string testName = "ConditionalDeletePolicy";
        var provider = new RecordingRequestProvider(new TestResponse { StatusCode = actualStatus });
        var runner = CreateMethodStatusAlternativeRunner(
            provider,
            WarningOnlyMethodStatusAlternativesDefinition(
                metadataName,
                testName,
                operationType,
                firstAllowed,
                secondAllowed),
            MethodStatusAlternativesPlan(metadataName, testName, method, firstAllowed, secondAllowed));

        var outcome = await runner.RunAsync(
            new RunRequest { TargetUrl = TargetUrl, SuiteIds = ["conditional-delete-status.json"] },
            CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        var failed = outcome.Report!.Results.Should().ContainSingle().Which;
        failed.Status.Should().Be(ConformanceStatus.Fail);
        failed.Error!.Received.Should()
            .Contain(actualStatus.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static TestScriptRunner CreateStatusAlternativeRunner(
        RecordingRequestProvider provider,
        StatusAlternativeEnforcementPlan plan) =>
        new(
            new FakeSuiteCatalog(
                "create-status.json",
                WarningOnlyCreateStatusAlternativesDefinition(),
                plan),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

    private static TestScriptRunner CreateMethodStatusAlternativeRunner(
        RecordingRequestProvider provider,
        TestScriptDefinition definition,
        StatusAlternativeEnforcementPlan plan) =>
        new(
            new FakeSuiteCatalog("conditional-delete-status.json", definition, plan),
            new FakeEvaluatorFactory(provider),
            new CapabilityStatementFetcher(
                new FixedResponseHttpClientFactory(CapabilityStatementWithoutReindex),
                Options.Create(new IgnixaLabOptions())),
            Options.Create(new IgnixaLabOptions()),
            new SchemaProviderFactory(),
            NullLogger<TestScriptRunner>.Instance);

    private sealed class FakeSuiteCatalog(
        string id,
        TestScriptDefinition definition,
        StatusAlternativeEnforcementPlan? statusAlternativePlan = null) : ISuiteCatalog
    {
        public IReadOnlyList<SuiteDescriptor> GetSuites() => [];

        public bool TryGet(string suiteId, out CatalogEntry entry)
        {
            if (!string.Equals(suiteId, id, StringComparison.OrdinalIgnoreCase))
            {
                entry = null!;
                return false;
            }

            entry = new CatalogEntry(
                new SuiteDescriptor(id, definition.Metadata.Name, definition.Metadata.Description ?? "", "test", "", id, definition.Tests.Count, []),
                id,
                definition,
                statusAlternativePlan ?? StatusAlternativeEnforcementPlan.Empty);
            return true;
        }
    }

    private sealed class FakeEvaluatorFactory(ITestRequestProvider provider) : IEvaluatorFactory
    {
        public RequestProviderScope CreateRequestProvider(Uri target) => new(provider, null);
    }

    private sealed class RecordingRequestProvider : ITestRequestProvider
    {
        private readonly Queue<TestResponse> _responses;

        public RecordingRequestProvider(params TestResponse[] responses)
        {
            responses.Should().NotBeEmpty();
            _responses = new Queue<TestResponse>(responses);
        }

        public List<TestRequest> Requests { get; } = [];

        public int CallCount { get; private set; }

        public Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
            var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(response);
        }
    }

    /// <summary>An <see cref="IHttpClientFactory"/> that always returns a fixed 200 response body.</summary>
    private sealed class FixedResponseHttpClientFactory(string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FixedResponseHandler(body));

        private sealed class FixedResponseHandler(string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
        }
    }

    /// <summary>An <see cref="IHttpClientFactory"/> whose clients always throw, simulating an unreachable target.</summary>
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("simulated unreachable target");
        }
    }
}
