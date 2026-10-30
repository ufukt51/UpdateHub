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
}
