using System.Reflection;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Predefined;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>One discovered predefined scenario factory method, reflected from <c>Ignixa.FhirFakes.Scenarios.Predefined</c> by naming convention.</summary>
public sealed class DiscoveredScenario
{
    public required string Id { get; init; }
    public required MethodInfo Method { get; init; }

    /// <summary>The method's own parameters, excluding the leading <see cref="IFhirSchemaProvider"/>.</summary>
    public IReadOnlyList<ParameterInfo> Parameters => Method.GetParameters().Skip(1).ToList();
}

/// <summary>
/// Reflects the real, published <c>Ignixa.FhirFakes</c> library for its predefined
/// scenario factory methods, so the Fakes bench always reflects what the library
/// actually offers rather than a hand-maintained list that can drift out of sync.
/// A public static method in the <c>Ignixa.FhirFakes.Scenarios.Predefined</c>
/// namespace returning <see cref="ScenarioContext"/> whose first parameter is
/// <see cref="IFhirSchemaProvider"/> counts as a scenario; its "Get" prefix (if
/// any) is stripped to form the id (e.g. <c>GetDiabeticPatient</c> -&gt; <c>DiabeticPatient</c>).
/// </summary>
public sealed class ScenarioDiscovery
{
    private readonly Lazy<IReadOnlyDictionary<string, DiscoveredScenario>> _scenarios = new(Discover);

    public IReadOnlyList<DiscoveredScenario> All() => _scenarios.Value.Values.ToList();

    public DiscoveredScenario? Find(string id) =>
        _scenarios.Value.TryGetValue(id, out var scenario) ? scenario : null;

    /// <summary>
    /// Invokes a discovered scenario's factory method, using each entry in
    /// <paramref name="parameters"/> (matched by parameter name, case-insensitive)
    /// to override that parameter's own default value.
    /// </summary>
#pragma warning disable CA1822
    public ScenarioContext Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var allParameters = scenario.Method.GetParameters();
        var args = new object?[allParameters.Length];
        args[0] = schemaProvider;

        var overrides = parameters is null
            ? null
            : new Dictionary<string, JsonElement>(parameters, StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < allParameters.Length; i++)
        {
            var parameter = allParameters[i];
            args[i] = overrides != null && overrides.TryGetValue(parameter.Name!, out var overrideValue)
                ? ConvertParameter(overrideValue, parameter.ParameterType)
                : parameter.DefaultValue;
        }

        return (ScenarioContext)scenario.Method.Invoke(null, args)!;
    }
#pragma warning restore CA1822

    private static object? ConvertParameter(JsonElement value, Type parameterType)
    {
        var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (underlyingType == typeof(int))
        {
            return value.GetInt32();
        }

        if (underlyingType == typeof(bool))
        {
            return value.GetBoolean();
        }

        if (underlyingType == typeof(decimal))
        {
            return value.GetDecimal();
        }

        return value.GetString();
    }

    private static IReadOnlyDictionary<string, DiscoveredScenario> Discover()
    {
        var scenarios = new Dictionary<string, DiscoveredScenario>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(DiabeticPatientScenario).Assembly;

        var scenarioTypes = assembly.GetTypes()
            .Where(type => type.Namespace == "Ignixa.FhirFakes.Scenarios.Predefined" && type.IsClass && type.IsPublic);

        foreach (var type in scenarioTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.ReturnType == typeof(ScenarioContext));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(IFhirSchemaProvider))
                {
                    continue;
                }

                var id = method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name[3..] : method.Name;
                scenarios[id] = new DiscoveredScenario { Id = id, Method = method };
            }
        }

        return scenarios;
    }
}
