using Microsoft.AspNetCore.Http;

namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// Maps an Azure Functions function name (<c>FunctionContext.FunctionDefinition.Name</c>)
/// and HTTP method to its <see cref="EndpointClass"/>. Classification is by name, not URL
/// parsing. CORS preflight <c>OPTIONS</c> requests are always exempt here as a defence in
/// depth measure — in production <see cref="CorsMiddleware"/> already terminates them
/// before <see cref="RateLimitMiddleware"/> runs — so exemption stays correct even if
/// middleware ordering ever changes.
/// </summary>
public static class EndpointClassifier
{
    public static EndpointClass Classify(string functionName, string httpMethod)
    {
        ArgumentNullException.ThrowIfNull(functionName);
        ArgumentNullException.ThrowIfNull(httpMethod);

        if (HttpMethods.IsOptions(httpMethod))
        {
            return EndpointClass.Exempt;
        }

        return functionName switch
        {
            "Health" => EndpointClass.Exempt,
            "Suites" => EndpointClass.Suites,
            "Capability" => EndpointClass.Capability,
            "ResourceValidation" => EndpointClass.Validation,
            "Run" => EndpointClass.Run,
            // FHIRPath evaluation is a single unit of work per call (like
            // Capability), not a fan-out run — classify at the same tier.
            "FhirPathMetadata" or "FhirPathStu3" or "FhirPathR4" or "FhirPathR4B"
                or "FhirPathR5" or "FhirPathR6" => EndpointClass.Capability,
            // Fakes generation is likewise a single in-process unit of work per
            // call with no outbound HTTP (no amplification risk) — the Run tier
            // was never meant for this and was only hit because these endpoints
            // fell through to the fail-safe default below.
            "FakesMetadata" or "FakesPopulation" or "FakesScenario" or "FakesResource" => EndpointClass.Capability,
            // Search tracing is likewise a single in-process compile call with no
            // outbound HTTP — same tier as FhirPath/Fakes, not the Run tier's
            // fail-safe default (which a debounced auto-run UI would exhaust in
            // seconds).
            "SearchTrace" => EndpointClass.Capability,
            // Fail safe: an unrecognized (e.g. newly added) endpoint gets the
            // strictest tier rather than silently running unlimited.
            _ => EndpointClass.Run,
        };
    }
}
