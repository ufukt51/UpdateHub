using System.Text;

namespace UpdateHub.Services.Elevation;

/// <summary>Başka süreç tarafından yazılmakta olan dosyadan yalnızca tamamlanmış yeni satırları okur.</summary>
public sealed class JsonlTail(string path)
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _pending = new();
    private long _offset;

    public List<string> ReadNewLines()
    {
        var lines = new List<string>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_offset, SeekOrigin.Begin);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = fs.Read(buffer)) > 0)
            {
                _offset += read;
                lines.AddRange(Feed(buffer.AsSpan(0, read)));
            }
        }
        catch (FileNotFoundException)
        {
            // Yükseltilmiş süreç dosyayı henüz oluşturmadı.
        }
        catch (DirectoryNotFoundException)
        {
        }
        return lines;
    }

    /// <summary>Bayt parçasını ekler; bölünmüş satır ve çok baytlı karakterler sonraki parçayı bekler.</summary>
    internal List<string> Feed(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        _decoder.GetChars(bytes, chars, flush: false);
        _pending.Append(chars);

        var lines = new List<string>();
        var text = _pending.ToString();
        var start = 0;
        int nl;
        while ((nl = text.IndexOf('\n', start)) >= 0)
        {
            var line = text[start..nl].TrimEnd('\r');
            if (line.Length > 0)
                lines.Add(line);
            start = nl + 1;
        }
        _pending.Clear().Append(text, start, text.Length - start);
        return lines;
    }
}
