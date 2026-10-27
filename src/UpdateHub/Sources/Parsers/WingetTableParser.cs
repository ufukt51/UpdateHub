using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary>
/// <c>winget upgrade</c> tablosunu ayrıştırır. Başlık metnine güvenmez (yerelleştirilmiş olabilir);
/// sütun konumlarını <c>---</c> satırının üstündeki başlıkta kelime başlangıçlarından alır.
/// </summary>
public static class WingetTableParser
{
    public static IReadOnlyList<ParsedPackage> Parse(string output)
    {
        var lines = TextUtil.Lines(output).ToList();
        var dashIndex = lines.FindIndex(l => l.Trim().Length >= 10 && l.Trim().All(c => c == '-'));
        if (dashIndex < 1)
            return [];

        var cols = ColumnStarts(lines[dashIndex - 1]);
        if (cols.Count < 4)
            return [];

        var result = new List<ParsedPackage>();
        for (var i = dashIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.Length <= cols[3])
                break; // tablo bitti: boş satır veya "N upgrades available." özeti

            var name = Cut(line, cols, 0);
            var id = Cut(line, cols, 1);
            var current = Cut(line, cols, 2);
            var available = Cut(line, cols, 3);

            if (id.Length == 0 || id.Contains(' ') || available.Length == 0)
                continue;

            result.Add(new ParsedPackage(id, name.TrimEnd('…'), current, available));
        }
        return result;
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
                starts.Add(i);
        }
        return starts;
    }

    private static string Cut(string line, List<int> cols, int index)
    {
        var start = cols[index];
        if (start >= line.Length)
            return "";
        var end = index + 1 < cols.Count ? Math.Min(cols[index + 1], line.Length) : line.Length;
        return line[start..end].Trim();
    }
}
