using Ignixa.Lab.Functions.Models;
using Ignixa.TestScript.Model;

namespace Ignixa.Lab.Functions.Suites;

/// <summary>A discovered bundled suite: its descriptor, source path, and parsed definition.</summary>
public sealed record CatalogEntry(SuiteDescriptor Descriptor, string FilePath, TestScriptDefinition Definition);

/// <summary>Provides access to the bundled TestScript suite catalog.</summary>
public interface ISuiteCatalog
{
    /// <summary>Returns descriptors for all discovered suites, ordered by category then name.</summary>
    IReadOnlyList<SuiteDescriptor> GetSuites();

    /// <summary>Attempts to resolve a suite by its identifier (relative path).</summary>
    bool TryGet(string id, out CatalogEntry entry);
}
