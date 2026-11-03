using System.Windows;
using UpdateHub.Models;

namespace UpdateHub.Views;

/// <summary>pip güncellemesinden sonra <c>pip check</c>'in bulduğu yeni uyumsuzlukları gösterir.</summary>
public partial class PipConflictWindow : Window
{
    public PipConflictWindow(IReadOnlyList<string> conflicts, IReadOnlyList<PackageUpdate> updated)
    {
        InitializeComponent();
        Conflicts.ItemsSource = conflicts;
        Subtitle.Text = $"python -m pip check {conflicts.Count} yeni sorun buldu. Güncellenen: "
            + string.Join(", ", updated.Select(p => $"{p.Id} {p.Current} → {p.Available}"));
        Question.Text = "Devam etmek istiyor musunuz? \"Geri al\" güncellenen pip paketlerini eski sürümlerine döndürür.";
        Loaded += (_, _) => RollbackButton.Focus();
    }

    /// <returns>Kullanıcı "Devam et" dediyse true; kapatma ve "Geri al" false.</returns>
    public static bool Ask(IReadOnlyList<string> conflicts, IReadOnlyList<PackageUpdate> updated)
    {
        var owner = Application.Current?.MainWindow;
        var window = new PipConflictWindow(conflicts, updated) { Owner = owner is { IsLoaded: true } ? owner : null };
        return window.ShowDialog() == true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
