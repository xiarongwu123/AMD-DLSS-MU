using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

public sealed class PlansModel(AppDbContext db) : AdminPageModel
{
    [BindProperty] public string? Id { get; set; }
    [BindProperty, Required, StringLength(80)] public string Name { get; set; } = "";
    [BindProperty, Range(1, 3650)] public int DurationDays { get; set; }
    [BindProperty, Range(typeof(decimal), "0", "1000000")] public decimal Price { get; set; }
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string Reason { get; set; } = "";
    public List<Plan> Items { get; private set; } = [];
    public async Task OnGetAsync() => Items = await db.Plans.AsNoTracking().OrderBy(plan => plan.DurationDays).ToListAsync();
    public async Task<IActionResult> OnPostAsync()
    {
        ValidateReason(Reason);
        if (Price * 100 != decimal.Truncate(Price * 100)) ModelState.AddModelError("", "价格最多保留两位小数。");
        if (string.IsNullOrWhiteSpace(Name)) ModelState.AddModelError("", "请输入套餐名称。");
        if (!ModelState.IsValid) { await OnGetAsync(); return Page(); }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var isNew = string.IsNullOrEmpty(Id);
        var plan = isNew ? new Plan() : await db.Plans.SingleOrDefaultAsync(item => item.Id == Id);
        if (plan is null) return NotFound();
        var before = isNew ? "{}" : JsonSerializer.Serialize(plan);
        plan.Name = Name.Trim();
        plan.DurationDays = DurationDays;
        plan.PriceMinor = decimal.ToInt64(Price * 100);
        plan.Currency = "CNY";
        plan.Enabled = !isNew && Enabled;
        if (isNew) db.Plans.Add(plan);
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid().ToString("N"), Actor = Actor, Action = isNew ? "plan.created" : "plan.updated",
            Reason = Reason.Trim(), BeforeJson = before, AfterJson = JsonSerializer.Serialize(plan),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Notice"] = "套餐已保存。购买入口仍处于关闭状态。";
        return RedirectToPage();
    }
}
