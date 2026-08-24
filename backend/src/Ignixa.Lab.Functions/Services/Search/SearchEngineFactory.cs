using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>The three per-FHIR-version dependencies the Search compiler needs beyond the symbol resolver: the
/// options builder, the search-parameter definition manager, and the compartment definition manager.</summary>
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
    /// value, same fallback <see cref="SchemaProviderFactory"/> uses. Callers that report the version back to
    /// the user must echo <see cref="Resolve"/>, not their raw input — otherwise an unrecognized value is
    /// silently served an R4 trace labelled with a version that was never consulted.
    /// </summary>
    public SearchEngine Get(string fhirVersion) => Resolve(fhirVersion) switch
    {
        "STU3" => _stu3.Value,
        "R4B" => _r4B.Value,
        "R5" => _r5.Value,
        "R6" => _r6.Value,
        // "R4" plus every unrecognized value, which Resolve has already folded to "R4".
        _ => _r4.Value,
    };

    /// <summary>Canonical name of the version <see cref="Get"/> would actually build for this input — its
    /// canonical spelling when recognized (so "R3" resolves to "STU3" and "r4b" to "R4B", not the input
    /// verbatim), otherwise "R4" (the fallback). This is the only version string safe to put in a response
    /// body or an error message.</summary>
    public static string Resolve(string fhirVersion) => TryNormalize(fhirVersion) ?? "R4";

    /// <summary>Canonical name for a recognized version, or null when nothing matches. Distinct from
    /// <see cref="Resolve"/> so a caller that wants to know whether the fallback fired can tell.</summary>
    public static string? TryNormalize(string fhirVersion) => fhirVersion.ToUpperInvariant() switch
    {
        "STU3" or "R3" => "STU3",
        "R4" => "R4",
        "R4B" => "R4B",
        "R5" => "R5",
        "R6" => "R6",
        _ => null,
    };

    private static SearchEngine Build(SchemaProviderFactory schemaProviderFactory, string version, FhirVersion fhirVersion)
    {
        var schema = schemaProviderFactory.GetSchemaProvider(version);

        var definitionManager = new SearchParameterDefinitionManager(
            schema, NullLogger<SearchParameterDefinitionManager>.Instance);

        var referenceParser = new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance);
        var searchParamExpressionParser = new SearchParameterExpressionParser(referenceParser, schema);

        ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver resolver =
            () => definitionManager;
        var expressionParser = new ExpressionParser(resolver, searchParamExpressionParser, schema);

        var builder = new SearchOptionsBuilder(expressionParser, definitionManager);
        var compartments = new CompartmentDefinitionManager(fhirVersion);

        return new SearchEngine(builder, definitionManager, compartments);
    }
}
