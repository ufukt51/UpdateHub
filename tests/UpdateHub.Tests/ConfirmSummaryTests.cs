using UpdateHub.Models;
using UpdateHub.Sources;
using UpdateHub.ViewModels;

namespace UpdateHub.Tests;

public class ConfirmSummaryTests
{
    private static PackageUpdate Pkg(SourceKind source, string id, bool major = false, string? dup = null) => new()
    {
        Source = source, Id = id, Name = id, Current = "1.0", Available = major ? "2.0" : "1.1",
        IsMajor = major, DuplicateGroup = dup,
    };

    private static UpdateCommand Job(SourceKind source, bool admin, params string[] ids) =>
        new(source, "x.exe", [], ids, admin);

    [Fact]
    public void GroupsBySource()
    {
        var selected = new[] { Pkg(SourceKind.Npm, "npm"), Pkg(SourceKind.Winget, "Git.Git"), Pkg(SourceKind.Npm, "eslint") };

        var s = ConfirmSummary.Create(selected, [], isPrivileged: false);

        s.PackageCount.ShouldBe(3);
        s.Groups.Select(g => g.Source).ShouldBe(["Winget", "Npm"]);
        s.Groups[1].Items.Select(i => i.Name).ShouldBe(["eslint", "npm"]);
        s.Groups[1].Items[0].Current.ShouldBe("1.0");
        s.Groups[1].Items[0].Available.ShouldBe("1.1");
    }

    [Fact]
    public void NoWarnings_WhenNothingSpecial()
    {
        var s = ConfirmSummary.Create([Pkg(SourceKind.Npm, "npm")], [Job(SourceKind.Npm, false, "npm")], isPrivileged: false);

        s.MajorWarning.ShouldBeNull();
        s.DuplicateWarning.ShouldBeNull();
        s.UacPrompts.ShouldBe(0);
    }

    [Fact]
    public void Major_IsListed()
    {
        var s = ConfirmSummary.Create([Pkg(SourceKind.Npm, "npm", major: true), Pkg(SourceKind.Npm, "eslint")], [], false);

        s.MajorPackages.ShouldBe(["npm"]);
        s.MajorWarning!.ShouldContain("npm");
        s.Groups[0].Items.Single(i => i.Name == "npm").IsMajor.ShouldBeTrue();
    }

    [Fact]
    public void DuplicatePair_BothSelected_Warns()
    {
        var selected = new[]
        {
            Pkg(SourceKind.Choco, "git", dup: "git"),
            Pkg(SourceKind.Winget, "Git.Git", dup: "git"),
            Pkg(SourceKind.Choco, "nodejs-lts", dup: "nodejs"),
        };

        var s = ConfirmSummary.Create(selected, [], false);

        s.DuplicateSets.Count.ShouldBe(1);
        s.DuplicateSets[0].Select(p => p.Id).ShouldBe(["Git.Git", "git"]);
        s.DuplicateWarning!.ShouldContain("Winget, Choco");
        s.Groups.SelectMany(g => g.Items).Where(i => i.IsDuplicate).Select(i => i.Name).ShouldBe(["Git.Git", "git"], ignoreOrder: true);
    }

    [Fact]
    public void DuplicateAlone_DoesNotWarn()
    {
        var s = ConfirmSummary.Create([Pkg(SourceKind.Choco, "git", dup: "git")], [], false);

        s.DuplicateSets.ShouldBeEmpty();
        s.DuplicateWarning.ShouldBeNull();
        s.Groups[0].Items[0].IsDuplicate.ShouldBeFalse();
    }

    [Fact]
    public void DuplicatesFromSameSource_DoNotWarn()
    {
        var s = ConfirmSummary.Create(
            [Pkg(SourceKind.Choco, "python", dup: "python"), Pkg(SourceKind.Choco, "python3", dup: "python")], [], false);

        s.DuplicateWarning.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public void UacPrompts_AtMostOne(bool anyAdmin, bool privileged, int expected)
    {
        var jobs = new[]
        {
            Job(SourceKind.Choco, anyAdmin, "git"),
            Job(SourceKind.Winget, anyAdmin, "Git.Git"),
            Job(SourceKind.Npm, false, "npm"),
        };
        var selected = new[] { Pkg(SourceKind.Choco, "git"), Pkg(SourceKind.Winget, "Git.Git"), Pkg(SourceKind.Npm, "npm") };

        var s = ConfirmSummary.Create(selected, jobs, privileged);

        s.UacPrompts.ShouldBe(expected);
        s.NeedsUac.ShouldBe(expected > 0);
    }
}
