namespace Ignixa.Lab.Functions.Http;

/// <summary>
/// A single HTTP request/response pair captured by <see cref="RecordingHttpHandler"/>
/// while a TestScript executes against the target FHIR server.
/// </summary>
public sealed record CapturedExchange(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string? RequestBody,
    int StatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody);
