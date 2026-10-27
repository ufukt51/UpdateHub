using System.Text.Json;
using NuGet.Versioning;
using UpdateHub.Services;

namespace UpdateHub.Sources.Parsers;

/// <summary>NuGet flat container <c>index.json</c> içinden en yeni sürümü seçer.</summary>
public static class NuGetIndexParser
{
    /// <summary>Kurulu sürüm kararlıysa ön sürümler elenir.</summary>
    public static string? GetLatest(string json, string current)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("versions", out var versions))
            return null;

        var allowPrerelease = VersionInfo.TryParse(current)?.IsPrerelease ?? false;
        NuGetVersion? best = null;
        foreach (var v in versions.EnumerateArray())
        {
            if (!NuGetVersion.TryParse(v.GetString(), out var parsed))
                continue;
            if (parsed.IsPrerelease && !allowPrerelease)
                continue;
            if (best is null || parsed > best)
                best = parsed;
        }
        return best?.ToNormalizedString();
    }
}
