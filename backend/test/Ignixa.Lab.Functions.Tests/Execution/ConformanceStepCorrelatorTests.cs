using FluentAssertions;
using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Http;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class ConformanceStepCorrelatorTests
{
    private static ConformanceStep Step(string kind, string label) =>
        new(
            Phase: "test",
            Kind: kind,
            Status: ConformanceStatus.Pass,
            DurationMs: 1,
            Label: label,
            Description: null,
            Message: null,
            Request: null,
            Response: null);

    private static CapturedExchange Exchange(string url) =>
        new(
            Method: "GET",
            Url: url,
            RequestHeaders: new Dictionary<string, string>(),
            RequestBody: null,
            StatusCode: 200,
            ResponseHeaders: new Dictionary<string, string>(),
            ResponseBody: null);

    [Fact]
    public void Attach_AssignsExchangesToOperationStepsInOrder_LeavesAssertionStepsNull()
    {
        var steps = new[]
        {
            Step("operation", "op1"),
            Step("assertion", "assert1"),
            Step("operation", "op2"),
            Step("assertion", "assert2"),
        };
        var queue = new Queue<CapturedExchange>(new[] { Exchange("https://x/1"), Exchange("https://x/2") });

        var result = ConformanceStepCorrelator.Attach(steps, queue);

        result[0].Request.Should().NotBeNull();
        result[0].Request!.Url.Should().Be("https://x/1");
        result[0].Response.Should().NotBeNull();

        result[1].Request.Should().BeNull();
        result[1].Response.Should().BeNull();

        result[2].Request.Should().NotBeNull();
        result[2].Request!.Url.Should().Be("https://x/2");

        result[3].Request.Should().BeNull();
        result[3].Response.Should().BeNull();

        queue.Should().BeEmpty();
    }

    [Fact]
    public void Attach_FewerExchangesThanOperationSteps_LeavesTrailingOperationsWithoutTrace()
    {
        var steps = new[] { Step("operation", "op1"), Step("operation", "op2"), Step("operation", "op3") };
        var queue = new Queue<CapturedExchange>(new[] { Exchange("https://x/1") });

        var act = () => ConformanceStepCorrelator.Attach(steps, queue);

        var result = act.Should().NotThrow().Subject;
        result[0].Request!.Url.Should().Be("https://x/1");
        result[1].Request.Should().BeNull();
        result[2].Request.Should().BeNull();
        queue.Should().BeEmpty();
    }

    [Fact]
    public void Attach_MoreExchangesThanOperationSteps_LeavesUnusedExchangesInQueue()
    {
        var steps = new[] { Step("operation", "op1") };
        var queue = new Queue<CapturedExchange>(new[] { Exchange("https://x/1"), Exchange("https://x/2") });

        var act = () => ConformanceStepCorrelator.Attach(steps, queue);

        var result = act.Should().NotThrow().Subject;
        result[0].Request!.Url.Should().Be("https://x/1");
        queue.Should().ContainSingle().Which.Url.Should().Be("https://x/2");
    }

    [Fact]
    public void Attach_EmptySteps_ReturnsEmptyWithoutConsumingExchanges()
    {
        var queue = new Queue<CapturedExchange>(new[] { Exchange("https://x/1") });

        var result = ConformanceStepCorrelator.Attach(Array.Empty<ConformanceStep>(), queue);

        result.Should().BeEmpty();
        queue.Should().ContainSingle();
    }

    [Fact]
    public void Attach_NoExchanges_LeavesAllOperationStepsWithoutTrace()
    {
        var steps = new[] { Step("operation", "op1"), Step("assertion", "assert1") };
        var queue = new Queue<CapturedExchange>();

        var result = ConformanceStepCorrelator.Attach(steps, queue);

        result[0].Request.Should().BeNull();
        result[0].Response.Should().BeNull();
        result[1].Request.Should().BeNull();
    }
}
