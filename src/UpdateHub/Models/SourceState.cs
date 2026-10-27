using CommunityToolkit.Mvvm.ComponentModel;
using UpdateHub.Sources;

namespace UpdateHub.Models;

public enum SourceScanState
{
    Idle,
    Scanning,
    Done,
    NotInstalled,
    Error,
}

/// <summary>Bir kaynağın arayüzdeki durumu. Tablo bu nesneye göre gruplanır.</summary>
public sealed partial class SourceState(IPackageSource source) : ObservableObject, ISourceTab
{
    public IPackageSource Source { get; } = source;
    public SourceKind Kind => Source.Kind;
    public string DisplayName => Source.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsUnavailable), nameof(Badge), nameof(BadgeKind), nameof(Hint))]
    public partial SourceScanState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Badge), nameof(BadgeKind), nameof(Hint))]
    public partial int UpdateCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Hint))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? Warning { get; set; }

    /// <summary>Grup başlığındaki "tümünü seç" kutusu.</summary>
    [ObservableProperty]
    public partial bool IsAllSelected { get; set; }

    public event EventHandler<bool>? AllSelectedChanged;

    public bool IsUnavailable => State is SourceScanState.NotInstalled or SourceScanState.Error;

    public string StatusText => State switch
    {
        SourceScanState.Scanning => "taranıyor…",
        SourceScanState.Done => UpdateCount == 0 ? "güncel" : $"{UpdateCount} güncelleme",
        SourceScanState.NotInstalled => "kurulu değil",
        SourceScanState.Error => "hata",
        _ => "",
    };

    public string Badge => State switch
    {
        SourceScanState.Scanning => "…",
        SourceScanState.Done => UpdateCount.ToString(),
        SourceScanState.NotInstalled => "—",
        SourceScanState.Error => "!",
        _ => "",
    };

    public BadgeKind BadgeKind => State switch
    {
        SourceScanState.Error => BadgeKind.Error,
        SourceScanState.Done when UpdateCount > 0 => BadgeKind.Accent,
        _ => BadgeKind.Neutral,
    };

    public string Hint => Error is null ? $"{DisplayName}: {StatusText}" : $"{DisplayName}: {Error}";

    partial void OnIsAllSelectedChanged(bool value) => AllSelectedChanged?.Invoke(this, value);
}
