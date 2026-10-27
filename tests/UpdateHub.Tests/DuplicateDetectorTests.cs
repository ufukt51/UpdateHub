using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Tests;

public class DuplicateDetectorTests
{
    private static PackageUpdate Pkg(SourceKind source, string id) => new()
    {
        Source = source,
        Id = id,
        Name = id,
        Current = "1.0",
        Available = "2.0",
    };

    [Fact]
    public void ChocoPythonMatchesInstalledPythonRuntime()
    {
        var updates = new[] { Pkg(SourceKind.Choco, "python"), Pkg(SourceKind.Choco, "python3"), Pkg(SourceKind.Choco, "python315") };

        DuplicateDetector.Assign(updates, [(SourceKind.PythonRuntime, "3.15-64")]);

        updates.ShouldAllBe(u => u.DuplicateGroup == "python");
        updates[0].DuplicateInfo!.ShouldContain("PythonRuntime (3.15-64)");
    }

    [Fact]
    public void SameSourceOnlyIsNotDuplicate()
    {
        var updates = new[] { Pkg(SourceKind.Choco, "python"), Pkg(SourceKind.Choco, "python3") };

        DuplicateDetector.Assign(updates, []);

        updates.ShouldAllBe(u => !u.IsDuplicate);
    }

    [Theory]
    [InlineData("nodejs-lts", "OpenJS.NodeJS.LTS")]
    [InlineData("git", "Git.Git")]
    [InlineData("powershell-core", "Microsoft.PowerShell")]
    [InlineData("winrar", "RARLab.WinRAR")]          // ad normalizasyonu
    [InlineData("7zip.install", "7zip.7zip")]
    public void ChocoAndWingetPairs(string chocoId, string wingetId)
    {
        var updates = new[] { Pkg(SourceKind.Choco, chocoId), Pkg(SourceKind.Winget, wingetId) };

        DuplicateDetector.Assign(updates, []);

        updates.ShouldAllBe(u => u.IsDuplicate);
        updates[0].DuplicateGroup.ShouldBe(updates[1].DuplicateGroup);
    }

    [Fact]
    public void LibrarySourcesAreNotNameMatched()
    {
        var updates = new[] { Pkg(SourceKind.Npm, "typescript"), Pkg(SourceKind.Choco, "typescript") };

        DuplicateDetector.Assign(updates, []);

        updates.ShouldAllBe(u => !u.IsDuplicate);
    }
}
