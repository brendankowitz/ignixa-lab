using Ignixa.Lab.Functions.Suites;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Ignixa.Lab.Functions.Functions;

/// <summary>Returns the catalog of bundled TestScript suites available to run.</summary>
public sealed class SuitesFunction(ISuiteCatalog catalog)
{
    [Function("Suites")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "suites")] HttpRequest request)
    {
        return new OkObjectResult(catalog.GetSuites());
    }
}
