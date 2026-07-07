namespace Ignixa.Lab.Functions.Models.Validation;

/// <summary>Validation result returned to the benches UI.</summary>
public sealed class ResourceValidationResponse
{
    public required string FhirVersion { get; init; }

    public required string EngineVersion { get; init; }

    public required string ResourceType { get; init; }

    public required string Depth { get; init; }

    public required bool IsValid { get; init; }

    public required ValidationSummary Summary { get; init; }

    public required IReadOnlyList<ValidationIssueDto> Issues { get; init; }
}

public sealed class ValidationSummary
{
    public int Fatal { get; init; }

    public int Error { get; init; }

    public int Warning { get; init; }

    public int Information { get; init; }

    public int Total => Fatal + Error + Warning + Information;
}

public sealed class ValidationIssueDto
{
    public required string Severity { get; init; }

    public required string Code { get; init; }

    public required string Path { get; init; }

    public required string Message { get; init; }

    public string? Details { get; init; }
}
