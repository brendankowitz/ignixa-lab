using FluentAssertions;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.Fml;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.Fml;

public sealed class FmlServiceTests
{
    private const string ValidMap = """
        map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'

        uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
        uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target

        group PatientToPerson(source src : Patient, target tgt : Person) {
          src.gender as vG -> tgt.gender = vG 'copy_gender';
          src.birthDate as vB -> tgt.birthDate = vB 'copy_birthDate';
        }
        """;

    private const string PatientJson = """{"resourceType":"Patient","id":"example","gender":"male","birthDate":"1974-12-25"}""";

    [Fact]
    public void Transform_ValidMapAndResource_ProducesExpectedOutput()
    {
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = ValidMap,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Output.Should().NotBeNull();
        var outputJson = result.Output!.SerializeToString();
        outputJson.Should().Contain("\"resourceType\":\"Person\"");
        outputJson.Should().Contain("\"gender\":\"male\"");
        outputJson.Should().Contain("\"birthDate\":\"1974-12-25\"");
    }

    [Fact]
    public void Transform_MalformedMap_ReturnsStructuredParseError()
    {
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = "this is not valid FML",
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Failed to parse FML map");
    }

    [Fact]
    public void Transform_UsesAliasNotDeclared_ReturnsUnsupportedModelReferenceError()
    {
        const string mapWithUndeclaredTargetAlias = """
            map 'http://ignixa.dev/StructureMap/Bad' = 'Bad'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source

            group Bad(source src : Patient, target tgt : SomeLogicalModel) {
              src.gender as vG -> tgt.gender = vG 'copy_gender';
            }
            """;
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = mapWithUndeclaredTargetAlias,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unsupported model reference");
        result.Error.Should().Contain("SomeLogicalModel");
    }

    [Fact]
    public void Transform_EntryGroupMissingSourceOrTarget_ReturnsStructuredError()
    {
        const string mapWithNoTarget = """
            map 'http://ignixa.dev/StructureMap/Bad' = 'Bad'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source

            group Bad(source src : Patient) {
              src.gender as vG -> src.gender = vG 'noop';
            }
            """;
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = mapWithNoTarget,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("single source and single target parameter");
    }

    [Fact]
    public void Transform_LogClauseInMap_CapturesLogLines()
    {
        const string mapWithLog = """
            map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target

            group PatientToPerson(source src : Patient, target tgt : Person) {
              src.gender as vG log 'copied gender' -> tgt.gender = vG 'copy_gender';
            }
            """;
        var service = new FmlService(new SchemaProviderFactory());
        var request = new FmlRequest
        {
            Map = mapWithLog,
            Resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientJson)
        };

        var result = service.Transform(request);

        result.IsSuccess.Should().BeTrue();
        result.LogLines.Should().Contain(line => line.Contains("copied gender"));
    }
}
