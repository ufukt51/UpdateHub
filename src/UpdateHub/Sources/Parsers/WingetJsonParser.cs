using System.Text.Json;
using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary>
/// Microsoft.WinGet.Client modülü çıktısı:
/// <c>Get-WinGetPackage | ? IsUpdateAvailable | Select Name, Id, InstalledVersion, Available | ConvertTo-Json -AsArray</c>.
/// </summary>
public static class WingetJsonParser
{
    public static IReadOnlyList<ParsedPackage> Parse(string output)
    {
        var json = TextUtil.ExtractJson(output, '[');
        if (json.Length == 0)
            return [];

        using var doc = JsonDocument.Parse(json);
        var result = new List<ParsedPackage>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = Str(item, "Id");
            var current = Str(item, "InstalledVersion");
            var available = Str(item, "Available");
            if (id is null || current is null || available is null)
                continue;
            result.Add(new ParsedPackage(id, Str(item, "Name") ?? id, current, available));
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
