using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mu.Neural;

namespace AmdNrAssistant;

public static class NeuralRuleClient
{
    public static async Task<NeuralRuleDocument?> FetchAsync(string baseUrl, string gameId, CancellationToken token, bool forceRefresh = false)
    {
        var baseUri = new Uri(baseUrl);
        if (baseUri.Scheme != "https") throw new IOException("兼容规则服务必须使用 HTTPS。");
        var cache = Path.Combine(Core.InstallerCacheDirectory, "neural-rules",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseUrl + gameId))) + ".json");
        Core.RejectLinks(cache);
        if (!forceRefresh && File.Exists(cache) && File.GetLastWriteTimeUtc(cache) > DateTime.UtcNow.AddMinutes(-5))
        {
            try { var local = NeuralRules.Parse(File.ReadAllText(cache)); if (local.ExpiresUtc > DateTimeOffset.UtcNow) return local; }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { }
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 1024 * 1024 };
            using var response = await http.GetAsync(new Uri(baseUri, "api/v1/render-rules?gameId=" + Uri.EscapeDataString(gameId)), token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("规则服务重定向到非 HTTPS 地址。");
            var json = await response.Content.ReadAsStringAsync(token); var document = NeuralRules.Parse(json);
            if (document.ExpiresUtc <= DateTimeOffset.UtcNow || document.ExpiresUtc > DateTimeOffset.UtcNow.AddDays(31))
                throw new IOException("兼容规则有效期无效。");
            NeuralRenderInstaller.AtomicWrite(cache, Encoding.UTF8.GetBytes(json)); return document;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
        {
            if (File.Exists(cache))
            {
                try { var cached = NeuralRules.Parse(File.ReadAllText(cache)); if (cached.ExpiresUtc > DateTimeOffset.UtcNow) return cached; }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException) { }
            }
            return null;
        }
    }
}
