using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Server.Data;
using Mu.Telemetry;

internal static class TelemetryHttpTests
{
    public static async Task<int> RunAsync(AccountFactory factory, HttpClient client)
    {
        int assertions = 0;
        void Check(bool condition, string label) { assertions++; if (!condition) throw new Exception("Telemetry HTTP: " + label); }
        var now = DateTimeOffset.UtcNow;
        var session = new GameTelemetryUpload(Guid.NewGuid().ToString("D"), "Cyberpunk 2077", "1091500", "2.3",
            "Radeon RX 7900 XT", 20480, "32.0.1", "AMD Ryzen", 32768, "Windows 11", "optiscaler", "2.0",
            now.AddMinutes(-2), now, "DXGI", 1000, 120, 71, 6.2, 4.1, "display_change", "captured");
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Check((await anonymous.PostAsJsonAsync("/api/v1/telemetry/sessions", session)).StatusCode == HttpStatusCode.Unauthorized, "authentication required");
        var response = await client.PostAsJsonAsync("/api/v1/telemetry/sessions", session);
        Check(response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<GameTelemetryReceipt>())?.Stored == true, "new session stored");
        var retry = await client.PostAsJsonAsync("/api/v1/telemetry/sessions", session);
        Check(retry.IsSuccessStatusCode && (await retry.Content.ReadFromJsonAsync<GameTelemetryReceipt>())?.Stored == false, "retry is idempotent");
        Check((await client.PostAsJsonAsync("/api/v1/telemetry/sessions", session with { AverageFps = 121 })).StatusCode == HttpStatusCode.Conflict, "same session ID cannot replace metrics");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.GameTelemetry.AsNoTracking().SingleAsync(x => x.Id == session.SessionId);
            Check(stored.FrameCount == 1000 && stored.AverageFps == 120 && stored.PayloadJson.Contains("optiscaler"), "metrics retained without duplicate rows");
        }
        Check((await client.PostAsJsonAsync("/api/v1/telemetry/sessions", session with { SessionId = Guid.NewGuid().ToString("D"), AverageFps = -1 })).StatusCode == HttpStatusCode.BadRequest, "invalid FPS rejected");
        Check((await client.PostAsJsonAsync("/api/v1/telemetry/sessions", session with { SessionId = Guid.NewGuid().ToString("D"), FrameCount = 10 })).StatusCode == HttpStatusCode.BadRequest, "insufficient frames rejected");
        Check((await client.PostAsJsonAsync("/api/v1/telemetry/sessions", session with { SessionId = Guid.NewGuid().ToString("D"), CpuName = @"C:\Users\private" })).StatusCode == HttpStatusCode.BadRequest, "paths rejected");
        Check((await anonymous.DeleteAsync("/api/v1/telemetry/sessions")).StatusCode == HttpStatusCode.Unauthorized, "deletion requires authentication");
        Check((await client.DeleteAsync("/api/v1/telemetry/sessions")).StatusCode == HttpStatusCode.NoContent, "own sessions deleted");
        using (var scope = factory.Services.CreateScope())
            Check(!await scope.ServiceProvider.GetRequiredService<AppDbContext>().GameTelemetry.AnyAsync(x => x.Id == session.SessionId), "deleted row absent");
        Console.WriteLine($"Telemetry HTTP passed: {assertions} assertions.");
        return assertions;
    }
}
