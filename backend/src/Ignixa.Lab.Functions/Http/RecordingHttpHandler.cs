using System.Net.Http.Headers;
using System.Text;
using Ignixa.Lab.Functions.Configuration;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Http;

/// <summary>
/// <see cref="DelegatingHandler"/> installed on the "fhir-target" HTTP client
/// that records each request/response pair into the ambient
/// <see cref="IHttpExchangeScope"/> collector, so conformance steps can show
/// the real HTTP traffic the engine made. Requests pass straight through when
/// no scope is active (e.g. capture disabled) or capture is turned off.
/// </summary>
public sealed class RecordingHttpHandler(
    IHttpExchangeScope scope,
    IOptions<IgnixaLabOptions> options) : DelegatingHandler
{
    private const string RedactedValue = "***redacted***";

    private static readonly IReadOnlySet<string> RedactedHeaderNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Authorization", "Proxy-Authorization" };

    private readonly IgnixaLabOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collector = scope.Current;
        if (collector is null || !_options.HttpCaptureEnabled)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var maxBodyBytes = _options.HttpCaptureMaxBodyBytes;
        var requestBody = await CaptureBodyAsync(request.Content, maxBodyBytes, cancellationToken);
        var requestHeaders = MergeHeaders(request.Headers, request.Content?.Headers);

        var response = await base.SendAsync(request, cancellationToken);

        var responseBody = await CaptureBodyAsync(response.Content, maxBodyBytes, cancellationToken);
        var responseHeaders = MergeHeaders(response.Headers, response.Content?.Headers);

        collector.Add(new CapturedExchange(
            Method: request.Method.Method,
            Url: request.RequestUri?.ToString() ?? string.Empty,
            RequestHeaders: requestHeaders,
            RequestBody: requestBody,
            StatusCode: (int)response.StatusCode,
            ResponseHeaders: responseHeaders,
            ResponseBody: responseBody));

        return response;
    }

    /// <summary>
    /// Buffers <paramref name="content"/> so it can be read here and still be
    /// read again downstream by the engine, then returns its text with a
    /// truncation marker applied if it exceeds <paramref name="maxBodyBytes"/>.
    /// </summary>
    private static async Task<string?> CaptureBodyAsync(
        HttpContent? content,
        int maxBodyBytes,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        await content.LoadIntoBufferAsync(cancellationToken);
        var body = await content.ReadAsStringAsync(cancellationToken);
        return Truncate(body, maxBodyBytes);
    }

    private static string Truncate(string body, int maxBodyBytes)
    {
        var byteCount = Encoding.UTF8.GetByteCount(body);
        if (byteCount <= maxBodyBytes)
        {
            return body;
        }

        var charLimit = Math.Min(body.Length, maxBodyBytes);
        return $"{body[..charLimit]}…[truncated {byteCount} bytes]";
    }

    private static IReadOnlyDictionary<string, string> MergeHeaders(HttpHeaders headers, HttpContentHeaders? contentHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AppendHeaders(merged, headers);
        if (contentHeaders is not null)
        {
            AppendHeaders(merged, contentHeaders);
        }

        return merged;
    }

    private static void AppendHeaders(Dictionary<string, string> target, HttpHeaders headers)
    {
        foreach (var header in headers)
        {
            target[header.Key] = RedactedHeaderNames.Contains(header.Key)
                ? RedactedValue
                : string.Join(", ", header.Value);
        }
    }
}
