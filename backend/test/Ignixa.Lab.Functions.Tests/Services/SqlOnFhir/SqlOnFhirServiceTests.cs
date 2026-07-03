using FluentAssertions;
using Ignixa.Lab.Functions.Models;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.SqlOnFhir;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Tests.Services.SqlOnFhir;

public sealed class SqlOnFhirServiceTests
{
    private const string ValidViewDefinition = """
        {
          "resourceType": "ViewDefinition",
          "status": "active",
          "resource": "Patient",
          "select": [
            { "column": [ { "name": "id", "path": "id" }, { "name": "gender", "path": "gender" } ] }
          ]
        }
        """;

    private static SqlOnFhirRequest MakeRequest(string viewDefinition, params string[] resources) => new()
    {
        ViewResource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(viewDefinition),
        Resources = resources.Select(r => JsonSourceNodeFactory.Parse<ResourceJsonNode>(r)).ToList()
    };

    [Fact]
    public void Evaluate_ValidViewDefinitionAndResource_ReturnsRow()
    {
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(ValidViewDefinition, """{"resourceType":"Patient","id":"p1","gender":"male"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeTrue();
        result.Rows.Should().ContainSingle();
        result.Rows[0]["id"]?.ToString().Should().Be("p1");
        result.Rows[0]["gender"]?.ToString().Should().Be("male");
    }

    [Fact]
    public void Evaluate_MultipleResources_ReturnsOneRowPerResource()
    {
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(
            ValidViewDefinition,
            """{"resourceType":"Patient","id":"p1","gender":"male"}""",
            """{"resourceType":"Patient","id":"p2","gender":"female"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeTrue();
        result.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_LimitLowerThanRowCount_TruncatesRows()
    {
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = new SqlOnFhirRequest
        {
            ViewResource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(ValidViewDefinition),
            Resources =
            [
                JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"p1","gender":"male"}"""),
                JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"p2","gender":"female"}""")
            ],
            Limit = 1
        };

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeTrue();
        result.Rows.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_MalformedColumnPath_ReturnsStructuredError()
    {
        const string badView = """
            {
              "resourceType": "ViewDefinition",
              "status": "active",
              "resource": "Patient",
              "select": [ { "column": [ { "name": "bad", "path": "id.." } ] } ]
            }
            """;
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(badView, """{"resourceType":"Patient","id":"p1"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("ViewDefinition evaluation error");
    }

    // The underlying SqlOnFhirEvaluator doesn't validate `select`'s structure before
    // processing it - a non-array `select` is silently treated as "no select items"
    // (producing an empty row) rather than throwing, so this must be checked explicitly
    // before evaluation. Found via a live end-to-end smoke test during final verification.
    [Fact]
    public void Evaluate_SelectIsNotAnArray_ReturnsStructuredError()
    {
        const string badView = """
            {
              "resourceType": "ViewDefinition",
              "status": "active",
              "resource": "Patient",
              "select": "not-an-array"
            }
            """;
        var service = new SqlOnFhirService(new SchemaProviderFactory());
        var request = MakeRequest(badView, """{"resourceType":"Patient","id":"p1"}""");

        var result = service.Evaluate(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("select").And.Contain("array");
    }
}
