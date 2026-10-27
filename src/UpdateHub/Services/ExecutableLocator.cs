namespace UpdateHub.Services;

/// <summary>PATH ve PATHEXT üzerinden çalıştırılabilir dosyayı bulur (<c>where</c> eşdeğeri).</summary>
public static class ExecutableLocator
{
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat", ".com"];

    public static string? Find(string name)
    {
        if (Path.IsPathRooted(name))
            return File.Exists(name) ? name : null;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = Path.HasExtension(name) ? [name] : Extensions.Select(e => name + e).ToArray();

        foreach (var dir in dirs)
        {
            foreach (var file in candidates)
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), file);
                    if (File.Exists(full))
                        return full;
                }
                catch (ArgumentException)
                {
                    // PATH'te geçersiz karakterli dizin olabilir.
                }
            }
        }
        return null;
    }
}
