namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>Thrown when a resource-generation request names edge case selector ids that the <c>EdgeCaseCatalog</c> can't resolve.</summary>
public sealed class UnknownEdgeCaseSelectorsException : Exception
{
    public UnknownEdgeCaseSelectorsException()
    {
    }

    public UnknownEdgeCaseSelectorsException(string message)
        : base(message)
    {
    }

    public UnknownEdgeCaseSelectorsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The requested selector ids that could not be matched to a registered strategy.</summary>
    public IReadOnlyList<string> Unmatched { get; init; } = [];
}
