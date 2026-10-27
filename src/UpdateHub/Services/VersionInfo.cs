using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace UpdateHub.Services;

public static partial class VersionInfo
{
    /// <summary>
    /// Sürümü ayrıştırır. "v1.2", 4 bileşenli, CalVer ve Python tarzı ("3.16.0a1") desteklenir.
    /// "Unknown", "&lt; 1.0" gibi değerler için null döner.
    /// </summary>
    public static NuGetVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var s = text.Trim();
        if (s.Length > 1 && s[0] is 'v' or 'V' && char.IsDigit(s[1]))
            s = s[1..];

        if (NuGetVersion.TryParse(s, out var v))
            return v;

        // Python/PEP 440 ön sürümleri: 3.16.0a1 → 3.16.0-a.1
        var m = Pep440Regex().Match(s);
        if (m.Success && NuGetVersion.TryParse($"{m.Groups[1].Value}-{m.Groups[2].Value}.{(m.Groups[3].Success ? m.Groups[3].Value : "0")}", out v))
            return v;

        return null;
    }

    /// <summary>İki sürümü karşılaştırır. Biri ayrıştırılamazsa null.</summary>
    public static int? Compare(string? a, string? b)
    {
        var va = TryParse(a);
        var vb = TryParse(b);
        return va is null || vb is null ? null : va.CompareTo(vb);
    }

    /// <summary>İlk bileşen farklıysa true. Ayrıştırılamayan sürümler için false.</summary>
    public static bool IsMajorChange(string? current, string? available)
    {
        var a = TryParse(current);
        var b = TryParse(available);
        return a is not null && b is not null && a.Major != b.Major;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+){0,3})[-._]?(a|b|rc|alpha|beta|dev|pre|preview)\.?(\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pep440Regex();
}
