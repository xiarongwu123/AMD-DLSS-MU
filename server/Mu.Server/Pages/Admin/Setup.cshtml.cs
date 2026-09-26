using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

[Authorize(Policy = AdminSecurity.PendingScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SetupModel(UserManager<ApplicationUser> users, AdminLoginThrottle throttle, AppDbContext db) : PageModel
{
    [BindProperty, Required, StringLength(8, MinimumLength = 6)] public string Code { get; set; } = "";
    public string SharedKey { get; private set; } = "";
    public string AccountEmail { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Admin/Login");
        if (await users.GetTwoFactorEnabledAsync(user)) return RedirectToPage("/Admin/Verify");
        await LoadKeyAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Admin/Login");
        if (await users.GetTwoFactorEnabledAsync(user)) return RedirectToPage("/Admin/Verify");
        await LoadKeyAsync(user);
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
            ModelState.AddModelError("", "动态码无效或已使用，请检查设备时间并使用新的动态码。");
            return Page();
        }
        var result = await users.SetTwoFactorEnabledAsync(user, true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "双因素验证设置失败，请重试。");
            return Page();
        }
        await users.ResetAccessFailedCountAsync(user);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid().ToString("N"), Actor = user.Id, UserId = user.Id,
            Action = "admin.totp.enrolled", Reason = "首次绑定身份验证器", BeforeJson = "{}", AfterJson = "{\"twoFactorEnabled\":true}",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        await db.SaveChangesAsync();
        await AdminSecurity.SignInAsync(HttpContext, users, user, verified: true);
        return RedirectToPage("/Admin/Index");
    }

    private async Task LoadKeyAsync(ApplicationUser user)
    {
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded) throw new InvalidOperationException("Unable to initialize authenticator.");
            key = await users.GetAuthenticatorKeyAsync(user);
            // ResetAuthenticatorKey rotates the stamp. Refresh only the pending cookie, never elevate it.
            await AdminSecurity.SignInAsync(HttpContext, users, user, verified: false);
        }
        SharedKey = key ?? "";
        AccountEmail = user.Email ?? "";
    }
}
