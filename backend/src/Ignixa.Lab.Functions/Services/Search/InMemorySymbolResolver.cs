using System.Collections.Concurrent;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>
/// The Search bench has no live SQL Server (<c>Ignixa.DataLayer.SqlEntityFramework</c> is not referenced),
/// so this stands in for the compiler's only I/O seam. <see cref="ISymbolResolver"/> resolves search
/// parameters and resource types to <see cref="short"/> surrogate ids; the compiler only cares whether an
/// id is present, never its value, so any deterministic assignment produces real plan/SQL shape. Ids are
/// assigned sequentially on first sight from two independent registries.
///
/// Search parameters are keyed by their globally-unique <see cref="SearchParameterInfo.Url"/> (falling back
/// to <see cref="SearchParameterInfo.Code"/> if <c>Url</c> is null), ensuring the same parameter always
/// resolves to the same id within a request. Resource types are keyed by name.
///
/// A new instance is created per HTTP request, so ids are stable within a trace and need not persist across
/// requests. The <c>parameter</c> argument is assumed valid per the method contract (defensive null-checking
/// is not performed). Id assignment uses <see cref="short"/> to match the database surrogate id width; ids
/// wrap after 32,767 entries per instance, which is acceptable because a fresh resolver is created per
/// request and no single query should exhaust that ceiling.
/// </summary>
public sealed class InMemorySymbolResolver : ISymbolResolver
{
    private readonly ConcurrentDictionary<string, short> _searchParamIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, short> _resourceTypeIds = new(StringComparer.Ordinal);
    private int _nextSearchParamId;
    private int _nextResourceTypeId;

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
    {
        var key = parameter.Url?.ToString() ?? parameter.Code;
        // Cast wraps after 32,767 entries. Acceptable because a fresh resolver is created per request.
        var id = _searchParamIds.GetOrAdd(key, _ => (short)Interlocked.Increment(ref _nextSearchParamId));
        return Task.FromResult<short?>(id);
    }

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        // Cast wraps after 32,767 entries. Acceptable because a fresh resolver is created per request.
        var id = _resourceTypeIds.GetOrAdd(resourceType, _ => (short)Interlocked.Increment(ref _nextResourceTypeId));
        return Task.FromResult<short?>(id);
    }
}
