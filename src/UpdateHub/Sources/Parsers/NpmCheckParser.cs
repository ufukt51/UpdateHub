using System.Text.Json;
using System.Text.RegularExpressions;

namespace UpdateHub.Sources.Parsers;

public static partial class NpmCheckParser
{
    /// <summary><c>npm ls -g --all --json</c> çıktısının <c>problems</c> dizisi.</summary>
    public static IReadOnlyList<string> ParseProblems(string output)
    {
        var json = TextUtil.ExtractJson(output, '{');
        if (json.Length == 0)
            return [];

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array
            ? [.. problems.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!)]
            : [];
    }

    /// <summary>
    /// <c>npm install</c>'ın EBADENGINE uyarıları. Sürüm aralığını npm değerlendirir;
    /// <c>npm ls</c> bu sorunu göstermez.
    /// </summary>
    public static IReadOnlyList<string> ParseEngineWarnings(string stderr) =>
        [.. EngineWarning().Matches(EnginePrefix().Replace(stderr, " "))
            .Select(m => $"{m.Groups["pkg"].Value}: gereken {Clean(m.Groups["req"].Value)}; kurulu {Clean(m.Groups["cur"].Value)}")];

    private static string Clean(string value) => Whitespace().Replace(value, " ").Replace("'", "").Trim();

    [GeneratedRegex("npm warn EBADENGINE")]
    private static partial Regex EnginePrefix();

    // Uzun aralıklarda npm nesneyi birkaç satıra böler; önek silindiği için \s ve . bunları kapsar.
    [GeneratedRegex(@"package: '(?<pkg>[^']+)',\s*required: \{(?<req>.*?)\},\s*current: \{(?<cur>.*?)\}", RegexOptions.Singleline)]
    private static partial Regex EngineWarning();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
