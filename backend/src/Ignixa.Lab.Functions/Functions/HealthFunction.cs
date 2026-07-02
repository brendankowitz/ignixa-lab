using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Liveness endpoint reporting service status and the engine version in use.</summary>
public sealed class HealthFunction
{
    [Function("Health")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request)
    {
        var engineVersion = typeof(Ignixa.TestScript.Parsing.TestScriptParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Ignixa.TestScript.Parsing.TestScriptParser).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new OkObjectResult(new
        {
            status = "ok",
            engineVersion,
        });
    }
}
