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
    /// app's output alongside <c>testscripts/</c>. Missing during local dev if
    /// <c>pack-suites.ps1</c> hasn't been rerun since the file was introduced.
    /// </summary>
    private static string GetTestScriptsRevision()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "testscripts", "source-revision.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "local";
    }

    [Function("Health")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "health")] HttpRequest request)
    {
        var engineVersion = typeof(Ignixa.TestScript.Parsing.TestScriptParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Ignixa.TestScript.Parsing.TestScriptParser).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new OkObjectResult(new
        {
            status = "ok",
            engineVersion,
            testScriptsRevision = GetTestScriptsRevision(),
        });
    }
}
