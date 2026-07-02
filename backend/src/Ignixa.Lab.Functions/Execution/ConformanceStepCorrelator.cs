using Ignixa.Lab.Functions.Conformance;
using Ignixa.Lab.Functions.Http;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Attaches captured HTTP exchanges to the operation steps that produced
/// them. Kept pure and independent of the engine's report types so it can be
/// unit tested directly against <see cref="ConformanceStep"/> values.
/// </summary>
public static class ConformanceStepCorrelator
{
    /// <summary>
    /// Dequeues one exchange, in order, for each step whose
    /// <see cref="ConformanceStep.Kind"/> is <c>"operation"</c>; assertion
    /// steps are returned unchanged. <paramref name="exchanges"/> is a FIFO
    /// queue shared across the whole TestScript execution (setup, every test
    /// case, then teardown), so it is mutated in place and callers must
    /// process phases in true execution order.
    /// </summary>
    /// <remarks>
    /// If the number of operation steps does not match the number of
    /// remaining exchanges, attachment is greedy: as many steps as line up
    /// get a trace and the rest are left with a null request/response. This
    /// never throws, since classification of "operation" vs "assertion" is a
    /// heuristic and a mismatch must still produce a valid report.
    /// </remarks>
    public static IReadOnlyList<ConformanceStep> Attach(
        IReadOnlyList<ConformanceStep> steps,
        Queue<CapturedExchange> exchanges)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(exchanges);

        if (steps.Count == 0)
        {
            return steps;
        }

        var result = new ConformanceStep[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            result[i] = step.Kind == "operation" && exchanges.TryDequeue(out var exchange)
                ? step with { Request = ToRequest(exchange), Response = ToResponse(exchange) }
                : step;
        }

        return result;
    }

    private static ConformanceHttpRequest ToRequest(CapturedExchange exchange) =>
        new(exchange.Method, exchange.Url, exchange.RequestHeaders, exchange.RequestBody);

    private static ConformanceHttpResponse ToResponse(CapturedExchange exchange) =>
        new(exchange.StatusCode, exchange.ResponseHeaders, exchange.ResponseBody, BodyParseError: null);
}
