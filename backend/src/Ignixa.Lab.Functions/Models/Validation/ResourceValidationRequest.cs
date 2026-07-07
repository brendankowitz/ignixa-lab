using System.Text.Json.Nodes;

namespace Ignixa.Lab.Functions.Models.Validation;

/// <summary>Request body for validating a FHIR resource in the benches UI.</summary>
public sealed class ResourceValidationRequest
{
    /// <summary>FHIR release label: stu3, r4, r4b, r5, or r6.</summary>
    public string FhirVersion { get; set; } = "r4";

    /// <summary>Validation depth: minimal, spec, full, or compatibility.</summary>
    public string Depth { get; set; } = "spec";

    /// <summary>When true, terminology binding checks are skipped.</summary>
    public bool SkipTerminology { get; set; }

    /// <summary>FHIR IG package specs in id@version form, matching the CLI's --package option.</summary>
    public IReadOnlyList<string> Packages { get; set; } = [];

    /// <summary>The resource JSON to validate.</summary>
    public JsonNode? Resource { get; set; }
}
