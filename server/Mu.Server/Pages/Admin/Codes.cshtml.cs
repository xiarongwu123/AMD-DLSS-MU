using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class CodesModel(AppDbContext db, MembershipCodeService codes) : AdminPageModel
{
    [BindProperty] public string? PlanId { get; set; }
    public List<Plan> Plans { get; private set; } = [];
    [BindProperty, Range(1, 3650)] public int DurationDays { get; set; } = 30;
    [BindProperty, Range(1, 100)] public int Count { get; set; } = 1;
    [BindProperty, Range(1, 3650)] public int ValidDays { get; set; } = 90;
    [BindProperty] public string Reason { get; set; } = "";
    public string[] Generated { get; private set; } = [];
    public List<MembershipCode> Items { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Plans = await db.Plans.AsNoTracking().Where(p => p.Enabled).ToListAsync();
        Items = await db.MembershipCodes.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        ValidateReason(Reason);
        if (ModelState.IsValid)
        {
            try { Generated = await codes.GenerateAsync(DurationDays, Count, ValidDays, Actor, Reason, PlanId); }
            catch (ApiException error) { ModelState.AddModelError("", error.Message); }
        }
        await OnGetAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostRevokeAsync(string id)
    {
        ValidateReason(Reason);
        if (ModelState.IsValid)
        {
            try
            {
                await codes.RevokeAsync(id, Actor, Reason);
                TempData["Notice"] = "兑换码已停用。";
                return RedirectToPage();
            }
            catch (ApiException error) { ModelState.AddModelError("", error.Message); }
        }
        await OnGetAsync();
        return Page();
    }
}
