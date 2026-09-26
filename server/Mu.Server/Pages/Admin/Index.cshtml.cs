using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

public sealed class IndexModel(AppDbContext db) : AdminPageModel
{
    public int UserCount { get; private set; }
    public int FeatureCount { get; private set; }
    public int OrderCount { get; private set; }
    public async Task OnGetAsync()
    {
        UserCount = await db.Users.CountAsync();
        FeatureCount = await db.FeatureDefinitions.CountAsync();
        OrderCount = await db.Orders.CountAsync();
    }
}
