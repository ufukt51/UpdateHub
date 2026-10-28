using UpdateHub.Models;

namespace UpdateHub.Services;

/// <summary>
/// Güncelleme sonrası yeniden taramayla sürümün gerçekten değiştiğini denetler.
/// Bazı yükleyiciler (ör. Logitech G HUB önyükleyicisi) hiçbir şey kurmadan 0 ile çıkar.
/// </summary>
public static class UpdateVerifier
{
    /// <summary>Kurulu sürümü okunamayan paketler denetlenemez; yeniden taramada hep aynı görünürler.</summary>
    public static bool CanVerify(PackageUpdate p) => !p.IsVersionUnknown;

    /// <summary>Yeniden taramada aynı kurulu sürümle hâlâ listelenen paketler.</summary>
    public static IReadOnlyList<PackageUpdate> Unchanged(IEnumerable<PackageUpdate> updated, IEnumerable<PackageUpdate> rescanned)
    {
        var still = rescanned.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. updated.Where(p => CanVerify(p) && still.Contains(Key(p)))];
    }

    private static string Key(PackageUpdate p) => $"{p.Id}\n{p.Current}";
}
