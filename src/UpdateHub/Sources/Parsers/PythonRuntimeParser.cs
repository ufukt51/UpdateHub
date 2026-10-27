using System.Text.Json;
using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Sources.Parsers;

/// <summary>
/// <c>py list -f json</c> ve <c>py list --online -f json</c> çıktılarını <c>id</c> bazında eşleştirir.
/// Tag eşleştirmesi yanıltıcıdır (pythonembed aynı tag'i kullanır).
/// Id = install tag'i (güncellemede kullanılır), Name = görünen ad.
/// </summary>
public static class PythonRuntimeParser
{
    public static IReadOnlyList<ParsedPackage> Parse(string installedJson, string onlineJson)
    {
        var online = Versions(onlineJson)
            .Where(v => v.Id is not null && VersionInfo.TryParse(v.SortVersion) is not null)
            .ToLookup(v => v.Id!, v => (Version: VersionInfo.TryParse(v.SortVersion)!, Text: v.SortVersion!), StringComparer.OrdinalIgnoreCase);

        var result = new List<ParsedPackage>();
        foreach (var v in Versions(installedJson))
        {
            // Yönetilmeyen (eski yükleyiciyle kurulmuş) sürümler "py install" ile güncellenemez.
            if (v.Id is null || v.Tag is null || v.SortVersion is null || !v.Managed)
                continue;

            string? available = null;
            var installed = VersionInfo.TryParse(v.SortVersion);
            if (installed is not null)
            {
                var latest = online[v.Id]
                    .Where(o => installed.IsPrerelease || !o.Version.IsPrerelease)
                    .OrderByDescending(o => o.Version)
                    .FirstOrDefault();

                // Python'un kendi sürüm yazımı korunur (3.16.0a1).
                if (latest.Version is not null && latest.Version > installed)
                    available = latest.Text;
            }
            result.Add(new ParsedPackage(v.Tag, v.DisplayName ?? v.Id, v.SortVersion, available));
        }
        return result;
    }

    private sealed record Entry(string? Id, string? Tag, string? SortVersion, string? DisplayName, bool Managed);

    private static List<Entry> Versions(string json)
    {
        json = TextUtil.ExtractJson(json, '{');
        if (json.Length == 0)
            return [];

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("versions", out var versions))
            return [];

        return versions.EnumerateArray().Select(e => new Entry(
            Str(e, "id"),
            Str(e, "tag"),
            Str(e, "sort-version"),
            Str(e, "display-name"),
            e.TryGetProperty("source", out _) && !e.TryGetProperty("unmanaged", out _))).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
