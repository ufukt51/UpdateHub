using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public sealed class UvSource(ProcessRunner runner) : PackageSourceBase(runner)
{
    public override SourceKind Kind => SourceKind.Uv;
    public override string DisplayName => "uv tools";
    protected override string ExecutableName => "uv";

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        // Bozuk ortam uyarıları stderr'e yazılır; ikisi birlikte ayrıştırılır.
        var r = Ensure(await RunAsync(ct, "tool", "list", "--outdated"));
        return FromParsed(UvToolListParser.Parse(r.Combined));
    }

    protected override string[] UpdateArguments(PackageUpdate p) =>
        p.IsBroken ? ["tool", "install", p.Id, "--reinstall"] : ["tool", "upgrade", p.Id];
}
