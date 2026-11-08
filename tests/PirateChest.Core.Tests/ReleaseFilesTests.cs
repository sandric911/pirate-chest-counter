using PirateChest.Core.Updates;

namespace PirateChest.Core.Tests;

public class ReleaseFilesTests
{
    [Theory]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2.0.0-beta", "2.0.0")]
    public void ParsesTags(string tag, string expected) => Assert.Equal(Version.Parse(expected), ReleaseFiles.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.0")]
    public void RejectsBadTags(string? tag) => Assert.Null(ReleaseFiles.ParseTag(tag));

    [Fact]
    public void NewerVersionComparesCorrectly() =>
        Assert.True(ReleaseFiles.ParseTag("v1.0.10") > ReleaseFiles.ParseTag("v1.0.9"));

    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Theory]
    [InlineData(Hash + "  PirateChestCounter.exe")]
    [InlineData(Hash + " *PirateChestCounter.exe\r\n")]
    [InlineData("deadbeef  other.zip\n" + Hash + "\tPirateChestCounter.exe")]
    public void FindsHash(string sums) => Assert.Equal(Hash, ReleaseFiles.ExpectedHash(sums, "PirateChestCounter.exe"));

    [Theory]
    [InlineData(Hash + "  PirateChestCounter.exe.old")]
    [InlineData("nothex  PirateChestCounter.exe")]
    [InlineData("")]
    public void NoHashForOtherOrBrokenLines(string sums) => Assert.Null(ReleaseFiles.ExpectedHash(sums, "PirateChestCounter.exe"));
}
