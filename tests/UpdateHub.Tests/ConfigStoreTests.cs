using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UpdateHubTests", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static PackageUpdate Wkhtmltox(string available) => new()
    {
        Source = SourceKind.Winget, Id = "wkhtmltopdf.wkhtmltox", Name = "wkhtmltox",
        Current = "Unknown", Available = available,
    };

    [Fact]
    public void RememberedVersion_IsHiddenAfterReload()
    {
        var store = new ConfigStore(ConfigPath);
        store.RememberInstalled(Wkhtmltox("0.12.6-1")).ShouldBeTrue();
        store.Save();

        var reloaded = new ConfigStore(ConfigPath);
        reloaded.Load();
        reloaded.IsAlreadyInstalled(Wkhtmltox("0.12.6-1")).ShouldBeTrue();
    }

    [Fact]
    public void NewerVersion_IsShownAgain()
    {
        var store = new ConfigStore(ConfigPath);
        store.RememberInstalled(Wkhtmltox("0.12.6-1"));

        store.IsAlreadyInstalled(Wkhtmltox("0.12.7")).ShouldBeFalse();
    }

    [Fact]
    public void KnownVersion_IsNeverRemembered()
    {
        var store = new ConfigStore(ConfigPath);
        var git = new PackageUpdate { Source = SourceKind.Winget, Id = "Git.Git", Name = "Git", Current = "2.53.0", Available = "2.53.1" };

        store.RememberInstalled(git).ShouldBeFalse();
        store.IsAlreadyInstalled(git).ShouldBeFalse();
    }

    [Fact]
    public void CorruptFile_LoadsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, "{ bozuk");
        var store = new ConfigStore(ConfigPath);

        store.Load();

        store.Config.InstalledUnknown.ShouldBeEmpty();
    }
}
