using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Tests;

public sealed class ExclusionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UpdateHubTests", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static PackageUpdate Pkg(SourceKind source, string id, string available) => new()
    {
        Source = source, Id = id, Name = id, Current = "1.0", Available = available,
    };

    private ConfigStore LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, json);
        var store = new ConfigStore(ConfigPath);
        store.Load();
        return store;
    }

    private const string PlanJson = """
        { "exclusions": [
            { "source": "DotnetTool", "id": "dotnet-ef", "mode": "pin", "pattern": "8.*" },
            { "source": "Pip", "id": "certifi", "mode": "skipVersion", "version": "2026.10.23" },
            { "source": "Choco", "id": "visualstudio2019buildtools", "mode": "ignore" } ] }
        """;

    [Fact]
    public void PlanExample_Loads()
    {
        var store = LoadJson(PlanJson);

        store.LoadError.ShouldBeNull();
        store.Config.Exclusions.Count.ShouldBe(3);
        store.Config.Exclusions[0].Mode.ShouldBe(ExclusionMode.Pin);
        store.Config.Exclusions[1].Source.ShouldBe(SourceKind.Pip);
        store.Config.Exclusions[2].Mode.ShouldBe(ExclusionMode.Ignore);
    }

    [Fact]
    public void Ignore_HidesEveryVersion()
    {
        var store = LoadJson(PlanJson);

        store.IsExcluded(Pkg(SourceKind.Choco, "visualstudio2019buildtools", "16.11.50")).ShouldBeTrue();
        store.IsExcluded(Pkg(SourceKind.Choco, "visualstudio2019buildtools", "17.0")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("8.0.11", false)]
    [InlineData("8.1", false)]
    [InlineData("9.0.0", true)]
    [InlineData("10.0.0", true)]
    [InlineData("18.0", true)]
    public void Pin_HidesVersionsOutsidePattern(string available, bool hidden)
    {
        var store = LoadJson(PlanJson);

        store.IsExcluded(Pkg(SourceKind.DotnetTool, "dotnet-ef", available)).ShouldBe(hidden);
    }

    [Fact]
    public void Pin_WithoutPattern_HidesNothing()
    {
        var store = LoadJson("""{ "exclusions": [ { "source": "Npm", "id": "npm", "mode": "pin" } ] }""");

        store.IsExcluded(Pkg(SourceKind.Npm, "npm", "11.0.0")).ShouldBeFalse();
    }

    [Fact]
    public void SkipVersion_HidesOnlyThatVersion()
    {
        var store = LoadJson(PlanJson);

        store.IsExcluded(Pkg(SourceKind.Pip, "certifi", "2026.10.23")).ShouldBeTrue();
        store.IsExcluded(Pkg(SourceKind.Pip, "certifi", "2026.11.1")).ShouldBeFalse();
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        var store = LoadJson("""
            { "Exclusions": [
                { "Source": "choco", "Id": "VisualStudio2019BuildTools", "Mode": "IGNORE" },
                { "source": "winget", "id": "Git.Git", "mode": "skipversion", "version": "2.53.1-RC" } ] }
            """);

        store.LoadError.ShouldBeNull();
        store.IsExcluded(Pkg(SourceKind.Choco, "visualstudio2019buildtools", "1.0")).ShouldBeTrue();
        store.IsExcluded(Pkg(SourceKind.Winget, "git.git", "2.53.1-rc")).ShouldBeTrue();
    }

    [Fact]
    public void OtherSource_IsNotExcluded()
    {
        var store = LoadJson(PlanJson);

        store.IsExcluded(Pkg(SourceKind.Winget, "visualstudio2019buildtools", "1.0")).ShouldBeFalse();
        store.IsExcluded(Pkg(SourceKind.Pip, "requests", "2.0")).ShouldBeFalse();
    }

    [Fact]
    public void Reason_DescribesRule()
    {
        var store = LoadJson(PlanJson);

        store.ExclusionReason(Pkg(SourceKind.DotnetTool, "dotnet-ef", "10.0.0")).ShouldBe("8.* sürümüne sabitlendi");
        store.ExclusionReason(Pkg(SourceKind.Pip, "certifi", "2026.10.23")).ShouldBe("2026.10.23 sürümü atlandı");
        store.ExclusionReason(Pkg(SourceKind.Pip, "requests", "2.0")).ShouldBeNull();
    }

    [Fact]
    public void OldConfig_WithoutExclusions_Loads()
    {
        var store = LoadJson("""
            {
              "InstalledUnknown": {
                "winget:wkhtmltopdf.wkhtmltox": "0.12.6-1"
              }
            }
            """);

        store.LoadError.ShouldBeNull();
        store.Config.Exclusions.ShouldBeEmpty();
        store.Config.InstalledUnknown["winget:wkhtmltopdf.wkhtmltox"].ShouldBe("0.12.6-1");
    }

    [Fact]
    public void AddedRules_SurviveReload_AsStrings()
    {
        var store = new ConfigStore(ConfigPath);
        store.AddIgnore(Pkg(SourceKind.Choco, "git", "2.0"));
        store.AddSkipVersion(Pkg(SourceKind.Pip, "certifi", "2026.10.23"));
        store.Save();

        var json = File.ReadAllText(ConfigPath);
        json.ShouldContain("\"ignore\"");
        json.ShouldContain("\"skipVersion\"");
        json.ShouldContain("\"Choco\"");
        json.ShouldNotContain("\"pattern\"");

        var reloaded = new ConfigStore(ConfigPath);
        reloaded.Load();
        reloaded.IsExcluded(Pkg(SourceKind.Choco, "git", "3.0")).ShouldBeTrue();
        reloaded.IsExcluded(Pkg(SourceKind.Pip, "certifi", "2026.10.23")).ShouldBeTrue();
    }

    [Fact]
    public void SkipVersion_ReplacesPreviousSkip()
    {
        var store = new ConfigStore(ConfigPath);
        store.AddSkipVersion(Pkg(SourceKind.Pip, "certifi", "2026.10.23"));
        store.AddSkipVersion(Pkg(SourceKind.Pip, "Certifi", "2026.11.1"));

        store.Config.Exclusions.Count.ShouldBe(1);
        store.IsExcluded(Pkg(SourceKind.Pip, "certifi", "2026.10.23")).ShouldBeFalse();
        store.IsExcluded(Pkg(SourceKind.Pip, "certifi", "2026.11.1")).ShouldBeTrue();
    }

    [Fact]
    public void RemoveExclusions_ShowsPackageAgain()
    {
        var store = LoadJson(PlanJson);

        store.RemoveExclusions(SourceKind.DotnetTool, "DOTNET-EF").ShouldBe(1);

        store.IsExcluded(Pkg(SourceKind.DotnetTool, "dotnet-ef", "10.0.0")).ShouldBeFalse();
    }

    [Fact]
    public void InvalidFile_IsBackedUpBeforeOverwrite()
    {
        const string broken = """{ "exclusions": [ { "source": "Yok", "id": "x", "mode": "ignore" } ] }""";
        var store = LoadJson(broken);
        store.LoadError.ShouldNotBeNull();

        store.AddIgnore(Pkg(SourceKind.Npm, "npm", "11.0.0"));
        store.Save();

        File.ReadAllText(ConfigPath + ".bak").ShouldBe(broken);
        store.LoadError.ShouldBeNull();
    }

    [Fact]
    public void MissingFile_HasNoLoadError()
    {
        var store = new ConfigStore(ConfigPath);

        store.Load();

        store.LoadError.ShouldBeNull();
        store.Config.Exclusions.ShouldBeEmpty();
    }

    [Fact]
    public void ExcludedPackage_CannotBeSelected()
    {
        var p = Pkg(SourceKind.Choco, "python", "3.15.2");
        p.IsSelected = true;

        p.ExclusionReason = "hariç tutuldu";
        p.IsSelected.ShouldBeFalse();

        p.IsSelected = true;
        p.IsSelected.ShouldBeFalse();

        p.ExclusionReason = null;
        p.IsSelected = true;
        p.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void UpdatedPackage_CannotBeSelected()
    {
        var p = Pkg(SourceKind.Winget, "OpenJS.NodeJS", "27.0.0");
        p.IsSelected = true;

        p.Status = UpdateStatus.Succeeded;
        p.IsSelected.ShouldBeFalse();
        p.IsSelectable.ShouldBeFalse();

        p.IsSelected = true;
        p.IsSelected.ShouldBeFalse();
    }
}
