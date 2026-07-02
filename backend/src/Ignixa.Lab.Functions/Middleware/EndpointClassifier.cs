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
            "Run" => EndpointClass.Run,
            // Fail safe: an unrecognized (e.g. newly added) endpoint gets the
            // strictest tier rather than silently running unlimited.
            _ => EndpointClass.Run,
        };
    }
}
