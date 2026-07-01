using Ignixa.TestScript.Reporting;

namespace Ignixa.Lab.Functions.Conformance;

/// <summary>
/// Canonical conformance status strings and the mapping from the engine's
/// <see cref="TestScriptOutcome"/> to those strings. The values (pass / fail /
/// error / skipped) match the ignixa-fhir dashboard contract.
/// </summary>
public static class ConformanceStatus
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Error = "error";
    public const string Skipped = "skipped";

    /// <summary>
    /// Maps a <see cref="TestScriptOutcome"/> to its conformance status string.
    /// Warning is treated as a pass (the assertion succeeded with advisories),
    /// consistent with the reference dashboard.
    /// </summary>
    public static string FromOutcome(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass => Pass,
        TestScriptOutcome.Warning => Pass,
        TestScriptOutcome.Skip => Skipped,
        TestScriptOutcome.Error => Error,
        _ => Fail,
    };
}
