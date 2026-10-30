using System.Globalization;
using System.Text.RegularExpressions;

namespace UpdateHub.Services;

public enum InstallPhase
{
    Waiting,
    Downloading,
    Verifying,
    Installing,
    Finishing,
}

/// <summary>
/// Tek bir güncelleme işinin ilerlemesi; paket yöneticisinin çıktı satırlarından beslenir.
/// İndirme %2–60, doğrulama %65, kurulum %75, kurulum bitti %95 sayılır.
/// winget çıktısı yönlendirilince ilerleme yazmaz; indirilen bayt <see cref="OnBytes"/> ile dışarıdan (yaklaşık) verilir.
/// </summary>
public sealed partial class InstallProgress
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Choco önce paketi (yalnız yüzde), sonra yükleyiciyi (bayt) indirir; ikinci indirme kaldığı yerden devam eder.
    private double _downloadBase = 0.02;
    private double _downloadCap = 0.6;

    public InstallPhase Phase { get; private set; }

    /// <summary>İndirme adresi (winget, choco "Downloading ... from").</summary>
    public string? DownloadUrl { get; private set; }

    public long? Received { get; private set; }
    public long? Total { get; private set; }

    /// <summary>Bayt sayısı ağ trafiğinden tahmin edildi; metinde "≈" ile gösterilir.</summary>
    public bool IsEstimate { get; private set; }

    /// <summary>0–100; bilinmiyorsa null.</summary>
    public double? DownloadPercent { get; private set; }

    /// <summary>0–1; geri gitmez.</summary>
    public double Fraction { get; private set; }

    /// <returns>İlerleme değiştiyse true.</returns>
    public bool OnLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return false;

        if (SavingRegex().Match(line) is { Success: true } saving)
        {
            Phase = InstallPhase.Downloading;
            Received = ParseSize(saving.Groups[2].Value, saving.Groups[3].Value);
            Total = ParseSize(saving.Groups[4].Value, saving.Groups[5].Value);
            IsEstimate = false;
            DownloadPercent = double.Parse(saving.Groups[1].Value, CultureInfo.InvariantCulture);
            return Update();
        }
        if (PercentRegex().Match(line) is { Success: true } percent)
        {
            Phase = InstallPhase.Downloading;
            DownloadPercent = double.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture);
            Received = Total = null;
            // choco paket (.nupkg) indirmesi; ardından yükleyici indirilebilir, payın yarısı kullanılır.
            if (_downloadBase < 0.3)
                _downloadCap = 0.3;
            return Update();
        }
        if (DownloadRegex().Match(line) is { Success: true } download)
        {
            Phase = InstallPhase.Downloading;
            DownloadUrl = download.Groups[1].Success ? download.Groups[1].Value : null;
            DownloadPercent = null;
            Received = Total = null;
            IsEstimate = false;
            // Sonraki indirmeler (choco: paket, sonra yükleyici) kaldığı yerden, kalan payın bir kısmıyla ilerler.
            _downloadBase = Math.Max(_downloadBase, Fraction);
            _downloadCap = _downloadBase < 0.6 ? 0.6 : _downloadBase + (0.9 - _downloadBase) * 0.3;
            return Update();
        }
        // choco adresi ayrı satıra yazar: "Downloading nodejs 64 bit" / "  from 'https://…'"
        if (Phase == InstallPhase.Downloading && DownloadUrl is null && FromRegex().Match(line) is { Success: true } from)
        {
            DownloadUrl = from.Groups[1].Value;
            return false;
        }
        if (FinishedRegex().IsMatch(line))
            return SetPhase(InstallPhase.Finishing);
        if (InstallingRegex().IsMatch(line))
            return SetPhase(InstallPhase.Installing);
        if (VerifyingRegex().IsMatch(line))
            return SetPhase(InstallPhase.Verifying);
        return false;
    }

    /// <summary>Çıktıda görünmeyen indirmenin (winget) tahmini ilerlemesi.</summary>
    /// <returns>İlerleme değiştiyse true.</returns>
    public bool OnBytes(long received, long? total)
    {
        if (Phase != InstallPhase.Downloading)
            return false;
        Total = total is > 0 ? total : null;
        // Tahmin başka trafikle şişebilir; doğrulama satırı gelmeden %100 gösterilmez.
        Received = Total is { } max ? Math.Min(received, max * 99 / 100) : received;
        IsEstimate = true;
        DownloadPercent = Total is { } t ? Received * 100.0 / t : null;
        return Update();
    }

    /// <summary>Durum çubuğu ve satır için kısa açıklama; henüz bir şey bilinmiyorsa null.</summary>
    public string? Text => Phase switch
    {
        InstallPhase.Downloading => DownloadText(),
        InstallPhase.Verifying => "Doğrulanıyor…",
        InstallPhase.Installing => "Kuruluyor…",
        InstallPhase.Finishing => "Tamamlanıyor…",
        _ => null,
    };

    private string DownloadText()
    {
        var approx = IsEstimate ? "≈" : "";
        if (Received is { } r && Total is { } t)
            return $"İndiriliyor: {approx}{FormatSize(r)} / {FormatSize(t)} ({approx}%{DownloadPercent:0}) · {approx}{FormatSize(Math.Max(0, t - r))} kaldı";
        if (DownloadPercent is { } p)
            return $"İndiriliyor: %{p:0}";
        if (Received is { } received)
            return $"İndiriliyor: {approx}{FormatSize(received)}";
        return "İndiriliyor…";
    }

    private bool SetPhase(InstallPhase phase)
    {
        if (phase <= Phase)
            return false;
        Phase = phase;
        return Update();
    }

    private bool Update()
    {
        var target = Phase switch
        {
            InstallPhase.Downloading => _downloadBase + Math.Max(0, _downloadCap - _downloadBase) * (DownloadPercent ?? 0) / 100,
            InstallPhase.Verifying => 0.65,
            InstallPhase.Installing => 0.75,
            InstallPhase.Finishing => 0.95,
            _ => 0,
        };
        Fraction = Math.Max(Fraction, target);
        return true;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes} B" : string.Format(Tr, "{0:0.0} {1}", value, units[i]);
    }

    /// <summary>"13.51" veya "13,51" (choco sayıyı sistem kültürüyle yazar) + birim.</summary>
    internal static long? ParseSize(string number, string unit)
    {
        var last = number.LastIndexOfAny(['.', ',']);
        var normalized = last < 0
            ? number
            : number[..last].Replace(".", "").Replace(",", "") + "." + number[(last + 1)..];
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        var power = unit.ToUpperInvariant() switch
        {
            "KB" => 1,
            "MB" => 2,
            "GB" => 3,
            "TB" => 4,
            _ => 0,
        };
        return (long)(value * Math.Pow(1024, power));
    }

    // choco (Get-WebFile): "Progress: 45% - Saving 13.51 MB of 30.02 MB"
    [GeneratedRegex(@"^Progress:\s*(\d+(?:\.\d+)?)%\s*-\s*Saving\s+([\d.,]+)\s*([KMGT]?B)\s+of\s+([\d.,]+)\s*([KMGT]?B)", RegexOptions.IgnoreCase)]
    private static partial Regex SavingRegex();

    // choco paket indirmesi: "Progress: Downloading nodejs 27.0.0... 45%"
    [GeneratedRegex(@"^Progress:.*?(\d+(?:\.\d+)?)%\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PercentRegex();

    // winget: "Downloading https://...", choco: "Downloading nodejs 64 bit" veya "Downloading package from source '...'"
    [GeneratedRegex(@"^Downloading\b(?:.*?(https?://[^\s']+))?", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadRegex();

    [GeneratedRegex(@"^from\s+'(https?://[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex FromRegex();

    /// <summary>choco'nun yüzde satırları; çubuğu besler, günlüğe yazılmaz.</summary>
    public static bool IsProgressLine(string line) => line.TrimStart().StartsWith("Progress:", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"Successfully verified installer hash|^Download of .+ completed|^Hashes match", RegexOptions.IgnoreCase)]
    private static partial Regex VerifyingRegex();

    // winget: "Starting package install...", choco: "Installing nodejs.install..."
    [GeneratedRegex(@"^Starting package install|^Installing \S+\.\.\.", RegexOptions.IgnoreCase)]
    private static partial Regex InstallingRegex();

    // winget: "Successfully installed", choco: "The upgrade of nodejs was successful." / "has been installed."
    [GeneratedRegex(@"^Successfully installed|^The (?:upgrade|install) of .+ was successful|has been installed\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex FinishedRegex();
}
