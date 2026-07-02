using System.Text.Json.Serialization;

namespace Ignixa.Lab.Functions.Models;

/// <summary>
/// Request body for <c>POST /api/run</c>. Executes the selected bundled suites
/// (and any inline uploaded TestScripts) against <see cref="TargetUrl"/>.
/// </summary>
public sealed class RunRequest
{
    /// <summary>Base URL of the FHIR server under test (absolute http/https).</summary>
    [JsonPropertyName("targetUrl")]
    public string? TargetUrl { get; set; }

    /// <summary>Identifiers of bundled suites to run (see <c>GET /api/suites</c>).</summary>
    [JsonPropertyName("suiteIds")]
    public IReadOnlyList<string>? SuiteIds { get; set; }

    /// <summary>Optional FHIR version override (for example "4.0"). Defaults to the server configuration.</summary>
    [JsonPropertyName("fhirVersion")]
    public string? FhirVersion { get; set; }

    /// <summary>Optional inline TestScript resources uploaded by the client.</summary>
    [JsonPropertyName("uploadedTestScripts")]
    public IReadOnlyList<UploadedTestScript>? UploadedTestScripts { get; set; }
}

/// <summary>An inline TestScript supplied in a run request.</summary>
public sealed class UploadedTestScript
{
    /// <summary>Original file name, used for the report's <c>file</c> field.</summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>Raw TestScript JSON content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
