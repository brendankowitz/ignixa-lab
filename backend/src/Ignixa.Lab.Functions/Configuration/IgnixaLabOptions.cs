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
}
