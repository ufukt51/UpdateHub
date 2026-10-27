using System.Windows;
using System.Windows.Controls;
using UpdateHub.ViewModels;

namespace UpdateHub.Views;

/// <summary>Güncellemeden önce seçilenlerin özetini gösterir ve onay ister.</summary>
public partial class ConfirmUpdateWindow : Window
{
    public ConfirmUpdateWindow(ConfirmSummary summary)
    {
        InitializeComponent();
        DataContext = summary;
        if (summary.NeedsUac)
        {
            UacIcon.SetResourceReference(TextBlock.ForegroundProperty, "Admin");
            UacLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        }
        Loaded += (_, _) => UpdateButton.Focus();
    }

    /// <summary>Pencereyi ana pencerenin ortasında modal açar.</summary>
    /// <returns>Kullanıcı "Güncelle" dediyse true.</returns>
    public static bool Ask(ConfirmSummary summary)
    {
        var owner = Application.Current?.MainWindow;
        var window = new ConfirmUpdateWindow(summary) { Owner = owner is { IsLoaded: true } ? owner : null };
        return window.ShowDialog() == true;
    }

    private void Update_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
