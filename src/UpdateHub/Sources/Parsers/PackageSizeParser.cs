using System.Text.Json;
using System.Text.RegularExpressions;

namespace UpdateHub.Sources.Parsers;

/// <summary>Paket boyutu sorgularının yanıtlarını ayrıştırır.</summary>
public static partial class PackageSizeParser
{
    /// <summary>"winget show" çıktısındaki ilk "Installer Url".</summary>
    public static string? WingetInstallerUrl(string output) =>
        InstallerUrlRegex().Match(output) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>npm kayıt defterinin sürüm JSON'undaki arşiv adresi (dist.tarball).</summary>
    public static string? NpmTarball(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("dist", out var dist)
                && dist.TryGetProperty("tarball", out var tarball)
                && tarball.ValueKind == JsonValueKind.String
                ? tarball.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// PyPI sürüm JSON'undan bu makineye kurulacak dosyanın boyutu:
    /// önce win_amd64 tekerleği, sonra platformdan bağımsız tekerlek, yoksa kaynak arşivi.
    /// </summary>
    public static long? PyPiSize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Array)
                return null;

            var files = urls.EnumerateArray()
                .Select(u => (
                    Name: u.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "",
                    Size: u.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0))
                .Where(f => f.Size > 0)
                .ToList();

            return Pick(files, f => f.Name.EndsWith("win_amd64.whl", StringComparison.OrdinalIgnoreCase))
                ?? Pick(files, f => f.Name.EndsWith("-none-any.whl", StringComparison.OrdinalIgnoreCase))
                ?? Pick(files, f => f.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? Pick(List<(string Name, long Size)> files, Func<(string Name, long Size), bool> match) =>
        files.Where(match).Select(f => (long?)f.Size).FirstOrDefault();

    [GeneratedRegex(@"^\s*Installer Url:\s*(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex InstallerUrlRegex();
}
