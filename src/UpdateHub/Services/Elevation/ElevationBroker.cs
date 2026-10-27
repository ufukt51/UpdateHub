using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace UpdateHub.Services.Elevation;

/// <summary>
/// Yönetici gerektiren işleri tek UAC onayıyla çalıştırır: aynı exe <c>--elevated-run</c> ile
/// <c>runas</c> olarak başlatılır, çıktısı jsonl dosyasından izlenir.
/// </summary>
public sealed class ElevationBroker
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED: kullanıcı UAC'yi reddetti.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <param name="onEvent">Arka plan iş parçacığında çağrılır.</param>
    /// <returns>UAC reddedildiyse false.</returns>
    public async Task<bool> RunAsync(ElevatedJob job, Action<ElevationEvent> onEvent, CancellationToken ct)
    {
        Directory.CreateDirectory(ElevationProtocol.JobDirectory);
        var jobPath = ElevationProtocol.NewJobPath();
        var outPath = ElevationProtocol.OutputPath(jobPath);
        var cancelPath = ElevationProtocol.CancelPath(jobPath);
        await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job, ElevationProtocol.Json), CancellationToken.None);

        Process? process;
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add(ElevationProtocol.Argument);
            psi.ArgumentList.Add(jobPath);
            // UAC yanıtlanana kadar bloklar.
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            TryDelete(jobPath);
            return false;
        }

        try
        {
            using var signal = new SemaphoreSlim(0);
            using var watcher = new FileSystemWatcher(ElevationProtocol.JobDirectory, Path.GetFileName(outPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler wake = (_, _) => signal.Release();
            watcher.Created += wake;
            watcher.Changed += wake;
            watcher.EnableRaisingEvents = true;

            var tail = new JsonlTail(outPath);
            var cancelSent = false;

            while (true)
            {
                if (ct.IsCancellationRequested && !cancelSent)
                {
                    File.WriteAllText(cancelPath, "");
                    cancelSent = true;
                }

                var exited = HasExited(process);
                if (Drain(tail, onEvent))
                    break;
                if (exited)
                {
                    var code = TryGetExitCode(process);
                    onEvent(new(ElevationEventType.Error, Text: $"Yükseltilmiş süreç beklenmedik şekilde sonlandı (çıkış kodu {code?.ToString() ?? "?"})."));
                    break;
                }

                // İzleyici olay kaçırabilir; zaman aşımı yedek yoklamadır. İptalden sonra da "done" beklenir.
                await signal.WaitAsync(PollInterval, CancellationToken.None);
            }
        }
        finally
        {
            process?.Dispose();
            TryDelete(jobPath);
            TryDelete(outPath);
            TryDelete(cancelPath);
        }
        return true;
    }

    /// <returns>"done" olayı geldiyse true.</returns>
    private static bool Drain(JsonlTail tail, Action<ElevationEvent> onEvent)
    {
        foreach (var line in tail.ReadNewLines())
        {
            ElevationEvent? ev;
            try
            {
                ev = JsonSerializer.Deserialize<ElevationEvent>(line, ElevationProtocol.Json);
            }
            catch (JsonException)
            {
                continue;
            }
            if (ev is null)
                continue;
            if (ev.Type == ElevationEventType.Done)
                return true;
            onEvent(ev);
        }
        return false;
    }

    private static bool HasExited(Process? process)
    {
        try
        {
            return process is null || process.HasExited;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false; // Tanıtıcıya erişim yoksa sadece "done" olayına güvenilir.
        }
    }

    private static int? TryGetExitCode(Process? process)
    {
        try
        {
            return process?.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
