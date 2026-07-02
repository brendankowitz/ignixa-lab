using FluentAssertions;
using Ignixa.Lab.Functions.Http;

namespace Ignixa.Lab.Functions.Tests.Http;

public sealed class HttpExchangeScopeTests
{
    [Fact]
    public void Current_IsNull_BeforeAnyScopeBegins()
    {
        var scope = new HttpExchangeScope();

        scope.Current.Should().BeNull();
    }

    [Fact]
    public void Begin_MakesTheNewCollectorCurrent_AndClearsItOnDispose()
    {
        var scope = new HttpExchangeScope();

        using (scope.Begin(out var collector))
        {
            scope.Current.Should().BeSameAs(collector);
        }

        scope.Current.Should().BeNull();
    }

    [Fact]
    public void Begin_SequentialScopesDoNotLeakExchangesIntoEachOther()
    {
        var scope = new HttpExchangeScope();

        using (scope.Begin(out var first))
        {
            first.Add(Exchange("https://x/1"));
            first.Exchanges.Should().ContainSingle();
        }

        using (scope.Begin(out var second))
        {
            second.Exchanges.Should().BeEmpty();
        }
    }

    [Fact]
    public void Begin_NestedScope_RestoresOuterCollectorOnDispose()
    {
        var scope = new HttpExchangeScope();

        using var outerHandle = scope.Begin(out var outerCollector);
        using (scope.Begin(out var innerCollector))
        {
            scope.Current.Should().BeSameAs(innerCollector);
        }

        scope.Current.Should().BeSameAs(outerCollector);
    }

    private static CapturedExchange Exchange(string url) =>
        new(
            Method: "GET",
            Url: url,
            RequestHeaders: new Dictionary<string, string>(),
            RequestBody: null,
            StatusCode: 200,
            ResponseHeaders: new Dictionary<string, string>(),
            ResponseBody: null);
}
