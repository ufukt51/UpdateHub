using System.Windows;
using UpdateHub.Services;

namespace UpdateHub.Views;

/// <summary>GitHub'da yeni UpdateHub sürümü bulunduğunda sürümleri ve release notlarını gösterip onay ister.</summary>
public partial class SelfUpdateWindow : Window
{
    public SelfUpdateWindow(SelfRelease release)
    {
        InitializeComponent();
        CurrentText.Text = SelfUpdate.CurrentVersion;
        NewText.Text = release.Version;
        NotesText.Text = release.Notes;
        if (release.Notes.Length == 0)
            NotesBox.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => UpdateButton.Focus();
    }

    /// <returns>Kullanıcı "Güncelle ve yeniden başlat" dediyse true.</returns>
    public static bool Ask(SelfRelease release)
    {
        var owner = Application.Current?.MainWindow;
        var window = new SelfUpdateWindow(release) { Owner = owner is { IsLoaded: true } ? owner : null };
        return window.ShowDialog() == true;
    }

    private void Update_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
