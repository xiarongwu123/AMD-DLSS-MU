using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class FeaturesModel(AppDbContext db, AccountManagementService management) : AdminPageModel
{
    public static string DisplayName(string key) => key switch
    {
        "library.manage" => "游戏库管理",
        "dlss.configure" => "DLSS 配置",
        "optiscaler.configure" => "OptiScaler 配置",
        "magpie.launch" => "Magpie 启动",
        "game.launch" => "游戏启动",
        "game.restore" => "游戏配置恢复",
        "configuration.edit" => "客户端与游戏配置",
        "diagnostics.use" => "诊断与独立面板",
        "app.update" => "客户端更新",
        _ => key
    };

    [BindProperty] public string Key { get; set; } = "";
    [BindProperty] public string Access { get; set; } = "standard";
    [BindProperty] public string Reason { get; set; } = "";
    public List<FeatureDefinition> Items { get; private set; } = [];
    public async Task OnGetAsync() => Items = await db.FeatureDefinitions.AsNoTracking().OrderBy(feature => feature.Key).ToListAsync();
    public async Task<IActionResult> OnPostAsync()
    {
        ValidateReason(Reason);
        if (Access is not ("standard" or "pro" or "disabled")) ModelState.AddModelError("", "无效的权限配置。");
        if (!await db.FeatureDefinitions.AnyAsync(feature => feature.Key == Key)) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                await management.SetFeatureAsync(Key, Access != "disabled", Access == "pro" ? "pro" : "standard", Reason.Trim(), Actor);
                TempData["Notice"] = "功能权限已保存。";
                return RedirectToPage();
            }
            catch (ApiException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await OnGetAsync();
        return Page();
    }
}
