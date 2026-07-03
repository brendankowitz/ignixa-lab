namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>Thrown when a scenario request's <c>parameters</c> bag can't be converted to the scenario method's actual reflected parameter types.</summary>
public sealed class InvalidScenarioParametersException(string message) : Exception(message);
