using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UpdateHub.Models;
using UpdateHub.Sources;

namespace UpdateHub.Services.Elevation;

/// <summary>Yükseltilmiş sürece verilen iş: her öğe bir kaynağın paket listesidir.</summary>
/// <remarks>Komut satırı değil, sadece kaynak ve kimlik taşır; komutu yükseltilmiş süreç kendisi kurar.</remarks>
public sealed record ElevatedJob(IReadOnlyList<ElevatedJobItem> Items);

public sealed record ElevatedJobItem(SourceKind Source, IReadOnlyList<string> PackageIds);

public enum ElevationEventType
{
    Start,
    Line,
    Exit,
    Error,
    Cancelled,
    Done,
}

/// <summary><c>job-*.out.jsonl</c> dosyasındaki bir satır. <see cref="Index"/> iş öğesinin sırasıdır (-1: genel).</summary>
public sealed record ElevationEvent(
    ElevationEventType Type,
    int Index = -1,
    string? Text = null,
    bool IsError = false,
    int? ExitCode = null,
    long? DurationMs = null);

public static partial class ElevationProtocol
{
    public const string Argument = "--elevated-run";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        // WhenWritingDefault olmaz: Index = 0 yazılmaz ve okurken -1 olur.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string JobDirectory => Path.Combine(Path.GetTempPath(), "UpdateHub");

    public static string NewJobPath() => Path.Combine(JobDirectory, $"job-{Guid.NewGuid():N}.json");

    public static string OutputPath(string jobPath) => Path.ChangeExtension(jobPath, ".out.jsonl");

    /// <summary>Ana uygulama iptal için bu dosyayı oluşturur (yükseltilmiş süreci doğrudan sonlandıramaz).</summary>
    public static string CancelPath(string jobPath) => Path.ChangeExtension(jobPath, ".cancel");

    public static bool IsValidJobPath(string path) => JobFileName().IsMatch(Path.GetFileName(path));

    /// <summary>
    /// İş dosyası kullanıcının yazabildiği bir klasörde durur. Yönetici olarak çalıştırmadan önce
    /// sadece yönetici gerektiren veya yönetici olarak yeniden denenebilen kaynaklara ve düz paket kimliklerine izin verilir.
    /// </summary>
    /// <returns>Hata mesajı; geçerliyse null.</returns>
    public static string? Validate(ElevatedJob job, IReadOnlyList<IPackageSource> sources)
    {
        if (job.Items is not { Count: > 0 })
            return "İş dosyası boş.";

        foreach (var item in job.Items)
        {
            var source = sources.FirstOrDefault(s => s.Kind == item.Source);
            if (source is null || (!source.RequiresAdmin && source.ElevationRetryExitCode is null))
                return $"{item.Source} kaynağı yönetici olarak çalıştırılamaz.";
            if (item.PackageIds is not { Count: > 0 })
                return $"{item.Source} için paket yok.";
            if (item.PackageIds.FirstOrDefault(id => !PackageId().IsMatch(id)) is { } bad)
                return $"Geçersiz paket kimliği: {bad}";
        }
        return null;
    }

    [GeneratedRegex(@"^job-[0-9a-f]{32}\.json$")]
    private static partial Regex JobFileName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,199}$")]
    private static partial Regex PackageId();
}
