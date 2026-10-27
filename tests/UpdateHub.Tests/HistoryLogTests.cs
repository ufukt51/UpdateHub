using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Tests;

public sealed class HistoryLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UpdateHubTests", Guid.NewGuid().ToString("N"));
    private string HistoryPath => Path.Combine(_dir, "history.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static HistoryEntry Entry(string id, int? exitCode = 0, bool elevated = false) =>
        new(new DateTimeOffset(2026, 10, 27, 14, 30, 0, TimeSpan.FromHours(3)), SourceKind.Npm, id, "12.0.2", "12.2.1", exitCode, 4200, elevated);

    [Fact]
    public void MissingFile_ReadsEmpty() =>
        new HistoryLog(HistoryPath).ReadAll().ShouldBeEmpty();

    [Fact]
    public void Append_RoundTrips()
    {
        var log = new HistoryLog(HistoryPath);
        var a = Entry("npm");
        var b = Entry("visualstudio2019buildtools", exitCode: 1, elevated: true) with { Source = SourceKind.Choco, From = null };

        log.Append(a);
        log.Append(b);

        new HistoryLog(HistoryPath).ReadAll().ShouldBe([a, b]);
    }

    [Fact]
    public void Append_WritesOneJsonLinePerEntry()
    {
        var log = new HistoryLog(HistoryPath);

        log.Append(Entry("npm"), Entry("pnpm"));

        var lines = File.ReadAllLines(HistoryPath);
        lines.Length.ShouldBe(2);
        lines[0].ShouldStartWith("{\"ts\":");
        lines[0].ShouldContain("\"source\":\"Npm\"");
        lines[0].ShouldContain("\"exitCode\":0");
        lines[0].ShouldContain("\"durationMs\":4200");
        lines[0].ShouldContain("\"elevated\":false");
        lines[0].ShouldNotContain("isSuccess");
    }

    [Fact]
    public void CorruptLines_AreSkipped()
    {
        var log = new HistoryLog(HistoryPath);
        log.Append(Entry("a"));
        File.AppendAllText(HistoryPath, "{ bozuk\n\n{\"ts\":\"2026-10-27T00:00:00+03:00\"}\nçöp\n{\"id\":\"x\",\"source\":\"Yok\"}\n");
        log.Append(Entry("b"));

        log.ReadAll().Select(e => e.Id).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void ReadRecent_ReturnsNewestFirst()
    {
        var log = new HistoryLog(HistoryPath);
        foreach (var id in new[] { "a", "b", "c", "d" })
            log.Append(Entry(id));

        log.ReadRecent(2).Select(e => e.Id).ShouldBe(["d", "c"]);
        log.ReadRecent(10).Count.ShouldBe(4);
    }

    [Fact]
    public async Task Append_IsThreadSafe()
    {
        var log = new HistoryLog(HistoryPath);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => log.Append(Entry($"p{i}")))));

        log.ReadAll().Select(e => e.Id).Order().ShouldBe(Enumerable.Range(0, 50).Select(i => $"p{i}").Order());
    }

    [Theory]
    [InlineData(0, true, "Başarılı")]
    [InlineData(3010, true, "Başarılı (yeniden başlatma)")]
    [InlineData(1, false, "Hata (kod 1)")]
    [InlineData(null, false, "Hata (kod yok)")]
    public void Result_FollowsExitCode(int? exitCode, bool success, string text)
    {
        var e = Entry("npm", exitCode);

        e.IsSuccess.ShouldBe(success);
        e.ResultText.ShouldBe(text);
    }

    [Fact]
    public void VersionText_ShowsMissingVersionAsQuestionMark() =>
        (Entry("x") with { From = "" }).VersionText.ShouldBe("? → 12.2.1");
}
