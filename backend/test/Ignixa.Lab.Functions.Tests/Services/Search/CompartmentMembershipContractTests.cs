using FluentAssertions;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Search;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

/// <summary>
/// The Search bench's frontend hardcodes which member types it offers per compartment root
/// (COMPARTMENT_MEMBERS in frontend/src/benches/search/searchTypes.ts) because the backend exposes no
/// metadata endpoint for it. That duplication is only safe while the two agree, and the failure mode is
/// silent from the backend's side: the UI just renders a pill that always 400s. These tests pin the
/// membership facts that table encodes, so a package bump that changes a CompartmentDefinition fails here
/// rather than in someone's browser.
/// </summary>
public sealed class CompartmentMembershipContractTests
{
    [Theory]
    [InlineData("STU3")]
    [InlineData("R4")]
    [InlineData("R4B")]
    [InlineData("R5")]
    [InlineData("R6")]
    public void PatientCompartment_ContainsEveryBenchResourceType(string fhirVersion)
    {
        // Including Patient itself -- a Patient is in its own compartment (via `link`), so the bench offers
        // it as a member type rather than excluding the root.
        var engine = new SearchEngineFactory(new SchemaProviderFactory()).Get(fhirVersion);

        engine.Compartments.TryGetResourceTypes(CompartmentType.Patient, out var members).Should().BeTrue();
        members.Should().Contain(["Patient", "Observation", "Encounter"]);
    }

    [Theory]
    [InlineData("STU3")]
    [InlineData("R4")]
    [InlineData("R4B")]
    [InlineData("R5")]
    [InlineData("R6")]
    public void EncounterCompartment_ContainsObservationAndEncounterButNotPatient(string fhirVersion)
    {
        // The asymmetry the frontend table exists to encode: Patient is NOT an Encounter compartment member
        // in any supported version, so offering it as a member pill would be a guaranteed 400.
        var engine = new SearchEngineFactory(new SchemaProviderFactory()).Get(fhirVersion);

        engine.Compartments.TryGetResourceTypes(CompartmentType.Encounter, out var members).Should().BeTrue();
        members.Should().Contain(["Observation", "Encounter"]);
        members.Should().NotContain("Patient");
    }

    [Theory]
    [InlineData("STU3")]
    [InlineData("R4")]
    [InlineData("R4B")]
    [InlineData("R5")]
    [InlineData("R6")]
    public void ObservationIsNeverACompartmentRoot(string fhirVersion)
    {
        // Why the bench offers no Compartment mode for Observation: it is a member type only. FHIR defines
        // exactly five compartments and Observation is not among them.
        Enum.GetNames<CompartmentType>().Should().NotContain("Observation");

        var engine = new SearchEngineFactory(new SchemaProviderFactory()).Get(fhirVersion);
        engine.Compartments.Should().NotBeNull();
    }
}
