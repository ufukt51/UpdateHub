using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources.Parsers;

namespace UpdateHub.Sources;

public sealed class WingetSource(ProcessRunner runner) : PackageSourceBase(runner)
{
    public const string ModuleMissingWarning = "Daha güvenilir sonuç için Microsoft.WinGet.Client modülünü kurun.";

    public const string InstallModuleScript =
        "Install-PSResource Microsoft.WinGet.Client -Scope CurrentUser -TrustRepository";

    private const string ModuleQuery =
        "$ErrorActionPreference='Stop'; Import-Module Microsoft.WinGet.Client; " +
        "@(Get-WinGetPackage | Where-Object IsUpdateAvailable | " +
        "Select-Object Name, Id, InstalledVersion, @{n='Available';e={$_.AvailableVersions[0]}}) | " +
        "ConvertTo-Json -AsArray -Compress";

    public override SourceKind Kind => SourceKind.Winget;
    public override string DisplayName => "winget";
    protected override string ExecutableName => "winget";

    // 0x8A150030: eski sürümü kaldırma komutu başarısız. Makine geneli MSI'lar (ör. Java) /quiet ile UAC isteyemez, 1603 döner.
    public override int? ElevationRetryExitCode => -1978335184;

    public override async Task<ScanResult> GetOutdatedAsync(CancellationToken ct)
    {
        // Modül yoksa pwsh hiç başlatılmaz (EDR'ler "PowerShell -Command" çağrılarını işaretliyor).
        var pwsh = ExecutableLocator.Find("pwsh");
        if (pwsh is not null && IsModuleInstalled())
        {
            var r = Ensure(await Runner.RunAsync(pwsh, ["-NoProfile", "-NonInteractive", "-Command", ModuleQuery], null, ct));
            return FromParsed(WingetJsonParser.Parse(r.StdOut));
        }

        var result = await RunAsync(ct, "upgrade", "--include-unknown", "--accept-source-agreements", "--disable-interactivity");
        var parsed = WingetTableParser.Parse(result.StdOut);
        // Güncelleme yoksa tablo da yoktur; o durumda çıkış kodu bilgilendirme amaçlı olabilir.
        if (parsed.Count == 0 && result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.StdErr))
            Ensure(result);
        return FromParsed(parsed, ModuleMissingWarning);
    }

    // Kurulu sürümü "Unknown" olan paketler (ör. wkhtmltox) --include-unknown olmadan güncellenmez
    // (çıkış -1978335189). Bayrak sürümü bilinen paketlerde etkisizdir; yükseltilmiş taraf sürümü bilmediği için hep eklenir.
    protected override string[] UpdateArguments(PackageUpdate p) =>
    [
        "upgrade", "--id", p.Id, "--exact", "--silent", "--include-unknown",
        "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity",
    ];

    public override async Task<PackageSize?> GetSizeAsync(PackageUpdate p, CancellationToken ct)
    {
        string[] args = ["show", "--id", p.Id, "--exact", "--accept-source-agreements", "--disable-interactivity"];
        // Önerilen sürümün yükleyicisi; sürüm bulunamazsa en yenisi.
        var r = await RunAsync(ct, [.. args, "--version", p.Available]);
        var url = PackageSizeParser.WingetInstallerUrl(r.StdOut) ?? PackageSizeParser.WingetInstallerUrl((await RunAsync(ct, args)).StdOut);
        if (url is null)
            return null;
        return new(await Web.GetLengthAsync(url, ct), "Yükleyici boyutu. Bazı yükleyiciler kurulum sırasında ek dosya indirir.");
    }

    /// <summary>Modül klasörünü PowerShell'in arama yollarında arar.</summary>
    private static bool IsModuleInstalled()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var dirs = (Environment.GetEnvironmentVariable("PSModulePath") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PowerShell", "Modules"))
            .Append(Path.Combine(programFiles, "PowerShell", "Modules"))
            .Append(Path.Combine(programFiles, "PowerShell", "7", "Modules"));

        return dirs.Any(d => Directory.Exists(Path.Combine(d, "Microsoft.WinGet.Client")));
    }
}
