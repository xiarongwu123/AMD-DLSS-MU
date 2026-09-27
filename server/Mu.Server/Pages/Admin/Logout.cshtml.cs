using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mu.Server.Pages.Admin;

[Authorize(Policy = AdminSecurity.Scheme)]
public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Login");
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(AdminSecurity.Scheme);
        await HttpContext.SignOutAsync(AdminSecurity.PendingScheme);
        return RedirectToPage("/Admin/Login");
    }
}
