using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mu.Neural;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class NeuralRulesModel(NeuralRuleStore rules) : AdminPageModel
{
    [BindProperty] public string Document { get; set; } = "";
    [BindProperty] public string Revision { get; set; } = "";
    [BindProperty] public string Reason { get; set; } = "";
    [BindProperty] public string HistoryId { get; set; } = "";
    public string[] History => rules.HistoryIds();
    public void OnGet()
    {
        var current = rules.Read(); Revision = current.Revision;
        Document = JsonSerializer.Serialize(current, NeuralRules.Json);
    }
    public async Task<IActionResult> OnPostRollbackAsync()
    {
        ValidateReason(Reason);
        if(ModelState.IsValid)try{await rules.RollbackAsync(HistoryId,Revision,Actor,Reason);TempData["Notice"]="规则已回滚为新的审计版本；历史验证时间保留。";return RedirectToPage();}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException){ModelState.AddModelError("",e.Message);}
        OnGet();return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        ValidateReason(Reason);
        if (ModelState.IsValid)
        {
            try { await rules.PublishAsync(Document, Revision, Actor, Reason); TempData["Notice"] = "兼容规则已发布，客户端下次配置时生效。"; return RedirectToPage(); }
            catch (Exception e) when (e is InvalidDataException or JsonException) { ModelState.AddModelError("", e.Message); }
        }
        return Page();
    }
}
