using FluentAssertions;
using Ignixa.Lab.Functions.Execution;

namespace Ignixa.Lab.Functions.Tests.Execution;

public sealed class TargetUrlValidatorTests
{
    [Theory]
    [InlineData("https://hapi.fhir.org/baseR4")]
    [InlineData("http://fhir.example.org/base")]
    [InlineData("https://server.fire.ly")]
    public void TryValidate_AllowsPublicHttpAndHttpsTargets(string url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: false, out var uri, out var error);

        ok.Should().BeTrue();
        uri.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_RejectsMissingUrl(string? url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: false, out var uri, out var error);

        ok.Should().BeFalse();
        uri.Should().BeNull();
        error.Should().Contain("required");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("base")]
    [InlineData("example.org/fhir")]
    public void TryValidate_RejectsNonAbsoluteUrls(string url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: false, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("absolute");
    }

    [Theory]
    [InlineData("ftp://fhir.example.org/base")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.org")]
    public void TryValidate_RejectsNonHttpSchemes(string url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: false, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("http");
    }

    [Theory]
    [InlineData("http://localhost:8080/fhir")]
    [InlineData("https://app.localhost/fhir")]
    [InlineData("http://127.0.0.1/fhir")]
    [InlineData("http://127.10.20.30/fhir")]
    [InlineData("http://[::1]/fhir")]
    [InlineData("http://10.0.0.5/fhir")]
    [InlineData("http://10.255.255.255/fhir")]
    [InlineData("http://172.16.0.1/fhir")]
    [InlineData("http://172.31.255.1/fhir")]
    [InlineData("http://192.168.1.1/fhir")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://0.0.0.0/fhir")]
    public void TryValidate_BlocksPrivateAndLoopbackTargets_WhenNotAllowed(string url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: false, out var uri, out var error);

        ok.Should().BeFalse();
        uri.Should().BeNull();
        error.Should().Contain("private");
    }

    [Theory]
    [InlineData("http://localhost:8080/fhir")]
    [InlineData("http://127.0.0.1/fhir")]
    [InlineData("http://192.168.1.1/fhir")]
    public void TryValidate_AllowsPrivateTargets_WhenExplicitlyPermitted(string url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: true, out var uri, out var error);

        ok.Should().BeTrue();
        uri.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("http://172.15.0.1/fhir")] // just below the 172.16/12 block
    [InlineData("http://172.32.0.1/fhir")] // just above the 172.16/12 block
    [InlineData("http://192.169.0.1/fhir")] // adjacent to 192.168/16
    [InlineData("http://11.0.0.1/fhir")] // adjacent to 10/8
    public void TryValidate_AllowsPublicLiteralsNearPrivateRanges(string url)
    {
        var ok = TargetUrlValidator.TryValidate(url, allowPrivateTargets: false, out var uri, out var error);

        ok.Should().BeTrue();
        uri.Should().NotBeNull();
        error.Should().BeNull();
    }
}
