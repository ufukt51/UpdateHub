namespace UpdateHub.Sources.Parsers;

internal static class TextUtil
{
    /// <summary>
    /// Satırlara böler. Satır içindeki '\r' ilerleme göstergesi kalıntılarını atar
    /// (sadece son '\r' sonrasını tutar).
    /// </summary>
    public static IEnumerable<string> Lines(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var cr = line.LastIndexOf('\r');
            yield return cr >= 0 ? line[(cr + 1)..] : line;
        }
    }

    /// <summary>JSON çıktısından önce gelen uyarı satırlarını atlar.</summary>
    public static string ExtractJson(string text, char open)
    {
        var i = text.IndexOf(open);
        return i < 0 ? "" : text[i..];
    }
}
