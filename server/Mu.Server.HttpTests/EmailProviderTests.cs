using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class EmailProviderTests
{
    private const string Sensitive = "provider-secret-that-must-not-escape";
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 20, 0, 0, TimeSpan.Zero);

    public static async Task<int> RunAsync()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        async Task<ApiException> Failure(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
        {
            using var handler = new StubHandler(() => Response(status, body, headers));
            using var client = new HttpClient(handler);
            var logger = new CaptureLogger();
            var sender = Sender(client, logger);
            try { await sender.SendAsync("recipient@example.test", "123456", "register", "isolated-provider-request"); }
            catch (ApiException error)
            {
                Check(handler.Requests == 1, "Provider invoked exactly once; no automatic resend");
                Check(!error.Message.Contains(Sensitive, StringComparison.Ordinal) && !logger.Messages.Any(value => value.Contains(Sensitive, StringComparison.Ordinal)),
                    "Provider body and credentials never appear in application errors or logs");
                return error;
            }
            throw new InvalidOperationException("Expected safe provider error");
        }

        var daily = await Failure(HttpStatusCode.TooManyRequests, Body("daily_quota_exceeded"));
        Check(daily.StatusCode == 429 && daily.Error.Code == "email_daily_quota_exceeded" && daily.Error.RetryAfterSeconds == 14400,
            "Daily quota maps to next UTC midnight, not rolling 24 hours");
        Check(daily.Error.Message.Contains("今日发送额度", StringComparison.Ordinal), "Daily quota explanation is explicit");
        using (var midnightHandler = new StubHandler(() => Response(HttpStatusCode.TooManyRequests, Body("daily_quota_exceeded"))))
        using (var midnightClient = new HttpClient(midnightHandler))
        {
            try
            {
                await Sender(midnightClient, new CaptureLogger(), new DateTimeOffset(Now.UtcDateTime.Date.AddDays(1).AddSeconds(-1), TimeSpan.Zero))
                    .SendAsync("recipient@example.test", "123456", "register", "isolated-midnight");
                throw new InvalidOperationException("Expected daily quota rejection");
            }
            catch (ApiException error) { Check(error.Error.RetryAfterSeconds == 60, "Daily reset near midnight still respects local resend cooldown"); }
        }
        var monthly = await Failure(HttpStatusCode.TooManyRequests, Body("monthly_quota_exceeded"));
        Check(monthly.Error.Code == "email_monthly_quota_exceeded" && monthly.Error.RetryAfterSeconds is null,
            "Monthly quota without provider reset hint does not invent retry time");
        var monthlyWithHint = await Failure(HttpStatusCode.TooManyRequests, Body("monthly_quota_exceeded"), ("Retry-After", "172800"));
        Check(monthlyWithHint.Error.RetryAfterSeconds == 172800, "Monthly quota respects provider retry hint");
        var rate = await Failure(HttpStatusCode.TooManyRequests, Body("rate_limit_exceeded"), ("Retry-After", "120"));
        Check(rate.StatusCode == 429 && rate.Error.Code == "email_rate_limited" && rate.Error.RetryAfterSeconds == 120,
            "Rate limiting respects provider delay");
        var shortRate = await Failure(HttpStatusCode.TooManyRequests, Body("rate_limit_exceeded"), ("Retry-After", "1"));
        Check(shortRate.Error.RetryAfterSeconds == 60, "Rate delay is at least local resend cooldown");
        var dateRate = await Failure(HttpStatusCode.TooManyRequests, Body("rate_limit_exceeded"), ("Retry-After", Now.AddMinutes(3).ToString("R")));
        Check(dateRate.Error.RetryAfterSeconds == 180, "HTTP date retry headers supported");
        var malformed = await Failure(HttpStatusCode.TooManyRequests, "<html>" + Sensitive, ("Retry-After", "invalid"), ("ratelimit-reset", "90"));
        Check(malformed.Error.Code == "email_rate_limited" && malformed.Error.RetryAfterSeconds == 90, "Malformed provider payload has safe 429/reset fallback");
        var oversized = await Failure(HttpStatusCode.TooManyRequests, new string('x', 9000));
        Check(oversized.Error.Code == "email_rate_limited" && oversized.Error.RetryAfterSeconds == 60, "Oversized response discarded safely");
        var badType = await Failure(HttpStatusCode.TooManyRequests, "{\"name\":123}");
        Check(badType.Error.Code == "email_rate_limited", "Unexpected JSON field type handled safely");
        var authError = await Failure(HttpStatusCode.Forbidden, Body("restricted_api_key"));
        Check(authError.StatusCode == 503 && authError.Error.Code == "email_unavailable" && authError.Error.RetryAfterSeconds is null,
            "Credentials/domain failures remain generic");
        var serverError = await Failure(HttpStatusCode.InternalServerError, Body("monthly_quota_exceeded"));
        Check(serverError.StatusCode == 503 && serverError.Error.Code == "email_unavailable", "Provider body cannot override non-429 status");
        using (var successHandler = new StubHandler(() => Response(HttpStatusCode.OK, "{\"id\":\"test-only\"}")))
        using (var successClient = new HttpClient(successHandler))
        {
            await Sender(successClient, new CaptureLogger()).SendAsync("recipient@example.test", "123456", "reset", "isolated-success");
            Check(successHandler.Requests == 1, "Successful provider acceptance preserved");
        }

        using var httpHandler = new StubHandler(() => Response(HttpStatusCode.TooManyRequests, Body("daily_quota_exceeded")));
        using var providerClient = new HttpClient(httpHandler);
        using var factory = new AccountFactory(_ => Sender(providerClient, new CaptureLogger()));
        using var apiClient = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await apiClient.PostAsJsonAsync("/api/v1/auth/code", new { email = "quota-test@example.test", purpose = "register" });
        Check(response.StatusCode == HttpStatusCode.TooManyRequests, "Quota rejection reaches public API as 429");
        var apiError = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        Check(apiError.Code == "email_daily_quota_exceeded" && apiError.RetryAfterSeconds == 14400
            && response.Headers.RetryAfter?.Delta?.TotalSeconds == 14400, "API exposes stable error and matching Retry-After header");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var challenge = await db.VerificationCodes.SingleAsync();
            Check(!challenge.DeliverySucceeded, "Rejected provider request never activates verification code");
            Check(await db.Users.CountAsync() == 0 && await db.AuthSessions.CountAsync() == 0, "Rejected email creates no account or session");
        }
        return assertions;
    }

    private static ResendEmailSender Sender(HttpClient client, CaptureLogger logger, DateTimeOffset? now = null) => new(client,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Resend:ApiKey"] = Sensitive, ["Resend:From"] = "MU <noreply@example.test>" }).Build(), logger, new FixedClock(now ?? Now));
    private static string Body(string name) => System.Text.Json.JsonSerializer.Serialize(new { statusCode = 429, name, message = Sensitive });
    private static HttpResponseMessage Response(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class StubHandler(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(reply());
        }
    }
    private sealed class CaptureLogger : ILogger<ResendEmailSender>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
