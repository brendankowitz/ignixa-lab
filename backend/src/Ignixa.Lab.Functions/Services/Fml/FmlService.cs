using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization.SourceNodes;

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

    public FmlService(SchemaProviderFactory schemaFactory)
    {
        _schemaFactory = schemaFactory;
    }

    /// <summary>
    /// Parses and executes an FML transform. <see cref="MappingEvaluator"/>
    /// and <see cref="MappingContext"/> hold mutable per-execution state, so
    /// both are constructed fresh here on every call rather than
    /// shared/injected as singletons.
    /// </summary>
    public FmlResult Transform(FmlRequest request)
    {
        MapExpression map;
        try
        {
            map = Parser.Parse(request.Map);
        }
        catch (ParseException ex)
        {
            return new FmlResult
            {
                Request = request,
                Error = $"Failed to parse FML map: {ex.Message}",
                ErrorDiagnostics = request.Map
            };
        }

        if (map.Groups.Count == 0)
        {
            return new FmlResult
            {
                Request = request,
                Error = "The map defines no groups.",
                ErrorDiagnostics = request.Map
            };
        }

        var entryGroup = map.Groups[0];
        var sourceParams = entryGroup.Parameters.Where(p => p.Mode == ParameterMode.Source).ToList();
        var targetParams = entryGroup.Parameters.Where(p => p.Mode == ParameterMode.Target).ToList();

        if (sourceParams.Count != 1 || targetParams.Count != 1)
        {
            return new FmlResult
            {
                Request = request,
                Error = $"Only a single source and single target parameter are supported on the entry " +
                        $"group '{entryGroup.Name}' (found {sourceParams.Count} source, {targetParams.Count} target).",
                ErrorDiagnostics = request.Map
            };
        }

        var sourceParam = sourceParams[0];
        var targetParam = targetParams[0];

        if (string.IsNullOrEmpty(sourceParam.Type) || string.IsNullOrEmpty(targetParam.Type))
        {
            return new FmlResult
            {
                Request = request,
                Error = $"The entry group's source and target parameters must both declare a type, e.g. " +
                        $"'group {entryGroup.Name}(source {sourceParam.Name} : Patient, target {targetParam.Name} : Bundle)'.",
                ErrorDiagnostics = request.Map
            };
        }

        var sourceUses = map.Uses.FirstOrDefault(u => u.Alias == sourceParam.Type);
        var targetUses = map.Uses.FirstOrDefault(u => u.Alias == targetParam.Type);

        if (sourceUses == null || targetUses == null)
        {
            var missingAlias = sourceUses == null ? sourceParam.Type : targetParam.Type;
            return new FmlResult
            {
                Request = request,
                Error = $"Unsupported model reference: no 'uses' declaration found for type alias '{missingAlias}'. " +
                        "Custom or logical StructureDefinitions supplied via a 'model' parameter are not yet supported.",
                ErrorDiagnostics = request.Map
            };
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

            return new FmlResult
            {
                Request = request,
                Output = targetResource,
                LogLines = logLines,
                Errors = context.Errors
            };
        }
        catch (Exception ex)
        {
            return new FmlResult
            {
                Request = request,
                Error = $"Transform execution error: {ex.Message}",
                ErrorDiagnostics = request.Map
            };
        }
    }

    private static string ExtractResourceTypeName(string structureDefinitionUrl) =>
        structureDefinitionUrl.TrimEnd('/').Split('/').Last();
}
