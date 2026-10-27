using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary><c>choco outdated -r</c> çıktısı: <c>ad|mevcut|yeni|pinned</c>. Sabitlenmiş paketler atlanır.</summary>
public static class ChocoOutdatedParser
{
    public static IReadOnlyList<ParsedPackage> Parse(string output)
    {
        var result = new List<ParsedPackage>();
        foreach (var line in TextUtil.Lines(output))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 4 || parts[0].Length == 0)
                continue;
            if (bool.TryParse(parts[3], out var pinned) && pinned)
                continue;
            if (string.Equals(parts[1], parts[2], StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new ParsedPackage(parts[0], parts[0], parts[1], parts[2]));
        }
        return result;
    }
}
