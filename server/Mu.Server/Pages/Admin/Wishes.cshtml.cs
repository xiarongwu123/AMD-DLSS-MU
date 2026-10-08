using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;
using Mu.Wishes;

namespace Mu.Server.Pages.Admin;

public sealed class WishesModel(AppDbContext db, WishService wishes, TimeProvider clock) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty] public string Status { get; set; } = "pending";
    [BindProperty] public string ExpectedStatus { get; set; } = "";
    [BindProperty] public string Reason { get; set; } = "";
    public WishList Listing { get; private set; } = new([], new(0, 0, 0, 0, 0, 0), 0, 1, 20);
    public WishDetail? Detail { get; private set; }
    public static string StatusName(string status) => status switch
    { "planned" => "已计划", "developing" => "开发中", "completed" => "已完成", _ => "待评估" };

    public async Task OnGetAsync()
    {
        if (Filter is not ("all" or "hot" or "planned" or "developing" or "completed")) Filter = "all";
        Search = Search?.Trim() ?? "";
        if (Search.Length > 100) Search = Search[..100];
        PageNumber = Math.Clamp(PageNumber, 1, 10000);
        Listing = await wishes.ListAsync(Actor, Filter, Search, "newest", PageNumber, HttpContext.RequestAborted);
        if (!string.IsNullOrEmpty(Id))
        {
            try { Detail = await wishes.DetailAsync(Actor, Id, HttpContext.RequestAborted); }
            catch (ApiException e) when (e.StatusCode == 404) { ModelState.AddModelError("", e.Message); }
        }
    }

    public async Task<IActionResult> OnGetImageAsync(string id)
    {
        var data = await db.Wishes.Where(w => w.Id == id).Select(w => w.ImageData).SingleOrDefaultAsync();
        if (data == null) return NotFound();
        return File(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]), data.StartsWith("data:image/png;") ? "image/png" : "image/jpeg");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ValidateReason(Reason);
        if (Status is not ("pending" or "planned" or "developing" or "completed")) ModelState.AddModelError("", "无效的愿望状态。");
        if (ModelState.IsValid)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var wish = await db.Wishes.SingleOrDefaultAsync(w => w.Id == Id);
            if (wish == null) return NotFound();
            if (wish.Status != ExpectedStatus) ModelState.AddModelError("", "愿望状态已被更新，请刷新后重试。");
            else
            {
                var before = wish.Status;
                wish.Status = Status;
                db.AuditEntries.Add(new() { Actor = Actor, Action = "wish.status", UserId = wish.UserId,
                    Reason = Reason.Trim(), CreatedAt = clock.GetUtcNow().ToUnixTimeSeconds(),
                    BeforeJson = JsonSerializer.Serialize(new { wish.Id, Status = before }),
                    AfterJson = JsonSerializer.Serialize(new { wish.Id, wish.Status }) });
                await db.SaveChangesAsync(); await transaction.CommitAsync();
                TempData["Notice"] = "愿望状态已更新，客户端刷新后可见。";
                return RedirectToPage(new { Filter, Search, PageNumber });
            }
        }
        await OnGetAsync();
        return Page();
    }
}
