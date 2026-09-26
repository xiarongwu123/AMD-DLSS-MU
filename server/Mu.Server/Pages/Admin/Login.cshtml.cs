using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

[AllowAnonymous]
public sealed class LoginModel(UserManager<ApplicationUser> users, AdminLoginThrottle throttle) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [BindProperty, Required, StringLength(128)] public string Password { get; set; } = "";

    public void OnGet() => Response.Headers.CacheControl = "no-store";

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid) return Page();
        var allowedIp = throttle.Allow("password-ip", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", 30);
        var allowedEmail = throttle.Allow("password-email", Email.Trim().ToUpperInvariant(), 10);
        if (!allowedIp || !allowedEmail)
        {
            ModelState.AddModelError("", "尝试次数过多，请在 10 分钟后重试。");
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Page();
        }
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is null || user.DisabledAt is not null || await users.IsLockedOutAsync(user)
            || !await users.IsInRoleAsync(user, AdminSecurity.Role) || !await users.CheckPasswordAsync(user, Password))
        {
            if (user is not null) await users.AccessFailedAsync(user);
            ModelState.AddModelError("", "账号、密码无效，或账号暂不可用。");
            return Page();
        }
        await AdminSecurity.SignInAsync(HttpContext, users, user, verified: false);
        return RedirectToPage(await users.GetTwoFactorEnabledAsync(user) ? "/Admin/Verify" : "/Admin/Setup");
    }
}
