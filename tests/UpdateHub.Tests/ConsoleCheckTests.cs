using System.Text;
using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources;

namespace UpdateHub.Tests;

public sealed class ConsoleCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UpdateHubTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Komut çalıştırmayan sahte kaynak.</summary>
    private sealed class FakeSource(SourceKind kind, string name, bool available = true, Exception? error = null,
        IReadOnlyList<PackageUpdate>? updates = null) : IPackageSource
    {
        public SourceKind Kind => kind;
        public string DisplayName => name;
        public bool RequiresAdmin => false;

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(available);

        public Task<ScanResult> GetOutdatedAsync(CancellationToken ct) =>
            error is not null ? Task.FromException<ScanResult>(error) : Task.FromResult(new ScanResult(updates ?? [], [.. (updates ?? []).Select(u => u.Id)]));

        public IReadOnlyList<UpdateCommand> CreateUpdateJob(IReadOnlyList<PackageUpdate> packages) => [];
    }

    private static PackageUpdate Pkg(SourceKind source, string id, string current, string available, bool major = false) => new()
    {
        Source = source, Id = id, Name = id, Current = current, Available = available, IsMajor = major,
    };

    private static async Task<(int Code, string Text)> Run(IReadOnlyList<IPackageSource> sources,
        ConfigStore? config = null, Func<PackageUpdate, bool>? exclude = null)
    {
        var output = new StringWriter();
        var code = await ConsoleCheck.RunAsync(sources, output, config, exclude, TestContext.Current.CancellationToken);
        return (code, output.ToString());
    }

    [Fact]
    public async Task NoUpdates_ReturnsZero()
    {
        var (code, text) = await Run([
            new FakeSource(SourceKind.Npm, "npm"),
            new FakeSource(SourceKind.Choco, "Chocolatey", available: false),
        ]);

        code.ShouldBe(ConsoleCheck.NoUpdates);
        text.ShouldContain("npm         güncel");
        text.ShouldContain("Chocolatey  kurulu değil");
        text.ShouldContain("Güncelleme yok.");
    }

    [Fact]
    public async Task Updates_ReturnOne_AndListPackages()
    {
        var (code, text) = await Run([
            new FakeSource(SourceKind.Npm, "npm", updates: [Pkg(SourceKind.Npm, "npm", "12.0.2", "12.2.1")]),
            new FakeSource(SourceKind.DotnetTool, "dotnet tools", updates: [Pkg(SourceKind.DotnetTool, "dotnet-ef", "8.0.5", "10.0.1", major: true)]),
        ]);

        code.ShouldBe(ConsoleCheck.UpdatesFound);
        text.ShouldContain("npm           1 güncelleme");
        text.ShouldContain("  npm  12.0.2 → 12.2.1" + Environment.NewLine);
        text.ShouldContain("  dotnet-ef  8.0.5 → 10.0.1  MAJOR");
        text.ShouldContain("Toplam 2 güncelleme.");
    }

    [Fact]
    public async Task ErrorWithoutUpdates_ReturnsTwo()
    {
        var (code, text) = await Run([
            new FakeSource(SourceKind.Npm, "npm"),
            new FakeSource(SourceKind.Winget, "winget", error: new SourceScanException("winget yanıt vermedi")),
        ]);

        code.ShouldBe(ConsoleCheck.Error);
        text.ShouldContain("winget  hata: winget yanıt vermedi");
        text.ShouldContain("1 kaynakta hata.");
    }

    [Fact]
    public async Task ErrorWithUpdates_ReturnsOne()
    {
        var (code, _) = await Run([
            new FakeSource(SourceKind.Npm, "npm", updates: [Pkg(SourceKind.Npm, "npm", "12.0.2", "12.2.1")]),
            new FakeSource(SourceKind.Winget, "winget", error: new InvalidOperationException("x")),
        ]);

        code.ShouldBe(ConsoleCheck.UpdatesFound);
    }

    [Fact]
    public async Task Exclude_HidesPackages()
    {
        var (code, text) = await Run(
            [new FakeSource(SourceKind.Pip, "pip", updates: [Pkg(SourceKind.Pip, "certifi", "2026.10.1", "2026.10.23")])],
            exclude: p => p.Id == "certifi");

        code.ShouldBe(ConsoleCheck.NoUpdates);
        text.ShouldContain("pip  güncel (1 gizlendi)");
        text.ShouldNotContain("certifi");
    }

    [Fact]
    public async Task AlreadyInstalledUnknownVersion_IsHidden()
    {
        var config = new ConfigStore(Path.Combine(_dir, "config.json"));
        var wk = Pkg(SourceKind.Winget, "wkhtmltopdf.wkhtmltox", "Unknown", "0.12.6-1");
        config.RememberInstalled(wk);

        var (code, text) = await Run([new FakeSource(SourceKind.Winget, "winget", updates: [wk])], config);

        code.ShouldBe(ConsoleCheck.NoUpdates);
        text.ShouldContain("(1 gizlendi)");
    }

    [Fact]
    public async Task AsciiConsole_UsesPlainArrow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream, Encoding.GetEncoding(857));

        await ConsoleCheck.RunAsync([new FakeSource(SourceKind.Npm, "npm", updates: [Pkg(SourceKind.Npm, "npm", "12.0.2", "12.2.1")])],
            writer, ct: TestContext.Current.CancellationToken);

        var text = Encoding.GetEncoding(857).GetString(stream.ToArray());
        text.ShouldContain("12.0.2 -> 12.2.1");
        text.ShouldContain("1 güncelleme");
    }
}
