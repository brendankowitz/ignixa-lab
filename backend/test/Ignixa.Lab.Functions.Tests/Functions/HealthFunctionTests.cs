using FluentAssertions;
using Ignixa.Lab.Functions.Functions;

namespace Ignixa.Lab.Functions.Tests.Functions;

public sealed class HealthFunctionTests
{
    [Fact]
    public void ReadTestScriptsRevision_FileContainsCommitSha_ReturnsTrimmedContent()
    {
        var path = WriteTempFile("abc123def\n");

        var revision = HealthFunction.ReadTestScriptsRevision(path);

        revision.Should().Be("abc123def");
    }

    [Fact]
    public void ReadTestScriptsRevision_FileDoesNotExist_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");

        var revision = HealthFunction.ReadTestScriptsRevision(path);

        revision.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void ReadTestScriptsRevision_FileIsEmptyOrWhitespace_ReturnsNull(string contents)
    {
        var path = WriteTempFile(contents);

        var revision = HealthFunction.ReadTestScriptsRevision(path);

        revision.Should().BeNull();
    }

    private static string WriteTempFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(path, contents);
        return path;
    }
}
