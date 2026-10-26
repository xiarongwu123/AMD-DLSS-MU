using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mu.Server;
using Mu.Server.Services;

internal static class RemoteGuidanceHttpTests
{
    public static async Task RunAsync()
    {
        var count=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        using var factory=new AccountFactory();using var client=factory.CreateClient();
        var config=factory.Services.GetRequiredService<IConfiguration>();config["ManualPayment:Enabled"]="true";config["ManualPayment:Instructions"]="isolated test payment only";
        var offers=await client.GetFromJsonAsync<JsonElement>("/api/v1/plans");
        var plans=offers.GetProperty("items").EnumerateArray().ToArray();
        Check(plans.Length==4,"all four offers must be present");
        var lifetime=plans.Single(p=>p.GetProperty("id").GetString()=="mu-pro-lifetime");
        Check(lifetime.GetProperty("priceMinor").GetInt32()==8880&&lifetime.GetProperty("configurationSessions").GetInt32()==3,"lifetime pricing/credits not serialized");
        Check(!plans.Single(p=>p.GetProperty("id").GetString()=="mu-remote-once").GetProperty("grantsMembership").GetBoolean(),"service offer falsely includes membership");
        const string email="guidance-http@example.test";
        await client.PostAsJsonAsync("/api/v1/auth/code",new{email,purpose="register"});
        var registered=await client.PostAsJsonAsync("/api/v1/auth/register",new{email,code=factory.Mail.Codes[email+":register"],password="correct horse battery",termsVersion="2026-09-26",deviceName="guidance test"});
        var session=(await registered.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",session.AccessToken);
        var created=await client.PostAsJsonAsync("/api/v1/orders",new{planId="mu-remote-once"});
        Check(created.IsSuccessStatusCode,"service-only order cannot be created: "+await created.Content.ReadAsStringAsync());
        var order=await created.Content.ReadFromJsonAsync<JsonElement>();var id=order.GetProperty("id").GetString()!;
        Check(order.GetProperty("amountMinor").GetInt32()==3000&&!order.GetProperty("grantsMembership").GetBoolean(),"created order lost service-only snapshot");
        Check((await client.PostAsJsonAsync("/api/v1/account/authorize",new{featureKey="services.remote"})).StatusCode==HttpStatusCode.Forbidden,"unpaid service must not unlock contact");
        using(var scope=factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<ManualOrderService>().ConfirmAsync(id,"isolated-test-reference","isolated test confirmation","test-admin");
        var me=(await client.GetFromJsonAsync<AccountSnapshot>("/api/v1/account/me"))!;
        Check(me.Membership.Tier=="standard"&&me.Membership.RemoteSessionsRemaining==1,"service-only payment changes software tier or loses credit");
        for(var i=0;i<3;i++)Check((await client.PostAsJsonAsync("/api/v1/account/authorize",new{featureKey="services.remote"})).IsSuccessStatusCode,"paid contact permission failed");
        var detail=await client.GetFromJsonAsync<JsonElement>("/api/v1/orders/"+id);
        Check(!detail.GetProperty("grantsMembership").GetBoolean()&&detail.GetProperty("configurationSessionsUsed").GetInt32()==0,"viewing contact must never consume a guidance credit");
        using(var scope=factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(id,"isolated test guidance finished","test-admin",0);
        detail=await client.GetFromJsonAsync<JsonElement>("/api/v1/orders/"+id);
        Check(detail.GetProperty("configurationSessionsUsed").GetInt32()==1&&detail.GetProperty("configurationServiceStatus").GetString()=="completed","completion not exposed on order detail");
        Check((await client.PostAsJsonAsync("/api/v1/account/authorize",new{featureKey="services.remote"})).StatusCode==HttpStatusCode.Forbidden,"spent service-only credit still unlocks contact");
        Console.WriteLine($"Remote guidance HTTP: {count} assertions passed.");
    }
}
