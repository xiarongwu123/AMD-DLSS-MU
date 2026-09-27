using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mu.Server.Pages.Admin;

[Authorize(Policy = AdminSecurity.Scheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class AdminPageModel : PageModel
{
    protected string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Administrator identity missing.");
    protected bool ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
        {
            ModelState.AddModelError("", "请填写 3 至 500 字的操作原因。");
            return false;
        }
        return true;
    }
    public static string FormatTime(long? value) => value is null ? "—" : DateTimeOffset.FromUnixTimeSeconds(value.Value).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss") + " UTC+8";
}
