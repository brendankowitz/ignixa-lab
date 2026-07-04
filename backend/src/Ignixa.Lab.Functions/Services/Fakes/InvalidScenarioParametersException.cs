namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>Thrown when a scenario request's <c>parameters</c> bag can't be converted to, or accepted by, the scenario method's actual reflected parameter types.</summary>
public sealed class InvalidScenarioParametersException : Exception
{
    public InvalidScenarioParametersException()
    {
    }

    public InvalidScenarioParametersException(string message)
        : base(message)
    {
    }

    public InvalidScenarioParametersException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
