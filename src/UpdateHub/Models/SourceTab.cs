using CommunityToolkit.Mvvm.ComponentModel;

namespace UpdateHub.Models;

public enum BadgeKind
{
    Neutral,
    Accent,
    Error,
}

/// <summary>Kaynak sekmesi: ad ve yanında sayı rozeti.</summary>
public interface ISourceTab
{
    string DisplayName { get; }
    string Badge { get; }
    BadgeKind BadgeKind { get; }
    string Hint { get; }
}

/// <summary>Tüm kaynakları gösteren ilk sekme.</summary>
public sealed partial class AllSourcesTab : ObservableObject, ISourceTab
{
    public string DisplayName => "Tümü";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(BadgeKind))]
    public partial int Count { get; set; }

    public string Badge => Count.ToString();
    public BadgeKind BadgeKind => Count > 0 ? BadgeKind.Accent : BadgeKind.Neutral;
    public string Hint => "Tüm kaynaklar";
}
