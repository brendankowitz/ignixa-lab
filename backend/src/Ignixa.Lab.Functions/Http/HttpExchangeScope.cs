namespace Ignixa.Lab.Functions.Http;

/// <summary>
/// Default <see cref="IHttpExchangeScope"/> backed by an <see cref="AsyncLocal{T}"/>,
/// so each logical execution flow (one TestScript job) sees only the exchanges
/// captured within its own scope, even if runs ever overlap.
/// </summary>
public sealed class HttpExchangeScope : IHttpExchangeScope
{
    private readonly AsyncLocal<HttpExchangeCollector?> _current = new();

    public HttpExchangeCollector? Current => _current.Value;

    public IDisposable Begin(out HttpExchangeCollector collector)
    {
        var previous = _current.Value;
        collector = new HttpExchangeCollector();
        _current.Value = collector;
        return new ScopeHandle(_current, previous);
    }

    private sealed class ScopeHandle(AsyncLocal<HttpExchangeCollector?> current, HttpExchangeCollector? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }
}
