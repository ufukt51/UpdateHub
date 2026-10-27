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

        // --check modu 7. kilometre taşında eklenecek.
        if (e.Args.Contains("--check", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(2);
            return;
        }

        var vm = new MainViewModel(SourceCatalog.CreateAll(runner), runner);
        MainWindow = new MainWindow { DataContext = vm };
        MainWindow.Show();
        vm.ScanCommand.Execute(null);
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
