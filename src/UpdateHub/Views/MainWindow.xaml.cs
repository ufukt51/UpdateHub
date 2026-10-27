using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UpdateHub.ViewModels;

namespace UpdateHub.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version!;
        Title = $"UpdateHub v{version.ToString(3)}";
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old)
                old.LogLines.CollectionChanged -= OnLogChanged;
            if (e.NewValue is MainViewModel vm)
                vm.LogLines.CollectionChanged += OnLogChanged;
        };
    }

    private IList<string> LogLines => ((MainViewModel)DataContext).LogLines;

    private bool _scrollPending;

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RawLog.Clear();
            return;
        }
        if (e.Action != NotifyCollectionChangedAction.Add || LogLines.Count == 0)
            return;

        // Canlı günlük her zaman son satırı göstersin. Bu işleyici ListBox'tan önce çalışabilir;
        // ScrollIntoView'u burada çağırmak, liste olayı henüz işlemeden yerleşimi zorlar ve
        // "ItemsControl öğe kaynağıyla tutarlı değil" hatasıyla uygulamayı kapatır. Bu yüzden ertelenir.
        if (LogTabs.SelectedIndex == 0)
        {
            if (!_scrollPending)
            {
                _scrollPending = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    _scrollPending = false;
                    if (LogLines.Count > 0)
                        LogList.ScrollIntoView(LogLines[^1]);
                });
            }
        }
        else if (RawTab.IsSelected)
        {
            // Ham metin yalnızca açıkken güncellenir; satır sınırı nedeniyle silinenler burada kalır.
            foreach (string line in e.NewItems!)
                RawLog.AppendText(RawLog.Text.Length == 0 ? line : Environment.NewLine + line);
            RawLog.ScrollToEnd();
        }
    }

    private void LogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // İç ListBox'lardan kabaran olayları yok say.
        if (e.OriginalSource != LogTabs || !RawTab.IsSelected)
            return;
        RawLog.Text = string.Join(Environment.NewLine, LogLines);
        RawLog.ScrollToEnd();
    }

    private void CopyRaw_Click(object sender, RoutedEventArgs e)
    {
        // Ham metin yalnızca sekmesi açıkken dolar; Çıktı sekmesindeyken satırlardan kurulur.
        var text = RawTab.IsSelected ? RawLog.Text : string.Join(Environment.NewLine, LogLines);
        if (text.Length > 0)
            Clipboard.SetText(text);
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Ad sütunu kalan genişliği doldurur; gruplu tabloda * genişlik çöktüğü için elle hesaplanır.</summary>
    private void PackagesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double others = PackagesGrid.Columns.Where(c => c != NameColumn).Sum(c => c.ActualWidth);
        double free = PackagesGrid.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 2;
        NameColumn.Width = Math.Max(NameColumn.MinWidth, free);
    }
}
