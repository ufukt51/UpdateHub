using System.Text.Json;
using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary><c>python -m pip list --outdated --format=json</c> çıktısı.</summary>
public static class PipOutdatedParser
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
            var name = item.GetProperty("name").GetString();
            var version = item.GetProperty("version").GetString();
            var latest = item.GetProperty("latest_version").GetString();
            if (name is null || version is null || latest is null)
                continue;
            result.Add(new ParsedPackage(name, name, version, latest));
        }
        return result;
    }
}
