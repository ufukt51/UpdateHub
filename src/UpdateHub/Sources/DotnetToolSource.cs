using System.Net.Http;
using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public sealed class DotnetToolSource(ProcessRunner runner, HttpClient http) : PackageSourceBase(runner)
{
    private const string FlatContainer = "https://api.nuget.org/v3-flatcontainer/";

    public override SourceKind Kind => SourceKind.DotnetTool;
    public override string DisplayName => "dotnet tools";
    protected override string ExecutableName => "dotnet";

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        var r = Ensure(await RunAsync(ct, "tool", "list", "-g"));
        var installed = DotnetToolListParser.Parse(r.StdOut);

        var withLatest = await Task.WhenAll(installed.Select(async tool =>
        {
            var json = await http.GetStringAsync($"{FlatContainer}{tool.Id.ToLowerInvariant()}/index.json", ct);
            var latest = NuGetIndexParser.GetLatest(json, tool.Current);
            return VersionInfo.Compare(latest, tool.Current) > 0 ? tool with { Available = latest } : tool;
        }));

        return FromParsed(withLatest);
    }

    protected override string[] UpdateArguments(PackageUpdate p) => ["tool", "update", "-g", p.Id];
}
