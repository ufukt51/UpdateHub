using System.Text.Json;
using UpdateHub.Models;

namespace UpdateHub.Services;

/// <summary><c>%APPDATA%\UpdateHub\config.json</c> içeriği.</summary>
public sealed class AppConfig
{
    /// <summary>
    /// Kurulu sürümü bilinmeyen paketlerde UpdateHub'ın en son kurduğu sürüm.
    /// Anahtar: "kaynak:kimlik" (küçük harf).
    /// </summary>
    public Dictionary<string, string> InstalledUnknown { get; set; } = [];
}

/// <summary>Kullanıcı ayarlarını okur ve yazar. Eksik veya bozuk dosya boş ayar sayılır.</summary>
public sealed class ConfigStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UpdateHub", "config.json");

    public AppConfig Config { get; private set; } = new();

    public void Load()
    {
        try
        {
            Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Config = new();
        }
    }

    /// <summary>Önce geçici dosyaya yazar; yarıda kalan yazma eski ayarı bozmaz.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Config, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Sürümü bilinmeyen paket, UpdateHub'ın daha önce kurduğu sürümü mü öneriyor?</summary>
    public bool IsAlreadyInstalled(PackageUpdate p) =>
        p.IsVersionUnknown
        && Config.InstalledUnknown.TryGetValue(Key(p), out var version)
        && string.Equals(version, p.Available, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sürümü bilinmeyen paketin kurulan sürümünü hatırlar. Kaydetmek çağırana kalır.</summary>
    /// <returns>Kayıt değiştiyse true.</returns>
    public bool RememberInstalled(PackageUpdate p)
    {
        if (!p.IsVersionUnknown)
            return false;
        Config.InstalledUnknown[Key(p)] = p.Available;
        return true;
    }

    private static string Key(PackageUpdate p) => $"{p.Source}:{p.Id}".ToLowerInvariant();
}
