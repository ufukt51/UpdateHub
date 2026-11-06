using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public abstract class PackageSourceBase(ProcessRunner runner) : IPackageSource
{
    private string? _executable;

    protected ProcessRunner Runner { get; } = runner;

    public abstract SourceKind Kind { get; }
    public abstract string DisplayName { get; }
    public virtual bool RequiresAdmin => false;
    public virtual int? ElevationRetryExitCode => null;

    /// <summary>PATH'te aranacak çalıştırılabilir ad (uzantısız olabilir).</summary>
    protected abstract string ExecutableName { get; }

    protected string Executable => _executable ??= ExecutableLocator.Find(ExecutableName) ?? ExecutableName;

    public virtual Task<bool> IsAvailableAsync(CancellationToken ct) =>
        Task.FromResult(ExecutableLocator.Find(ExecutableName) is not null);

    public abstract Task<ScanResult> GetOutdatedAsync(CancellationToken ct);

    public virtual IReadOnlyList<UpdateCommand> CreateUpdateJob(IReadOnlyList<PackageUpdate> packages) =>
        [.. packages.Select(p => Command([p.Id], UpdateArguments(p)))];

    public virtual Task<PackageSize?> GetSizeAsync(PackageUpdate package, CancellationToken ct) => Task.FromResult<PackageSize?>(null);

    /// <summary>pip ve uv: PyPI'daki dosya boyutu. Paket başka bir dizinden (ör. git) kurulduysa bulunmayabilir.</summary>
    protected static async Task<PackageSize?> PyPiSizeAsync(PackageUpdate p, CancellationToken ct)
    {
        var json = await Web.GetStringAsync($"https://pypi.org/pypi/{Uri.EscapeDataString(p.Id)}/{Uri.EscapeDataString(p.Available)}/json", ct);
        return json is not null && PackageSizeParser.PyPiSize(json) is { } size
            ? new(size, "Yalnız paketin kendisi; bağımlılıklar dahil değil.")
            : null;
    }

    /// <summary>Tek paket için güncelleme argümanları.</summary>
    protected abstract string[] UpdateArguments(PackageUpdate package);

    protected UpdateCommand Command(IEnumerable<string> ids, string[] args) =>
        new(Kind, Executable, args, [.. ids], RequiresAdmin);

    protected Task<ProcessResult> RunAsync(CancellationToken ct, params string[] args) =>
        Runner.RunAsync(Executable, args, null, ct);

    /// <summary>Çıkış kodu beklenmiyorsa kaynağı hatalı işaretler.</summary>
    protected static ProcessResult Ensure(ProcessResult result, params int[] okCodes)
    {
        if (okCodes.Length == 0 ? result.ExitCode == 0 : okCodes.Contains(result.ExitCode))
            return result;

        var detail = (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr).Trim();
        if (detail.Length > 400)
            detail = detail[..400] + "…";
        throw new SourceScanException($"Çıkış kodu {result.ExitCode}: {detail}");
    }

    protected PackageUpdate ToUpdate(ParsedPackage p) => new()
    {
        Source = Kind,
        Id = p.Id,
        Name = p.Name,
        Current = p.Current,
        Available = p.Available ?? "",
        IsMajor = !p.IsBroken && VersionInfo.IsMajorChange(p.Current, p.Available),
        NeedsAdmin = RequiresAdmin,
        IsBroken = p.IsBroken,
    };

    protected ScanResult FromParsed(IReadOnlyList<ParsedPackage> parsed, string? warning = null) => new(
        [.. parsed.Where(p => p.HasUpdate).Select(ToUpdate)],
        [.. parsed.Select(p => p.Id)],
        warning);
}
