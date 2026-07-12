using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Suites;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using Ignixa.TestScript.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Executes selected TestScript suites against a target FHIR server and
/// aggregates the results into a single <see cref="ConformanceReport"/>.
/// </summary>
public sealed class TestScriptRunner(
    ISuiteCatalog catalog,
    IEvaluatorFactory evaluatorFactory,
    CapabilityStatementFetcher capabilityFetcher,
    IOptions<IgnixaLabOptions> options,
    SchemaProviderFactory schemaProviderFactory,
    ILogger<TestScriptRunner> logger)
{
    private readonly IgnixaLabOptions _options = options.Value;

    /// <summary>
    /// Validates the request and runs the requested suites. Returns a
    /// <see cref="RunOutcome"/> that is either a completed report or a
    /// validation failure with an explanatory message.
    /// </summary>
    public async Task<RunOutcome> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        if (!TargetUrlValidator.TryValidate(request.TargetUrl, _options.AllowPrivateTargets, out var target, out var urlError))
        {
            return RunOutcome.Invalid(urlError);
        }

        var jobs = ResolveJobs(request, out var jobError);
        if (jobError is not null)
        {
            return RunOutcome.Invalid(jobError);
        }

        if (jobs.Count == 0)
        {
            return RunOutcome.Invalid("Select at least one suite or provide an uploaded TestScript to run.");
        }

        if (jobs.Count > _options.MaxSuitesPerRun)
        {
            return RunOutcome.Invalid($"A run may include at most {_options.MaxSuitesPerRun} suites.");
        }

        var requestedFhirVersion = string.IsNullOrWhiteSpace(request.FhirVersion)
            ? _options.DefaultFhirVersion
            : request.FhirVersion!;

        var (capabilityStatement, capabilityWarning) = await FetchCapabilityStatementAsync(target, cancellationToken);

        var fhirVersion = ResolveFhirVersion(capabilityStatement, requestedFhirVersion);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ConformanceResult>();

        using var scope = evaluatorFactory.CreateRequestProvider(target);

        // The canonical suites declare fixtures via fhirfakes markers, which the
        // FhirFakes provider materializes into real resources; InlineFixtureProvider
        // handles any inline fixtures. Order matters: fakes first, inline fallback.
        var fixtureProvider = new CompositeFixtureProvider(
        [
            new FhirFakesFixtureProvider(),
            new InlineFixtureProvider(),
        ]);

        var schemaProvider = SelectSchemaProvider(fhirVersion);
        var evaluator = new TestScriptEvaluator(
            scope.Provider,
            fixtureProvider,
            schemaProvider,
            new NoOpValidator());

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(await ExecuteJobAsync(evaluator, job, fhirVersion, capabilityStatement, cancellationToken));
        }

        stopwatch.Stop();

        var report = new ConformanceReport(
            Impl: "ignixa-lab",
            Target: target.ToString(),
            // The target's own declared version (e.g. "4.0.1"), not the raw
            // request label: this field must stay identical in shape and value
            // convention to the ignixa-fhir conformance/latest.json artifact,
            // which is always numeric, never a release label like "R4".
            FhirVersion: fhirVersion,
            StartedAt: startedAt,
            DurationMs: stopwatch.ElapsedMilliseconds,
            Results: results,
            CapabilityWarning: capabilityWarning);

        return RunOutcome.Completed(report);
    }

    /// <summary>
    /// Resolves the FHIR version used for the engine's <c>fhirVersions</c> gating
    /// extension and the report's <see cref="ConformanceReport.FhirVersion"/>
    /// field, preferring the target's own declared <c>CapabilityStatement.fhirVersion</c>
    /// (e.g. <c>"4.0.1"</c>) over <paramref name="fallbackFhirVersion"/> (the
    /// request's <c>FhirVersion</c>, or <see cref="IgnixaLabOptions.DefaultFhirVersion"/> —
    /// both are expected to already be real semver; there is no release-label
    /// normalization step here or anywhere upstream of it). Gating against the
    /// target's own declared, patch-precise version — rather than a UI-selected
    /// default — lets the engine's granular <c>fhirVersions</c> matching
    /// (major/minor/patch/wildcard specs) work as designed instead of comparing an
    /// approximate guess. Falls back to <paramref name="fallbackFhirVersion"/> when
    /// the CapabilityStatement couldn't be fetched/parsed, omits <c>fhirVersion</c>,
    /// or declares a value that isn't valid semver (a non-conformant server should
    /// not poison gating with a value the engine can't reason about).
    /// </summary>
    private static string ResolveFhirVersion(ResourceJsonNode? capabilityStatement, string fallbackFhirVersion)
    {
        var declaredVersion = ((IMutableJsonNode?)capabilityStatement)?.MutableNode["fhirVersion"] is JsonValue value && value.TryGetValue<string>(out var declared)
            ? declared
            : null;

        return string.IsNullOrWhiteSpace(declaredVersion) || !NuGetVersion.TryParse(declaredVersion, out _)
            ? fallbackFhirVersion
            : declaredVersion;
    }

    /// <summary>
    /// Maps <paramref name="fhirVersion"/> (real semver, e.g. <c>"4.3.0"</c>) to the
    /// version label <see cref="SchemaProviderFactory.GetSchemaProvider"/> expects,
    /// reusing its already-cached <see cref="IFhirSchemaProvider"/> instances instead
    /// of constructing a fresh one per run. Falls back to R4 (logged) when
    /// <paramref name="fhirVersion"/> isn't valid semver or its major version isn't a
    /// recognized FHIR release, so a malformed or unexpected target version doesn't
    /// silently validate against the wrong schema with no trace of why.
    /// </summary>
    private IFhirSchemaProvider SelectSchemaProvider(string fhirVersion)
    {
        if (!NuGetVersion.TryParse(fhirVersion, out var version))
        {
            logger.LogWarning("FHIR version '{FhirVersion}' is not valid semver; falling back to R4 schema.", fhirVersion);
            return schemaProviderFactory.GetSchemaProvider("R4");
        }

        var label = version.Major switch
        {
            3 => "STU3",
            4 when version.Minor >= 3 => "R4B",
            4 => "R4",
            5 => "R5",
            6 => "R6",
            _ => null,
        };

        if (label is null)
        {
            logger.LogWarning("FHIR major version {Major} ({FhirVersion}) is not a recognized FHIR release; falling back to R4 schema.", version.Major, fhirVersion);
            label = "R4";
        }

        return schemaProviderFactory.GetSchemaProvider(label);
    }

    /// <summary>
    /// Fetches the target's CapabilityStatement once per run so every job's
    /// <c>requiresCapability</c>-gated tests can be evaluated against the same
    /// snapshot. Fetch/parse failures fail open (returns a null statement, so
    /// gated tests run as if ungated) with a warning surfaced on the report
    /// rather than silently skipping potentially large parts of the run.
    /// </summary>
    private async Task<(ResourceJsonNode? Statement, string? Warning)> FetchCapabilityStatementAsync(
        Uri target, CancellationToken cancellationToken)
    {
        var fetchResult = await capabilityFetcher.FetchAsync(target, cancellationToken);
        if (!fetchResult.Success)
        {
            logger.LogWarning("Capability fetch failed for {Target}: {Error}", target, fetchResult.Error);
            return (null, $"requiresCapability checks were not enforced: {fetchResult.Error}");
        }

        try
        {
            return (ResourceJsonNode.Parse(fetchResult.Json!), null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse CapabilityStatement for {Target}.", target);
            return (null, $"requiresCapability checks were not enforced: the target's CapabilityStatement could not be parsed ({ex.Message})");
        }
    }

    private async Task<IReadOnlyList<ConformanceResult>> ExecuteJobAsync(
        TestScriptEvaluator evaluator,
        SuiteJob job,
        string fhirVersion,
        ResourceJsonNode? capabilityStatement,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = RunScopedDefinitionPreparer.Prepare(job.Definition);
            var report = await evaluator.ExecuteAsync(definition, cancellationToken, fhirVersion, capabilityStatement);
            return ConformanceReportMapper.Map(report, job.Id, job.Category, job.File);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Suite {Suite} failed to execute.", job.Id);
            var step = new ConformanceStep(
                Phase: "test",
                Kind: "operation",
                Status: ConformanceStatus.Error,
                DurationMs: 0,
                Label: "Execution error",
                Description: null,
                Message: ex.Message,
                Request: null,
                Response: null);

            return new[]
            {
                new ConformanceResult(
                    Id: job.Id,
                    File: job.File,
                    Suite: job.Id,
                    Category: job.Category,
                    Status: ConformanceStatus.Error,
                    DurationMs: 0,
                    Error: new ConformanceError("Execution error", ex.Message),
                    Steps: new[] { step }),
            };
        }
    }

    private IReadOnlyList<SuiteJob> ResolveJobs(RunRequest request, out string? error)
    {
        error = null;
        var jobs = new List<SuiteJob>();

        foreach (var suiteId in request.SuiteIds ?? Array.Empty<string>())
        {
            if (!catalog.TryGet(suiteId, out var entry))
            {
                error = $"Unknown suite '{suiteId}'.";
                return Array.Empty<SuiteJob>();
            }

            jobs.Add(new SuiteJob(
                entry.Descriptor.Id,
                entry.Descriptor.Category,
                entry.Descriptor.File,
                entry.Definition));
        }

        foreach (var uploaded in request.UploadedTestScripts ?? Array.Empty<UploadedTestScript>())
        {
            if (string.IsNullOrWhiteSpace(uploaded.Content))
            {
                continue;
            }

            ParseResult<TestScriptDefinition> parseResult;
            try
            {
                parseResult = TestScriptParser.Parse(uploaded.Content);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException)
            {
                error = $"Uploaded TestScript '{uploaded.FileName ?? "(unnamed)"}' could not be parsed: {ex.Message}";
                return Array.Empty<SuiteJob>();
            }

            if (!parseResult.IsSuccess || parseResult.Value is null)
            {
                var reason = parseResult.Errors.Count > 0 ? parseResult.Errors[0].Message : "invalid TestScript";
                error = $"Uploaded TestScript '{uploaded.FileName ?? "(unnamed)"}' could not be parsed: {reason}";
                return Array.Empty<SuiteJob>();
            }

            var fileName = string.IsNullOrWhiteSpace(uploaded.FileName) ? "uploaded.json" : uploaded.FileName!;
            jobs.Add(new SuiteJob(fileName, "uploaded", fileName, parseResult.Value));
        }

        return jobs;
    }

    private sealed record SuiteJob(
        string Id,
        string Category,
        string File,
        TestScriptDefinition Definition);
}
