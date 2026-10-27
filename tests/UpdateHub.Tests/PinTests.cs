using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.ViewModels;

namespace UpdateHub.Tests;

public sealed class PinTests
{
    private static PackageUpdate Pkg(string current, string available) => new()
    {
        Source = SourceKind.DotnetTool, Id = "dotnet-ef", Name = "dotnet-ef", Current = current, Available = available,
    };

    [Theory]
    [InlineData("8.0.5", new[] { "8.*", "8.0.*" })]
    [InlineData("5.1.0.1111", new[] { "5.*", "5.1.*" })]
    [InlineData("1.2", new[] { "1.*" })]
    [InlineData("2026", new[] { "2026.*" })]
    [InlineData("Unknown", new string[0])]
    [InlineData("< 1.0", new string[0])]
    public void Suggest_UsesLeadingNumericParts(string current, string[] expected) =>
        PinDialogModel.Suggest(current).ShouldBe(expected);

    [Fact]
    public void Model_DefaultsToWidestSuggestion_AndPreviewsHiding()
    {
        var m = new PinDialogModel(Pkg("8.0.5", "10.0.1"));

        m.Pattern.ShouldBe("8.*");
        m.IsValid.ShouldBeTrue();
        m.WouldHide.ShouldBeTrue();
        m.PreviewText.ShouldContain("gizlenir");

        m.UseSuggestionCommand.Execute("10.*");
        m.WouldHide.ShouldBeFalse();

        m.Pattern = "  ";
        m.IsValid.ShouldBeFalse();
        m.WouldHide.ShouldBeFalse();
    }

    [Fact]
    public void AddPin_ReplacesEarlierPin_KeepsOtherRules()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "UpdateHubTests", Guid.NewGuid().ToString("N"), "config.json"));
        var p = Pkg("8.0.5", "10.0.1");
        store.AddSkipVersion(p);

        store.AddPin(p, "9.*");
        store.AddPin(p, " 8.* ");

        store.Config.Exclusions.Count(e => e.Mode == ExclusionMode.Pin).ShouldBe(1);
        store.Config.Exclusions.Single(e => e.Mode == ExclusionMode.Pin).Pattern.ShouldBe("8.*");
        store.Config.Exclusions.ShouldContain(e => e.Mode == ExclusionMode.SkipVersion);
        store.IsExcluded(p).ShouldBeTrue();
    }
}
