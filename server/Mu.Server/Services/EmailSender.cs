using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mu.Server.Services;

public interface IVerificationEmailSender
{
    Task SendAsync(string email, string code, string purpose, string requestId, CancellationToken cancellationToken = default);
}

public sealed class ResendEmailSender(HttpClient client, IConfiguration config, ILogger<ResendEmailSender> logger, TimeProvider clock) : IVerificationEmailSender
{
    public async Task SendAsync(string email, string code, string purpose, string requestId, CancellationToken cancellationToken = default)
    {
        var key = config["Resend:ApiKey"];
        var from = config["Resend:From"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(from))
            throw new ApiException(503, "email_unavailable", "邮件服务暂未配置，请稍后重试。");
        var action = purpose == "register" ? "注册账号" : "重置密码";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new("Bearer", key);
        request.Headers.Add("Idempotency-Key", requestId);
        request.Content = JsonContent.Create(new
        {
            from,
            to = new[] { email },
            subject = $"MU {action}验证码",
            text = $"您的 MU {action}验证码是 {code}，10 分钟内有效。请勿向他人透露验证码。如果并非您本人操作，请忽略此邮件。"
        });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Verification email provider returned HTTP {StatusCode}", (int)response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var name = await ReadErrorNameAsync(response, timeout.Token);
                    var now = clock.GetUtcNow();
                    var retry = ReadRetryAfter(response, now);
                    if (name == "daily_quota_exceeded")
                    {
                        // Resend's free daily quota resets at midnight UTC, not after a rolling 24 hours.
                        var midnight = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                        var seconds = (int)Math.Ceiling((midnight - now).TotalSeconds);
                        throw new ApiException(429, "email_daily_quota_exceeded", "邮件服务今日发送额度已用尽，请在 UTC 零点额度重置后重试，或等待管理员处理。",
                            Math.Max(60, Math.Max(seconds, retry ?? seconds)));
                    }
                    if (name == "monthly_quota_exceeded")
                        throw new ApiException(429, "email_monthly_quota_exceeded", "邮件服务本月发送额度已用尽，请等待额度恢复或管理员处理。", retry);
                    throw new ApiException(429, "email_rate_limited", "邮件服务请求过于频繁，请稍后重新获取验证码。",
                        Math.Max(60, retry ?? ReadResetSeconds(response) ?? 60));
                }
                throw new ApiException(503, "email_unavailable", "验证码暂时发送失败，请稍后重试。");
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            throw new ApiException(503, "email_unavailable", "验证码暂时发送失败，请稍后重试。");
        }
    }

    private static async Task<string?> ReadErrorNameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maximumLength = 8192;
        if (response.Content.Headers.ContentLength > maximumLength) return null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[maximumLength + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0) break;
                length += read;
            }
            if (length > maximumLength) return null;
            using var error = JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 8 });
            if (error.RootElement.ValueKind != JsonValueKind.Object || !error.RootElement.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String) return null;
            return name.GetString() switch
            {
                "daily_quota_exceeded" => "daily_quota_exceeded",
                "monthly_quota_exceeded" => "monthly_quota_exceeded",
                _ => null
            };
        }
        catch (Exception error) when (error is JsonException or IOException or HttpRequestException) { return null; }
    }

    private static int? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values)) return null;
        foreach (var value in values)
        {
            if (!RetryConditionHeaderValue.TryParse(value, out var parsed)) continue;
            var seconds = parsed.Delta?.TotalSeconds ?? (parsed.Date - now)?.TotalSeconds;
            if (seconds is >= 0 and <= int.MaxValue) return Math.Max(1, (int)Math.Ceiling(seconds.Value));
        }
        return null;
    }

    private static int? ReadResetSeconds(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("ratelimit-reset", out var values)) return null;
        foreach (var value in values)
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                return Math.Max(1, seconds);
        return null;
    }
}
