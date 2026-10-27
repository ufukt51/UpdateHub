using Microsoft.Win32;

namespace UpdateHub.Services;

/// <summary>
/// Windows açılışında başlatma: <c>HKCU\...\CurrentVersion\Run</c> altındaki "UpdateHub" değeri.
/// Yönetici gerekmez; Ayarlar &gt; Uygulamalar &gt; Başlangıç'ta da görünür ve oradan kapatılabilir.
/// </summary>
/// <param name="runKey">HKCU altındaki Run anahtarı. Testler başka anahtar verir.</param>
/// <param name="approvedKey">Ayarlar'dan kapatılan girişlerin tutulduğu anahtar.</param>
public sealed class AutoStart(
    string runKey = AutoStart.RunKey,
    string approvedKey = AutoStart.ApprovedKey,
    string valueName = "UpdateHub")
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>Run kaydının verdiği bağımsız değişken: pencere açmadan tarar, güncelleme varsa pencereyi açar.</summary>
    public const string Argument = "--startup";

    /// <summary>Kayıtlı komut satırı; kayıt yoksa null.</summary>
    public string? Command
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey);
            return key?.GetValue(valueName) as string;
        }
    }

    /// <summary>Kayıt var ve Ayarlar'dan kapatılmamış mı?</summary>
    public bool IsEnabled => Command is not null && !IsDisabledInSettings();

    public static string CommandFor(string exePath) => $"\"{exePath}\" {Argument}";

    /// <summary>Kayıt bu exe'yi mi başlatıyor? Exe taşındıysa veya başka kopya kaydedildiyse false.</summary>
    public bool PointsTo(string exePath) => string.Equals(Command, CommandFor(exePath), StringComparison.OrdinalIgnoreCase);

    public void Enable(string exePath)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(runKey))
            key.SetValue(valueName, CommandFor(exePath));
        // Ayarlar'dan kapatılmışsa işaret silinir; Windows işaretsiz girişi açık sayar.
        using var approved = Registry.CurrentUser.OpenSubKey(approvedKey, writable: true);
        approved?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <summary>StartupApproved değerinin ilk baytı tek sayıysa (ör. 0x03) giriş kapalıdır.</summary>
    private bool IsDisabledInSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(approvedKey);
        return key?.GetValue(valueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1;
    }
}
