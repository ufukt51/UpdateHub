using System.Text.RegularExpressions;
using UpdateHub.Models;

namespace UpdateHub.Services;

/// <summary>
/// Aynı yazılımın birden fazla kaynaktan kurulu olduğunu tespit eder.
/// Önce bilinen eşleşmelere bakar, sonra (sadece winget/choco için) normalize edilmiş adları karşılaştırır.
/// </summary>
public static partial class DuplicateDetector
{
    private static readonly (SourceKind Source, Regex Pattern, string Key)[] Known =
    [
        (SourceKind.Choco, new(@"^python(3\d*)?$", RegexOptions.IgnoreCase), "python"),
        (SourceKind.Winget, new(@"^Python\.Python\.", RegexOptions.IgnoreCase), "python"),
        (SourceKind.PythonRuntime, new(@".*"), "python"),
        (SourceKind.Choco, new(@"^nodejs(-lts)?(\.install)?$", RegexOptions.IgnoreCase), "nodejs"),
        (SourceKind.Winget, new(@"^OpenJS\.NodeJS", RegexOptions.IgnoreCase), "nodejs"),
        (SourceKind.Choco, new(@"^git(\.install|\.portable)?$", RegexOptions.IgnoreCase), "git"),
        (SourceKind.Winget, new(@"^Git\.Git$", RegexOptions.IgnoreCase), "git"),
        (SourceKind.Choco, new(@"^(powershell-core|pwsh)$", RegexOptions.IgnoreCase), "powershell"),
        (SourceKind.Winget, new(@"^Microsoft\.PowerShell$", RegexOptions.IgnoreCase), "powershell"),
        (SourceKind.DotnetTool, new(@"^powershell$", RegexOptions.IgnoreCase), "powershell"),
        (SourceKind.Choco, new(@"^vscode(\.install)?$", RegexOptions.IgnoreCase), "vscode"),
        (SourceKind.Winget, new(@"^Microsoft\.VisualStudioCode$", RegexOptions.IgnoreCase), "vscode"),
        (SourceKind.Choco, new(@"^uv$", RegexOptions.IgnoreCase), "uv"),
        (SourceKind.Winget, new(@"^astral-sh\.uv$", RegexOptions.IgnoreCase), "uv"),
        (SourceKind.Pip, new(@"^uv$", RegexOptions.IgnoreCase), "uv"),
        (SourceKind.Choco, new(@"^7zip(\.install)?$", RegexOptions.IgnoreCase), "7zip"),
        (SourceKind.Winget, new(@"^7zip\.7zip$", RegexOptions.IgnoreCase), "7zip"),
    ];

    /// <summary>Eşleştirme anahtarı. Eşleşme aranmayacaksa null.</summary>
    public static string? Key(SourceKind source, string id)
    {
        foreach (var (s, pattern, key) in Known)
        {
            if (s == source && pattern.IsMatch(id))
                return key;
        }

        // Ad normalizasyonu sadece uygulama düzeyi kaynaklar için: kütüphane adları (npm/pip) yanlış eşleşir.
        return source switch
        {
            SourceKind.Winget => Normalize(id[(id.LastIndexOf('.') + 1)..]),
            SourceKind.Choco => Normalize(SuffixRegex().Replace(id, "")),
            _ => null,
        };
    }

    /// <summary>
    /// Güncellemelere <see cref="PackageUpdate.DuplicateGroup"/> ve <see cref="PackageUpdate.DuplicateInfo"/> atar.
    /// <paramref name="installed"/>: güncel olsa bile kurulu olduğu bilinen paketler (ör. py list).
    /// </summary>
    public static void Assign(IReadOnlyCollection<PackageUpdate> updates, IEnumerable<(SourceKind Source, string Id)> installed)
    {
        var all = updates.Select(u => (u.Source, u.Id))
            .Concat(installed)
            .Distinct()
            .Select(x => (x.Source, x.Id, Key: Key(x.Source, x.Id)))
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .ToList();

        var groups = all
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Source).Distinct().Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var u in updates)
        {
            var key = Key(u.Source, u.Id);
            if (key is null || !groups.TryGetValue(key, out var members))
            {
                u.DuplicateGroup = null;
                u.DuplicateInfo = null;
                continue;
            }

            u.DuplicateGroup = key;
            var others = members.Where(m => m.Source != u.Source).Select(m => $"{m.Source} ({m.Id})");
            u.DuplicateInfo = "Başka kaynakta da kurulu: " + string.Join(", ", others);
        }
    }

    private static string Normalize(string s) => NonAlnumRegex().Replace(s.ToLowerInvariant(), "");

    [GeneratedRegex(@"\.(install|portable)$", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixRegex();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlnumRegex();
}
