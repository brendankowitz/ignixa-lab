using System.Collections.Concurrent;
using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Models;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Lab.Functions.Suites;

/// <summary>
/// Discovers and caches the bundled TestScript suites that ship with the
/// backend. The immediate sub-folder of the suites directory is treated as the
/// suite's category (for example <c>testscripts/patient/patient-read.json</c>
/// belongs to the "patient" category).
/// </summary>
public sealed class SuiteCatalog : ISuiteCatalog
{
    private readonly IgnixaLabOptions _options;
    private readonly ILogger<SuiteCatalog> _logger;
    private readonly Lazy<IReadOnlyList<CatalogEntry>> _entries;

    public SuiteCatalog(IOptions<IgnixaLabOptions> options, ILogger<SuiteCatalog> logger)
    {
        _options = options.Value;
        _logger = logger;
        _entries = new Lazy<IReadOnlyList<CatalogEntry>>(LoadEntries);
    }

    /// <summary>Absolute path of the directory that suites are loaded from.</summary>
    public string SuitesDirectory => ResolveSuitesDirectory(_options.SuitesDirectory);

    /// <inheritdoc />
    public IReadOnlyList<SuiteDescriptor> GetSuites() =>
        _entries.Value.Select(e => e.Descriptor).ToArray();

    /// <inheritdoc />
    public bool TryGet(string id, out CatalogEntry entry)
    {
        entry = _entries.Value.FirstOrDefault(e =>
            string.Equals(e.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return entry is not null;
    }

    private IReadOnlyList<CatalogEntry> LoadEntries()
    {
        var directory = SuitesDirectory;
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Suites directory {Directory} does not exist; no bundled suites loaded.", directory);
            return Array.Empty<CatalogEntry>();
        }

        var entries = new ConcurrentBag<CatalogEntry>();
        var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var content = TestScriptContentNormalizer.Normalize(File.ReadAllText(file));
                var parseResult = TestScriptParser.Parse(content);
                if (!parseResult.IsSuccess || parseResult.Value is null)
                {
                    _logger.LogWarning("Skipping {File}: not a valid TestScript.", file);
                    continue;
                }

                var descriptor = BuildDescriptor(directory, file, parseResult.Value);
                entries.Add(new CatalogEntry(
                    descriptor,
                    file,
                    parseResult.Value));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load TestScript {File}.", file);
            }
        }

        return entries
            .OrderBy(e => e.Descriptor.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SuiteDescriptor BuildDescriptor(string root, string file, TestScriptDefinition definition)
    {
        var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
        var category = CategoryFromRelativePath(relative);
        var metadata = definition.Metadata;
        var name = string.IsNullOrWhiteSpace(metadata?.Name)
            ? Path.GetFileNameWithoutExtension(file)
            : metadata!.Name;

        var tests = definition.Tests
            .Select(test => new SuiteTest(test.Name, test.Description))
            .ToArray();

        return new SuiteDescriptor(
            Id: relative,
            Name: name,
            Description: metadata?.Description ?? string.Empty,
            Category: category,
            FhirVersion: metadata?.Version ?? string.Empty,
            File: relative,
            TestCount: tests.Length,
            Tests: tests);
    }

    private static string CategoryFromRelativePath(string relativePath)
    {
        var separatorIndex = relativePath.IndexOf('/', StringComparison.Ordinal);
        return separatorIndex > 0 ? relativePath[..separatorIndex] : "general";
    }

    private static string ResolveSuitesDirectory(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(AppContext.BaseDirectory, "testscripts");
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }
}
