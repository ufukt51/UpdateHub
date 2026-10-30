using CommunityToolkit.Mvvm.ComponentModel;
using UpdateHub.Services;

namespace UpdateHub.Models;

public sealed partial class PackageUpdate : ObservableObject
{
    public required SourceKind Source { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Current { get; init; }
    public required string Available { get; init; }
    public bool IsMajor { get; init; }
    public bool NeedsAdmin { get; init; }
    public bool IsBroken { get; init; }

    /// <summary>Tablodaki grup (kaynak durumu). Tarama sırasında atanır.</summary>
    public SourceState? Group { get; set; }

    /// <summary>Aynı yazılımın başka kaynaktaki kopyalarıyla ortak anahtar. Çift değilse null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDuplicate))]
    public partial string? DuplicateGroup { get; set; }

    /// <summary>Çiftin diğer kaynaklarını açıklayan tooltip metni.</summary>
    [ObservableProperty]
    public partial string? DuplicateInfo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectable))]
    public partial UpdateStatus Status { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Paketi gizleyen hariç tutma kuralının açıklaması; kural yoksa null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExcluded), nameof(IsSelectable))]
    public partial string? ExclusionReason { get; set; }

    partial void OnExclusionReasonChanged(string? value)
    {
        if (value is not null)
            IsSelected = false;
    }

    partial void OnStatusChanged(UpdateStatus value)
    {
        if (value == UpdateStatus.Succeeded)
            IsSelected = false;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !IsSelectable)
            IsSelected = false;
    }

    public bool IsDuplicate => DuplicateGroup is not null;

    /// <summary>Hariç tutulan paket yalnızca "Hariç tutulanları göster" açıkken görünür ve seçilemez.</summary>
    public bool IsExcluded => ExclusionReason is not null;

    /// <summary>Hariç tutulan ve bu oturumda başarıyla güncellenen paket seçilemez.</summary>
    public bool IsSelectable => !IsExcluded && Status != UpdateStatus.Succeeded;

    public string DisplayName => string.Equals(Name, Id, StringComparison.OrdinalIgnoreCase) ? Id : $"{Name} ({Id})";

    /// <summary>Kurulu sürüm okunamadı (ör. winget "Unknown"); paket her taramada güncellenecek görünür.</summary>
    public bool IsVersionUnknown => VersionInfo.TryParse(Current) is null;

    /// <summary>Ad kimlikten farklıysa addan sonra soluk gösterilen kimlik.</summary>
    public string? SubtitleId => string.Equals(Name, Id, StringComparison.OrdinalIgnoreCase) ? null : Id;
}
