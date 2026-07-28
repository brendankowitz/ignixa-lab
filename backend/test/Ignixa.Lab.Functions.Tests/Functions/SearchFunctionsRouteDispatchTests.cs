using System.Reflection;
using Ignixa.Lab.Functions.Functions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.Lab.Functions.Tests.Functions;

/// <summary>
/// SearchFunctionsTests calls each handler method directly, which exercises the handler logic perfectly
/// but says nothing about which handler a real HTTP request actually reaches -- that's decided earlier, by
/// route-template matching, which those direct-call tests bypass entirely. This is exactly how the
/// SearchCompartmentTrace/SearchEverythingTrace ambiguity slipped past the existing suite: both routes'
/// [HttpTrigger] templates structurally match "search/R4/Patient/example/$everything" (a bare {resourceType}
/// segment matches the literal text "$everything" just as well as the everything route's own literal
/// segment does), and the Functions host resolves that ambiguity by first-match-wins in its own route
/// registration order -- not by preferring the more specific (literal) template the way endpoint routing's
/// newer Matcher/precedence system would. These tests reproduce that dispatch behavior directly with
/// <see cref="Route"/>/<see cref="RouteCollection"/> (the classic IRouter-based routing components, matching
/// the host's own first-match-wins semantics) against the routes' real template strings, read via reflection
/// off the live [HttpTrigger] attributes so this can't silently drift from the actual routes.
/// </summary>
public sealed class SearchFunctionsRouteDispatchTests
{
    private static readonly IReadOnlyList<(string FunctionName, string RouteTemplate)> Routes = GetHttpFunctionRoutes(typeof(SearchFunctions));

    private static IReadOnlyList<(string FunctionName, string RouteTemplate)> GetHttpFunctionRoutes(Type functionsType) =>
        functionsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => (Method: method, Function: method.GetCustomAttribute<FunctionAttribute>()))
            .Where(x => x.Function is not null)
            .Select(x =>
            {
                var httpTrigger = x.Method.GetParameters()
                    .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                    .FirstOrDefault(a => a is not null);
                return (FunctionName: x.Function!.Name, RouteTemplate: httpTrigger?.Route);
            })
            .Where(x => x.RouteTemplate is not null)
            .Select(x => (x.FunctionName, RouteTemplate: x.RouteTemplate!))
            .ToArray();

    // A router that never actually invokes anything -- it just records which function's route matched, so
    // RouteCollection.RouteAsync's normal "first router down the chain whose target sets Handler wins"
    // short-circuit reveals the winner without needing a real Functions host.
    private sealed class RecordingTarget(string functionName) : IRouter
    {
        public VirtualPathData? GetVirtualPath(VirtualPathContext context) => null;

        public Task RouteAsync(RouteContext context)
        {
            context.RouteData.Values["__matchedFunction"] = functionName;
            context.Handler = _ => Task.CompletedTask;
            return Task.CompletedTask;
        }
    }

    // Builds a RouteCollection from the given (name, template) pairs, in the given order -- registration
    // order is exactly what decides the winner under first-match-wins semantics, so callers control it
    // explicitly rather than relying on Routes' incidental declaration order.
    private static async Task<string?> DispatchAsync(IEnumerable<(string FunctionName, string RouteTemplate)> routesInOrder, string path)
    {
        // Plain `new DefaultInlineConstraintResolver(Options.Create(new RouteOptions()), ...)` leaves
        // RouteOptions.ConstraintMap empty, so the "regex" constraint SearchCompartmentTrace's route now
        // relies on isn't registered -- AddRouting() is what wires up the built-in constraint map
        // (including "regex") the same way a real ASP.NET Core host does at startup.
        var provider = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var constraintResolver = provider.GetRequiredService<IInlineConstraintResolver>();

        var collection = new RouteCollection();
        foreach (var (functionName, routeTemplate) in routesInOrder)
        {
            collection.Add(new Route(new RecordingTarget(functionName), routeTemplate, constraintResolver));
        }

        var httpContext = new DefaultHttpContext
        {
            // RouteBase.RouteAsync resolves an ILoggerFactory off RequestServices internally; without it,
            // matching throws before it even gets to compare templates.
            RequestServices = provider,
        };
        httpContext.Request.Path = path;
        var routeContext = new RouteContext(httpContext);

        await collection.RouteAsync(routeContext);

        return routeContext.Handler is null ? null : (string?)routeContext.RouteData.Values["__matchedFunction"];
    }

    [Fact]
    public void SearchFunctions_DeclaresTheThreeExpectedHttpRoutes()
    {
        // Guards the reflection helper itself -- if a route is renamed/removed/added, the dispatch tests
        // below should fail loudly here rather than silently testing fewer routes than intended.
        Routes.Select(r => r.FunctionName).Should().BeEquivalentTo(
            "SearchTrace", "SearchCompartmentTrace", "SearchEverythingTrace");
    }

    // The order the real Functions host registers these in (confirmed live via `func start`'s "Mapped
    // function route" log lines): alphabetical by function name, not file declaration order. Reproduced here
    // to prove the fix holds under the host's actual registration order, not just an order chosen to make
    // the test easy to pass.
    private static readonly (string FunctionName, string RouteTemplate)[] HostRegistrationOrder =
        Routes.OrderBy(r => r.FunctionName, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task EverythingUrl_UnderHostRegistrationOrder_DispatchesToSearchEverythingTrace()
    {
        var matched = await DispatchAsync(HostRegistrationOrder, "/search/R4/Patient/example/$everything");

        matched.Should().Be("SearchEverythingTrace",
            "the compartment route's {resourceType:regex(...)} constraint must exclude the literal " +
            "\"$everything\" so this can no longer resolve to SearchCompartmentTrace, which is what a real " +
            "GET to this URL did before the constraint was added");
    }

    [Fact]
    public async Task CompartmentScopedUrl_UnderHostRegistrationOrder_DispatchesToSearchCompartmentTrace()
    {
        var matched = await DispatchAsync(HostRegistrationOrder, "/search/R4/Patient/example/Observation");

        matched.Should().Be("SearchCompartmentTrace", "a real member resource type must still reach the compartment route");
    }

    [Fact]
    public async Task CompartmentWildcardUrl_UnderHostRegistrationOrder_DispatchesToSearchCompartmentTrace()
    {
        var matched = await DispatchAsync(HostRegistrationOrder, "/search/R4/Patient/example/*");

        matched.Should().Be("SearchCompartmentTrace", "the wildcard member-type segment must still reach the compartment route");
    }

    [Fact]
    public async Task PlainTypeSearchUrl_UnderHostRegistrationOrder_DispatchesToSearchTrace()
    {
        var matched = await DispatchAsync(HostRegistrationOrder, "/search/R4/Patient");

        matched.Should().Be("SearchTrace");
    }

    // The fix works by making the two templates mutually exclusive (the constraint), not by relying on a
    // favorable registration order -- so the everything URL must resolve correctly regardless of which
    // route was registered first. This is the case that would have failed before the fix even if the host
    // happened to register SearchEverythingTrace first.
    [Fact]
    public async Task EverythingUrl_RegardlessOfRegistrationOrder_NeverDispatchesToSearchCompartmentTrace()
    {
        var reversedOrder = HostRegistrationOrder.Reverse();

        var matched = await DispatchAsync(reversedOrder, "/search/R4/Patient/example/$everything");

        matched.Should().Be("SearchEverythingTrace");
    }
}
