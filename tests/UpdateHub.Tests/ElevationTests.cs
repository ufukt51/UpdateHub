using System.Text;
using System.Text.Json;
using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Services.Elevation;
using UpdateHub.Sources;

namespace UpdateHub.Tests;

public class ElevationTests
{
    private static readonly IReadOnlyList<IPackageSource> Sources = SourceCatalog.CreateAll(new ProcessRunner());

    [Fact]
    public void Validate_AcceptsChocoPackages() =>
        ElevationProtocol.Validate(new([new(SourceKind.Choco, ["python3", "visualstudio2019buildtools", "git.install"])]), Sources)
            .ShouldBeNull();

    [Theory]
    [InlineData(SourceKind.Npm, "npm")]           // yönetici gerektirmeyen kaynak
    [InlineData(SourceKind.Choco, "--force")]     // seçenek enjeksiyonu
    [InlineData(SourceKind.Choco, "a b")]
    [InlineData(SourceKind.Choco, "x&calc")]
    public void Validate_RejectsUnsafeJobs(SourceKind source, string id) =>
        ElevationProtocol.Validate(new([new(source, [id])]), Sources).ShouldNotBeNull();

    [Fact]
    public void Validate_RejectsEmptyJob() => ElevationProtocol.Validate(new([]), Sources).ShouldNotBeNull();

    [Theory]
    [InlineData(@"C:\Temp\UpdateHub\job-0123456789abcdef0123456789abcdef.json", true)]
    [InlineData(@"C:\Temp\UpdateHub\job-x.json", false)]
    [InlineData(@"C:\Windows\System32\evil.json", false)]
    public void JobPath_MustMatchGeneratedName(string path, bool valid) =>
        ElevationProtocol.IsValidJobPath(path).ShouldBe(valid);

    [Fact]
    public void Paths_DeriveFromJobPath()
    {
        var job = ElevationProtocol.NewJobPath();

        ElevationProtocol.IsValidJobPath(job).ShouldBeTrue();
        ElevationProtocol.OutputPath(job).ShouldEndWith(".out.jsonl");
        Path.GetFileNameWithoutExtension(ElevationProtocol.CancelPath(job)).ShouldBe(Path.GetFileNameWithoutExtension(job));
    }

    [Fact]
    public void Event_RoundTripsFirstIndex()
    {
        var ev = new ElevationEvent(ElevationEventType.Exit, 0, ExitCode: 0, DurationMs: 1500);

        var json = JsonSerializer.Serialize(ev, ElevationProtocol.Json);

        json.ShouldContain("\"exit\"", Case.Insensitive);
        JsonSerializer.Deserialize<ElevationEvent>(json, ElevationProtocol.Json).ShouldBe(ev);
    }

    [Fact]
    public void Job_RoundTrips()
    {
        var job = new ElevatedJob([new(SourceKind.Choco, ["python", "python3"])]);

        var back = JsonSerializer.Deserialize<ElevatedJob>(JsonSerializer.Serialize(job, ElevationProtocol.Json), ElevationProtocol.Json)!;

        back.Items.ShouldHaveSingleItem().Source.ShouldBe(SourceKind.Choco);
        back.Items[0].PackageIds.ShouldBe(["python", "python3"]);
    }

    [Fact]
    public void Tail_WaitsForCompleteLinesAndSplitCharacters()
    {
        var tail = new JsonlTail("unused");
        var bytes = Encoding.UTF8.GetBytes("{\"a\":\"ş\"}\r\n{\"b\":");
        var split = Array.IndexOf(bytes, (byte)0xC5) + 1; // "ş" baytlarının ortası

        tail.Feed(bytes.AsSpan(0, split)).ShouldBeEmpty();
        tail.Feed(bytes.AsSpan(split)).ShouldBe(["{\"a\":\"ş\"}"]);
        tail.Feed("1}\n"u8).ShouldBe(["{\"b\":1}"]);
    }

    [Fact]
    public void Tail_ReadsAppendedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"updatehub-test-{Guid.NewGuid():N}.jsonl");
        try
        {
            var tail = new JsonlTail(path);
            tail.ReadNewLines().ShouldBeEmpty(); // dosya henüz yok

            using (var w = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete))
            {
                w.Write("one\ntw"u8);
                w.Flush();
                tail.ReadNewLines().ShouldBe(["one"]);
                w.Write("o\n"u8);
                w.Flush();
                tail.ReadNewLines().ShouldBe(["two"]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)]
    [InlineData(1641, true)]
    [InlineData(1, false)]
    public void ExitCodes_TreatRebootAsSuccess(int code, bool ok) => ExitCodes.IsSuccess(code).ShouldBe(ok);
}
