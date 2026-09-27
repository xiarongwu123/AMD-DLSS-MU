using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

public sealed class AuditModel(AppDbContext db) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    public List<AuditEntry> Items { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Search = Search?.Trim();
        if (Search?.Length > 128) Search = Search[..128];
        var query = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrEmpty(Search)) query = query.Where(entry => entry.Action.Contains(Search) || entry.UserId == Search || entry.Actor == Search);
        Items = await query.OrderByDescending(entry => entry.CreatedAt).Take(100).ToListAsync();
    }
}
