using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public sealed class ChocoSource(ProcessRunner runner) : PackageSourceBase(runner)
{
    public override SourceKind Kind => SourceKind.Choco;
    public override string DisplayName => "Chocolatey";
    public override bool RequiresAdmin => true;
    protected override string ExecutableName => "choco";

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        // Güncelleme varken choco 2 döndürür (enhanced exit codes).
        var r = Ensure(await RunAsync(ct, "outdated", "-r", "--ignore-unfound"), 0, 2);
        return FromParsed(ChocoOutdatedParser.Parse(r.StdOut));
    }

    /// <summary>
    /// choco toplu güncellemeyi destekler: tek komut, tek UAC.
    /// "--no-progress" verilmez: "Progress: 45% - Saving …" satırları ilerleme çubuğunu besler, günlüğe yazılmaz.
    /// </summary>
    public override IReadOnlyList<UpdateCommand> CreateUpdateJob(IReadOnlyList<PackageUpdate> packages) =>
        packages.Count == 0
            ? []
            : [Command(packages.Select(p => p.Id), ["upgrade", .. packages.Select(p => p.Id), "-y"])];

    protected override string[] UpdateArguments(PackageUpdate p) => ["upgrade", p.Id, "-y"];

    /// <summary>Bu boyutun altındaki paketler yükleyiciyi içermez, kurulumda indirir.</summary>
    private const long EmbeddedInstallerThreshold = 1024 * 1024;

    public override async Task<PackageSize?> GetSizeAsync(PackageUpdate p, CancellationToken ct)
    {
        var length = await Web.GetLengthAsync($"https://community.chocolatey.org/api/v2/package/{Uri.EscapeDataString(p.Id)}/{Uri.EscapeDataString(p.Available)}", ct);
        return length switch
        {
            null => null,
            >= EmbeddedInstallerThreshold => new(length, "Chocolatey paketi (yükleyici içinde)."),
            _ => new(null, "Yükleyiciyi kurulum sırasında indirir; boyutu bilinmiyor."),
        };
    }
}
