using FluentAssertions;
using Ignixa.Lab.Functions.Services.Fakes;

namespace Ignixa.Lab.Functions.Tests.Services.Fakes;

public sealed class ObservationStateDiscoveryTests
{
    [Fact]
    public void Names_IncludesBloodGlucoseAndBodyTemperature()
    {
        var discovery = new ObservationStateDiscovery();

        var names = discovery.Names();

        names.Should().Contain(["BloodGlucose", "BodyTemperature"]);
    }

    [Fact]
    public void Create_UnknownName_ReturnsNull()
    {
        var discovery = new ObservationStateDiscovery();

        discovery.Create("NotARealState").Should().BeNull();
    }

    [Fact]
    public void Create_BloodGlucose_ReturnsAState()
    {
        var discovery = new ObservationStateDiscovery();

        var state = discovery.Create("BloodGlucose");

        state.Should().NotBeNull();
    }
}
