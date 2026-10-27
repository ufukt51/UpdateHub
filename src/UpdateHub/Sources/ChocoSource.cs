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

    /// <summary>choco toplu güncellemeyi destekler: tek komut, tek UAC.</summary>
    public override IReadOnlyList<UpdateCommand> CreateUpdateJob(IReadOnlyList<PackageUpdate> packages) =>
        packages.Count == 0
            ? []
            : [Command(packages.Select(p => p.Id), ["upgrade", .. packages.Select(p => p.Id), "-y", "--no-progress"])];

    protected override string[] UpdateArguments(PackageUpdate p) => ["upgrade", p.Id, "-y", "--no-progress"];
}
