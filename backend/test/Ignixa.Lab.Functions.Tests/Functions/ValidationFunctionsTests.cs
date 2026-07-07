using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ignixa.Lab.Functions.Functions;
using Ignixa.Lab.Functions.Models.Validation;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Lab.Functions.Services.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class ValidationFunctionsTests
{
    [Fact]
    public async Task ValidateResource_ValidPatient_ReturnsNoErrors()
    {
        var functions = CreateFunctions();
        var request = BuildPostRequest("""
            {
              "fhirVersion": "r4",
              "depth": "spec",
              "resource": {
                "resourceType": "Patient",
                "id": "example",
                "name": [{ "family": "Chalmers", "given": ["Peter"] }]
              }
            }
            """);

        var result = await functions.ValidateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ResourceValidationResponse>().Subject;
        response.ResourceType.Should().Be("Patient");
        response.IsValid.Should().BeTrue();
        response.Summary.Error.Should().Be(0);
        response.Summary.Total.Should().Be(response.Summary.Fatal + response.Summary.Error + response.Summary.Warning + response.Summary.Information);
    }

    [Fact]
    public async Task ValidateResource_InvalidPatient_ReturnsIssues()
    {
        var functions = CreateFunctions();
        var request = BuildPostRequest("""
            {
              "fhirVersion": "r4",
              "depth": "spec",
              "resource": {
                "resourceType": "Patient",
                "active": "not-a-boolean"
              }
            }
            """);

        var result = await functions.ValidateResource(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ResourceValidationResponse>().Subject;
        response.IsValid.Should().BeFalse();
        response.Issues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateResource_UnknownDepth_ReturnsBadRequest()
    {
        var functions = CreateFunctions();
        var request = BuildPostRequest("""
            {
              "fhirVersion": "r4",
              "depth": "deep",
              "resource": { "resourceType": "Patient" }
            }
            """);

        var result = await functions.ValidateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("stu3")]
    [InlineData("r4")]
    [InlineData("r4b")]
    [InlineData("r5")]
    [InlineData("r6")]
    [InlineData("not-a-version")]
    public async Task ValidateResource_FhirVersionMappings_ReturnOk(string fhirVersion)
    {
        var functions = CreateFunctions();
        var request = BuildPostRequest($$"""
            {
              "fhirVersion": "{{fhirVersion}}",
              "depth": "minimal",
              "resource": { "resourceType": "Patient" }
            }
            """);

        var result = await functions.ValidateResource(request, CancellationToken.None);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ResourceValidationResponse>().Subject;
        response.FhirVersion.Should().Be(fhirVersion);
        response.EngineVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public async Task ValidateResource_EmptyPackageSpec_IsIgnored()
    {
        var functions = CreateFunctions();
        var request = BuildPostRequest("""
            {
              "fhirVersion": "r4",
              "depth": "minimal",
              "packages": [""],
              "resource": { "resourceType": "Patient" }
            }
            """);

        var result = await functions.ValidateResource(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ValidateResource_MalformedPackageSpec_ReturnsBadRequestWithoutHttpCall()
    {
        var functions = CreateFunctions();
        var request = BuildPostRequest("""
            {
              "fhirVersion": "r4",
              "depth": "minimal",
              "packages": ["hl7.fhir.us.core"],
              "resource": { "resourceType": "Patient" }
            }
            """);

        var result = await functions.ValidateResource(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ValidateResource_MalformedRequest_ReturnsBadRequest()
    {
        var functions = CreateFunctions();

        var result = await functions.ValidateResource(BuildPostRequest("{"), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static ValidationFunctions CreateFunctions()
    {
        var schemaFactory = new SchemaProviderFactory();
        var service = new ResourceValidationService(
            schemaFactory,
            new ThrowingHttpClientFactory(),
            NullLoggerFactory.Instance);
        return new ValidationFunctions(NullLogger<ValidationFunctions>.Instance, service);
    }

    private static HttpRequest BuildPostRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The HTTP client should only be used when package validation is requested.");
        }
    }
}
