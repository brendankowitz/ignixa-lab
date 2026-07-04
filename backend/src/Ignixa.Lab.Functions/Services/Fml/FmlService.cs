using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Lab.Functions.Services.Fml;

/// <summary>
/// Orchestrator service for FHIR Mapping Language (FML) transforms: parses
/// the map text, resolves the entry group's source/target types from its
/// "uses" declarations, and runs the transform via
/// <c>Ignixa.FhirMappingLanguage</c>.
/// </summary>
public sealed class FmlService
{
    // MappingParser has no per-parse mutable state (confirmed against
    // ignixa-fhir source), so it's safe as a static singleton - mirrors
    // ExpressionAnalyzer's `private static readonly FhirPathParser Parser = new();`.
    private static readonly MappingParser Parser = new();

    private readonly SchemaProviderFactory _schemaFactory;
    private readonly ILogger<FmlService> _logger;

    public FmlService(SchemaProviderFactory schemaFactory, ILogger<FmlService> logger)
    {
        _schemaFactory = schemaFactory;
        _logger = logger;
    }

    /// <summary>
    /// Parses and executes an FML transform. <see cref="MappingEvaluator"/>
    /// and <see cref="MappingContext"/> hold mutable per-execution state, so
    /// both are constructed fresh here on every call rather than
    /// shared/injected as singletons.
    /// </summary>
    public FmlResult Transform(FmlRequest request, CancellationToken cancellationToken = default)
    {
        // The underlying MappingParser/MappingEvaluator are fully synchronous and
        // expose no cancellation hook, so the token can only be observed at the
        // method boundary - a cancelled request is rejected before the (CPU-bound)
        // parse/execute work begins rather than being interruptible mid-transform.
        cancellationToken.ThrowIfCancellationRequested();

        MapExpression map;
        try
        {
            map = Parser.Parse(request.Map);
        }
        // ParseException is the documented failure, but MappingParser also throws
        // a raw ArgumentException for whitespace-only input (not wrapped in
        // ParseException). Catch broadly so any parser-side gap surfaces as a
        // structured error instead of an unhandled 500 - there is no global
        // exception middleware in this project.
        catch (Exception ex) when (ex is ParseException or ArgumentException)
        {
            return FmlResult.Failure(request, $"Failed to parse FML map: {ex.Message}", request.Map);
        }

        if (map.Groups.Count == 0)
        {
            return FmlResult.Failure(request, "The map defines no groups.", request.Map);
        }

        var entryGroup = map.Groups[0];
        var sourceParams = entryGroup.Parameters.Where(p => p.Mode == ParameterMode.Source).ToList();
        var targetParams = entryGroup.Parameters.Where(p => p.Mode == ParameterMode.Target).ToList();

        if (sourceParams.Count != 1 || targetParams.Count != 1)
        {
            return FmlResult.Failure(
                request,
                $"Only a single source and single target parameter are supported on the entry " +
                $"group '{entryGroup.Name}' (found {sourceParams.Count} source, {targetParams.Count} target).",
                request.Map);
        }

        var sourceParam = sourceParams[0];
        var targetParam = targetParams[0];

        if (string.IsNullOrEmpty(sourceParam.Type) || string.IsNullOrEmpty(targetParam.Type))
        {
            return FmlResult.Failure(
                request,
                $"The entry group's source and target parameters must both declare a type, e.g. " +
                $"'group {entryGroup.Name}(source {sourceParam.Name} : Patient, target {targetParam.Name} : Bundle)'.",
                request.Map);
        }

        var sourceUses = map.Uses.FirstOrDefault(u => u.Alias == sourceParam.Type);
        var targetUses = map.Uses.FirstOrDefault(u => u.Alias == targetParam.Type);

        if (sourceUses == null || targetUses == null)
        {
            var missingAlias = sourceUses == null ? sourceParam.Type : targetParam.Type;
            return FmlResult.Failure(
                request,
                $"Unsupported model reference: no 'uses' declaration found for type alias '{missingAlias}'. " +
                "Custom or logical StructureDefinitions supplied via a 'model' parameter are not yet supported.",
                request.Map);
        }

        var targetTypeName = ExtractResourceTypeName(targetUses.Url);
        var schema = _schemaFactory.GetSchema("R4");

        try
        {
            var sourceElement = request.Resource.ToElement(schema);
            var targetResource = new ResourceJsonNode { ResourceType = targetTypeName };
            var targetElement = targetResource.ToElement(schema);

            var logLines = new List<string>();
            var context = new MappingContext
            {
                ErrorMode = ErrorMode.Lenient,
                Logger = line => logLines.Add(line)
            };
            context.SetSource(sourceParam.Name, sourceElement);
            // MappingEvaluator's required-parameter check reads context.GetTarget
            // (the IElement dictionary), not GetTargetResource, so both must be set:
            // SetTarget satisfies that check, SetTargetResource is what the
            // JsonNodeMutator actually mutates.
            context.SetTarget(targetParam.Name, targetElement);
            context.SetTargetResource(targetParam.Name, targetResource);

            var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
            var options = MappingEvaluatorOptions.Default;
            options.ErrorMode = ErrorMode.Lenient;
            var evaluator = new MappingEvaluator(options, mutator);

            evaluator.ExecuteGroup(map, entryGroup.Name, context);

            return FmlResult.Success(request, targetResource, logLines, context.Errors);
        }
        catch (Exception ex)
        {
            // A failure here is an engine-side fault (not user input validation), so
            // log it server-side. Log the map length, not the map text, to avoid
            // writing potentially large/sensitive payloads to logs.
            _logger.LogError(ex, "FML transform execution failed (map length {MapLength}).", request.Map.Length);
            return FmlResult.Failure(request, $"Transform execution error: {ex.Message}", request.Map);
        }
    }

    private static string ExtractResourceTypeName(string structureDefinitionUrl) =>
        structureDefinitionUrl.TrimEnd('/').Split('/').Last();
}
