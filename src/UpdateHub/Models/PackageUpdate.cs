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
    public partial UpdateStatus Status { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public bool IsDuplicate => DuplicateGroup is not null;

    public string DisplayName => string.Equals(Name, Id, StringComparison.OrdinalIgnoreCase) ? Id : $"{Name} ({Id})";

    /// <summary>Kurulu sürüm okunamadı (ör. winget "Unknown"); paket her taramada güncellenecek görünür.</summary>
    public bool IsVersionUnknown => VersionInfo.TryParse(Current) is null;

    /// <summary>Ad kimlikten farklıysa addan sonra soluk gösterilen kimlik.</summary>
    public string? SubtitleId => string.Equals(Name, Id, StringComparison.OrdinalIgnoreCase) ? null : Id;
}
