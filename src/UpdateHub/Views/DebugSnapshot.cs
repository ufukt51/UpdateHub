#if DEBUG
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UpdateHub.ViewModels;

namespace UpdateHub.Views;

/// <summary>
/// Yalnızca DEBUG: tarama bitince pencerenin kendi görsel ağacını PNG'ye çizer ve çıkar.
/// Ekran yakalama değildir; yalnızca uygulamanın içeriği çizilir.
/// </summary>
internal static class DebugSnapshot
{
    public const string Argument = "--snapshot";

    public static async void Run(MainWindow window, MainViewModel vm, string basePath)
    {
        await Task.Delay(500);
        while (vm.IsBusy)
            await Task.Delay(250);

        // Günlük satırları canlı kaydırma yolunu da çalıştırır.
        for (int i = 1; i <= 12; i++)
            vm.LogLines.Add($"> örnek günlük satırı {i}");

        window.PackagesGrid.SelectedIndex = 2;
        var first = vm.Packages.FirstOrDefault();
        if (first is not null)
            first.IsSelected = true;
        await Save(window, basePath + "-1.png");
        await Save(window.UpdateButton, basePath + "-button.png", 8);
        // İşaretli onay kutusu: ilk paket seçili olduğu için tablodaki ilk işaretli kutu.
        var check = Descendants(window.PackagesGrid).OfType<System.Windows.Controls.CheckBox>().FirstOrDefault(c => c.IsChecked == true);
        if (check is not null)
            await Save(check, basePath + "-check.png", 12);

        // Onay penceresi: her kaynaktan ilk paket seçili.
        var picked = vm.Packages.GroupBy(p => p.Source).Select(g => g.First()).ToList();
        var confirm = new ConfirmUpdateWindow(ConfirmSummary.Create(picked,
            picked.GroupBy(p => p.Group!).SelectMany(g => g.Key.Source.CreateUpdateJob([.. g])), false)) { Owner = window };
        confirm.Show();
        await Save(confirm, basePath + "-confirm.png");
        confirm.Close();

        if (vm.Tabs.Count > 2)
            vm.SelectedTab = vm.Tabs[2];
        await Save(window, basePath + "-2.png");

        Application.Current.Shutdown();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child))
                yield return d;
        }
    }

    private static Task Save(Window window, string path) =>
        Save((FrameworkElement)VisualTreeHelper.GetChild(window, 0), path, VisualTreeHelper.GetDpi(window).DpiScaleX);

    /// <summary>Öğeyi verilen ölçekte çizer; küçük ayrıntılara bakmak için büyütülebilir.</summary>
    private static async Task Save(FrameworkElement root, string path, double scale)
    {
        await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap(
            (int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // VisualBrush ile çizilir; öğenin ebeveyn içindeki konumu görüntüyü kaydırmaz.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
