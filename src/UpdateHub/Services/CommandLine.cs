using System.Text;

namespace UpdateHub.Services;

/// <summary>config.json'daki komut satırlarını program ve argümanlara ayırır.</summary>
public static class CommandLine
{
    /// <summary>Boşlukla ayırır; çift tırnak içindeki boşluklar argümanda kalır.</summary>
    /// <returns>Boş satırda boş dizi.</returns>
    public static string[] Split(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var started = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                    parts.Add(current.ToString());
                current.Clear();
                started = false;
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }
        if (started)
            parts.Add(current.ToString());
        return [.. parts];
    }
}
