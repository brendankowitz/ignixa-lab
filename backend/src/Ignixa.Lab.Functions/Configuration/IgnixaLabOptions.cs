namespace Ignixa.Lab.Functions.Configuration;

/// <summary>
/// Strongly-typed configuration for the Ignixa Lab backend, bound from the
/// <c>IgnixaLab</c> configuration section.
/// </summary>
public sealed class IgnixaLabOptions
{
    public const string SectionName = "IgnixaLab";

    /// <summary>
    /// When <see langword="true"/>, runs are permitted against loopback and
    /// private/link-local addresses. Defaults to <see langword="false"/> so a
    /// hosted deployment cannot be used to probe internal networks (SSRF).
    /// </summary>
    public bool AllowPrivateTargets { get; set; }

    /// <summary>FHIR version supplied to the engine when a request omits one.</summary>
    public string DefaultFhirVersion { get; set; } = "4.0";

    /// <summary>
    /// Directory containing bundled TestScript suites. Relative paths are
    /// resolved against the application base directory. When unset, the
    /// default <c>testscripts</c> folder shipped with the worker is used.
    /// </summary>
    public string? SuitesDirectory { get; set; }

    /// <summary>Maximum number of suites (bundled + uploaded) permitted in a single run.</summary>
    public int MaxSuitesPerRun { get; set; } = 50;

    /// <summary>Per-run HTTP timeout, in seconds, applied to the FHIR client.</summary>
    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// When <see langword="true"/>, HTTP requests/responses made against the
    /// target server are recorded onto their conformance steps so the
    /// dashboard can display real traces. Defaults to <see langword="true"/>.
    /// </summary>
    public bool HttpCaptureEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of bytes retained per captured request/response body
    /// before it is truncated with a "…[truncated N bytes]" marker.
    /// </summary>
    public int HttpCaptureMaxBodyBytes { get; set; } = 65536;

    /// <summary>
    /// Comma-separated list of origins permitted to call this API cross-origin.
    /// Defaults to the hosted GitHub Pages frontend plus the local Vite dev
    /// server, so a self-hosted deployment does not need to reconfigure this
    /// to work out of the box, while still allowing operators to add or
    /// replace origins per environment.
    /// </summary>
    public string CorsAllowedOrigins { get; set; } = "https://brendankowitz.github.io,http://localhost:5173";
}
