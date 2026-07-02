namespace Ignixa.Lab.Functions.Http;

/// <summary>
/// Ambient scope that lets <see cref="RecordingHttpHandler"/> find the
/// <see cref="HttpExchangeCollector"/> for the currently-executing TestScript
/// job without threading it through the engine's evaluator API. Implementations
/// are expected to be backed by an <see cref="AsyncLocal{T}"/> so capture stays
/// correct even if job execution becomes concurrent.
/// </summary>
public interface IHttpExchangeScope
{
    /// <summary>The collector for the currently active scope, or <see langword="null"/> if none is active.</summary>
    HttpExchangeCollector? Current { get; }

    /// <summary>
    /// Begins a new capture scope with a fresh <paramref name="collector"/>.
    /// Disposing the returned handle restores whatever scope (if any) was
    /// active before this call.
    /// </summary>
    IDisposable Begin(out HttpExchangeCollector collector);
}
