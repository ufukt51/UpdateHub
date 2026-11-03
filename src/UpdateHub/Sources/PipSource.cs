using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public sealed class PipSource(ProcessRunner runner) : PackageSourceBase(runner)
{
    public const string GlobalEnvironmentWarning = "Global ortam; bağımlılıklar bozulabilir.";

    public override SourceKind Kind => SourceKind.Pip;
    public override string DisplayName => "pip (global)";
    protected override string ExecutableName => "python";

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        var r = Ensure(await RunAsync(ct, "-m", "pip", "list", "--outdated", "--format=json", "--disable-pip-version-check"));
        return FromParsed(PipOutdatedParser.Parse(r.StdOut), GlobalEnvironmentWarning);
    }

    protected override string[] UpdateArguments(PackageUpdate p) =>
        ["-m", "pip", "install", "-U", p.Id, "--disable-pip-version-check"];

    public override Task<PackageSize?> GetSizeAsync(PackageUpdate p, CancellationToken ct) => PyPiSizeAsync(p, ct);

    /// <summary><c>pip check</c>: kurulu paketler arasındaki uyumsuzluklar. Sorun yoksa boş.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(CancellationToken ct)
    {
        var r = Ensure(await RunAsync(ct, "-m", "pip", "check", "--disable-pip-version-check"), 0, 1);
        return ParseCheck(r.ExitCode, r.StdOut);
    }

    /// <summary>pip check sorun bulursa 1 ile çıkar ve her sorunu stdout'ta bir satıra yazar.</summary>
    public static IReadOnlyList<string> ParseCheck(int exitCode, string stdout) => exitCode == 0 ? [] :
        [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Paketleri güncelleme öncesi sürümlerine döndüren tek komut.</summary>
    public UpdateCommand RollbackCommand(IReadOnlyList<PackageUpdate> packages) => Command(packages.Select(p => p.Id),
        ["-m", "pip", "install", .. packages.Select(p => $"{p.Id}=={p.Current}"), "--disable-pip-version-check"]);
}
