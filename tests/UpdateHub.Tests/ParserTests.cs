using UpdateHub.Sources.Parsers;

namespace UpdateHub.Tests;

public class ParserTests
{
    [Fact]
    public void Winget_ParsesTableByColumnPositions()
    {
        var result = WingetTableParser.Parse(Fixture.Read("winget-upgrade.txt"));

        result.Count.ShouldBe(6);
        result[0].ShouldBe(new("HP.ConnectionOptimizer", "HP Connection Optimizer", "2.0.20.0", "2.0.22"));
        result.Single(p => p.Id == "Microsoft.msodbcsql.18").Name.ShouldBe("Microsoft ODBC Driver 18 for SQL Server");
        result.Single(p => p.Id == "wkhtmltopdf.wkhtmltox").Current.ShouldBe("Unknown");
    }

    [Fact]
    public void Winget_HandlesLocalizedHeaderSpinnerAndSecondTable()
    {
        const string output =
            "   - \r   \\ \r" +
            "Ad              Kimlik      Sürüm  Kullanılabilir Kaynak\r\n" +
            "-------------------------------------------------------\r\n" +
            "Git             Git.Git     < 2.40 2.51.0         winget\r\n" +
            "1 yükseltme var.\r\n" +
            "\r\n" +
            "Aşağıdaki paketler açık hedefleme gerektirir:\r\n" +
            "Ad   Kimlik  Sürüm  Kullanılabilir Kaynak\r\n" +
            "----------------------------------------\r\n" +
            "Foo  Foo.Bar 1.0    2.0            winget\r\n";

        var result = WingetTableParser.Parse(output);

        result.ShouldHaveSingleItem().ShouldBe(new("Git.Git", "Git", "< 2.40", "2.51.0"));
    }

    [Fact]
    public void Winget_ReturnsEmptyWhenNoTable() =>
        WingetTableParser.Parse("No installed package found matching input criteria.").ShouldBeEmpty();

    [Fact]
    public void WingetJson_ParsesModuleOutput()
    {
        const string output = """
            [{"Name":"Microsoft Edge","Id":"Microsoft.Edge","InstalledVersion":"156.0.4314.42","Available":"156.0.4314.50"},
             {"Name":"Broken","Id":"X.Y","InstalledVersion":"1.0","Available":null}]
            """;

        WingetJsonParser.Parse(output).ShouldHaveSingleItem()
            .ShouldBe(new("Microsoft.Edge", "Microsoft Edge", "156.0.4314.42", "156.0.4314.50"));
    }

    [Fact]
    public void Choco_ParsesPipeSeparatedAndSkipsPinned()
    {
        var result = ChocoOutdatedParser.Parse(Fixture.Read("choco-outdated.txt") + "nodejs|20.0.0|22.0.0|true\r\n");

        result.Count.ShouldBe(5);
        result.Select(p => p.Id).ShouldBe(["python", "python3", "python315", "visualstudio2019buildtools", "visualstudio2026buildtools"]);
        result[0].Current.ShouldBe("3.15.0");
        result[0].Available.ShouldBe("3.15.2");
    }

    [Fact]
    public void Npm_ParsesOutdatedJson()
    {
        var result = NpmOutdatedParser.Parse(Fixture.Read("npm-outdated.json"));

        result.ShouldHaveSingleItem().ShouldBe(new("npm", "npm", "12.0.2", "12.2.1"));
    }

    [Fact]
    public void Npm_EmptyOutputMeansNoUpdates() => NpmOutdatedParser.Parse("").ShouldBeEmpty();

    [Fact]
    public void Uv_ParsesOutdatedAndBrokenTools()
    {
        var result = UvToolListParser.Parse(Fixture.Read("uv-tool-list-outdated.txt"));

        result.Count.ShouldBe(3);
        result.Single(p => p.Id == "graphifyy").ShouldBe(new("graphifyy", "graphifyy", "0.9.96", "0.9.98"));
        result.Single(p => p.Id == "ruff").Available.ShouldBe("1.0.0");
        var broken = result.Single(p => p.Id == "specify-cli");
        broken.IsBroken.ShouldBeTrue();
        broken.HasUpdate.ShouldBeTrue();
    }

    [Fact]
    public void Uv_UpToDateToolsHaveNoAvailableVersion()
    {
        var result = UvToolListParser.Parse(Fixture.Read("uv-tool-list.txt"));

        result.Select(p => p.Id).ShouldBe(["graphifyy", "specify-cli"]);
        result.ShouldAllBe(p => !p.HasUpdate);
    }

