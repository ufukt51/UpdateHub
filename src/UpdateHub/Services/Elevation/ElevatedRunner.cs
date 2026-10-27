using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UpdateHub.Models;
using UpdateHub.Sources;

namespace UpdateHub.Services.Elevation;

/// <summary><c>--elevated-run &lt;job.json&gt;</c> modu: yönetici olarak çalışır, olayları jsonl dosyasına yazar.</summary>
public sealed class ElevatedRunner(IReadOnlyList<IPackageSource> sources, ProcessRunner runner)
{
    /// <returns>0: hepsi başarılı, 1: en az biri başarısız, 2: geçersiz iş.</returns>
    public async Task<int> RunAsync(string jobPath)
    {
        if (!ElevationProtocol.IsValidJobPath(jobPath) || !File.Exists(jobPath))
            return 2;

        ElevatedJob? job;
        try
        {
            job = JsonSerializer.Deserialize<ElevatedJob>(File.ReadAllText(jobPath), ElevationProtocol.Json);
        }
        catch (JsonException)
        {
            job = null;
        }

        using var writer = new EventWriter(ElevationProtocol.OutputPath(jobPath));
        var error = job is null ? "İş dosyası okunamadı." : ElevationProtocol.Validate(job, sources);
        if (error is not null)
        {
            writer.Write(new(ElevationEventType.Error, Text: error));
            writer.Write(new(ElevationEventType.Done));
            return 2;
        }

        using var cts = new CancellationTokenSource();
        using var cancelWatcher = WatchCancel(ElevationProtocol.CancelPath(jobPath), cts);
        var failed = false;

        for (var i = 0; i < job!.Items.Count; i++)
        {
            if (cts.IsCancellationRequested)
            {
                writer.Write(new(ElevationEventType.Cancelled, i));
                continue;
            }

            var item = job.Items[i];
            var source = sources.First(s => s.Kind == item.Source);
            var packages = item.PackageIds
                .Select(id => new PackageUpdate { Source = item.Source, Id = id, Name = id, Current = "", Available = "" })
                .ToList();
            var index = i;
            var exitCode = 0;
            var sw = Stopwatch.StartNew();

            try
            {
                foreach (var cmd in source.CreateUpdateJob(packages))
                {
                    writer.Write(new(ElevationEventType.Start, index, cmd.DisplayText));
                    var r = await runner.RunAsync(cmd.FileName, cmd.Arguments,
                        (line, isError) => writer.Write(new(ElevationEventType.Line, index, line, isError)), cts.Token);
                    if (exitCode == 0)
                        exitCode = r.ExitCode;
                }
                writer.Write(new(ElevationEventType.Exit, index, ExitCode: exitCode, DurationMs: sw.ElapsedMilliseconds));
                failed |= !ExitCodes.IsSuccess(exitCode);
            }
            catch (OperationCanceledException)
            {
                writer.Write(new(ElevationEventType.Cancelled, index));
            }
            catch (Exception ex)
            {
                writer.Write(new(ElevationEventType.Error, index, ex.Message));
                failed = true;
            }
        }

        writer.Write(new(ElevationEventType.Done));
        return failed ? 1 : 0;
    }

    private static FileSystemWatcher WatchCancel(string cancelPath, CancellationTokenSource cts)
    {
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(cancelPath)!, Path.GetFileName(cancelPath));
        watcher.Created += (_, _) => cts.Cancel();
        watcher.EnableRaisingEvents = true;
        // İzleyici kurulmadan önce oluşturulmuş olabilir.
        if (File.Exists(cancelPath))
            cts.Cancel();
        return watcher;
    }

    /// <summary>stdout ve stderr aynı anda yazabildiği için kilitli, her satırda boşaltılan yazıcı.</summary>
    private sealed class EventWriter(string path) : IDisposable
    {
        private readonly StreamWriter _writer = new(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete),
            new UTF8Encoding(false)) { AutoFlush = true };

        public void Write(ElevationEvent ev)
        {
            var json = JsonSerializer.Serialize(ev, ElevationProtocol.Json);
            lock (_writer)
                _writer.Write(json + "\n");
        }

        public void Dispose() => _writer.Dispose();
    }
}
