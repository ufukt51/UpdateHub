using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UpdateHub.Models;

namespace UpdateHub.Services;

/// <summary><c>history.jsonl</c> dosyasındaki bir satır: biten tek bir paket güncellemesi.</summary>
/// <param name="ExitCode">Sürecin çıkış kodu. Null: kod alınamadı.</param>
/// <param name="Elevated">Komut yönetici yetkisiyle çalıştı.</param>
/// <param name="Unchanged">Komut başarılı döndü ama yeniden taramada kurulu sürüm değişmemişti.</param>
public sealed record HistoryEntry(
    DateTimeOffset Ts,
    SourceKind Source,
    string Id,
    string? From,
    string? To,
    int? ExitCode,
    long DurationMs,
    bool Elevated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Unchanged = false)
{
    [JsonIgnore]
    public bool IsSuccess => ExitCode is int code && ExitCodes.IsSuccess(code) && !Unchanged;

    // ---------- Geçmiş sekmesi için biçimli alanlar ----------

    /// <summary>Kaynağın görünen adı (ör. "Chocolatey"). Dosyaya yazılmaz; okuyan atar.</summary>
    [JsonIgnore]
    public string? SourceName { get; init; }

    [JsonIgnore]
    public string SourceText => SourceName ?? Source.ToString();

    [JsonIgnore]
    public string TimeText => Ts.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public string VersionText => $"{Show(From)} → {Show(To)}";

    [JsonIgnore]
    public string DurationText => $"{DurationMs / 1000.0:0.0} sn";

    [JsonIgnore]
    public string ResultText => ExitCode switch
    {
        _ when Unchanged => "Sürüm değişmedi",
        null => "Hata (kod yok)",
        int c when ExitCodes.IsRebootRequired(c) => "Başarılı (yeniden başlatma)",
        int c when ExitCodes.IsSuccess(c) => "Başarılı",
        int c => $"Hata (kod {c})",
    };

    /// <summary>Panoya kopyalanan tek satırlık biçim (sekmeyle ayrılmış).</summary>
    public string ToLine() =>
        string.Join('\t', TimeText, SourceText, Id, VersionText, ResultText, DurationText, Elevated ? "yönetici" : "");

    private static string Show(string? version) => string.IsNullOrWhiteSpace(version) ? "?" : version;
}

/// <summary>
/// Güncelleme geçmişi: her satır bir <see cref="HistoryEntry"/> (JSON Lines).
/// Sadece sona eklenir; bozuk satırlar okurken atlanır.
/// </summary>
public sealed class HistoryLog(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateHub", "history.jsonl");

    public string FilePath { get; } = path;

    /// <summary>Kayıtları dosyanın sonuna ekler. Aynı anda gelen çağrılar sırayla yazar.</summary>
    /// <exception cref="IOException">Dosya yazılamadı; çağıran günlüğe yazar.</exception>
    /// <exception cref="UnauthorizedAccessException">Klasöre erişim yok.</exception>
    public void Append(params IEnumerable<HistoryEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var e in entries)
            text.Append(JsonSerializer.Serialize(e, Json)).Append('\n');
        if (text.Length == 0)
            return;

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(Utf8.GetBytes(text.ToString()));
        }
    }

    /// <summary>Tüm kayıtlar, dosyadaki sırayla (eskiden yeniye). Dosya yoksa boş.</summary>
    public IReadOnlyList<HistoryEntry> ReadAll()
    {
        string[] lines;
        try
        {
            lock (_gate)
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Utf8);
                lines = reader.ReadToEnd().Split('\n');
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        var entries = new List<HistoryEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (Parse(line) is { } e)
                entries.Add(e);
        }
        return entries;
    }

    /// <summary>Son <paramref name="count"/> kayıt, yeniden eskiye.</summary>
    public IReadOnlyList<HistoryEntry> ReadRecent(int count) =>
        [.. ReadAll().TakeLast(count).Reverse()];

    /// <returns>Geçerli kayıt; boş, bozuk veya eksik satırda null.</returns>
    private static HistoryEntry? Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return null;
        try
        {
            var e = JsonSerializer.Deserialize<HistoryEntry>(line, Json);
            return string.IsNullOrWhiteSpace(e?.Id) ? null : e;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
