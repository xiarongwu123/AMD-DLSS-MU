using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class UsersModel(AppDbContext db, UserManager<ApplicationUser> users, AccountManagementService management) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty] public string UserId { get; set; } = "";
    [BindProperty] public string Reason { get; set; } = "";
    [BindProperty] public int Days { get; set; } = 30;
    public List<ApplicationUser> Items { get; private set; } = [];
    public HashSet<string> AdminIds { get; private set; } = [];
    public long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public Task OnGetAsync() => LoadAsync();
    public Task<IActionResult> OnPostDisableAsync() => ChangeAsync(() => management.SetDisabledAsync(UserId, true, Reason.Trim(), Actor));
    public Task<IActionResult> OnPostEnableAsync() => ChangeAsync(() => management.SetDisabledAsync(UserId, false, Reason.Trim(), Actor));
    public Task<IActionResult> OnPostRevokeProAsync() => ChangeAsync(() => management.SetProExpiryAsync(UserId, null, Reason.Trim(), Actor));
    public Task<IActionResult> OnPostRevokeSessionsAsync() => ChangeAsync(() => management.RevokeSessionsAsync(UserId, Reason.Trim(), Actor));
    public Task<IActionResult> OnPostGrantAsync()
    {
        if (Days is < 1 or > 3650) ModelState.AddModelError("", "会员期限必须为 1 至 3650 天。");
        return ChangeAsync(() => management.GrantProDaysAsync(UserId, Days, Reason.Trim(), Actor));
    }

    private async Task<IActionResult> ChangeAsync(Func<Task> change)
    {
        ValidateReason(Reason);
        var target = await users.FindByIdAsync(UserId);
        if (target is null) return NotFound();
        if (await users.IsInRoleAsync(target, AdminSecurity.Role))
            ModelState.AddModelError("", "管理员账号需要通过服务器维护。");
        if (ModelState.IsValid)
        {
            try
            {
                await change();
                TempData["Notice"] = "操作已完成并记录审计。新操作会读取最新权限。";
                return RedirectToPage(new { Search });
            }
            catch (ArgumentException ex) { ModelState.AddModelError("", ex.Message); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
            catch (ApiException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Search = Search?.Trim();
        if (Search?.Length > 254) Search = Search[..254];
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(Search))
        {
            var normalized = Search.ToUpperInvariant();
            query = query.Where(user => user.Id == Search || (user.NormalizedEmail != null && user.NormalizedEmail.Contains(normalized)));
        }
        Items = await query.OrderByDescending(user => user.CreatedAt).Take(100).ToListAsync();
        AdminIds = (await users.GetUsersInRoleAsync(AdminSecurity.Role)).Select(user => user.Id).ToHashSet();
    }
}
