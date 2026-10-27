using CommunityToolkit.Mvvm.ComponentModel;

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
}
