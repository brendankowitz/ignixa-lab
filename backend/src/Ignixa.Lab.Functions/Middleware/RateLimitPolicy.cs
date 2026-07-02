using System.Threading.RateLimiting;
using Ignixa.Lab.Functions.Configuration;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// The testable rate-limiting decision core, built entirely on in-box
/// <see cref="System.Threading.RateLimiting"/> primitives. Held as a singleton;
/// partitions are created lazily per IP key and evicted by the built-in limiter.
/// Decisions can be exercised without a Functions host — see the middleware for
/// wiring. See ADR-2608.
/// </summary>
public sealed class RateLimitPolicy : IDisposable
{
    private const int SegmentsPerWindow = 6;

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    private readonly bool _enabled;
    private readonly PartitionedRateLimiter<string> _suitesLimiter;
    private readonly PartitionedRateLimiter<string> _capabilityLimiter;
    private readonly PartitionedRateLimiter<string> _runPerMinuteLimiter;
    private readonly PartitionedRateLimiter<string> _runPerHourLimiter;

    // In-memory, therefore per-instance at scale-out: the effective global cap is
    // RunGlobalPerHour × instanceCount. Phase 2 (not implemented here) moves this
    // counter to Azure Table Storage for a true cross-instance cap — ADR-2608 §8.
    private readonly FixedWindowRateLimiter _runGlobalHourlyLimiter;
    private readonly ConcurrencyLimiter _runConcurrencyLimiter;

    private long _currentHourBucket;

    public RateLimitPolicy(IOptions<IgnixaLabOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var config = options.Value.RateLimiting;
        _enabled = config.Enabled;

        _suitesLimiter = CreateSlidingWindowLimiter(config.SuitesPerMinutePerIp, OneMinute);
        _capabilityLimiter = CreateSlidingWindowLimiter(config.CapabilityPerMinutePerIp, OneMinute);
        _runPerMinuteLimiter = CreateSlidingWindowLimiter(config.RunPerMinutePerIp, OneMinute);
        _runPerHourLimiter = CreateSlidingWindowLimiter(config.RunPerHourPerIp, OneHour);

        _runGlobalHourlyLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.RunGlobalPerHour,
            Window = OneHour,
            AutoReplenishment = false,
            QueueLimit = 0,
        });
        _currentHourBucket = CurrentHourBucket();

        _runConcurrencyLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = config.RunMaxConcurrent,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    public RateLimitDecision Acquire(EndpointClass endpointClass, string ipKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipKey);

        if (!_enabled || endpointClass == EndpointClass.Exempt)
        {
            return RateLimitDecision.Allowed();
        }

        return endpointClass switch
        {
            EndpointClass.Suites => AcquirePerIp(_suitesLimiter, ipKey, OneMinute),
            EndpointClass.Capability => AcquirePerIp(_capabilityLimiter, ipKey, OneMinute),
            _ => AcquireRun(ipKey),
        };
    }

    private RateLimitDecision AcquireRun(string ipKey)
    {
        using (var perMinute = _runPerMinuteLimiter.AttemptAcquire(ipKey))
        {
            if (!perMinute.IsAcquired)
            {
                return RateLimitDecision.Denied(RetryAfterFor(perMinute, OneMinute));
            }
        }

        using (var perHour = _runPerHourLimiter.AttemptAcquire(ipKey))
        {
            if (!perHour.IsAcquired)
            {
                return RateLimitDecision.Denied(RetryAfterFor(perHour, OneHour));
            }
        }

        AlignGlobalWindow();
        using (var global = _runGlobalHourlyLimiter.AttemptAcquire(1))
        {
            if (!global.IsAcquired)
            {
                return RateLimitDecision.Denied(RetryAfterFor(global, TimeUntilNextHour()));
            }
        }

        var concurrencyLease = _runConcurrencyLimiter.AttemptAcquire(1);
        if (!concurrencyLease.IsAcquired)
        {
            // Concurrency has no natural window: a slot frees when an in-flight
            // run completes, which we cannot predict, so suggest a short back-off.
            var retryAfter = RetryAfterFor(concurrencyLease, TimeSpan.FromSeconds(1));
            concurrencyLease.Dispose();
            return RateLimitDecision.Denied(retryAfter);
        }

        // The concurrency lease is the only one held; the caller's using block
        // releases the permit once the downstream function finishes.
        return RateLimitDecision.Allowed(concurrencyLease);
    }

    private static RateLimitDecision AcquirePerIp(PartitionedRateLimiter<string> limiter, string ipKey, TimeSpan window)
    {
        using var lease = limiter.AttemptAcquire(ipKey);
        return lease.IsAcquired
            ? RateLimitDecision.Allowed()
            : RateLimitDecision.Denied(RetryAfterFor(lease, window));
    }

    private void AlignGlobalWindow()
    {
        var bucket = CurrentHourBucket();
        if (Interlocked.Exchange(ref _currentHourBucket, bucket) != bucket)
        {
            // A new UTC clock hour started; reset the window to align it to the
            // hour boundary (TryReplenish is a no-op if the window has not elapsed).
            _runGlobalHourlyLimiter.TryReplenish();
        }
    }

    private static PartitionedRateLimiter<string> CreateSlidingWindowLimiter(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    private static TimeSpan RetryAfterFor(RateLimitLease lease, TimeSpan fallback)
    {
        var window = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : fallback;
        return TimeSpan.FromSeconds(Math.Ceiling(window.TotalSeconds));
    }

    private static long CurrentHourBucket() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour;

    private static TimeSpan TimeUntilNextHour()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddHours(now.Hour + 1) - now;
    }

    public void Dispose()
    {
        _suitesLimiter.Dispose();
        _capabilityLimiter.Dispose();
        _runPerMinuteLimiter.Dispose();
        _runPerHourLimiter.Dispose();
        _runGlobalHourlyLimiter.Dispose();
        _runConcurrencyLimiter.Dispose();
    }
}
