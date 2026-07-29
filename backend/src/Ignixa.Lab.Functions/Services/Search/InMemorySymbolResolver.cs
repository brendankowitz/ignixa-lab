using System.Collections.Concurrent;
using System.Collections.Frozen;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>
/// The Search bench has no live SQL Server (<c>Ignixa.DataLayer.SqlEntityFramework</c> is not referenced),
/// so this stands in for the compiler's only I/O seam. <see cref="ISymbolResolver"/> resolves search
/// parameters, resource types, code systems, and quantity codes to surrogate ids; for the ids it does hand
/// out the compiler only cares that one is present, never its value, so any deterministic assignment
/// produces real plan/SQL shape. Ids are assigned on first sight from four independent registries, in
/// increasing order (a <c>GetOrAdd</c> factory can run more than once under contention and burn a value, so
/// they are monotonic rather than strictly contiguous). (<see cref="ISymbolResolver"/> declares a fifth member, <c>GetSystemIdsAsync</c>; it is a
/// default-implemented batch over <see cref="GetSystemIdAsync"/> and is deliberately not overridden.)
///
/// Search parameters are keyed by their globally-unique <see cref="SearchParameterInfo.Url"/> (falling back
/// to <see cref="SearchParameterInfo.Code"/> if <c>Url</c> is null), ensuring the same parameter always
/// resolves to the same id within a request. Resource types, systems, and quantity codes are each keyed by
/// their own value. <see cref="GetSystemIdAsync"/> serves both token and quantity systems — the compiler
/// unions the two sets before resolving them.
///
/// <b>What a null return means differs per lookup</b>, which is why only two of the four can decline:
/// <list type="bullet">
/// <item>System / quantity code: null lowers that parameter to an always-false predicate, which the compiler
/// restamps as <c>ParameterOutcome.KnownMiss</c> — the real "this value is not in the database's lookup
/// table, so it can never match" answer. <see cref="KnownSystems"/>/<see cref="KnownQuantityCodes"/> below
/// stand in for those lookup tables so the bench demonstrates that outcome instead of pretending every
/// system exists.</item>
/// <item>Search parameter: null produces <c>Failed(TraceStage.Resolve)</c> and skips lowering entirely.</item>
/// <item>Resource type: null produces an unmatchable-id sentinel, with no always-false predicate and so no
/// <c>KnownMiss</c>.</item>
/// </list>
/// The latter two are genuine "the request named something that does not exist" failures rather than the
/// "real value, absent from this database" answer a system/quantity miss carries, so this resolver always
/// answers them. The resource type is separately rejected with a 400 upstream in
/// <see cref="Functions.SearchFunctions"/>; an unresolvable search parameter is not — nothing in
/// <c>SearchFunctions</c> validates parameter names, and one that survives binding surfaces as a per-parameter
/// <c>Failed</c> outcome inside a 200 (see <c>SearchTraceMapperTests</c>).
///
/// A new instance is created per HTTP request, so ids are stable within a trace and need not persist across
/// requests. The <c>parameter</c> argument is assumed valid per the method contract (defensive null-checking
/// is not performed). Search-parameter and resource-type ids are <see cref="short"/> to match the database
/// surrogate id width and so are capped at 32,767 per instance; system and quantity-code ids are
/// <see cref="int"/> and are not. Neither ceiling is reachable within one request, and the casts are
/// <c>checked</c> so that a wrap — which would silently hand two different symbols the same id, producing a
/// plan that looks real and is wrong — throws instead.
/// </summary>
public sealed class InMemorySymbolResolver : ISymbolResolver
{
    /// <summary>
    /// Stands in for the <c>System</c> lookup table a real deployment resolves against. A system outside this
    /// set resolves to null, which is what makes <c>ParameterOutcome.KnownMiss</c> reachable in the bench —
    /// the same answer a real server gives for a system it has never indexed, and one that is otherwise
    /// visible only as a <c>1 = 0</c> buried in the emitted SQL.
    ///
    /// Deliberately a small, well-known set rather than an attempt at completeness: the bench's job is to
    /// demonstrate that the miss <i>happens</i> and how it lowers, not to be an authoritative terminology
    /// registry. Anything here is a system a real server would almost certainly have indexed; anything absent
    /// is treated as unindexed. Extend it when a bench example needs a system that is not listed.
    /// </summary>
    private static readonly FrozenSet<string> KnownSystems = new[]
    {
        "http://loinc.org",
        "http://snomed.info/sct",
        "http://unitsofmeasure.org",
        "http://www.nlm.nih.gov/research/umls/rxnorm",
        "http://hl7.org/fhir/sid/icd-9-cm",
        "http://hl7.org/fhir/sid/icd-10",
        "http://hl7.org/fhir/sid/icd-10-cm",
        "http://hl7.org/fhir/sid/cvx",
        "http://terminology.hl7.org/CodeSystem/observation-category",
        "http://terminology.hl7.org/CodeSystem/condition-clinical",
        "http://terminology.hl7.org/CodeSystem/v2-0203",
        "http://terminology.hl7.org/CodeSystem/v3-ActCode",
        "urn:iso:std:iso:3166",
        "urn:ietf:bcp:47",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Stands in for the <c>QuantityCode</c> lookup table, same contract as <see cref="KnownSystems"/>: a
    /// UCUM code outside this set resolves to null and lowers to <c>KnownMiss</c>. These are the units that
    /// show up in common vital-sign and lab examples, which is what the bench's sample queries use.
    /// </summary>
    private static readonly FrozenSet<string> KnownQuantityCodes = new[]
    {
        "mm[Hg]", "kg", "g", "mg", "ug", "cm", "m", "[in_i]", "[lb_av]",
        "%", "/min", "1/min", "{score}", "Cel", "[degF]",
        "mmol/L", "mg/dL", "g/dL", "U/L", "10*3/uL", "10*6/uL", "fL", "pg",
        "a", "mo", "d", "h", "min", "s",
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, short> _searchParamIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, short> _resourceTypeIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _systemIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _quantityCodeIds = new(StringComparer.Ordinal);
    private int _nextSearchParamId;
    private int _nextResourceTypeId;
    private int _nextSystemId;
    private int _nextQuantityCodeId;

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
    {
        var key = parameter.Url?.ToString() ?? parameter.Code;
        var id = _searchParamIds.GetOrAdd(key, _ => checked((short)Interlocked.Increment(ref _nextSearchParamId)));
        return Task.FromResult<short?>(id);
    }

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        var id = _resourceTypeIds.GetOrAdd(resourceType, _ => checked((short)Interlocked.Increment(ref _nextResourceTypeId)));
        return Task.FromResult<short?>(id);
    }

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
    {
        if (!KnownSystems.Contains(system))
        {
            return Task.FromResult<int?>(null);
        }

        var id = _systemIds.GetOrAdd(system, _ => Interlocked.Increment(ref _nextSystemId));
        return Task.FromResult<int?>(id);
    }

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
    {
        if (!KnownQuantityCodes.Contains(code))
        {
            return Task.FromResult<int?>(null);
        }

        var id = _quantityCodeIds.GetOrAdd(code, _ => Interlocked.Increment(ref _nextQuantityCodeId));
        return Task.FromResult<int?>(id);
    }
}
