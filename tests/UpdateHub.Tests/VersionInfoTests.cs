using UpdateHub.Services;

namespace UpdateHub.Tests;

public class VersionInfoTests
{
    [Theory]
    [InlineData("12.0.2", "12.2.1", false)]
    [InlineData("8.0.5", "10.0.3", true)]
    [InlineData("156.0.4314.42", "156.0.4314.50", false)]   // 4 bileşen
    [InlineData("2.0.20.0", "2.0.22", false)]
    [InlineData("2026.8.14", "2026.10.23", false)]          // CalVer
    [InlineData("2025.12.1", "2026.1.0", true)]
    [InlineData("v0.9.96", "0.9.98", false)]
    [InlineData("0.14.2", "1.0.0", true)]
    [InlineData("Unknown", "0.12.6-1", false)]
    [InlineData("< 1.0", "2.0", false)]
    [InlineData("7.00.0", "7.23.0", false)]
    public void IsMajorChange(string current, string available, bool expected) =>
        VersionInfo.IsMajorChange(current, available).ShouldBe(expected);

    [Theory]
    [InlineData("3.16.0a1", "3.15.2", 1)]
    [InlineData("3.15.3rc1", "3.15.2", 1)]
    [InlineData("3.15.3rc1", "3.15.3", -1)]
    [InlineData("1.2.3", "1.2.3.0", 0)]
    [InlineData("10.0.0-rc.2.25502.107", "10.0.0", -1)]
    public void Compare(string a, string b, int expected) =>
        Math.Sign(VersionInfo.Compare(a, b)!.Value).ShouldBe(expected);

    [Theory]
    [InlineData("Unknown")]
    [InlineData("< 1.0")]
    [InlineData("")]
    [InlineData(null)]
    public void UnparseableReturnsNull(string? text)
    {
        VersionInfo.TryParse(text).ShouldBeNull();
        VersionInfo.Compare(text, "1.0").ShouldBeNull();
    }

    [Fact]
    public void PythonPrereleaseIsPrerelease() =>
        VersionInfo.TryParse("3.16.0a1")!.IsPrerelease.ShouldBeTrue();
}
