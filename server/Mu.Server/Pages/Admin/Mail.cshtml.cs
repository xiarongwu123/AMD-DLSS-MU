using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Pages.Admin;

public sealed class MailModel(IDbContextFactory<MailLogDbContext> factory, MailDeliveryMonitor monitor, TimeProvider clock) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Purpose { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public List<MailLog> Items { get; private set; } = [];
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / 50d));
    public Dictionary<string, int> Counts { get; private set; } = [];
    public long? LastCollectionAt => monitor.LastCollectionAt;
    public bool CollectorHealthy => LastCollectionAt is long at && clock.GetUtcNow().ToUnixTimeSeconds() - at < 180;
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["submitting"] = "提交中 / 结果待确认", ["submitted"] = "发信服务已接收",
        ["submission_failed"] = "提交失败 / 结果不确定", ["delivered"] = "收件服务器已接受",
        ["deferred"] = "暂缓投递", ["bounced"] = "投递失败"
    };
    public static string Label(string status) => Labels.GetValueOrDefault(status, status);
    public async Task OnGetAsync()
    {
        Search = Search?.Trim();
        if (Search?.Length > 254) Search = Search[..254];
        if (Status is not null && !Labels.ContainsKey(Status)) Status = null;
        if (Purpose is not ("register" or "reset")) Purpose = null;
        await using var db = await factory.CreateDbContextAsync();
        var since = clock.GetUtcNow().ToUnixTimeSeconds() - 86400;
        Counts = await db.MailLogs.AsNoTracking().Where(x => x.CreatedAt >= since).GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count);
        var query = db.MailLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(Search)) query = query.Where(x => x.Email.Contains(Search) || x.Id == Search || x.QueueId == Search);
        if (!string.IsNullOrEmpty(Status)) query = query.Where(x => x.Status == Status);
        if (!string.IsNullOrEmpty(Purpose)) query = query.Where(x => x.Purpose == Purpose);
        if (From is DateOnly from) { var at = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUnixTimeSeconds(); query = query.Where(x => x.CreatedAt >= at); }
        if (To is DateOnly to) { var at = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(8)).ToUnixTimeSeconds(); query = query.Where(x => x.CreatedAt <= at); }
        Total = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((PageNumber - 1) * 50).Take(50).ToListAsync();
    }
}
