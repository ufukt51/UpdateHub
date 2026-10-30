using System.Net.NetworkInformation;

namespace UpdateHub.Services;

/// <summary>
/// winget indirmeyi Delivery Optimization hizmetine yaptırır; dosya bitene kadar görünmez ve çıktıya ilerleme yazılmaz.
/// Toplam boyut indirme adresinden (Content-Length), indirilen miktar ağ arabirimlerinin aldığı bayt farkından tahmin edilir.
/// Aynı anda başka trafik varsa tahmin fazla çıkar.
/// </summary>
public static class DownloadMeter
{
    /// <summary>Etkin ağ arabirimlerinin şimdiye kadar aldığı toplam bayt.</summary>
    public static long BytesReceived()
    {
        long total = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                total += n.GetIPStatistics().BytesReceived;
            }
            catch (NetworkInformationException)
            {
            }
        }
        return total;
    }
}
