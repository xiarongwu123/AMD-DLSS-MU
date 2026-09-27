using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

public sealed class OrdersModel(AppDbContext db) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    public List<Order> Items { get; private set; } = [];
    public Dictionary<string, string> Emails { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Search = Search?.Trim();
        if (Search?.Length > 128) Search = Search[..128];
        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrEmpty(Search)) query = query.Where(order => order.Id == Search || order.UserId == Search);
        Items = await query.OrderByDescending(order => order.CreatedAt).Take(100).ToListAsync();
        var userIds = Items.Select(order => order.UserId).Distinct().ToArray();
        Emails = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.Email ?? user.Id);
    }
}
