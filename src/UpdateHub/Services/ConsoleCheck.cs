using System.Runtime.InteropServices;
using System.Text;
using UpdateHub.Models;
using UpdateHub.Sources;

namespace UpdateHub.Services;

/// <summary>
/// <c>--check</c> modu: pencere açmadan tarar, kaynak bazında özet yazar.
/// Çıkış kodu: 0 = güncelleme yok, 1 = güncelleme var, 2 = hata.
/// </summary>
public static class ConsoleCheck
{
    public const string Argument = "--check";

    public const int NoUpdates = 0;
    public const int UpdatesFound = 1;
    public const int Error = 2;

    /// <summary>Bir kaynağın tarama sonucu. <see cref="Updates"/> süzülmüş listedir.</summary>
    private sealed record SourceResult(
        IPackageSource Source,
        bool Available,
        IReadOnlyList<PackageUpdate> Updates,
        int Hidden,
        string? Error);

    /// <summary>
    /// Tüm kaynakları paralel tarar, özeti <paramref name="output"/>'a yazar.
    /// Güncelleme varsa 1 döner; bazı kaynaklar hata verse bile güncelleme bulunduysa sonuç 1'dir.
    /// </summary>
    /// <param name="config">Sürümü bilinmeyen ve daha önce kurulan paketleri gizler. Null: süzme yok.</param>
    /// <param name="exclude">Ek süzgeç (ör. kullanıcının hariç tutmaları). True dönen paket gösterilmez.</param>
    public static async Task<int> RunAsync(
        IReadOnlyList<IPackageSource> sources,
        TextWriter output,
        ConfigStore? config = null,
        Func<PackageUpdate, bool>? exclude = null,
        CancellationToken ct = default)
    {
        var results = await Task.WhenAll(sources.Select(s => ScanAsync(s, config, exclude, ct)));

        // Ok karakteri konsolun kod sayfasında yoksa düz ok kullanılır.
        var arrow = CanEncode(output.Encoding, "→") ? "→" : "->";
        var nameWidth = results.Max(r => r.Source.DisplayName.Length);

        foreach (var r in results)
        {
            var name = r.Source.DisplayName.PadRight(nameWidth);
            if (r.Error is not null)
            {
                output.WriteLine($"{name}  hata: {r.Error}");
                continue;
            }
            if (!r.Available)
            {
                output.WriteLine($"{name}  kurulu değil");
                continue;
            }

            var hidden = r.Hidden > 0 ? $" ({r.Hidden} gizlendi)" : "";
            output.WriteLine(r.Updates.Count == 0
                ? $"{name}  güncel{hidden}"
                : $"{name}  {r.Updates.Count} güncelleme{hidden}");

            var idWidth = r.Updates.Select(p => p.Id.Length).DefaultIfEmpty(0).Max();
            foreach (var p in r.Updates.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                var major = p.IsMajor ? "  MAJOR" : "";
                output.WriteLine($"  {p.Id.PadRight(idWidth)}  {p.Current} {arrow} {p.Available}{major}");
            }
        }

        var total = results.Sum(r => r.Updates.Count);
        var errors = results.Count(r => r.Error is not null);
        output.WriteLine();
        output.WriteLine(total > 0 ? $"Toplam {total} güncelleme." : "Güncelleme yok.");
        if (errors > 0)
            output.WriteLine($"{errors} kaynakta hata.");
        await output.FlushAsync(ct);

        return total > 0 ? UpdatesFound : errors > 0 ? Error : NoUpdates;
    }

    private static async Task<SourceResult> ScanAsync(
        IPackageSource source, ConfigStore? config, Func<PackageUpdate, bool>? exclude, CancellationToken ct)
    {
        try
        {
            // Kaynaklar senkron iş de yapabilir; birbirini bekletmesin.
            return await Task.Run(async () =>
            {
                if (!await source.IsAvailableAsync(ct))
                    return new SourceResult(source, false, [], 0, null);

                var result = await source.GetOutdatedAsync(ct);
                var shown = result.Updates
                    .Where(p => config?.IsAlreadyInstalled(p) != true && exclude?.Invoke(p) != true)
                    .ToList();
                return new SourceResult(source, true, shown, result.Updates.Count - shown.Count, null);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return new SourceResult(source, true, [], 0, "iptal edildi");
        }
        catch (Exception ex)
        {
            return new SourceResult(source, true, [], 0, ex.Message);
        }
    }

    private static bool CanEncode(Encoding encoding, string text)
    {
        try
        {
            Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                .GetBytes(text);
            return true;
        }
        catch (Exception ex) when (ex is EncoderFallbackException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // ---------- Konsol ----------

    /// <summary>
    /// Standart çıktıyı hazırlar ve <see cref="RunAsync"/>'i çalıştırır.
    /// WinExe'nin konsolu yoktur: çıktı yönlendirilmemişse üst sürecin konsoluna bağlanır.
    /// </summary>
    public static async Task<int> RunConsoleAsync(
        IReadOnlyList<IPackageSource> sources,
        ConfigStore? config = null,
        Func<PackageUpdate, bool>? exclude = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Sadece boru veya dosya yönlendirme sayılır; geçersiz ya da konsol tanıtıcısı sayılmaz.
        var handle = GetStdHandle(StdOutputHandle);
        var redirected = handle != 0 && handle != -1 && GetFileType(handle) is FileTypeDisk or FileTypePipe;
        Encoding? previous = null;
        Encoding encoding;
        Stream stream;

        if (redirected)
        {
            // Boru veya dosya (ör. PowerShell'de "| Out-Host"). Akış, konsola bağlanmadan önce açılır.
            stream = Console.OpenStandardOutput();
            // PowerShell boruyu konsolun kod sayfasıyla çözer; aynı kodlamayla yaz.
            var cp = AttachConsole(AttachParentProcess) ? GetConsoleOutputCP() : 0u;
            encoding = cp is 0 or 65001 ? new UTF8Encoding(false) : GetEncodingOrUtf8((int)cp);
        }
        else
        {
            // cmd gibi doğrudan konsoldan çalıştırıldı.
            if (!AttachConsole(AttachParentProcess))
                return Error;
            stream = Console.OpenStandardOutput();
            try
            {
                previous = Console.OutputEncoding;
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (IOException)
            {
                previous = null;
            }
            encoding = new UTF8Encoding(false);
        }

        try
        {
            using var writer = new StreamWriter(stream, encoding) { AutoFlush = true };
            if (!redirected)
                writer.WriteLine();   // cmd beklemediği için istemden sonraki satıra geç.
            return await RunAsync(sources, writer, config, exclude);
        }
        finally
        {
            // Konsolun kod sayfası paylaşılır; kullanıcının ayarını geri ver.
            if (previous is not null)
            {
                try { Console.OutputEncoding = previous; }
                catch (IOException) { }
            }
        }
    }

    private static Encoding GetEncodingOrUtf8(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new UTF8Encoding(false);
        }
    }

    private const int StdOutputHandle = -11;
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int stdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(nint handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();
}
