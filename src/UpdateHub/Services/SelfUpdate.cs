using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace UpdateHub.Services;

/// <summary>GitHub'daki en son release.</summary>
/// <param name="Version">Etiket ("v" olmadan), ör. 0.1.4.</param>
/// <param name="ZipUrl">*-win-x64.zip dosyasının indirme adresi.</param>
public sealed record SelfRelease(string Version, string ZipUrl);

/// <summary>UpdateHub'ın kendi güncellemesi: GitHub "latest" release'ine bakar, exe'yi açıldığı yerde değiştirir.</summary>
public static class SelfUpdate
{
    private const string LatestUrl = "https://api.github.com/repos/ufukt51/UpdateHub/releases/latest";
    private const string ExeName = "UpdateHub.exe";

    public static string CurrentVersion => typeof(SelfUpdate).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Daha yeni sürüm varsa döndürür; yoksa veya ağ hatasında null.</summary>
    public static async Task<SelfRelease?> CheckAsync(CancellationToken ct)
    {
        var json = await Web.GetStringAsync(LatestUrl, ct);
        return json is not null && Parse(json) is { } r && VersionInfo.Compare(r.Version, CurrentVersion) > 0 ? r : null;
    }

    /// <summary>releases/latest yanıtı; taslak/ön sürüm veya win-x64 zip'i yoksa null.</summary>
    public static SelfRelease? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean() || root.TryGetProperty("prerelease", out var p) && p.GetBoolean())
            return null;
        if (root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') is not { Length: > 0 } version)
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString()?.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) == true
                && asset.GetProperty("browser_download_url").GetString() is { } url)
                return new(version, url);
        }
        return null;
    }

    /// <summary>
    /// Zip'i indirir, çalışan exe'yi .old olarak kenara alır ve yenisini yerine koyar.
    /// Windows çalışan exe'nin yeniden adlandırılmasına izin verir; silinmesine izin vermez.
    /// </summary>
    /// <returns>Yeni exe'nin yolu. Hata olursa eski exe geri konur ve hata fırlatılır.</returns>
    public static async Task<string> InstallAsync(SelfRelease release, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Çalışan exe bulunamadı.");
        var old = exe + ".old";
        var download = Path.Combine(Path.GetTempPath(), $"UpdateHub-{release.Version}-{Guid.NewGuid():N}.exe");

        try
        {
            using (var zip = new ZipArchive(await Web.Http.GetStreamAsync(release.ZipUrl, ct)))
            {
                var entry = zip.GetEntry(ExeName) ?? throw new InvalidDataException($"Zip içinde {ExeName} yok.");
                entry.ExtractToFile(download);
            }

            File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(download, exe);
            }
            catch
            {
                File.Move(old, exe);
                throw;
            }
            return exe;
        }
        finally
        {
            File.Delete(download);
        }
    }

    /// <summary>Önceki güncellemeden kalan .old dosyasını siler. Eski süreç hâlâ kapanıyorsa sonraki açılışa kalır.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                File.Delete(exe + ".old");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Restart(string exe) => Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
}
