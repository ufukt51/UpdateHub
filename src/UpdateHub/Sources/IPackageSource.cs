using UpdateHub.Models;

namespace UpdateHub.Sources;

/// <param name="Updates">Güncellemesi olan paketler.</param>
/// <param name="InstalledIds">Güncel olsun olmasın kurulu olduğu bilinen paketler (çift tespiti için).</param>
/// <param name="Warning">Kaynağa özel uyarı (ör. eksik PowerShell modülü).</param>
public sealed record ScanResult(
    IReadOnlyList<PackageUpdate> Updates,
    IReadOnlyList<string> InstalledIds,
    string? Warning = null);

/// <summary>Bir veya daha fazla paketi güncelleyen tek bir komut.</summary>
public sealed record UpdateCommand(
    SourceKind Source,
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> PackageIds,
    bool NeedsAdmin)
{
    /// <summary>Kullanıcıya gösterilen ve panoya kopyalanan biçim.</summary>
    public string DisplayText
    {
        get
        {
            var exe = Path.GetFileName(FileName);
            if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                exe = exe[..^4];
            return string.Join(' ', Arguments.Select(Quote).Prepend(exe));
        }
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;
}

public sealed class SourceScanException(string message) : Exception(message);

public interface IPackageSource
{
    SourceKind Kind { get; }
    string DisplayName { get; }
    bool RequiresAdmin { get; }

    /// <summary>Kaynak bu makinede kurulu mu?</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    Task<ScanResult> GetOutdatedAsync(CancellationToken ct);

    /// <summary>Verilen paketleri güncelleyecek komutlar. Toplu güncelleme destekleyen kaynaklar tek komut döndürür.</summary>
    IReadOnlyList<UpdateCommand> CreateUpdateJob(IReadOnlyList<PackageUpdate> packages);
}
