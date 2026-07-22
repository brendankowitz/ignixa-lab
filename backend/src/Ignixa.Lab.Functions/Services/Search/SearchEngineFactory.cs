using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>The three per-FHIR-version dependencies <see cref="Ignixa.Search.Sql.Tracing.SearchCompiler"/>
/// needs beyond the symbol resolver: the options builder, the search-parameter definition manager, and the
/// compartment definition manager.</summary>
public sealed record SearchEngine(
    ISearchOptionsBuilder Builder,
    ISearchParameterDefinitionManager SearchParameters,
    ICompartmentDefinitionManager Compartments);

/// <summary>
/// Builds and caches the Search engine dependencies per FHIR version, mirroring
/// <see cref="SchemaProviderFactory"/>'s lazy-singleton-per-version shape and supported version set
/// (STU3, R4, R4B, R5, R6) exactly, so the two factories never silently disagree about which versions this
/// app offers. Each version's build is expensive (loads the full base search-parameter set), so it runs at
/// most once per version.
/// </summary>
public sealed class SearchEngineFactory(SchemaProviderFactory schemaProviderFactory)
{
    private readonly Lazy<SearchEngine> _stu3 = new(() => Build(schemaProviderFactory, "STU3", FhirVersion.Stu3));
    private readonly Lazy<SearchEngine> _r4 = new(() => Build(schemaProviderFactory, "R4", FhirVersion.R4));
    private readonly Lazy<SearchEngine> _r4B = new(() => Build(schemaProviderFactory, "R4B", FhirVersion.R4B));
    private readonly Lazy<SearchEngine> _r5 = new(() => Build(schemaProviderFactory, "R5", FhirVersion.R5));
    private readonly Lazy<SearchEngine> _r6 = new(() => Build(schemaProviderFactory, "R6", FhirVersion.R6));

    /// <summary>
    /// Gets the cached Search engine dependencies for the given FHIR version (case-insensitive; "STU3" and
    /// "R3" are synonyms, matching <see cref="SchemaProviderFactory"/>). Defaults to R4 for an unrecognized
    /// value, same fallback <see cref="SchemaProviderFactory"/> uses.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public SearchEngine Get(string fhirVersion) => fhirVersion.ToUpperInvariant() switch
    {
        "STU3" or "R3" => _stu3.Value,
        "R4" => _r4.Value,
        "R4B" => _r4B.Value,
        "R5" => _r5.Value,
        "R6" => _r6.Value,
        _ => _r4.Value,
    };

    private static SearchEngine Build(SchemaProviderFactory schemaProviderFactory, string version, FhirVersion fhirVersion)
    {
        var schema = schemaProviderFactory.GetSchemaProvider(version);

        var definitionManager = new SearchParameterDefinitionManager(
            schema, NullLogger<SearchParameterDefinitionManager>.Instance);

        var referenceParser = new ReferenceSearchValueParser(schema);
        var searchParamExpressionParser = new SearchParameterExpressionParser(referenceParser, schema);

        ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver resolver =
            () => definitionManager;
        var expressionParser = new ExpressionParser(resolver, searchParamExpressionParser, schema);

        var builder = new SearchOptionsBuilder(expressionParser, definitionManager);
        var compartments = new CompartmentDefinitionManager(fhirVersion);

        return new SearchEngine(builder, definitionManager, compartments);
    }
}
