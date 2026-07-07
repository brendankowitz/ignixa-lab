using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Lab.Functions.Models.Validation;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Services.Validation;

/// <summary>Orchestrates Ignixa.Validation for the resource validation bench.</summary>
public sealed class ResourceValidationService(
    SchemaProviderFactory schemaProviderFactory,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory)
{
    private const int MaxPackageSpecs = 5;
    private const string PackageCacheDirectoryName = "ignixa-lab-validator-package-cache";
    private static readonly string EngineVersion = GetEngineVersion();

    public async Task<ResourceValidationResponse> ValidateAsync(
        ResourceValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Resource is null)
        {
            throw new InvalidResourceException("A 'resource' JSON object is required.");
        }

        var sourceNode = JsonNodeSourceNode.Create(request.Resource);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new InvalidResourceException("Could not determine resourceType from JSON.");
        }

        if (!Enum.TryParse<ValidationDepth>(request.Depth, ignoreCase: true, out var depth))
        {
            throw new UnsupportedValidationOptionException(
                $"Unknown depth '{request.Depth}'. Use minimal, spec, full, or compatibility.");
        }

        var requestedFhirVersion = string.IsNullOrWhiteSpace(request.FhirVersion) ? "r4" : request.FhirVersion;
        var schemaProvider = schemaProviderFactory.GetSchemaProvider(NormalizeFhirVersion(requestedFhirVersion));
        var packageSpecs = (request.Packages ?? [])
            .Where(spec => !string.IsNullOrWhiteSpace(spec))
            .ToArray();
        if (packageSpecs.Length > MaxPackageSpecs)
        {
            throw new UnsupportedValidationOptionException(
                $"Too many packages. At most {MaxPackageSpecs} package specs can be layered per validation request.");
        }

        var setup = await BuildSetupAsync(schemaProvider, packageSpecs, cancellationToken);
        var element = sourceNode.ToElement(setup.EffectiveSchema);
        var schema = setup.Resolver.ResolveForElement(element);
        if (schema is null)
        {
            throw new InvalidResourceException($"Validation schema not found for resource type '{resourceType}'.");
        }

        var settings = new ValidationSettings
        {
            Depth = depth,
            SkipTerminologyValidation = request.SkipTerminology,
            TerminologyService = setup.TerminologyService,
        };
        var result = schema.Validate(element, settings, new ValidationState());
        return ToResponse(requestedFhirVersion, EngineVersion, resourceType, depth, result);
    }

    private async Task<ValidationSetup> BuildSetupAsync(
        IFhirSchemaProvider schemaProvider,
        IReadOnlyList<string> packageSpecs,
        CancellationToken cancellationToken)
    {
        ISchema effectiveSchema = schemaProvider;
        ITerminologyService terminology = new InMemoryTerminologyService(schemaProvider.ValueSetProvider);

        if (packageSpecs.Count > 0)
        {
            var packageResources = await LoadPackageResourcesAsync(packageSpecs, cancellationToken);
            effectiveSchema = new ProfileLayeredSchemaProvider(
                schemaProvider,
                packageResources,
                loggerFactory.CreateLogger<ProfileLayeredSchemaProvider>());
            var packageValueSets = new PackageValueSetSource(
                packageResources,
                loggerFactory.CreateLogger<PackageValueSetSource>());
            terminology = new InMemoryTerminologyService(
                primary: schemaProvider.ValueSetProvider,
                additional: [(IValueSetProvider)packageValueSets]);
        }

        var parser = new FhirPathParser(preserveTrivia: false);
        var builder = new StructureDefinitionSchemaBuilder(
            parser,
            loggerFactory.CreateLogger<StructureDefinitionSchemaBuilder>());
        var innerResolver = new StructureDefinitionSchemaResolver(
            effectiveSchema,
            builder,
            terminology);
        var resolver = new ProfileAwareValidationSchemaResolver(new CachedValidationSchemaResolver(innerResolver));

        return new ValidationSetup(effectiveSchema, terminology, resolver);
    }

    private async Task<IReadOnlyList<Ignixa.PackageManagement.Models.ExtractedResource>> LoadPackageResourcesAsync(
        IReadOnlyList<string> packageSpecs,
        CancellationToken cancellationToken)
    {
        var resources = new List<Ignixa.PackageManagement.Models.ExtractedResource>();
        var cacheDir = Path.Combine(Path.GetTempPath(), PackageCacheDirectoryName);
        Directory.CreateDirectory(cacheDir);

        var cache = new PackageCacheManager(
            cacheDir,
            loggerFactory.CreateLogger<PackageCacheManager>());
        var loader = new NpmPackageLoader(
            httpClientFactory.CreateClient(),
            cache,
            options: null,
            loggerFactory.CreateLogger<NpmPackageLoader>());
        var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());

        foreach (var spec in packageSpecs)
        {
            var (packageId, packageVersion) = SplitPackageSpec(spec);
            if (packageId is null || packageVersion is null)
            {
                throw new UnsupportedValidationOptionException(
                    $"Invalid package '{spec}'. Expected id@version, for example hl7.fhir.us.core@6.1.0.");
            }

            await using var stream = await loader.DownloadPackageAsync(packageId, packageVersion, cancellationToken);
            var extracted = await extractor.ExtractAsync(stream, cancellationToken);
            resources.AddRange(extracted.Resources);
        }

        return resources;
    }

    private static (string? Id, string? Version) SplitPackageSpec(string spec)
    {
        var atIndex = spec.LastIndexOf('@');
        return atIndex <= 0 || atIndex == spec.Length - 1
            ? (null, null)
            : (spec[..atIndex], spec[(atIndex + 1)..]);
    }

    private static ResourceValidationResponse ToResponse(
        string fhirVersion,
        string engineVersion,
        string resourceType,
        ValidationDepth depth,
        ValidationResult result)
    {
        var summary = new ValidationSummary
        {
            Fatal = result.Issues.Count(issue => issue.Severity == IssueSeverity.Fatal),
            Error = result.Issues.Count(issue => issue.Severity == IssueSeverity.Error),
            Warning = result.Issues.Count(issue => issue.Severity == IssueSeverity.Warning),
            Information = result.Issues.Count(issue => issue.Severity == IssueSeverity.Information),
        };

        var issues = result.Issues
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.Path, StringComparer.Ordinal)
            .Select(issue => new ValidationIssueDto
            {
                Severity = issue.Severity.ToString().ToLowerInvariant(),
                Code = issue.Code,
                Path = issue.Path,
                Message = issue.Message,
                Details = issue.Details?.Text,
            })
            .ToList();

        return new ResourceValidationResponse
        {
            FhirVersion = fhirVersion,
            EngineVersion = engineVersion,
            ResourceType = resourceType,
            Depth = depth.ToString().ToLowerInvariant(),
            IsValid = result.IsValid,
            Summary = summary,
            Issues = issues,
        };
    }

    private static string NormalizeFhirVersion(string fhirVersion) => fhirVersion.ToUpperInvariant() switch
    {
        "STU3" or "R3" => "STU3",
        "R4B" => "R4B",
        "R5" => "R5",
        "R6" => "R6",
        _ => "R4",
    };

    private static string GetEngineVersion()
    {
        var assembly = typeof(ValidationResult).Assembly;
        var fullVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        var separatorIndex = fullVersion.IndexOfAny(['-', '+']);
        return separatorIndex > 0 ? fullVersion[..separatorIndex] : fullVersion;
    }

    private sealed record ValidationSetup(
        ISchema EffectiveSchema,
        ITerminologyService TerminologyService,
        ProfileAwareValidationSchemaResolver Resolver);
}

public sealed class InvalidResourceException(string message) : Exception(message);

public sealed class UnsupportedValidationOptionException(string message) : Exception(message);
