using System.Diagnostics;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Suites;
using Ignixa.TestScript.Evaluation;
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

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ConformanceResult>();

        using var scope = evaluatorFactory.CreateRequestProvider(target);
        var evaluator = new TestScriptEvaluator(
            scope.Provider,
            new InlineFixtureProvider(),
            new R4CoreSchemaProvider(),
            new NoOpValidator());

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(await ExecuteJobAsync(evaluator, job, fhirVersion, cancellationToken));
        }

        stopwatch.Stop();

        var report = new ConformanceReport(
            Impl: "ignixa-lab",
            Target: target.ToString(),
            FhirVersion: fhirVersion,
            StartedAt: startedAt,
            DurationMs: stopwatch.ElapsedMilliseconds,
            Results: results);

        return RunOutcome.Completed(report);
    }

    private async Task<IReadOnlyList<ConformanceResult>> ExecuteJobAsync(
        TestScriptEvaluator evaluator,
        SuiteJob job,
        string fhirVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await evaluator.ExecuteAsync(job.Definition, cancellationToken, fhirVersion);
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
