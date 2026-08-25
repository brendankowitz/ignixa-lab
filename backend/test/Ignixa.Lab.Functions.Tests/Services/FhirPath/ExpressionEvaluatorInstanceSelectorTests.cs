using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.FhirPath;

public sealed class ExpressionEvaluatorInstanceSelectorTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "active": true,
          "name": [
            { "use": "official", "family": "Chalmers", "given": ["Peter", "James"] },
            { "use": "usual", "given": ["Jim"] },
            { "use": "maiden", "family": "Windsor", "given": ["Peter", "James"], "period": { "end": "2002" } }
          ],
          "telecom": [
            { "system": "phone", "value": "(03) 5555 6473", "use": "work", "rank": 1 },
            { "system": "email", "value": "p.chalmers@example.org", "use": "home" }
          ],
          "gender": "male",
          "birthDate": "1974-12-25",
          "address": [{ "use": "home", "line": ["534 Erewhon St"], "city": "PleasantVille", "state": "Vic", "postalCode": "3999" }]
        }
        """;

    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "id": "blood-pressure",
          "status": "final",
          "category": [
            {
              "coding": [
                { "system": "http://terminology.hl7.org/CodeSystem/observation-category", "code": "vital-signs", "display": "Vital Signs" }
              ]
            }
          ],
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "85354-9", "display": "Blood pressure panel" }],
            "text": "Blood pressure"
          },
          "subject": { "reference": "Patient/example" },
          "effectiveDateTime": "2026-05-02T09:30:00Z",
          "component": [
            {
              "code": { "coding": [{ "system": "http://loinc.org", "code": "8480-6", "display": "Systolic blood pressure" }] },
              "valueQuantity": { "value": 127, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
            },
            {
              "code": { "coding": [{ "system": "http://loinc.org", "code": "8462-4", "display": "Diastolic blood pressure" }] },
              "valueQuantity": { "value": 81, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
            }
          ]
        }
        """;

    [Fact]
    public void Evaluate_StandaloneCodingInstanceSelector_ConstructsCodingWithoutInstanceCreatorError()
    {
        const string expression = "Coding { system: 'http://loinc.org', code: '8480-6' }";
        var schemaFactory = new SchemaProviderFactory();
        var analyzer = new ExpressionAnalyzer(schemaFactory);
        var (parsed, contextExpression, parseError) = analyzer.ParseAndAnalyze(expression, null, null, "R4");

        parseError.Should().BeNull();

        var results = new ExpressionEvaluator(schemaFactory).Evaluate(
            parsed!,
            contextExpression,
            resource: null,
            variables: null,
            fhirVersion: "R4");

        var result = results.Should().ContainSingle().Subject;
        result.Error.Should().BeNull();
        result.OutputValues.Should().ContainSingle().Which.InstanceType.Should().Be("Coding");
        PrimitiveValues(result.OutputValues[0], "system").Should().ContainSingle().Which.Should().Be("http://loinc.org");
        PrimitiveValues(result.OutputValues[0], "code").Should().ContainSingle().Which.Should().Be("8480-6");
        string.Join(Environment.NewLine, results.Select(evaluation => evaluation.Error))
            .Should().NotContain("no instance creator");
    }

    [Fact]
    public void Evaluate_ObservationCodingReshapingSelector_ConstructsCodingForEachComponent()
    {
        var result = Evaluate(
            "component.code.coding.select(Coding { system: system, code: code })",
            ObservationJson)
            .Should().ContainSingle().Subject;

        result.Error.Should().BeNull();
        result.OutputValues.Should().HaveCount(2);
        result.OutputValues.Should().OnlyContain(element => element.InstanceType == "Coding");
        result.OutputValues.Select(coding => $"{PrimitiveValues(coding, "system").Single()}|{PrimitiveValues(coding, "code").Single()}")
            .Should()
            .BeEquivalentTo(
            [
                "http://loinc.org|8480-6",
                "http://loinc.org|8462-4",
            ]);
    }

    [Fact]
    public void Evaluate_PatientHumanNameSelector_ConstructsHumanNameForEachName()
    {
        var result = Evaluate(
            "name.select(HumanName { family: family, given: given.first() })",
            PatientJson)
            .Should().ContainSingle().Subject;

        result.Error.Should().BeNull();
        result.OutputValues.Should().HaveCount(3);
        result.OutputValues.Should().OnlyContain(element => element.InstanceType == "HumanName");
        result.OutputValues.Select(name => $"{PrimitiveValues(name, "family").SingleOrDefault()}|{PrimitiveValues(name, "given").SingleOrDefault()}")
            .Should()
            .BeEquivalentTo(
            [
                "Chalmers|Peter",
                "|Jim",
                "Windsor|Peter",
            ]);
    }

    [Fact]
    public void Evaluate_NestedObservationSelector_ConstructsObservation()
    {
        var result = Evaluate(
            "Observation { code: CodeableConcept { text: 'demo' } }",
            ObservationJson)
            .Should().ContainSingle().Subject;

        result.Error.Should().BeNull();
        result.OutputValues.Should().ContainSingle().Which.InstanceType.Should().Be("Observation");
        var code = result.OutputValues[0].Children("code").Should().ContainSingle().Subject;
        code.InstanceType.Should().Be("CodeableConcept");
        PrimitiveValues(code, "text").Should().ContainSingle().Which.Should().Be("demo");
    }

    private static List<EvaluationResult> Evaluate(string expression, string resourceJson)
    {
        var schemaFactory = new SchemaProviderFactory();
        var analyzer = new ExpressionAnalyzer(schemaFactory);
        var resource = ResourceJsonNode.Parse(resourceJson);
        var (parsed, contextExpression, parseError) = analyzer.ParseAndAnalyze(expression, null, resource.ResourceType, "R4");

        parseError.Should().BeNull();

        return new ExpressionEvaluator(schemaFactory).Evaluate(
            parsed!,
            contextExpression,
            resource,
            variables: null,
            fhirVersion: "R4");
    }

    private static IEnumerable<string> PrimitiveValues(IElement element, string name) =>
        element.Children(name).Select(child => child.Value!.ToString()!);
}
