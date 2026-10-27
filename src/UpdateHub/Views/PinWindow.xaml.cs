using System.ComponentModel;
using System.Windows;
using UpdateHub.Models;
using UpdateHub.ViewModels;

namespace UpdateHub.Views;

/// <summary>Paketi bir sürüm desenine sabitlemek için desen sorar.</summary>
public partial class PinWindow : Window
{
    private readonly PinDialogModel _model;

    public PinWindow(PinDialogModel model)
    {
        InitializeComponent();
        DataContext = _model = model;
        model.PropertyChanged += OnModelChanged;
        UpdatePreview();
        Loaded += (_, _) =>
        {
            PatternBox.Focus();
            PatternBox.SelectAll();
        };
    }

    /// <summary>Pencereyi ana pencerenin ortasında modal açar.</summary>
    /// <returns>Girilen desen; vazgeçildiyse null.</returns>
    public static string? Ask(PackageUpdate package)
    {
        var owner = Application.Current?.MainWindow;
        var window = new PinWindow(new PinDialogModel(package)) { Owner = owner is { IsLoaded: true } ? owner : null };
        return window.ShowDialog() == true ? window._model.Pattern.Trim() : null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PinDialogModel.WouldHide))
            UpdatePreview();
    }

    /// <summary>Paket gizlenecekse önizleme uyarı renginde.</summary>
    private void UpdatePreview()
    {
        Preview.SetResourceReference(BackgroundProperty, _model.WouldHide ? "WarnSoft" : "Raised");
        Preview.SetResourceReference(BorderBrushProperty, _model.WouldHide ? "WarnEdge" : "Line");
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
