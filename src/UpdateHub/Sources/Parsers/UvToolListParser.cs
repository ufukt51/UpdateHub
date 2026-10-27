using System.Text.RegularExpressions;
using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary>
/// <c>uv tool list --outdated</c> çıktısı (stdout + stderr birlikte).
/// Kurulu tüm araçları döndürür; güncel olanlarda <see cref="ParsedPackage.Available"/> null olur.
/// Ortamı bozuk araçlar uyarı satırlarından yakalanır.
/// </summary>
public static partial class UvToolListParser
{
    public const string ReinstallMarker = "yeniden kurulum";

    public static IReadOnlyList<ParsedPackage> Parse(string output)
    {
        var tools = new Dictionary<string, ParsedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in TextUtil.Lines(output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("- ", StringComparison.Ordinal))
                continue;

            if (trimmed.StartsWith("warning:", StringComparison.OrdinalIgnoreCase))
            {
                var broken = BrokenRegex().Match(trimmed);
                if (broken.Success)
                {
                    var name = broken.Groups["name"].Value;
                    tools[name] = new ParsedPackage(name, name, "?", ReinstallMarker, IsBroken: true);
                }
                continue;
            }

            var m = ToolRegex().Match(trimmed);
            if (!m.Success || tools.ContainsKey(m.Groups["name"].Value))
                continue;

            var id = m.Groups["name"].Value;
            var latest = m.Groups["latest"].Success ? m.Groups["latest"].Value : null;
            tools[id] = new ParsedPackage(id, id, m.Groups["ver"].Value, latest);
        }
        return [.. tools.Values];
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)\s+v(?<ver>\S+)(?:.*?\[latest:\s*v?(?<latest>[^\]\s]+)\])?")]
    private static partial Regex ToolRegex();

    [GeneratedRegex(@"uv tool install (?<name>[^\s`]+) --reinstall|[Tt]ool `(?<name>[^`]+)`")]
    private static partial Regex BrokenRegex();
}