    [Fact]
    public void Uv_DetectsInvalidEnvironmentWarning()
    {
        const string output =
            "warning: Invalid environment at `C:\\uv\\tools\\graphifyy`: missing Python executable (run `uv tool install graphifyy --reinstall` to reinstall)\n";

        UvToolListParser.Parse(output).ShouldHaveSingleItem().IsBroken.ShouldBeTrue();
    }

    [Fact]
    public void Pip_ParsesOutdatedJson()
    {
        var result = PipOutdatedParser.Parse(Fixture.Read("pip-outdated.json"));

        result.Select(p => p.Id).ShouldBe(["certifi", "jsonschema"]);
        result[0].Available.ShouldBe("2026.10.23");
        PipOutdatedParser.Parse(Fixture.Read("pip-outdated-empty.json")).ShouldBeEmpty();
    }

    [Fact]
    public void Pip_IgnoresLeadingWarnings() =>
        PipOutdatedParser.Parse("WARNING: something\n[]").ShouldBeEmpty();

    [Fact]
    public void PythonRuntime_UpToDateInstallHasNoUpdate()
    {
        var result = PythonRuntimeParser.Parse(Fixture.Read("py-list.json"), Fixture.Read("py-list-online.json"));

        var py = result.ShouldHaveSingleItem();
        py.Id.ShouldBe("3.15-64");
        py.Name.ShouldBe("Python 3.15.2");
        py.HasUpdate.ShouldBeFalse();
    }

    [Fact]
    public void PythonRuntime_MatchesByIdAndIgnoresPrerelease()
    {
        const string installed = """
            {"versions": [{"id": "pythoncore-3.15-64", "tag": "3.15-64", "sort-version": "3.15.0", "display-name": "Python 3.15.0", "source": "x"},
                          {"id": "pythoncore-3.14-64", "tag": "3.14-64", "sort-version": "3.14.1", "display-name": "Python 3.14.1"}]}
            """;
        const string online = """
            {"versions": [{"id": "pythonembed-3.15-64", "tag": "3.15-64", "sort-version": "3.15.9"},
                          {"id": "pythoncore-3.15-64", "tag": "3.15-64", "sort-version": "3.15.2"},
                          {"id": "pythoncore-3.15-64", "tag": "3.15-64", "sort-version": "3.15.3rc1"}]}
            """;

        var result = PythonRuntimeParser.Parse(installed, online);

        result.ShouldHaveSingleItem().Available.ShouldBe("3.15.2"); // 3.14 yönetilmiyor (source yok)
    }

    [Theory]
    [InlineData("dotnet-tool-list-tr.txt", 3)]
    [InlineData("dotnet-tool-list-en.txt", 1)]
    [InlineData("dotnet-tool-list-empty.txt", 0)]
    public void DotnetTools_ParsesRegardlessOfLanguage(string fixture, int count)
    {
        var result = DotnetToolListParser.Parse(Fixture.Read(fixture));

        result.Count.ShouldBe(count);
        if (count > 0)
            result[0].ShouldBe(new("dotnet-ef", "dotnet-ef", "8.0.5", null));
    }

    [Fact]
    public void DotnetTools_TurkishFixtureHandlesMisalignedRows()
    {
        var result = DotnetToolListParser.Parse(Fixture.Read("dotnet-tool-list-tr.txt"));

        result[1].ShouldBe(new("dotnet-outdated-tool", "dotnet-outdated-tool", "4.6.4", null));
        result[2].Current.ShouldBe("7.5.0-preview.2");
    }

    [Theory]
    [InlineData("8.0.5", "10.0.3")]
    [InlineData("10.0.0-rc.1.25451.107", "10.0.3")]
    public void NuGetIndex_PicksLatestStableUnlessCurrentIsPrerelease(string current, string expected) =>
        NuGetIndexParser.GetLatest(Fixture.Read("nuget-dotnet-ef-index.json"), current).ShouldBe(expected);

    [Fact]
    public void NuGetIndex_AllowsPrereleaseWhenCurrentIsPrerelease() =>
        NuGetIndexParser.GetLatest("""{"versions":["1.0.0","2.0.0-beta.1"]}""", "1.0.0-alpha").ShouldBe("2.0.0-beta.1");
}
