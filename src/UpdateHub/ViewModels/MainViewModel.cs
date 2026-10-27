using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Services.Elevation;
using UpdateHub.Sources;

namespace UpdateHub.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 5000;

    private readonly ProcessRunner _runner;
    private readonly ConfigStore _config;
    private readonly ElevationBroker _broker = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<SourceKind, IReadOnlyList<string>> _installed = [];
    private CancellationTokenSource? _cts;

    public MainViewModel(IEnumerable<IPackageSource> sources, ProcessRunner runner, ConfigStore? config = null)
    {
        _runner = runner;
        _config = config ?? new ConfigStore(ConfigStore.DefaultPath);
        _config.Load();

        var cvs = new CollectionViewSource { Source = Packages };
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PackageUpdate.Group)));
        cvs.SortDescriptions.Add(new SortDescription(nameof(PackageUpdate.Source), ListSortDirection.Ascending));
        cvs.SortDescriptions.Add(new SortDescription(nameof(PackageUpdate.Name), ListSortDirection.Ascending));
        PackagesView = cvs.View;
        PackagesView.Filter = o => o is PackageUpdate p && IsVisible(p);

        Sources = [.. sources.Select(s => new SourceState(s))];
        foreach (var s in Sources)
        {
            s.AllSelectedChanged += (_, value) =>
            {
                foreach (var p in Packages.Where(p => p.Group == s && IsVisible(p)))
                    p.IsSelected = value;
            };
        }
        Tabs = [AllTab, .. Sources];
        SelectedTab = AllTab;

        Packages.CollectionChanged += (_, e) =>
        {
            foreach (PackageUpdate p in e.NewItems ?? Array.Empty<PackageUpdate>())
            {
                p.PropertyChanged += (_, pe) =>
                {
                    if (pe.PropertyName == nameof(PackageUpdate.IsSelected))
                        SelectedCount = Packages.Count(x => x.IsSelected);
                };
            }
            AllTab.Count = Packages.Count;
            SelectedCount = Packages.Count(x => x.IsSelected);
        };
    }

    public ObservableCollection<SourceState> Sources { get; }
    public ObservableCollection<PackageUpdate> Packages { get; } = [];
    public ICollectionView PackagesView { get; }
    public ObservableCollection<string> LogLines { get; } = [];

    public AllSourcesTab AllTab { get; } = new();
    public IReadOnlyList<ISourceTab> Tabs { get; }

    /// <summary>Seçili sekme tabloyu o kaynağa süzer; "Tümü" hepsini gösterir.</summary>
    [ObservableProperty]
    public partial ISourceTab? SelectedTab { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateSelectedCommand))]
    public partial int SelectedCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(UpdateSelectedCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Hazır.";

    partial void OnSearchTextChanged(string value) => PackagesView.Refresh();

    partial void OnSelectedTabChanged(ISourceTab? value) => PackagesView.Refresh();

    private bool IsVisible(PackageUpdate p)
    {
        if (SelectedTab is SourceState s && p.Group != s)
            return false;
        var q = SearchText.Trim();
        return q.Length == 0
            || p.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Tarama ----------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusMessage = "Taranıyor…";
        Packages.Clear();
        _installed.Clear();

        try
        {
            await Task.WhenAll(Sources.Select(s => ScanSourceAsync(s, _cts.Token)));
            var errors = Sources.Count(s => s.State == SourceScanState.Error);
            StatusMessage = $"{Packages.Count} güncelleme bulundu." + (errors > 0 ? $" {errors} kaynakta hata." : "");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanSourceAsync(SourceState s, CancellationToken ct)
    {
        s.State = SourceScanState.Scanning;
        s.Error = null;
        s.Warning = null;
        s.UpdateCount = 0;
        s.IsAllSelected = false;

        try
        {
            if (!await s.Source.IsAvailableAsync(ct))
            {
                s.State = SourceScanState.NotInstalled;
                return;
            }

            // Tarama arka planda; devamı arayüz iş parçacığında.
            var result = await Task.Run(() => s.Source.GetOutdatedAsync(ct), ct);
            var hidden = 0;
            foreach (var u in result.Updates)
            {
                // Sürümü bilinmeyen ve önerilen sürümü zaten UpdateHub'ın kurduğu paketler gösterilmez.
                if (_config.IsAlreadyInstalled(u))
                {
                    hidden++;
                    continue;
                }
                u.Group = s;
                Packages.Add(u);
            }
            if (hidden > 0)
                Log($"[{s.DisplayName}] {hidden} paket gizlendi: kurulu sürümü bilinmiyor, önerilen sürüm daha önce UpdateHub ile kuruldu.");
            _installed[s.Kind] = result.InstalledIds;
            s.Warning = result.Warning;
            s.UpdateCount = result.Updates.Count - hidden;
            s.State = SourceScanState.Done;

            DuplicateDetector.Assign(Packages, _installed.SelectMany(kv => kv.Value.Select(id => (kv.Key, id))));
        }
        catch (OperationCanceledException)
        {
            s.State = SourceScanState.Idle;
        }
        catch (Exception ex)
        {
            s.Error = ex.Message;
            s.State = SourceScanState.Error;
            Log($"[{s.DisplayName}] tarama hatası: {ex.Message}");
        }
    }

    // ---------- Güncelleme ----------

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateSelectedAsync()
    {
        // Sekme ve arama sadece görünümü süzer; başka sekmede seçilenler de güncellenir (sayı düğmede görünür).
        var selected = Packages.Where(p => p.IsSelected && p.Group is not null).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Güncellenecek paket seçilmedi.";
            return;
        }

        var jobs = selected
            .GroupBy(p => p.Group!)
            .SelectMany(g => g.Key.Source.CreateUpdateJob([.. g]).Select(cmd => (Command: cmd, Packages: g.Where(p => cmd.PackageIds.Contains(p.Id)).ToList())))
            .ToList();

        foreach (var p in selected)
            SetStatus([p], UpdateStatus.Queued, "Sırada");

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var ok = 0;

        try
        {
            // UAC hemen istensin diye yönetici işleri önce, tek seferde çalışır.
            // Uygulama zaten yönetici olarak açıldıysa aracıya gerek yok.
            var adminJobs = jobs.Where(j => j.Command.NeedsAdmin && !Environment.IsPrivilegedProcess).ToList();
            if (adminJobs.Count > 0)
                ok += await RunElevatedAsync(adminJobs, ct);

            // Paket yöneticileri kilit kullandığından işler sırayla çalışır.
            foreach (var (cmd, pkgs) in jobs.Except(adminJobs))
            {
                if (ct.IsCancellationRequested)
                {
                    SetStatus(pkgs, UpdateStatus.Cancelled, "İptal edildi");
                    continue;
                }

                SetStatus(pkgs, UpdateStatus.Running, "Güncelleniyor…");
                StatusMessage = $"Güncelleniyor: {string.Join(", ", cmd.PackageIds)}";
                Log($"> {cmd.DisplayText}");

                try
                {
                    var r = await _runner.RunAsync(cmd.FileName, cmd.Arguments,
                        (line, _) => _dispatcher.BeginInvoke(() => Log(line)), ct);
                    Log($"< çıkış kodu {r.ExitCode} ({r.Duration.TotalSeconds:0.0} sn)");
                    ok += ApplyExitCode(pkgs, r.ExitCode);
                }
                catch (OperationCanceledException)
                {
                    Log("< iptal edildi");
                    SetStatus(pkgs, UpdateStatus.Cancelled, "İptal edildi");
                }
                catch (Exception ex)
                {
                    Log($"< hata: {ex.Message}");
                    SetStatus(pkgs, UpdateStatus.Failed, $"Hata: {ex.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
            StatusMessage = $"{ok}/{selected.Count} paket güncellendi.";
        }
    }

    /// <summary>Yönetici işlerini tek UAC onayıyla çalıştırır.</summary>
    /// <returns>Başarıyla güncellenen paket sayısı.</returns>
    private async Task<int> RunElevatedAsync(List<(UpdateCommand Command, List<PackageUpdate> Packages)> jobs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            foreach (var (_, pkgs) in jobs)
                SetStatus(pkgs, UpdateStatus.Cancelled, "İptal edildi");
            return 0;
        }

        foreach (var (_, pkgs) in jobs)
            SetStatus(pkgs, UpdateStatus.Queued, "Yönetici onayı bekleniyor…");
        StatusMessage = "Yönetici onayı (UAC) bekleniyor…";
        Log("> yönetici olarak çalıştırılıyor (UAC)");

        var job = new ElevatedJob([.. jobs.Select(j => new ElevatedJobItem(j.Command.Source, j.Command.PackageIds))]);
        var finished = new bool[jobs.Count];
        var ok = 0;

        void Handle(ElevationEvent ev)
        {
            var pkgs = ev.Index >= 0 && ev.Index < jobs.Count ? jobs[ev.Index].Packages : null;
            switch (ev.Type)
            {
                case ElevationEventType.Start when pkgs is not null:
                    SetStatus(pkgs, UpdateStatus.Running, "Güncelleniyor…");
                    StatusMessage = $"Güncelleniyor (yönetici): {string.Join(", ", pkgs.Select(p => p.Id))}";
                    Log($"> {ev.Text}");
                    break;
                case ElevationEventType.Line:
                    Log(ev.Text ?? "");
                    break;
                case ElevationEventType.Exit when pkgs is not null:
                    Log($"< çıkış kodu {ev.ExitCode} ({(ev.DurationMs ?? 0) / 1000.0:0.0} sn)");
                    ok += ApplyExitCode(pkgs, ev.ExitCode ?? -1);
                    finished[ev.Index] = true;
                    break;
                case ElevationEventType.Cancelled when pkgs is not null:
                    Log("< iptal edildi");
                    SetStatus(pkgs, UpdateStatus.Cancelled, "İptal edildi");
                    finished[ev.Index] = true;
                    break;
                case ElevationEventType.Error when pkgs is not null:
                    Log($"< hata: {ev.Text}");
                    SetStatus(pkgs, UpdateStatus.Failed, $"Hata: {ev.Text}");
                    finished[ev.Index] = true;
                    break;
                case ElevationEventType.Error:
                    Log($"[yönetici] {ev.Text}");
                    break;
            }
        }

        bool accepted;
        try
        {
            // UAC penceresi yanıtlanana kadar Process.Start bloklar; arayüz donmasın.
            accepted = await Task.Run(() => _broker.RunAsync(job, ev => _dispatcher.Invoke(() => Handle(ev)), ct));
        }
        catch (Exception ex)
        {
            Log($"[yönetici] başlatılamadı: {ex.Message}");
            accepted = true;
        }

        if (!accepted)
        {
            Log("< UAC reddedildi");
            foreach (var (_, pkgs) in jobs)
                SetStatus(pkgs, UpdateStatus.CancelledByUac, "İptal edildi (UAC)");
            return 0;
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            if (!finished[i])
                SetStatus(jobs[i].Packages, UpdateStatus.Failed, "Yönetici işi tamamlanmadı");
        }
        return ok;
    }

    /// <returns>Başarılıysa paket sayısı, değilse 0.</returns>
    private int ApplyExitCode(List<PackageUpdate> pkgs, int exitCode)
    {
        if (!ExitCodes.IsSuccess(exitCode))
        {
            SetStatus(pkgs, UpdateStatus.Failed, $"Hata (çıkış kodu {exitCode})");
            return 0;
        }

        SetStatus(pkgs, UpdateStatus.Succeeded,
            ExitCodes.IsRebootRequired(exitCode) ? "Güncellendi (yeniden başlatma gerekli)" : "Güncellendi");
        foreach (var p in pkgs)
            p.IsSelected = false;
        RememberInstalled(pkgs);
        return pkgs.Count;
    }

    /// <summary>Sürümü bilinmeyen paketlerin kurulan sürümünü saklar; sonraki taramada tekrar listelenmezler.</summary>
    private void RememberInstalled(List<PackageUpdate> pkgs)
    {
        if (pkgs.Count(_config.RememberInstalled) == 0)
            return;
        try
        {
            _config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Ayar dosyası yazılamadı: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    private bool IsIdle() => !IsBusy;

    private bool CanUpdate() => !IsBusy && SelectedCount > 0;

    // ---------- Diğer ----------

    [RelayCommand]
    private void Copy(PackageUpdate? package)
    {
        if (package?.Group is null)
            return;
        var cmd = package.Group.Source.CreateUpdateJob([package]).FirstOrDefault();
        if (cmd is not null)
            Clipboard.SetText(cmd.DisplayText);
    }

    [RelayCommand]
    private async Task InstallWingetModuleAsync()
    {
        var pwsh = ExecutableLocator.Find("pwsh");
        if (pwsh is null)
        {
            Log("PowerShell 7 (pwsh) bulunamadı.");
            return;
        }

        Log($"> pwsh -Command {WingetSource.InstallModuleScript}");
        var r = await _runner.RunAsync(pwsh, ["-NoProfile", "-NonInteractive", "-Command", WingetSource.InstallModuleScript],
            (line, _) => _dispatcher.BeginInvoke(() => Log(line)));
        Log($"< çıkış kodu {r.ExitCode}. Yeniden taradığınızda modül kullanılır.");
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    private static void SetStatus(IEnumerable<PackageUpdate> packages, UpdateStatus status, string text)
    {
        foreach (var p in packages)
        {
            p.Status = status;
            p.StatusText = text;
        }
    }

    private void Log(string line)
    {
        LogLines.Add(line);
        if (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);
    }
}
