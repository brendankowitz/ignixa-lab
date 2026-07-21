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
/// <see cref="SchemaProviderFactory"/>'s lazy-singleton-per-version shape. Only R4 is wired today; the
/// per-version cache keeps adding R4B/R5/STU3 later a non-event. The build is expensive (loads the full
/// base search-parameter set), so it runs at most once.
/// </summary>
public sealed class SearchEngineFactory(SchemaProviderFactory schemaProviderFactory)
{
    private readonly Lazy<SearchEngine> _r4 = new(() => Build(schemaProviderFactory, "R4", FhirVersion.R4));

    /// <summary>Gets the cached R4 Search engine dependencies.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public SearchEngine GetR4() => _r4.Value;

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
