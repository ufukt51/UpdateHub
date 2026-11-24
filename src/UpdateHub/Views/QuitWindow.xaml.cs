using System.Windows;

namespace UpdateHub.Views;

/// <summary>Güncelleme sürerken pencere kapatılınca onay ister.</summary>
public partial class QuitWindow : Window
{
    public QuitWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StayButton.Focus();
    }

    /// <returns>Kullanıcı "Çık" dediyse true.</returns>
    public static bool Ask(Window owner) => new QuitWindow { Owner = owner }.ShowDialog() == true;

    private void Quit_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
