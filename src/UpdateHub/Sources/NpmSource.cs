using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public sealed class NpmSource(ProcessRunner runner) : PackageSourceBase(runner)
{
    public override SourceKind Kind => SourceKind.Npm;
    public override string DisplayName => "npm (global)";
    protected override string ExecutableName => "npm.cmd";

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        // Eski paket varsa npm outdated 1 döndürür.
        var r = await RunAsync(ct, "outdated", "-g", "--json");
        if (r.ExitCode is not (0 or 1) || (r.ExitCode == 1 && string.IsNullOrWhiteSpace(r.StdOut)))
            Ensure(r);
        return FromParsed(NpmOutdatedParser.Parse(r.StdOut));
    }

    protected override string[] UpdateArguments(PackageUpdate p) => ["install", "-g", $"{p.Id}@latest"];

    public override async Task<PackageSize?> GetSizeAsync(PackageUpdate p, CancellationToken ct)
    {
        // Kapsamlı adlarda (@scope/ad) eğik çizgi kodlanır.
        var json = await Web.GetStringAsync($"https://registry.npmjs.org/{p.Id.Replace("/", "%2F")}/{Uri.EscapeDataString(p.Available)}", ct);
        if (json is null || PackageSizeParser.NpmTarball(json) is not { } tarball)
            return null;
        return new(await Web.GetLengthAsync(tarball, ct), "Paket arşivi; bağımlılıklar dahil değil.");
    }
}
