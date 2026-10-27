using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
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

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RawLog.Clear();
            return;
        }
        if (e.Action != NotifyCollectionChangedAction.Add || LogLines.Count == 0)
            return;

        // Canlı günlük her zaman son satırı göstersin.
        if (LogTabs.SelectedIndex == 0)
        {
            LogList.ScrollIntoView(LogLines[^1]);
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
        if (RawLog.Text.Length > 0)
            Clipboard.SetText(RawLog.Text);
    }
}
