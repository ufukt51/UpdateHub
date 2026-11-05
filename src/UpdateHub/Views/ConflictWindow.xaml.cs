using System.Windows;
using UpdateHub.Models;

namespace UpdateHub.Views;

/// <summary>Güncellemeden sonra bulunan yeni uyumsuzlukları gösterir (pip, npm).</summary>
public partial class ConflictWindow : Window
{
    public ConflictWindow(string sourceName, IReadOnlyList<string> conflicts, IReadOnlyList<PackageUpdate> updated)
    {
        InitializeComponent();
        Conflicts.ItemsSource = conflicts;
        Subtitle.Text = $"{sourceName}: {conflicts.Count} yeni sorun bulundu. Güncellenen: "
            + string.Join(", ", updated.Select(p => $"{p.Id} {p.Current} → {p.Available}"));
        Question.Text = "Devam etmek istiyor musunuz? \"Geri al\" güncellenen paketleri eski sürümlerine döndürür.";
        Loaded += (_, _) => RollbackButton.Focus();
    }

    /// <returns>Kullanıcı "Devam et" dediyse true; kapatma ve "Geri al" false.</returns>
    public static bool Ask(string sourceName, IReadOnlyList<string> conflicts, IReadOnlyList<PackageUpdate> updated)
    {
        var owner = Application.Current?.MainWindow;
        var window = new ConflictWindow(sourceName, conflicts, updated) { Owner = owner is { IsLoaded: true } ? owner : null };
        return window.ShowDialog() == true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
