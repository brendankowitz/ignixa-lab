using System.Diagnostics;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Suites;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using Ignixa.TestScript.Validation;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        var fhirVersion = string.IsNullOrWhiteSpace(request.FhirVersion)
            ? _options.DefaultFhirVersion
            : request.FhirVersion!;
        var engineFhirVersion = NormalizeFhirVersionForEngine(fhirVersion);

        var (capabilityStatement, capabilityWarning) = await FetchCapabilityStatementAsync(target, cancellationToken);

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

        var evaluator = new TestScriptEvaluator(
            scope.Provider,
            fixtureProvider,
            new R4CoreSchemaProvider(),
            new NoOpValidator());

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(await ExecuteJobAsync(evaluator, job, engineFhirVersion, capabilityStatement, cancellationToken));
        }

        stopwatch.Stop();

        var report = new ConformanceReport(
            Impl: "ignixa-lab",
            Target: target.ToString(),
            // Numeric form, not the raw request label: this field must stay
            // identical in shape and value convention to the ignixa-fhir
            // conformance/latest.json artifact, which is always numeric
            // (e.g. "4.0"), never "R4".
            FhirVersion: engineFhirVersion,
            StartedAt: startedAt,
            DurationMs: stopwatch.ElapsedMilliseconds,
            Results: results,
            CapabilityWarning: capabilityWarning);

        return RunOutcome.Completed(report);
    }

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
            var report = await evaluator.ExecuteAsync(job.Definition, cancellationToken, fhirVersion, capabilityStatement);
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

            jobs.Add(new SuiteJob(entry.Descriptor.Id, entry.Descriptor.Category, entry.Descriptor.File, entry.Definition));
        }

        foreach (var uploaded in request.UploadedTestScripts ?? Array.Empty<UploadedTestScript>())
        {
            if (string.IsNullOrWhiteSpace(uploaded.Content))
            {
                continue;
            }

            var parseResult = TestScriptParser.Parse(uploaded.Content);
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

    private sealed record SuiteJob(string Id, string Category, string File, TestScriptDefinition Definition);
}
