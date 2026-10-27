using UpdateHub.Models;
using UpdateHub.Sources;

namespace UpdateHub.ViewModels;

/// <summary>Onay penceresindeki tek paket satırı.</summary>
public sealed record ConfirmItem(string Name, string Current, string Available, bool IsMajor, bool IsDuplicate, bool NeedsAdmin);

/// <summary>Onay penceresinde bir kaynağın paketleri.</summary>
public sealed record ConfirmGroup(string Source, IReadOnlyList<ConfirmItem> Items);

/// <summary>Güncelleme öncesi özet: kaynaklara göre paketler, MAJOR ve ÇİFT uyarıları, UAC sayısı.</summary>
public sealed class ConfirmSummary
{
    private ConfirmSummary(IReadOnlyList<ConfirmGroup> groups, IReadOnlyList<string> majors,
        IReadOnlyList<IReadOnlyList<PackageUpdate>> duplicates, int uacPrompts, int packageCount)
    {
        Groups = groups;
        MajorPackages = majors;
        DuplicateSets = duplicates;
        UacPrompts = uacPrompts;
        PackageCount = packageCount;
    }

    public IReadOnlyList<ConfirmGroup> Groups { get; }

    /// <summary>Ana sürümü değişen paketlerin adları.</summary>
    public IReadOnlyList<string> MajorPackages { get; }

    /// <summary>Aynı yazılımın birden çok kaynaktan birlikte seçilen kopyaları.</summary>
    public IReadOnlyList<IReadOnlyList<PackageUpdate>> DuplicateSets { get; }

    /// <summary>İstenecek UAC onayı sayısı. Yönetici işleri tek seferde çalıştığından 0 veya 1.</summary>
    public int UacPrompts { get; }

    public int PackageCount { get; }

    public string Title => $"{PackageCount} paket güncellenecek";

    public string Subtitle => Groups.Count == 1 ? Groups[0].Source : $"{Groups.Count} kaynak";

    public string? MajorWarning => MajorPackages.Count == 0 ? null
        : $"Ana sürüm değişiyor, uyumsuzluk olabilir: {string.Join(", ", MajorPackages)}.";

    public string? DuplicateWarning => DuplicateSets.Count == 0 ? null
        : "Aynı yazılım birden çok kaynaktan seçildi: "
          + string.Join("; ", DuplicateSets.Select(set =>
              $"{set[0].Name} ({string.Join(", ", set.Select(p => SourceName(p)))})"))
          + ". Birini bırakmanız önerilir.";

    public string UacText => UacPrompts == 0
        ? "Yönetici onayı (UAC) istenmeyecek."
        : "Bir kez yönetici onayı (UAC) istenecek.";

    public bool NeedsUac => UacPrompts > 0;

    /// <param name="selected">Güncellenecek paketler.</param>
    /// <param name="jobs">Bu paketler için oluşturulan komutlar.</param>
    /// <param name="isPrivileged">Uygulama zaten yönetici olarak mı çalışıyor?</param>
    public static ConfirmSummary Create(IReadOnlyList<PackageUpdate> selected, IEnumerable<UpdateCommand> jobs, bool isPrivileged)
    {
        var duplicates = selected
            .Where(p => p.DuplicateGroup is not null)
            .GroupBy(p => p.DuplicateGroup!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<PackageUpdate>)[.. g.OrderBy(p => p.Source)])
            .ToList();
        var duplicateSet = duplicates.SelectMany(d => d).ToHashSet();

        var groups = selected
            .GroupBy(p => p.Source)
            .OrderBy(g => g.Key)
            .Select(g => new ConfirmGroup(SourceName(g.First()),
                [.. g.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Select(p => new ConfirmItem(p.DisplayName, p.Current, p.Available, p.IsMajor, duplicateSet.Contains(p), p.NeedsAdmin))]))
            .ToList();

        var majors = selected.Where(p => p.IsMajor).Select(p => p.DisplayName).ToList();

        // Aracı tüm yönetici işlerini tek UAC onayıyla çalıştırır.
        var uac = !isPrivileged && jobs.Any(j => j.NeedsAdmin) ? 1 : 0;

        return new ConfirmSummary(groups, majors, duplicates, uac, selected.Count);
    }

    private static string SourceName(PackageUpdate p) => p.Group?.DisplayName ?? p.Source.ToString();
}
