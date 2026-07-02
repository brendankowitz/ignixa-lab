using System.Threading.RateLimiting;

namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// Outcome of a <see cref="RateLimitPolicy"/> acquisition. When allowed it may
/// carry a held <see cref="RateLimitLease"/> (the <c>Run</c> concurrency permit),
/// which must stay acquired for the duration of the downstream invocation and is
/// released via <see cref="Dispose"/>.
/// </summary>
public sealed class RateLimitDecision : IDisposable
{
    private readonly RateLimitLease? _heldLease;

    private RateLimitDecision(bool isAllowed, TimeSpan? retryAfter, RateLimitLease? heldLease)
    {
        IsAllowed = isAllowed;
        RetryAfter = retryAfter;
        _heldLease = heldLease;
    }

    public static RateLimitDecision Allowed(RateLimitLease? heldLease = null) => new(true, null, heldLease);

    public static RateLimitDecision Denied(TimeSpan retryAfter) => new(false, retryAfter, null);

    public bool IsAllowed { get; }

    public TimeSpan? RetryAfter { get; }

    public void Dispose() => _heldLease?.Dispose();
}
