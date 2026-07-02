namespace Ignixa.Lab.Functions.Http;

/// <summary>
/// Accumulates <see cref="CapturedExchange"/> instances for a single capture
/// scope (one TestScript execution), in the order the HTTP calls occurred.
/// </summary>
public sealed class HttpExchangeCollector
{
    private readonly List<CapturedExchange> _exchanges = [];
    private readonly Lock _gate = new();

    /// <summary>Appends a captured exchange to the end of the ordered list.</summary>
    public void Add(CapturedExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        lock (_gate)
        {
            _exchanges.Add(exchange);
        }
    }

    /// <summary>The exchanges recorded so far, in call order.</summary>
    public IReadOnlyList<CapturedExchange> Exchanges
    {
        get
        {
            lock (_gate)
            {
                return _exchanges.ToArray();
            }
        }
    }
}
