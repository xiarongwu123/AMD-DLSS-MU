using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class OrdersModel(AppDbContext db, ManualOrderService manualOrders) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty] public string? OrderId { get; set; }
    [BindProperty] public string? PaymentReference { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public bool ReceiptVerified { get; set; }
    public List<Order> Items { get; private set; } = [];
    public Dictionary<string, string> Emails { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Search = Search?.Trim();
        if (Search?.Length > 128) Search = Search[..128];
        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrEmpty(Search)) query = query.Where(order => order.Id == Search || order.UserId == Search);
        Items = await query.OrderByDescending(order => order.Status == "review_requested")
            .ThenByDescending(order => order.CreatedAt).Take(100).ToListAsync();
        var userIds = Items.Select(order => order.UserId).Distinct().ToArray();
        Emails = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.Email ?? user.Id);
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        if (!ReceiptVerified) ModelState.AddModelError("", "请先在收款平台独立核对到账，再勾选确认。");
        if (string.IsNullOrWhiteSpace(OrderId)) ModelState.AddModelError("", "订单编号不能为空。");
        if (!ValidateReason(Reason) || !ModelState.IsValid) { await OnGetAsync(); return Page(); }
        try { await manualOrders.ConfirmAsync(OrderId!, PaymentReference, Reason, Actor); }
        catch (ApiException e) { ModelState.AddModelError("", e.Message); await OnGetAsync(); return Page(); }
        TempData["Notice"] = "订单已确认，会员有效期已更新。";
        return RedirectToPage(new { Search = OrderId });
    }
}
