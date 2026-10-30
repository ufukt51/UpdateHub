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
        // Boyutlar arka planda gelir; en çok 60 sn beklenir.
        for (int i = 0; i < 240 && vm.Packages.Any(p => p.IsSizeLoading); i++)
            await Task.Delay(250);
        foreach (var p in vm.Packages)
            System.Diagnostics.Debug.WriteLine($"[size] {p.Source} {p.Id} {p.Available}: {p.SizeText} ({p.SizeNote})");
        File.WriteAllLines(basePath + "-sizes.txt", vm.Packages.Select(p => $"{p.Source} {p.Id} {p.Available}: {p.SizeText} | {p.SizeNote}"));

        // Günlük satırları canlı kaydırma yolunu da çalıştırır.
        for (int i = 1; i <= 12; i++)
            vm.LogLines.Add($"> örnek günlük satırı {i}");

        window.PackagesGrid.SelectedIndex = 2;
        var first = vm.Packages.FirstOrDefault(p => !p.IsExcluded);
        if (first is not null)
            first.IsSelected = true;
        await Save(window, basePath + "-1.png");
        await Save(window.UpdateButton, basePath + "-button.png", 8);

        // Sağ tık menüsü ayrı bir açılır pencerede; kendisi çizilir.
        var menu = window.PackagesGrid.ContextMenu;
        menu.PlacementTarget = window.PackagesGrid;
        menu.IsOpen = true;
        // Fluent açılış animasyonu bitsin.
        await Task.Delay(800);
        await Save(menu, basePath + "-menu.png", 2);
        menu.IsOpen = false;

        // Güncelleme sırasındaki görünüm: ilerleme çubuğu ve "sürüm değişmedi" durumu.
        if (first is not null)
        {
            first.Status = Models.UpdateStatus.Unchanged;
            first.StatusText = "Sürüm değişmedi";
        }
        vm.ProgressMax = 3;
        vm.ProgressValue = 2;
        vm.IsBusy = true;
        await Save(window, basePath + "-busy.png");

        // Güncelleme sırasında: kırmızıdan yeşile çubuk, indirme bilgisi ve yüzde.
        vm.IsUpdating = true;
        vm.ProgressValue = 1.31;
        vm.ProgressText = "İndiriliyor: ≈15,0 MB / 30,0 MB (≈%50) · ≈15,0 MB kaldı";
        vm.ProgressPercent = "%44";
        await Save(window, basePath + "-updating.png");
        vm.ProgressValue = 2.75;
        vm.ProgressText = "Kuruluyor…";
        vm.ProgressPercent = "%92";
        await Save(window, basePath + "-updating2.png");
        vm.IsUpdating = false;
        vm.ProgressText = vm.ProgressPercent = null;
        vm.IsBusy = false;
        // İşaretli onay kutusu: ilk paket seçili olduğu için tablodaki ilk işaretli kutu.
        var check = Descendants(window.PackagesGrid).OfType<System.Windows.Controls.CheckBox>().FirstOrDefault(c => c.IsChecked == true);
        if (check is not null)
            await Save(check, basePath + "-check.png", 12);

        // Onay penceresi: her kaynaktan ilk paket seçili.
        var picked = vm.Packages.Where(p => !p.IsExcluded).GroupBy(p => p.Source).Select(g => g.First()).ToList();
        var confirm = new ConfirmUpdateWindow(ConfirmSummary.Create(picked,
            picked.GroupBy(p => p.Group!).SelectMany(g => g.Key.Source.CreateUpdateJob([.. g])), false)) { Owner = window };
        confirm.Show();
        await Save(confirm, basePath + "-confirm.png");
        confirm.Close();

        // Sabitleme penceresi: önerilen desen, sonra gizleyen bir desen.
        // Yalnızca pencere çizilir; kural eklenmez, bu yüzden hariç tutulan paket de olur.
        if ((vm.Packages.FirstOrDefault(p => !p.IsExcluded) ?? vm.Packages.FirstOrDefault()) is { } pinned)
        {
            var model = new PinDialogModel(pinned);
            var pin = new PinWindow(model) { Owner = window };
            pin.Show();
            await Save(pin, basePath + "-pin.png");
            model.Pattern = "0.*";
            await Save(pin, basePath + "-pin-hide.png");
            pin.Close();
        }

        if (vm.Tabs.Count > 2)
            vm.SelectedTab = vm.Tabs[2];
        await Save(window, basePath + "-2.png");

        // Geçmiş sekmesi: dosyaya yazmadan örnek iki satır.
        vm.History.Insert(0, vm.WithSourceName(new Services.HistoryEntry(DateTimeOffset.Now, Models.SourceKind.Choco, "python", "3.15.0", "3.15.2", 0, 41200, true)));
        vm.History.Insert(0, vm.WithSourceName(new Services.HistoryEntry(DateTimeOffset.Now, Models.SourceKind.Winget, "RARLab.WinRAR", "7.00.0", "7.23.0", 1, 3100, false)));
        window.HistoryTab.IsSelected = true;
        await Save(window, basePath + "-3.png");

        // Hariç tutulanlar açık, Tümü sekmesi.
        vm.ShowExcluded = true;
        vm.SelectedTab = vm.AllTab;
        await Save(window, basePath + "-4.png");

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
        {
            // Viewbox öğenin kendi sınırları; taşan alt öğeler görüntüyü küçültmez.
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var brush = new VisualBrush(root) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.None };
            dc.DrawRectangle(brush, null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
