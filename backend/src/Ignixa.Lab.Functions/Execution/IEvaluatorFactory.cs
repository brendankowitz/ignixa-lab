using Ignixa.TestScript.Client;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Creates the <see cref="Ignixa.TestScript.Evaluation.TestScriptEvaluator"/>
/// dependency graph for a run against a specific target FHIR server. Abstracted
/// so tests can substitute a stub <see cref="ITestRequestProvider"/> instead of
/// performing real HTTP calls.
/// </summary>
public interface IEvaluatorFactory
{
    /// <summary>
    /// Builds the request provider used to talk to <paramref name="target"/>.
    /// The returned provider is disposed by the caller when the run completes.
    /// </summary>
    RequestProviderScope CreateRequestProvider(Uri target);
}

/// <summary>
/// Wraps an <see cref="ITestRequestProvider"/> together with any disposable
/// resources (such as an <see cref="HttpClient"/>) that must live for the
/// duration of a run.
/// </summary>
public sealed class RequestProviderScope(ITestRequestProvider provider, IDisposable? owned) : IDisposable
{
    public ITestRequestProvider Provider { get; } = provider;

    public void Dispose() => owned?.Dispose();
}
