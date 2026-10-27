using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

    /// <summary>Taramada gizlenen paketler.</summary>
    public List<Exclusion> Exclusions { get; set; } = [];
}

/// <summary>Dosyada camelCase yazılır ("ignore", "pin", "skipVersion"); okurken harf duyarsız.</summary>
[JsonConverter(typeof(ExclusionModeConverter))]
public enum ExclusionMode
{
    /// <summary>Paket hiç gösterilmez.</summary>
    Ignore,

    /// <summary>Yeni sürüm desene uymuyorsa gösterilmez.</summary>
    Pin,

    /// <summary>Yalnızca belirtilen sürüm gösterilmez.</summary>
    SkipVersion,
}

public sealed class ExclusionModeConverter() : JsonStringEnumConverter<ExclusionMode>(JsonNamingPolicy.CamelCase);

/// <summary>Tek bir hariç tutma kuralı. Kaynak ve kimlik büyük/küçük harf duyarsızdır.</summary>
public sealed class Exclusion
{
    public SourceKind Source { get; set; }
    public string Id { get; set; } = "";
    public ExclusionMode Mode { get; set; }

    /// <summary><see cref="ExclusionMode.Pin"/> için joker desen (<c>*</c>, <c>?</c>), ör. "8.*".</summary>
    public string? Pattern { get; set; }

    /// <summary><see cref="ExclusionMode.SkipVersion"/> için atlanan sürüm.</summary>
    public string? Version { get; set; }

    public bool IsFor(SourceKind source, string id) =>
        Source == source && string.Equals(Id, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Bu kural, önerilen sürümüyle birlikte paketi gizliyor mu?</summary>
    public bool Hides(PackageUpdate p) => IsFor(p.Source, p.Id) && Mode switch
    {
        ExclusionMode.Ignore => true,
        // Desensiz sabitleme geçersiz sayılır; güncellemeyi gizlemez.
        ExclusionMode.Pin => !string.IsNullOrWhiteSpace(Pattern) && !WildcardMatch(p.Available, Pattern),
        ExclusionMode.SkipVersion => string.Equals(Version, p.Available, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Günlükte gösterilen kısa açıklama.</summary>
    public string Describe() => Mode switch
    {
        ExclusionMode.Ignore => "hariç tutuldu",
        ExclusionMode.Pin => $"{Pattern} sürümüne sabitlendi",
        ExclusionMode.SkipVersion => $"{Version} sürümü atlandı",
        _ => Mode.ToString(),
    };

    internal static bool WildcardMatch(string value, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

/// <summary>Kullanıcı ayarlarını okur ve yazar. Eksik veya bozuk dosya boş ayar sayılır.</summary>
public sealed class ConfigStore(string path)
{
    // Eski dosyalar PascalCase yazıldı; okurken harf duyarsız, yazarken camelCase.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UpdateHub", "config.json");

    public AppConfig Config { get; private set; } = new();

    /// <summary>Son <see cref="Load"/> dosyayı okuyamadıysa nedeni; dosya yoksa null.</summary>
    public string? LoadError { get; private set; }

    public void Load()
    {
        LoadError = null;
        try
        {
            Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? new();
            // Dosyada "null" yazılmışsa koleksiyonlar boş kalsın.
            Config.InstalledUnknown ??= [];
            Config.Exclusions ??= [];
        }
        catch (FileNotFoundException)
        {
            Config = new();
        }
        catch (DirectoryNotFoundException)
        {
            Config = new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadError = ex.Message;
            Config = new();
        }
    }

    /// <summary>Önce geçici dosyaya yazar; yarıda kalan yazma eski ayarı bozmaz.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Okunamayan dosya (ör. elle düzenlemede yazım hatası) ezilmeden önce yedeklenir.
        if (LoadError is not null && File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
            LoadError = null;
        }
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

    // ---------- Hariç tutma ----------

    /// <summary>Paketi gizleyen ilk kural; yoksa null.</summary>
    public Exclusion? FindExclusion(PackageUpdate p) => Config.Exclusions.FirstOrDefault(e => e.Hides(p));

    /// <summary>Paket hariç tutma kurallarına göre gizlenmeli mi?</summary>
    public bool IsExcluded(PackageUpdate p) => FindExclusion(p) is not null;

    /// <summary>Paketin gizlenme nedeni (ör. "8.* sürümüne sabitlendi"); gizlenmiyorsa null.</summary>
    public string? ExclusionReason(PackageUpdate p) => FindExclusion(p)?.Describe();

    /// <summary>Paketi kalıcı olarak gizler; paketin diğer kuralları silinir. Kaydetmek çağırana kalır.</summary>
    public void AddIgnore(PackageUpdate p)
    {
        RemoveExclusions(p.Source, p.Id);
        Config.Exclusions.Add(new() { Source = p.Source, Id = p.Id, Mode = ExclusionMode.Ignore });
    }

    /// <summary>Önerilen sürümü atlar; önceki atlanan sürümün yerine geçer. Kaydetmek çağırana kalır.</summary>
    public void AddSkipVersion(PackageUpdate p)
    {
        Config.Exclusions.RemoveAll(e => e.IsFor(p.Source, p.Id) && e.Mode == ExclusionMode.SkipVersion);
        Config.Exclusions.Add(new() { Source = p.Source, Id = p.Id, Mode = ExclusionMode.SkipVersion, Version = p.Available });
    }

    /// <summary>Paketi joker desene sabitler; önceki sabitlemenin yerine geçer. Kaydetmek çağırana kalır.</summary>
    public void AddPin(PackageUpdate p, string pattern)
    {
        Config.Exclusions.RemoveAll(e => e.IsFor(p.Source, p.Id) && e.Mode == ExclusionMode.Pin);
        Config.Exclusions.Add(new() { Source = p.Source, Id = p.Id, Mode = ExclusionMode.Pin, Pattern = pattern.Trim() });
    }

    /// <summary>Paketin tüm kurallarını siler. Kaydetmek çağırana kalır.</summary>
    /// <returns>Silinen kural sayısı.</returns>
    public int RemoveExclusions(SourceKind source, string id) =>
        Config.Exclusions.RemoveAll(e => e.IsFor(source, id));

    private static string Key(PackageUpdate p) => $"{p.Source}:{p.Id}".ToLowerInvariant();
}
