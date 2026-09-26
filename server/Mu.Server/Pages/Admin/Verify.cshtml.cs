using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

[Authorize(Policy = AdminSecurity.PendingScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VerifyModel(UserManager<ApplicationUser> users, AdminLoginThrottle throttle, AppDbContext db) : PageModel
{
    [BindProperty, Required, StringLength(8, MinimumLength = 6)] public string Code { get; set; } = "";
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? RedirectToPage("/Admin/Login") : !await users.GetTwoFactorEnabledAsync(user) ? RedirectToPage("/Admin/Setup") : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Admin/Login");
        if (!await users.GetTwoFactorEnabledAsync(user)) return RedirectToPage("/Admin/Setup");
        if (!ModelState.IsValid) return Page();
        if (!throttle.Allow("totp", user.Id, 10) || await users.IsLockedOutAsync(user))
        {
            ModelState.AddModelError("", "验证次数过多，请稍后重新登录。");
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Page();
        }
        var code = AdminSecurity.NormalizeCode(Code);
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)
            || !throttle.ConsumeCode(user.Id, code))
        {
            await users.AccessFailedAsync(user);
            ModelState.AddModelError("", "动态码无效或已使用，请等待新的动态码。");
            return Page();
        }
        await users.ResetAccessFailedCountAsync(user);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid().ToString("N"), Actor = user.Id, UserId = user.Id,
            Action = "admin.login", Reason = "密码与双因素验证通过", BeforeJson = "{}", AfterJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        await db.SaveChangesAsync();
        await AdminSecurity.SignInAsync(HttpContext, users, user, verified: true);
        return RedirectToPage("/Admin/Index");
    }
}
