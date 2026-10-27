using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.ViewModels;

/// <summary>"Sürüme sabitle" penceresi: desen önerileri ve önerilen sürümün desene uyup uymadığı.</summary>
public sealed partial class PinDialogModel : ObservableObject
{
    public PinDialogModel(PackageUpdate package)
    {
        Package = package;
        Suggestions = Suggest(package.Current);
        Pattern = Suggestions.FirstOrDefault() ?? "";
    }

    public PackageUpdate Package { get; }

    /// <summary>Kurulu sürümden türetilen desenler, genişten dara (ör. "8.*", "8.0.*").</summary>
    public IReadOnlyList<string> Suggestions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(WouldHide), nameof(PreviewText))]
    public partial string Pattern { get; set; } = "";

    public bool IsValid => Pattern.Trim().Length > 0;

    /// <summary>Önerilen sürüm desene uymuyor; kayıttan sonra paket gizlenir.</summary>
    public bool WouldHide => IsValid && !Exclusion.WildcardMatch(Package.Available, Pattern);

    public string PreviewText => !IsValid
        ? "Bir desen yazın. * herhangi bir metin, ? tek karakter yerine geçer."
        : WouldHide
            ? $"{Package.Available} bu desene uymuyor; paket listeden gizlenir. Desene uyan bir sürüm çıkınca yine görünür."
            : $"{Package.Available} bu desene uyuyor; paket listede kalır. Desen dışı sürümler gizlenir.";

    [RelayCommand]
    private void UseSuggestion(string? pattern)
    {
        if (pattern is not null)
            Pattern = pattern;
    }

    /// <summary>Sürümün baştaki sayısal bileşenlerinden desen üretir. Son bileşen hariç; "8.0.5" → "8.*", "8.0.*".</summary>
    internal static IReadOnlyList<string> Suggest(string version)
    {
        var parts = version.Trim().Split('.');
        var numeric = parts.TakeWhile(p => p.Length > 0 && p.All(char.IsAsciiDigit)).ToList();
        // Tek bileşenli sürümde ("2026") daraltılacak bir şey yok; yine de ana bileşene sabitlemek anlamlı.
        var count = Math.Min(Math.Max(numeric.Count - 1, numeric.Count > 0 ? 1 : 0), 2);
        return [.. Enumerable.Range(1, count).Select(n => string.Join('.', numeric.Take(n)) + ".*")];
    }
}
