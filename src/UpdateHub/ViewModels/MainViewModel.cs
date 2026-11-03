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
using UpdateHub.Views;

namespace UpdateHub.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 5000;
    private const int MaxHistory = 500;

    private readonly ProcessRunner _runner;
    private readonly ConfigStore _config;
    private readonly HistoryLog _history;
    private readonly AutoStart _autoStart;
    private readonly ElevationBroker _broker = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<SourceKind, IReadOnlyList<string>> _installed = [];

    /// <summary>Doğrulama bitene kadar dosyaya yazılmayan geçmiş kayıtları.</summary>
    private readonly List<HistoryEntry> _pendingHistory = [];

    /// <summary>Şu an güncellenen paketler; ilerleme çubuğu bunlardan hesaplanır.</summary>
    private List<PackageUpdate> _batch = [];
    private CancellationTokenSource? _cts;

    /// <summary>Çalışan işin ilerlemesi ve paketleri; iş yokken null.</summary>
    private InstallProgress? _job;
    private List<PackageUpdate> _jobPackages = [];
    private SourceKind _jobSource;
    private CancellationTokenSource? _meterCts;
    private CancellationTokenSource? _sizeCts;

    public MainViewModel(IEnumerable<IPackageSource> sources, ProcessRunner runner, ConfigStore? config = null, HistoryLog? history = null,
        AutoStart? autoStart = null)
    {
        _runner = runner;
        _autoStart = autoStart ?? new AutoStart();
        if (_autoStart.Command is { } registered && !_autoStart.PointsTo(Environment.ProcessPath ?? ""))
            Log($"Windows açılışında başka bir exe başlatılıyor: {registered}. Bu exe'yi kaydetmek için kutuyu kapatıp yeniden açın.");
        _config = config ?? new ConfigStore(ConfigStore.DefaultPath);
        _config.Load();
        if (_config.LoadError is not null)
            Log($"Ayar dosyası okunamadı, varsayılanlar kullanılıyor (ilk kayıtta .bak yedeği alınır): {_config.LoadError}");
        _history = history ?? new HistoryLog(HistoryLog.DefaultPath);

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
                foreach (var p in Packages.Where(p => p.Group == s && p.IsSelectable && IsVisible(p)))
                    p.IsSelected = value;
            };
        }
        Tabs = [AllTab, .. Sources];
        SelectedTab = AllTab;
        // Kaynak adları için Sources hazır olmalı.
        LoadHistory();

        Packages.CollectionChanged += (_, e) =>
        {
            foreach (PackageUpdate p in e.NewItems ?? Array.Empty<PackageUpdate>())
            {
                p.PropertyChanged += (_, pe) =>
                {
                    if (pe.PropertyName is nameof(PackageUpdate.IsSelected) or nameof(PackageUpdate.ExclusionReason))
                        UpdateCounts();
                    else if (pe.PropertyName is nameof(PackageUpdate.Status))
                        RefreshProgress();
                };
            }
            UpdateCounts();
        };
    }

    /// <summary>Hariç tutulanlar güncelleme sayılarına girmez.</summary>
    private void UpdateCounts()
    {
        AllTab.Count = Packages.Count(x => !x.IsExcluded);
        ExcludedCount = Packages.Count(x => x.IsExcluded);
        SelectedCount = Packages.Count(x => x.IsSelected && x.IsSelectable);
    }

    public ObservableCollection<SourceState> Sources { get; }
    public ObservableCollection<PackageUpdate> Packages { get; } = [];
    public ICollectionView PackagesView { get; }
    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>Geçmiş sekmesi: yeniden eskiye, en çok <see cref="MaxHistory"/> kayıt.</summary>
    public ObservableCollection<HistoryEntry> History { get; } = [];

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

    /// <summary>Hariç tutma kuralıyla gizlenen paketler de tabloda gösterilsin mi?</summary>
    [ObservableProperty]
    public partial bool ShowExcluded { get; set; }

    [ObservableProperty]
    public partial int ExcludedCount { get; set; }

    /// <summary>
    /// İlerleme çubuğu: taramada biten kaynak, güncellemede biten paket sayısı
    /// artı çalışan işin indirme/kurulum adımına göre kesri.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double ProgressMax { get; set; }

    /// <summary>Güncelleme sürüyor; çubuk kırmızıdan yeşile dolar, yüzde ve indirme bilgisi görünür.</summary>
    [ObservableProperty]
    public partial bool IsUpdating { get; set; }

    /// <summary>Çalışan işin adımı (ör. "İndiriliyor: 12,3 MB / 30,5 MB (%40) · 18,2 MB kaldı").</summary>
    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    /// <summary>Toplam ilerleme, ör. "%62".</summary>
    [ObservableProperty]
    public partial string? ProgressPercent { get; set; }

    partial void OnSearchTextChanged(string value) => PackagesView.Refresh();

    partial void OnSelectedTabChanged(ISourceTab? value) => PackagesView.Refresh();

    partial void OnShowExcludedChanged(bool value) => PackagesView.Refresh();

    private bool IsVisible(PackageUpdate p)
    {
        if (p.IsExcluded && !ShowExcluded)
            return false;
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
        ProgressValue = 0;
        ProgressMax = Sources.Count;
        _sizeCts?.Cancel();
        _sizeCts = new CancellationTokenSource();

        try
        {
            await Task.WhenAll(Sources.Select(s => ScanSourceAsync(s, _cts.Token)));
            var errors = Sources.Count(s => s.State == SourceScanState.Error);
            StatusMessage = $"{AllTab.Count} güncelleme bulundu."
                + (ExcludedCount > 0 ? $" {ExcludedCount} hariç tutuldu." : "")
                + (errors > 0 ? $" {errors} kaynakta hata." : "");
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
            var excluded = new List<string>();
            foreach (var u in result.Updates)
            {
                // Sürümü bilinmeyen ve önerilen sürümü zaten UpdateHub'ın kurduğu paketler gösterilmez.
                if (_config.IsAlreadyInstalled(u))
                {
                    hidden++;
                    continue;
                }
                // config.json'daki hariç tutma kuralları (ignore, pin, skipVersion).
                // Paket listede kalır ama "Hariç tutulanları göster" açılana kadar görünmez.
                u.ExclusionReason = _config.ExclusionReason(u);
                if (u.ExclusionReason is { } reason)
                    excluded.Add($"{u.Id} {u.Available} ({reason})");
                u.Group = s;
                Packages.Add(u);
            }
            if (hidden > 0)
                Log($"[{s.DisplayName}] {hidden} paket gizlendi: kurulu sürümü bilinmiyor, önerilen sürüm daha önce UpdateHub ile kuruldu.");
            if (excluded.Count > 0)
                Log($"[{s.DisplayName}] {excluded.Count} paket hariç tutma kuralıyla gizlendi: {string.Join(", ", excluded)}. Görmek için \"Hariç tutulanları göster\".");
            _installed[s.Kind] = result.InstalledIds;
            s.Warning = result.Warning;
            s.UpdateCount = result.Updates.Count - hidden - excluded.Count;
            s.State = SourceScanState.Done;

            DuplicateDetector.Assign(Packages, _installed.SelectMany(kv => kv.Value.Select(id => (kv.Key, id))));
            _ = LoadSizesAsync(s, [.. Packages.Where(p => p.Group == s && !p.IsExcluded)], _sizeCts!.Token);
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
        finally
        {
            ProgressValue++;
        }
    }

    /// <summary>
    /// Boyutları tarama bittikten sonra arka planda getirir; tarama bunları beklemez.
    /// winget paket başına bir "winget show" çalıştırdığından aynı anda en çok <see cref="SizeGate"/> sorgu yapılır.
    /// </summary>
    private async Task LoadSizesAsync(SourceState s, List<PackageUpdate> packages, CancellationToken ct)
    {
        foreach (var p in packages)
            p.IsSizeLoading = true;

        await Task.WhenAll(packages.Select(async p =>
        {
            try
            {
                await SizeGate.WaitAsync(ct);
                try
                {
                    var size = await Task.Run(() => s.Source.GetSizeAsync(p, ct), ct);
                    p.Size = size?.Bytes;
                    p.SizeNote = size?.Note ?? "Kaynak boyut bildirmiyor.";
                }
                finally
                {
                    SizeGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                p.SizeNote = $"Boyut alınamadı: {ex.Message}";
            }
            finally
            {
                p.IsSizeLoading = false;
            }
        }));
    }

    private static readonly SemaphoreSlim SizeGate = new(3);

    // ---------- Güncelleme ----------

    /// <summary>Güncellemeden önce onay ister. Testler pencere açmamak için değiştirir.</summary>
    public Func<ConfirmSummary, bool> ConfirmUpdate { get; set; } = ConfirmUpdateWindow.Ask;

    /// <summary>pip güncellemesi uyumsuzluk getirdiğinde sorar; true ise güncelleme korunur, false ise geri alınır.</summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<PackageUpdate>, bool> ConfirmPipConflicts { get; set; } = PipConflictWindow.Ask;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateSelectedAsync()
    {
        // Sekme ve arama sadece görünümü süzer; başka sekmede seçilenler de güncellenir (sayı düğmede görünür).
        var selected = Packages.Where(p => p.IsSelected && p.IsSelectable && p.Group is not null).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Güncellenecek paket seçilmedi.";
            return;
        }

        var jobs = selected
            .GroupBy(p => p.Group!)
            .SelectMany(g => g.Key.Source.CreateUpdateJob([.. g]).Select(cmd => (Command: cmd, Packages: g.Where(p => cmd.PackageIds.Contains(p.Id)).ToList())))
            .ToList();

        var summary = ConfirmSummary.Create(selected, jobs.Select(j => j.Command), Environment.IsPrivilegedProcess);
        if (!ConfirmUpdate(summary))
        {
            StatusMessage = "Güncelleme iptal edildi.";
            return;
        }

        foreach (var p in selected)
            SetStatus([p], UpdateStatus.Queued, "Sırada");
        _batch = selected;
        ProgressValue = 0;
        ProgressMax = selected.Count;
        IsUpdating = true;
        RefreshProgress();

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var ok = 0;

        try
        {
            // Güncelleme öncesi pip check: yalnızca güncellemenin getirdiği yeni uyumsuzluklar sorulur.
            var pip = selected.Select(p => p.Group!).FirstOrDefault(g => g.Source is PipSource);
            var pipBefore = pip is null ? null : await PipCheckAsync(pip, ct);

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
                StatusMessage = $"{FinishedCount + 1} / {_batch.Count} paket güncelleniyor: {string.Join(", ", cmd.PackageIds)}";
                Log($"> {cmd.DisplayText}");
                BeginJob(cmd.Source, pkgs);

                try
                {
                    var r = await _runner.RunAsync(cmd.FileName, cmd.Arguments,
                        (line, _) => _dispatcher.BeginInvoke(() => OnJobLine(line)), ct);
                    Log($"< çıkış kodu {r.ExitCode} ({r.Duration.TotalSeconds:0.0} sn)");
                    RecordHistory(pkgs, r.ExitCode, (long)r.Duration.TotalMilliseconds, Environment.IsPrivilegedProcess);
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
                finally
                {
                    EndJob();
                }
            }

            ok -= await VerifyAsync([.. selected.Where(p => p.Status == UpdateStatus.Succeeded)], ct);
            if (pip is not null && pipBefore is not null)
                ok -= await CheckPipConflictsAsync(pip, pipBefore, [.. selected.Where(p => p.Group == pip && p.Status == UpdateStatus.Succeeded)], ct);

            // Yönetici işleri dahil; komutlar normal kullanıcı olarak çalışır.
            await RunPostUpdateAsync(selected.Where(p => p.Status == UpdateStatus.Succeeded), ct);
        }
        finally
        {
            FlushHistory();
            EndJob();
            _batch = [];
            IsUpdating = false;
            ProgressText = ProgressPercent = null;
            IsBusy = false;
            var unchanged = selected.Count(p => p.Status == UpdateStatus.Unchanged);
            StatusMessage = $"{ok}/{selected.Count} paket güncellendi."
                + (unchanged > 0 ? $" {unchanged} pakette sürüm değişmedi." : "");
        }
    }

    /// <summary>
    /// Başarılı görünen paketlerin kaynaklarını yeniden tarar; kurulu sürümü değişmeyenleri işaretler.
    /// Bazı yükleyiciler hiçbir şey kurmadan 0 ile çıkar; çıkış koduna tek başına güvenilmez.
    /// </summary>
    /// <returns>Sürümü değişmeyen paket sayısı.</returns>
    private async Task<int> VerifyAsync(List<PackageUpdate> succeeded, CancellationToken ct)
    {
        var count = 0;
        foreach (var g in succeeded.Where(UpdateVerifier.CanVerify).GroupBy(p => p.Group!))
        {
            if (ct.IsCancellationRequested)
                break;

            var s = g.Key;
            StatusMessage = $"Doğrulanıyor: {s.DisplayName}";
            Log($"[{s.DisplayName}] kurulu sürümler yeniden okunuyor…");
            IReadOnlyList<PackageUpdate> unchanged;
            try
            {
                var result = await Task.Run(() => s.Source.GetOutdatedAsync(ct), ct);
                unchanged = UpdateVerifier.Unchanged(g, result.Updates);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"[{s.DisplayName}] doğrulama taraması başarısız: {ex.Message}");
                continue;
            }

            foreach (var p in unchanged)
            {
                SetStatus([p], UpdateStatus.Unchanged, "Sürüm değişmedi");
                MarkHistoryUnchanged(p);
                Log($"[{s.DisplayName}] {p.Id}: komut başarılı döndü ama kurulu sürüm hâlâ {p.Current}. "
                    + "Yükleyici güncellemeyi yapmamış; programın kendi güncelleyicisini deneyin.");
            }
            count += unchanged.Count;
        }
        return count;
    }

    /// <returns>Uyumsuzluk satırları; pip check çalışmazsa null.</returns>
    private async Task<IReadOnlyList<string>?> PipCheckAsync(SourceState s, CancellationToken ct)
    {
        StatusMessage = "pip check: bağımlılıklar denetleniyor…";
        Log("> python -m pip check");
        try
        {
            var problems = await Task.Run(() => ((PipSource)s.Source).CheckAsync(ct), ct);
            Log(problems.Count == 0 ? "< uyumsuzluk yok" : $"< {problems.Count} uyumsuzluk");
            return problems;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log($"< pip check başarısız: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// pip güncellemesinden sonra yeni uyumsuzluk çıktıysa kullanıcıya sorar.
    /// Kullanıcı devam etmezse güncellenen pip paketleri eski sürümlerine döner.
    /// </summary>
    /// <returns>Geri alınan paket sayısı.</returns>
    private async Task<int> CheckPipConflictsAsync(SourceState s, IReadOnlyList<string> before, List<PackageUpdate> updated, CancellationToken ct)
    {
        if (updated.Count == 0 || ct.IsCancellationRequested)
            return 0;
        if (await PipCheckAsync(s, ct) is not { } after)
            return 0;

        var conflicts = after.Except(before).ToList();
        if (conflicts.Count == 0)
            return 0;
        foreach (var c in conflicts)
            Log($"[{s.DisplayName}] uyumsuz: {c}");

        if (ConfirmPipConflicts(conflicts, updated))
        {
            Log($"[{s.DisplayName}] uyumsuzluğa rağmen güncelleme korundu.");
            return 0;
        }

        var cmd = ((PipSource)s.Source).RollbackCommand(updated);
        StatusMessage = $"Geri alınıyor: {string.Join(", ", cmd.PackageIds)}";
        Log($"> {cmd.DisplayText}");
        try
        {
            var r = await _runner.RunAsync(cmd.FileName, cmd.Arguments, (l, _) => _dispatcher.BeginInvoke(() => Log(l)), ct);
            Log($"< çıkış kodu {r.ExitCode} ({r.Duration.TotalSeconds:0.0} sn)");
            if (r.ExitCode != 0)
                throw new InvalidOperationException($"çıkış kodu {r.ExitCode}");
        }
        catch (Exception ex)
        {
            Log($"< geri alma başarısız: {ex.Message}");
            foreach (var p in updated)
                p.StatusText = $"{p.StatusText}, geri alma başarısız";
            return 0;
        }

        foreach (var p in updated)
        {
            SetStatus([p], UpdateStatus.Unchanged, $"Geri alındı ({p.Current})");
            MarkHistoryUnchanged(p);
        }
        await PipCheckAsync(s, ct);
        return updated.Count;
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
                    StatusMessage = $"{FinishedCount + 1} / {_batch.Count} paket güncelleniyor (yönetici): {string.Join(", ", pkgs.Select(p => p.Id))}";
                    Log($"> {ev.Text}");
                    BeginJob(jobs[ev.Index].Command.Source, pkgs);
                    break;
                case ElevationEventType.Line:
                    OnJobLine(ev.Text ?? "");
                    break;
                case ElevationEventType.Exit when pkgs is not null:
                    EndJob();
                    Log($"< çıkış kodu {ev.ExitCode} ({(ev.DurationMs ?? 0) / 1000.0:0.0} sn)");
                    RecordHistory(pkgs, ev.ExitCode, ev.DurationMs ?? 0, elevated: true);
                    ok += ApplyExitCode(pkgs, ev.ExitCode ?? -1);
                    finished[ev.Index] = true;
                    break;
                case ElevationEventType.Cancelled when pkgs is not null:
                    EndJob();
                    Log("< iptal edildi");
                    SetStatus(pkgs, UpdateStatus.Cancelled, "İptal edildi");
                    finished[ev.Index] = true;
                    break;
                case ElevationEventType.Error when pkgs is not null:
                    EndJob();
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

    // ---------- İlerleme ----------

    private int FinishedCount => _batch.Count(x => x.Status is not (UpdateStatus.Queued or UpdateStatus.Running));

    private void BeginJob(SourceKind source, List<PackageUpdate> packages)
    {
        EndJob();
        _job = new InstallProgress();
        _jobPackages = packages;
        _jobSource = source;
        RefreshProgress();
    }

    private void EndJob()
    {
        _meterCts?.Cancel();
        _meterCts = null;
        _job = null;
        _jobPackages = [];
        RefreshProgress();
    }

    /// <summary>İşin çıktı satırı: günlüğe yazılır (choco yüzde satırları hariç) ve ilerlemeyi besler.</summary>
    private void OnJobLine(string line)
    {
        if (!InstallProgress.IsProgressLine(line))
            Log(line);
        if (_job is not { } job || !job.OnLine(line))
            return;
        // winget indirmeyi çıktıya yazmaz; ağ trafiğinden tahmin edilir.
        if (_jobSource == SourceKind.Winget && job.Phase == InstallPhase.Downloading && job.DownloadUrl is { } url && _meterCts is null)
        {
            _meterCts = new CancellationTokenSource();
            _ = MeterDownloadAsync(job, url, _meterCts.Token);
        }
        RefreshProgress();
    }

    private async Task MeterDownloadAsync(InstallProgress job, string url, CancellationToken ct)
    {
        try
        {
            var start = DownloadMeter.BytesReceived();
            var total = await Web.GetLengthAsync(url, ct);
            while (!ct.IsCancellationRequested && _job == job && job.Phase == InstallPhase.Downloading)
            {
                var received = await Task.Run(DownloadMeter.BytesReceived, ct) - start;
                if (job.OnBytes(Math.Max(0, received), total))
                    RefreshProgress();
                await Task.Delay(500, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Çubuğu, yüzdeyi ve çalışan paketlerin satır metnini günceller.</summary>
    private void RefreshProgress()
    {
        if (!IsUpdating || _batch.Count == 0)
            return;

        var running = _job is { } job ? _jobPackages.Count(p => p.Status == UpdateStatus.Running) * job.Fraction : 0;
        ProgressValue = FinishedCount + running;
        ProgressPercent = $"%{ProgressValue * 100 / ProgressMax:0}";
        ProgressText = _job?.Text;
        if (ProgressText is { } text)
        {
            foreach (var p in _jobPackages.Where(p => p.Status == UpdateStatus.Running))
                p.StatusText = text;
        }
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
        RememberInstalled(pkgs);
        return pkgs.Count;
    }

    /// <summary>
    /// config.json'daki "postUpdate" komutlarını çalıştırır (ör. graphifyy sonrası skill yenileme).
    /// Hata paketi başarısız yapmaz; durum metnine eklenir.
    /// </summary>
    private async Task RunPostUpdateAsync(IEnumerable<PackageUpdate> packages, CancellationToken ct)
    {
        foreach (var p in packages)
        {
            foreach (var line in _config.PostUpdateCommands(p))
            {
                if (ct.IsCancellationRequested)
                    return;
                if (CommandLine.Split(line) is not [var file, .. var args])
                    continue;

                StatusMessage = $"Güncelleme sonrası: {line}";
                Log($"> {line}");
                if (ExecutableLocator.Find(file) is not { } exe)
                {
                    Log($"< {file} bulunamadı");
                    p.StatusText = $"{p.StatusText}, sonrası çalışmadı";
                    continue;
                }

                try
                {
                    var r = await _runner.RunAsync(exe, args, (l, _) => _dispatcher.BeginInvoke(() => Log(l)), ct);
                    Log($"< çıkış kodu {r.ExitCode} ({r.Duration.TotalSeconds:0.0} sn)");
                    if (r.ExitCode != 0)
                        p.StatusText = $"{p.StatusText}, sonrası hatalı ({file})";
                }
                catch (OperationCanceledException)
                {
                    Log("< iptal edildi");
                    return;
                }
                catch (Exception ex)
                {
                    Log($"< hata: {ex.Message}");
                    p.StatusText = $"{p.StatusText}, sonrası hatalı ({file})";
                }
            }
        }
    }

    /// <summary>Sürümü bilinmeyen paketlerin kurulan sürümünü saklar; sonraki taramada tekrar listelenmezler.</summary>
    private void RememberInstalled(List<PackageUpdate> pkgs)
    {
        if (pkgs.Count(_config.RememberInstalled) == 0)
            return;
        SaveConfig();
    }

    /// <returns>Yazıldıysa true.</returns>
    private bool SaveConfig()
    {
        try
        {
            _config.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Ayar dosyası yazılamadı: {ex.Message}");
            return false;
        }
    }

    // ---------- Geçmiş ----------

    private void LoadHistory()
    {
        try
        {
            foreach (var e in _history.ReadRecent(MaxHistory))
                History.Add(WithSourceName(e));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Geçmiş okunamadı: {ex.Message}");
        }
    }

    /// <summary>
    /// Çıkış kodu alınan her işi paket başına bir satır olarak geçmişe yazar.
    /// İptal edilen, UAC'si reddedilen veya hiç başlatılamayan işler yazılmaz.
    /// </summary>
    /// <summary>Geçmiş satırına kaynağın görünen adını ekler (ör. Choco → Chocolatey).</summary>
    internal HistoryEntry WithSourceName(HistoryEntry e) =>
        Sources.FirstOrDefault(s => s.Kind == e.Source) is { } s ? e with { SourceName = s.DisplayName } : e;

    private void RecordHistory(List<PackageUpdate> pkgs, int? exitCode, long durationMs, bool elevated)
    {
        var now = DateTimeOffset.Now;
        var entries = pkgs.Select(p => WithSourceName(new HistoryEntry(now, p.Source, p.Id, p.Current, p.Available, exitCode, durationMs, elevated))).ToList();
        foreach (var e in entries)
            History.Insert(0, e);
        while (History.Count > MaxHistory)
            History.RemoveAt(History.Count - 1);
        _pendingHistory.AddRange(entries);
    }

    /// <summary>Doğrulamada sürümü değişmeyen paketin bekleyen geçmiş kaydını işaretler.</summary>
    private void MarkHistoryUnchanged(PackageUpdate p)
    {
        var i = _pendingHistory.FindLastIndex(e => e.Source == p.Source && e.Id == p.Id);
        if (i < 0)
            return;
        var old = _pendingHistory[i];
        var marked = old with { Unchanged = true };
        _pendingHistory[i] = marked;
        if (History.IndexOf(old) is var shown and >= 0)
            History[shown] = marked;
    }

    /// <summary>Bekleyen geçmiş kayıtlarını dosyaya yazar.</summary>
    private void FlushHistory()
    {
        if (_pendingHistory.Count == 0)
            return;
        try
        {
            _history.Append(_pendingHistory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Geçmiş dosyası yazılamadı: {ex.Message}");
        }
        _pendingHistory.Clear();
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

    /// <summary>Paketi kalıcı olarak gizler (config.json, "ignore").</summary>
    [RelayCommand]
    private void Exclude(PackageUpdate? package)
    {
        if (package is null || package.IsExcluded)
            return;
        _config.AddIgnore(package);
        HideExcluded(package, $"{package.Id} hariç tutuldu; sonraki taramalarda gösterilmeyecek.");
    }

    /// <summary>Yalnızca önerilen sürümü gizler; daha yeni sürüm çıkınca paket yine listelenir.</summary>
    [RelayCommand]
    private void SkipVersion(PackageUpdate? package)
    {
        if (package is null || package.IsExcluded)
            return;
        _config.AddSkipVersion(package);
        HideExcluded(package, $"{package.Id} {package.Available} sürümü atlandı; daha yeni sürüm çıkınca yine gösterilecek.");
    }

    /// <summary>Sabitleme deseni sorar; vazgeçilirse null. Testler pencere açmamak için değiştirir.</summary>
    public Func<PackageUpdate, string?> AskPinPattern { get; set; } = PinWindow.Ask;

    /// <summary>Paketi bir sürüm desenine sabitler; desene uymayan sürümler gizlenir.</summary>
    [RelayCommand]
    private void Pin(PackageUpdate? package)
    {
        if (package is null || package.IsExcluded || AskPinPattern(package) is not { Length: > 0 } pattern)
            return;
        _config.AddPin(package, pattern);
        if (_config.IsExcluded(package))
        {
            HideExcluded(package, $"{package.Id} {pattern} desenine sabitlendi; {package.Available} gizlendi.");
            return;
        }
        if (SaveConfig())
            Log($"[{package.Group?.DisplayName ?? package.Source.ToString()}] {package.Id} {pattern} desenine sabitlendi; {package.Available} desene uyduğu için listede kalıyor.");
    }

    /// <summary>Paketin tüm hariç tutma kurallarını siler; paket yeniden güncellenebilir olur.</summary>
    [RelayCommand]
    private void RemoveExclusion(PackageUpdate? package)
    {
        if (package is null || !package.IsExcluded)
            return;
        _config.RemoveExclusions(package.Source, package.Id);
        if (!SaveConfig())
            return;
        package.ExclusionReason = null;
        if (package.Group is { } s)
            s.UpdateCount++;
        PackagesView.Refresh();
        Log($"[{package.Group?.DisplayName ?? package.Source.ToString()}] {package.Id} için hariç tutma kuralı kaldırıldı.");
    }

    private void HideExcluded(PackageUpdate package, string message)
    {
        if (!SaveConfig())
            return;
        package.IsSelected = false;
        package.ExclusionReason = _config.ExclusionReason(package);
        if (package.Group is { } s)
            s.UpdateCount = Math.Max(0, s.UpdateCount - 1);
        PackagesView.Refresh();
        Log($"[{package.Group?.DisplayName ?? package.Source.ToString()}] {message}");
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

    /// <summary>
    /// Windows açılışında bu exe <c>--startup</c> ile başlasın mı? Durum her okumada kayıt defterinden gelir;
    /// yazma başarısız olursa kutu gerçek duruma döner.
    /// </summary>
    public bool StartWithWindows
    {
        get => _autoStart.IsEnabled;
        set
        {
            try
            {
                if (value)
                    _autoStart.Enable(Environment.ProcessPath!);
                else
                    _autoStart.Disable();
                Log(value
                    ? $"Windows açılışında başlatılacak: {_autoStart.Command}"
                    : "Windows açılışında başlatma kapatıldı.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                Log($"Açılış kaydı yazılamadı: {ex.Message}");
            }
            OnPropertyChanged();
        }
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
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        if (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);
    }
}
