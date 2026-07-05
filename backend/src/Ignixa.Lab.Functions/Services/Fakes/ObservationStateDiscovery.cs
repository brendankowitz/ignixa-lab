using System.Reflection;
using Ignixa.FhirFakes.Scenarios.States;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>
/// Reflects <c>Ignixa.FhirFakes.Scenarios.States.ObservationState</c>'s own
/// public static, no-required-argument factory methods (e.g. <c>BloodGlucose()</c>,
/// <c>BodyTemperature()</c>) so the Fakes bench's observation clinical-state
/// picker always reflects what the library actually offers. This type lives in
/// the core library itself (not the CLI tool), so it's genuinely reflectable
/// from this project.
/// </summary>
public sealed class ObservationStateDiscovery
{
    private readonly Lazy<IReadOnlyDictionary<string, MethodInfo>> _states = new(Discover);

    public IReadOnlyList<string> Names() => _states.Value.Keys.ToList();

    public ObservationState? Create(string name)
    {
        if (!_states.Value.TryGetValue(name, out var method))
        {
            return null;
        }

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = parameters[i].DefaultValue;
        }

        return method.Invoke(null, args) as ObservationState;
    }

    private static IReadOnlyDictionary<string, MethodInfo> Discover()
    {
        var states = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        var observationStateType = typeof(ObservationState);

        foreach (var method in observationStateType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != observationStateType)
            {
                continue;
            }

            if (method.GetParameters().All(parameter => parameter.HasDefaultValue))
            {
                states[method.Name] = method;
            }
        }

        return states;
    }
}
