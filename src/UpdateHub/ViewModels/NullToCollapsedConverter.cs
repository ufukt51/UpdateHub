using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UpdateHub.ViewModels;

public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    /// <summary>Tersi: değer boşsa görünür, doluysa gizli.</summary>
    public static readonly NullToCollapsedConverter Inverse = new() { IsInverse = true };

    private bool IsInverse { get; init; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null or "") != IsInverse ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
