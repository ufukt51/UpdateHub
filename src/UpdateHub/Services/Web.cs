using System.Net.Http;

namespace UpdateHub.Services;

/// <summary>Paylaşılan HttpClient ve küçük yardımcılar (boyut sorgusu, JSON).</summary>
public static class Web
{
    public static HttpClient Http { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdateHub");
        return client;
    }

    /// <summary>Dosyanın boyutu; sunucu bildirmiyorsa null. HEAD desteklenmezse yalnız başlıkları okuyan GET dener.</summary>
    public static async Task<long?> GetLengthAsync(string url, CancellationToken ct)
    {
        try
        {
            using (var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (head.IsSuccessStatusCode && head.Content.Headers.ContentLength is long length && length > 0)
                    return length;
            }
            using var get = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, ct);
            return get.IsSuccessStatusCode ? get.Content.Headers.ContentLength : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>Metin içerik; 404 veya ağ hatasında null.</summary>
    public static async Task<string?> GetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            using var r = await Http.GetAsync(url, ct);
            return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
