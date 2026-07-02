using FluentAssertions;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Middleware;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Tests.Middleware;

public sealed class RateLimitPolicyTests
{
    [Theory]
    [InlineData(EndpointClass.Suites)]
    [InlineData(EndpointClass.Capability)]
    [InlineData(EndpointClass.Run)]
    public void Acquire_PerIpBudgetExhausted_DeniesNextRequest(EndpointClass endpointClass)
    {
        const int limit = 3;
        using var policy = CreatePolicy(o =>
        {
            o.SuitesPerMinutePerIp = limit;
            o.CapabilityPerMinutePerIp = limit;
            o.RunPerMinutePerIp = limit;
            o.RunPerHourPerIp = 1000;
            o.RunGlobalPerHour = 1000;
            o.RunMaxConcurrent = 1000;
        });

        for (var i = 0; i < limit; i++)
        {
            using var allowed = policy.Acquire(endpointClass, "203.0.113.1");
            allowed.IsAllowed.Should().BeTrue();
        }

        using var denied = policy.Acquire(endpointClass, "203.0.113.1");
        denied.IsAllowed.Should().BeFalse();
        denied.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public void Acquire_DifferentIps_HaveIndependentBudgets()
    {
        using var policy = CreatePolicy(o => o.SuitesPerMinutePerIp = 1);

        using (var first = policy.Acquire(EndpointClass.Suites, "198.51.100.1"))
        {
            first.IsAllowed.Should().BeTrue();
        }

        using (var exhausted = policy.Acquire(EndpointClass.Suites, "198.51.100.1"))
        {
            exhausted.IsAllowed.Should().BeFalse();
        }

        using var otherIp = policy.Acquire(EndpointClass.Suites, "198.51.100.2");
        otherIp.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Acquire_ExemptClass_IsAlwaysAllowed()
    {
        using var policy = CreatePolicy(o =>
        {
            o.SuitesPerMinutePerIp = 1;
            o.RunGlobalPerHour = 1;
            o.RunMaxConcurrent = 1;
        });

        for (var i = 0; i < 50; i++)
        {
            using var decision = policy.Acquire(EndpointClass.Exempt, "203.0.113.9");
            decision.IsAllowed.Should().BeTrue();
        }
    }

    [Fact]
    public void Acquire_RunGlobalHourlyCapReached_DeniesFreshIp()
    {
        using var policy = CreatePolicy(o =>
        {
            o.RunGlobalPerHour = 2;
            o.RunPerMinutePerIp = 1000;
            o.RunPerHourPerIp = 1000;
            o.RunMaxConcurrent = 1000;
        });

        using (var first = policy.Acquire(EndpointClass.Run, "203.0.113.10"))
        {
            first.IsAllowed.Should().BeTrue();
        }

        using (var second = policy.Acquire(EndpointClass.Run, "203.0.113.10"))
        {
            second.IsAllowed.Should().BeTrue();
        }

        // A fresh IP with its own per-IP budget intact is still denied — proving
        // the global cap, not the per-IP cap, is what trips.
        using var freshIp = policy.Acquire(EndpointClass.Run, "203.0.113.11");
        freshIp.IsAllowed.Should().BeFalse();
        freshIp.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public void Acquire_RunConcurrencyPermitReleasedOnDispose_AllowsNewRun()
    {
        using var policy = CreatePolicy(o =>
        {
            o.RunMaxConcurrent = 2;
            o.RunPerMinutePerIp = 1000;
            o.RunPerHourPerIp = 1000;
            o.RunGlobalPerHour = 1000;
        });

        var first = policy.Acquire(EndpointClass.Run, "203.0.113.20");
        var second = policy.Acquire(EndpointClass.Run, "203.0.113.21");
        first.IsAllowed.Should().BeTrue();
        second.IsAllowed.Should().BeTrue();

        using (var third = policy.Acquire(EndpointClass.Run, "203.0.113.22"))
        {
            third.IsAllowed.Should().BeFalse();
        }

        first.Dispose();

        using var fourth = policy.Acquire(EndpointClass.Run, "203.0.113.23");
        fourth.IsAllowed.Should().BeTrue();

        second.Dispose();
    }

    [Fact]
    public void Acquire_Disabled_IsAlwaysAllowedBeyondConfiguredLimits()
    {
        using var policy = CreatePolicy(o =>
        {
            o.Enabled = false;
            o.SuitesPerMinutePerIp = 1;
            o.RunPerMinutePerIp = 1;
            o.RunGlobalPerHour = 1;
            o.RunMaxConcurrent = 1;
        });

        for (var i = 0; i < 50; i++)
        {
            using var suites = policy.Acquire(EndpointClass.Suites, "203.0.113.30");
            using var run = policy.Acquire(EndpointClass.Run, "203.0.113.30");
            suites.IsAllowed.Should().BeTrue();
            run.IsAllowed.Should().BeTrue();
        }
    }

    private static RateLimitPolicy CreatePolicy(Action<RateLimitingOptions> configure)
    {
        var options = new IgnixaLabOptions();
        configure(options.RateLimiting);
        return new RateLimitPolicy(Options.Create(options));
    }
}
