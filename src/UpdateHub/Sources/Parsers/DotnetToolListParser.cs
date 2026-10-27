using UpdateHub.Models;

namespace UpdateHub.Sources.Parsers;

/// <summary>
/// <c>dotnet tool list -g</c> tablosu. Başlık yerelleştirilmiş olabilir ("Paket Kimliği"),
/// bu yüzden <c>---</c> satırından sonrası boşluklara göre bölünür.
/// </summary>
public static class DotnetToolListParser
{
    public static IReadOnlyList<ParsedPackage> Parse(string output)
    {
        var result = new List<ParsedPackage>();
        var inBody = false;
        foreach (var line in TextUtil.Lines(output))
        {
            var trimmed = line.Trim();
            if (!inBody)
            {
                inBody = trimmed.Length >= 5 && trimmed.All(c => c == '-');
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            result.Add(new ParsedPackage(parts[0], parts[0], parts[1], null));
        }
        return result;
    }
}
