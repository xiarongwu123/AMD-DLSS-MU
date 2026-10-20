using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Mu.Server;
using Mu.Server.Data;

internal static class SupportedGamesHttpTests
{
    public static async Task RunAsync()
    {
        var count=0;
        void Check(bool value,string name) { if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name); }
        using var factory=new AccountFactory();
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false });
        const string route="/api/v1/catalog/supported-games";
        Check((await client.GetAsync(route)).StatusCode==HttpStatusCode.Unauthorized,"anonymous cannot read paid catalog");
        const string email="supported-games@example.test";
        await client.PostAsJsonAsync("/api/v1/auth/code",new {email,purpose="register"});
        var registration=await client.PostAsJsonAsync("/api/v1/auth/register",new {email,code=factory.Mail.Codes[email+":register"],password="correct horse battery",termsVersion="2026-09-26",deviceName="catalog test"});
        Check(registration.IsSuccessStatusCode,"register catalog test account");
        var session=(await registration.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",session.AccessToken);
        var denied=await client.GetAsync(route);
        Check(denied.StatusCode==HttpStatusCode.Forbidden && !(await denied.Content.ReadAsStringAsync()).Contains("aliases"),"standard account receives no catalog data");
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user=await db.Users.SingleAsync(u=>u.Id==session.Account.Id);user.ProExpiresAt=DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();await db.SaveChangesAsync();
            var feature=await db.FeatureDefinitions.SingleAsync(f=>f.Key=="games.supported.view");Check(feature.MinimumTier=="pro","new catalog feature defaults to Pro only");
        }
        var allowed=await client.GetAsync(route);Check(allowed.IsSuccessStatusCode,"active Pro can read catalog");
        using(var json=JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()))
            Check(json.RootElement.GetProperty("catalog").GetProperty("games").GetArrayLength()>=100,"actual paid catalog contains 100+ games");
        Check(allowed.Headers.CacheControl?.NoStore==true,"paid response is not cacheable");
        var snapshot=(await client.GetFromJsonAsync<AccountSnapshot>("/api/v1/account/me"))!;
        Check(new[]{"games.supported.view","schemes.premium.configure","schemes.game.view"}.All(key=>snapshot.Features.Single(f=>f.Key==key).Allowed),"ordinary advanced membership unlocks every software scheme and catalog");
        Check(!snapshot.Features.Single(f=>f.Key=="services.remote").Allowed,"remote service remains Plus-only");
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user=await db.Users.SingleAsync(u=>u.Id==session.Account.Id);user.ProExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();await db.SaveChangesAsync();
        }
        Check((await client.GetAsync(route)).StatusCode==HttpStatusCode.Forbidden,"expired Pro cannot read catalog");
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user=await db.Users.SingleAsync(u=>u.Id==session.Account.Id);user.ProExpiresAt=DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
            (await db.FeatureDefinitions.SingleAsync(f=>f.Key=="games.supported.view")).Enabled=false;await db.SaveChangesAsync();
        }
        Check((await client.GetAsync(route)).StatusCode==HttpStatusCode.Forbidden,"disabled feature denies even active Pro");
        Check((await client.GetAsync("/Data/Catalog/supported-games.json")).StatusCode==HttpStatusCode.NotFound,"catalog has no public static file endpoint");
        Console.WriteLine($"Supported games HTTP: {count} assertions passed.");
    }
}
