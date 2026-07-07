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

    private sealed class FakeSuiteCatalog(string id, TestScriptDefinition definition) : ISuiteCatalog
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
                definition);
            return true;
        }
    }

    private sealed class FakeEvaluatorFactory(ITestRequestProvider provider) : IEvaluatorFactory
    {
        public RequestProviderScope CreateRequestProvider(Uri target) => new(provider, null);
    }

    private sealed class RecordingRequestProvider(TestResponse response) : ITestRequestProvider
    {
        public List<TestRequest> Requests { get; } = [];

        public int CallCount { get; private set; }

        public Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
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
