using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Tests;

public sealed class PostUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UpdateHubTests", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static PackageUpdate Pkg(SourceKind source, string id) => new()
    {
        Source = source, Id = id, Name = id, Current = "1.0", Available = "1.1",
    };

    private ConfigStore LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, json);
        var store = new ConfigStore(ConfigPath);
        store.Load();
        return store;
    }

    [Fact]
    public void WithoutSetting_GraphifyRefreshesClaudeSkill()
    {
        var store = LoadJson("""{ "exclusions": [] }""");

        store.PostUpdateCommands(Pkg(SourceKind.Uv, "GraphifyY")).ShouldBe(["graphify install --platform windows"]);
        store.PostUpdateCommands(Pkg(SourceKind.Uv, "specify-cli")).ShouldBeEmpty();
        store.PostUpdateCommands(Pkg(SourceKind.Pip, "graphifyy")).ShouldBeEmpty();
    }

    [Fact]
    public void Setting_ReplacesDefaults()
    {
        var store = LoadJson("""
            { "postUpdate": [
                { "source": "uv", "id": "graphifyy", "commands": ["graphify install --platform windows", "graphify claude install"] } ] }
            """);

        store.LoadError.ShouldBeNull();
        store.PostUpdateCommands(Pkg(SourceKind.Uv, "graphifyy")).Count.ShouldBe(2);
    }

    [Fact]
    public void EmptySetting_RunsNothing()
    {
        var store = LoadJson("""{ "postUpdate": [] }""");

        store.PostUpdateCommands(Pkg(SourceKind.Uv, "graphifyy")).ShouldBeEmpty();
    }

    [Fact]
    public void Save_DoesNotWriteDefaults()
    {
        var store = new ConfigStore(ConfigPath);
        store.Save();

        File.ReadAllText(ConfigPath).ShouldNotContain("postUpdate");
    }

    [Theory]
    [InlineData("graphify install --platform windows", new[] { "graphify", "install", "--platform", "windows" })]
    [InlineData("  a   b ", new[] { "a", "b" })]
    [InlineData("\"C:\\Program Files\\x.exe\" --name \"a b\" \"\"", new[] { "C:\\Program Files\\x.exe", "--name", "a b", "" })]
    [InlineData("", new string[0])]
    public void CommandLine_Splits(string line, string[] expected)
    {
        CommandLine.Split(line).ShouldBe(expected);
    }
}
