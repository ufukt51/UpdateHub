using System.Text.Json;
using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary><c>npm outdated -g --json</c> çıktısı: ad → { current, latest }.</summary>
public static class NpmOutdatedParser
{
    public static IReadOnlyList<ParsedPackage> Parse(string output)
    {
        var json = TextUtil.ExtractJson(output, '{');
        if (json.Length == 0)
            return [];

        using var doc = JsonDocument.Parse(json);
        var result = new List<ParsedPackage>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            // Aynı paket birden fazla konumdaysa npm dizi döndürür; ilkini al.
            var info = prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0
                ? prop.Value[0]
                : prop.Value;
            if (info.ValueKind != JsonValueKind.Object)
                continue;

            var current = GetString(info, "current");
            var latest = GetString(info, "latest");
            if (current is null || latest is null || current == latest)
                continue;

            result.Add(new ParsedPackage(prop.Name, prop.Name, current, latest));
        }
        return result;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
