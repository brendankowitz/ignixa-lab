using Ignixa.Lab.Functions.Conformance;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Result of a run attempt: either a completed <see cref="ConformanceReport"/>
/// or a validation failure carrying an explanatory <see cref="Error"/> message.
/// </summary>
public sealed class RunOutcome
{
    private RunOutcome(bool isValid, ConformanceReport? report, string? error)
    {
        IsValid = isValid;
        Report = report;
        Error = error;
    }

    public bool IsValid { get; }

    public ConformanceReport? Report { get; }

    public string? Error { get; }

    public static RunOutcome Completed(ConformanceReport report) => new(true, report, null);

    public static RunOutcome Invalid(string error) => new(false, null, error);
}
