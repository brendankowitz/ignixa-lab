using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Liveness endpoint reporting service status and the engine version in use.</summary>
public sealed class HealthFunction
{
    /// <summary>
    /// Commit the packaged testscripts fixtures came from, written into the
    /// <c>IgnixaLab.TestScript.Suites</c> package at pack time (see that
    /// project's <c>WriteSourceRevisionFile</c> target) and copied into this
    /// app's output alongside <c>testscripts/</c>. Returns <c>null</c> when the
    /// revision can't be determined (missing during local dev if
    /// <c>pack-suites.ps1</c> hasn't been rerun, unreadable, or empty) so the
    /// frontend falls back to linking against <c>main</c> instead of a
    /// guaranteed-404 URL.
    /// </summary>
    /// <param name="sourceRevisionFilePath">Path to the packed <c>source-revision.txt</c>, exposed as a parameter for testing.</param>
    public static string? ReadTestScriptsRevision(string sourceRevisionFilePath)
    {
        try
        {
            var revision = File.ReadAllText(sourceRevisionFilePath).Trim();
            return revision.Length > 0 ? revision : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [Function("Health")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "health")] HttpRequest request)
    {
        var engineVersion = typeof(Ignixa.TestScript.Parsing.TestScriptParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Ignixa.TestScript.Parsing.TestScriptParser).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        var sourceRevisionFilePath = Path.Combine(AppContext.BaseDirectory, "testscripts", "source-revision.txt");

        return new OkObjectResult(new
        {
            status = "ok",
            engineVersion,
            testScriptsRevision = ReadTestScriptsRevision(sourceRevisionFilePath),
        });
    }
}
