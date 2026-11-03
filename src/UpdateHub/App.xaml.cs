using System.Windows;
using UpdateHub.Services;
using UpdateHub.Services.Elevation;
using UpdateHub.Sources;
using UpdateHub.ViewModels;
using UpdateHub.Views;

namespace UpdateHub;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var runner = new ProcessRunner();

        var elevated = Array.FindIndex(e.Args, a => a.Equals(ElevationProtocol.Argument, StringComparison.OrdinalIgnoreCase));
        if (elevated >= 0)
        {
            RunElevated(runner, elevated + 1 < e.Args.Length ? e.Args[elevated + 1] : "");
            return;
        }

        if (e.Args.Contains(ConsoleCheck.Argument, StringComparer.OrdinalIgnoreCase))
        {
            RunCheck(runner);
            return;
        }

        var vm = new MainViewModel(SourceCatalog.CreateAll(runner), runner);
        MainWindow = new MainWindow { DataContext = vm };
        SelfUpdate.CleanUp();
#if !DEBUG
        // Loaded: --startup ile pencere hiç açılmazsa sorulmaz.
        MainWindow.Loaded += async (_, _) => await OfferSelfUpdateAsync(vm);
#endif

        if (e.Args.Contains(AutoStart.Argument, StringComparer.OrdinalIgnoreCase))
        {
            ShowIfUpdates(vm);
            return;
        }

        MainWindow.Show();
        vm.ScanCommand.Execute(null);

#if DEBUG
        var snapshot = Array.FindIndex(e.Args, a => a.Equals(DebugSnapshot.Argument, StringComparison.OrdinalIgnoreCase));
        if (snapshot >= 0 && snapshot + 1 < e.Args.Length)
            DebugSnapshot.Run((MainWindow)MainWindow, vm, e.Args[snapshot + 1]);
#endif
    }

    /// <summary>GitHub'da yeni sürüm varsa sorar; onaylanırsa exe'yi açıldığı yerde değiştirip yeniden başlatır.</summary>
    private async Task OfferSelfUpdateAsync(MainViewModel vm)
    {
        if (await SelfUpdate.CheckAsync(CancellationToken.None) is not { } release)
            return;

        if (!SelfUpdateWindow.Ask(release))
            return;

        string exe;
        try
        {
            vm.StatusMessage = $"UpdateHub {release.Version} indiriliyor…";
            exe = await SelfUpdate.InstallAsync(release, CancellationToken.None);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"UpdateHub güncellenemedi: {ex.Message}";
            return;
        }

        // Paket güncellemesi sürerken kapatılmaz; yeni sürüm sonraki açılışta çalışır.
        if (vm.IsUpdating)
        {
            vm.StatusMessage = $"UpdateHub {release.Version} kuruldu; yeniden açınca geçerli olur.";
            return;
        }
        SelfUpdate.Restart(exe);
        Shutdown();
    }

    /// <summary>Pencere açmadan tarar, özeti konsola yazar ve çıkış koduyla kapanır.</summary>
    private async void RunCheck(ProcessRunner runner)
    {
        int code;
        try
        {
            var config = new ConfigStore(ConfigStore.DefaultPath);
            config.Load();
            // config.json'daki hariç tutma kuralları (ignore, pin, skipVersion).
            code = await ConsoleCheck.RunConsoleAsync(SourceCatalog.CreateAll(runner), config, exclude: config.IsExcluded);
        }
        catch (Exception)
        {
            code = ConsoleCheck.Error;
        }
        Shutdown(code);
    }

    /// <summary>
    /// Windows açılışı (<c>--startup</c>): pencere açmadan tarar.
    /// Hariç tutulmayan güncelleme varsa pencereyi açar, yoksa sessizce kapanır.
    /// </summary>
    private async void ShowIfUpdates(MainViewModel vm)
    {
        try
        {
            await vm.ScanCommand.ExecuteAsync(null);
            if (vm.AllTab.Count > 0)
            {
                MainWindow.Show();
                return;
            }
        }
        catch (Exception)
        {
        }
        Shutdown();
    }

    /// <summary>Pencere açmadan iş dosyasını çalıştırır ve çıkar.</summary>
    private async void RunElevated(ProcessRunner runner, string jobPath)
    {
        int code;
        try
        {
            code = await new ElevatedRunner(SourceCatalog.CreateAll(runner), runner).RunAsync(jobPath);
        }
        catch (Exception)
        {
            code = 2;
        }
        Shutdown(code);
    }
}
