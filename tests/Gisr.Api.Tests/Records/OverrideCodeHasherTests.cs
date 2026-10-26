using Gisr.Api.Records;

namespace Gisr.Api.Tests.Records;

public class OverrideCodeHasherTests
{
    [Fact]
    public void Hash_UsesTheDocumentedFormat()
    {
        var stored = OverrideCodeHasher.Hash("correct horse battery");

        var parts = stored.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("600000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
    }

    [Fact]
    public void Hash_NeverContainsTheCode()
    {
        var stored = OverrideCodeHasher.Hash("correct horse battery");

        Assert.DoesNotContain("correct", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hash_UsesAFreshSaltEachTime()
    {
        var first = OverrideCodeHasher.Hash("same code 123", 1000);
        var second = OverrideCodeHasher.Hash("same code 123", 1000);

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Split('$')[2], second.Split('$')[2]);
    }

    [Fact]
    public void Verify_AcceptsTheCorrectCode_AndRejectsOthers()
    {
        var stored = OverrideCodeHasher.Hash("correct horse battery", 1000);

        Assert.True(OverrideCodeHasher.Verify("correct horse battery", stored));
        Assert.False(OverrideCodeHasher.Verify("correct horse batterY", stored));
        Assert.False(OverrideCodeHasher.Verify("", stored));
        Assert.False(OverrideCodeHasher.Verify("correct horse battery ", stored));
    }

    [Fact]
    public void Verify_ReadsTheIterationCountFromTheStoredValue()
    {
        var cheap = OverrideCodeHasher.Hash("some override code", 1);

        Assert.StartsWith("pbkdf2-sha256$1$", cheap, StringComparison.Ordinal);
        Assert.True(OverrideCodeHasher.Verify("some override code", cheap));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("pbkdf2-sha256$600000$onlythree$parts")]
    [InlineData("md5$1$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1$!!!$aGFzaA==")]
    public void Verify_RejectsMalformedStoredValues_WithoutThrowing(string stored) =>
        Assert.False(OverrideCodeHasher.Verify("anything at all", stored));

    [Fact]
    public void Verify_RejectsAbsurdIterationCounts_SoStoredDataCannotCauseADenialOfService()
    {
        var stored = "pbkdf2-sha256$2000000000$c2FsdA==$aGFzaA==";

        Assert.False(OverrideCodeHasher.Verify("anything at all", stored));
    }
}
