using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class IndexModel(AppDbContext db, ManualOrderService manualOrders) : AdminPageModel
{
    public int UserCount { get; private set; }
    public int FeatureCount { get; private set; }
    public int OrderCount { get; private set; }
    public int TelemetryCount { get; private set; }
    public int ReviewCount { get; private set; }
    public bool ManualPaymentAvailable { get; private set; }
    public async Task OnGetAsync()
    {
        UserCount = await db.Users.CountAsync();
        FeatureCount = await db.FeatureDefinitions.CountAsync();
        OrderCount = await db.Orders.CountAsync();
        ReviewCount = await db.Orders.CountAsync(x => x.Status == "review_requested");
        ManualPaymentAvailable = await manualOrders.AvailableAsync();
        TelemetryCount = await db.GameTelemetry.CountAsync();
    }
}
