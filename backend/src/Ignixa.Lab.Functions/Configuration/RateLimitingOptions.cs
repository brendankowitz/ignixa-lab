namespace Ignixa.Lab.Functions.Configuration;

/// <summary>
/// Per-endpoint-class rate-limiting thresholds, bound from
/// <c>IgnixaLab:RateLimiting</c>. All limits are per client-IP key except
/// <see cref="RunGlobalPerHour"/> and <see cref="RunMaxConcurrent"/>, which are
/// process-wide. See ADR-2608 for the sizing rationale and the per-instance
/// (scale-out) caveat.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>
    /// Master kill switch. When <see langword="false"/>, every request passes
    /// through unlimited — used for local dev and incident response.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sliding-window per-IP limit for <c>GET /api/suites</c>, per minute.</summary>
    public int SuitesPerMinutePerIp { get; set; } = 30;

    /// <summary>Sliding-window per-IP limit for <c>GET /api/capability</c>, per minute.</summary>
    public int CapabilityPerMinutePerIp { get; set; } = 12;

    /// <summary>Sliding-window per-IP limit for interactive <c>POST /api/validate</c>, per minute.</summary>
    public int ValidationPerMinutePerIp { get; set; } = 30;

    /// <summary>Sliding-window per-IP limit for <c>POST /api/run</c>, per minute.</summary>
    public int RunPerMinutePerIp { get; set; } = 4;

    /// <summary>
    /// Sliding-window per-IP limit for <c>POST /api/run</c>, per hour. Stops
    /// slow-drip abuse that the per-minute tier alone would admit.
    /// </summary>
    public int RunPerHourPerIp { get; set; } = 20;

    /// <summary>
    /// Process-wide clock-aligned hourly cap on <c>POST /api/run</c>, bounding
    /// worst-case aggregate outbound amplification regardless of source IP count.
    /// </summary>
    public int RunGlobalPerHour { get; set; } = 100;

    /// <summary>
    /// Maximum simultaneous <c>POST /api/run</c> invocations in this process.
    /// A run holds outbound HTTP for up to 100s per call, so bounding
    /// concurrency is the most direct control on amplifier output.
    /// </summary>
    public int RunMaxConcurrent { get; set; } = 4;
}
