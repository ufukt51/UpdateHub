using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

/// <summary>Python Install Manager (<c>py</c>) ile kurulu Python sürümleri.</summary>
public sealed class PythonRuntimeSource(ProcessRunner runner) : PackageSourceBase(runner)
{
    public override SourceKind Kind => SourceKind.PythonRuntime;
    public override string DisplayName => "Python (py)";
    protected override string ExecutableName => "py";

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        var installed = RunAsync(ct, "list", "-f", "json");
        var online = RunAsync(ct, "list", "--online", "-f", "json");
        var i = Ensure(await installed);
        var o = Ensure(await online);
        return FromParsed(PythonRuntimeParser.Parse(i.StdOut, o.StdOut));
    }

    protected override string[] UpdateArguments(PackageUpdate p) => ["install", "-u", p.Id, "-y"];
}
