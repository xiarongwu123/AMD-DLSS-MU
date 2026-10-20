using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed class ManualOrderService(AppDbContext db, IConfiguration config, TimeProvider clock)
{
    private long Now => clock.GetUtcNow().ToUnixTimeSeconds();
    public bool Enabled => config.GetValue<bool>("ManualPayment:Enabled")
        && !string.IsNullOrWhiteSpace(config["ManualPayment:Instructions"]);
    public async Task<bool> AvailableAsync(CancellationToken ct = default) => Enabled
        && await db.FeatureDefinitions.AnyAsync(x => x.Enabled && x.MinimumTier == "pro", ct)
        && await db.Plans.AnyAsync(x => x.Enabled && x.DurationDays >= 0 && x.DurationDays <= 3650
            && x.PriceMinor > 0 && x.Currency == "CNY", ct);

    public object PaymentDetails => new
    {
        method = "manual",
        instructions = config["ManualPayment:Instructions"]?.Trim(),
        qrImageUrl = config["ManualPayment:QrImageUrl"]?.Trim(),
        channels = PaymentChannels()

    };

    private static object[] PaymentChannels()
    {
        var channels = new List<object>();
        foreach (var (id, name) in new[] { ("wechat", "微信支付"), ("alipay", "支付宝") })
        {
            using var stream = typeof(ManualOrderService).Assembly.GetManifestResourceStream($"Mu.Server.Payment.{id}.jpg");
            if (stream == null) continue;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            channels.Add(new { id, name, qrImageUrl = "data:image/jpeg;base64," + Convert.ToBase64String(bytes.ToArray()) });
        }
        return channels.ToArray();
    }

    public async Task<Order> CreateAsync(string userId, string? planId, CancellationToken ct = default)
    {
        if (!await AvailableAsync(ct)) throw new ApiException(503, "payments_unavailable", "购买申请暂未开放。");
        if (string.IsNullOrWhiteSpace(planId)) throw new ApiException(400, "invalid_plan", "请选择会员套餐。");
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId && x.Enabled, ct)
            ?? throw new ApiException(404, "plan_not_found", "套餐不存在或已下架。");
        if (plan.DurationDays is < 0 or > 3650 || plan.ConfigurationSessions is < 0 or > 100 ||
            (!plan.GrantsMembership && (plan.DurationDays != 0 || plan.ConfigurationSessions < 1)) || plan.PriceMinor <= 0 || plan.Currency != "CNY")
            throw new ApiException(409, "invalid_plan", "套餐设置不完整，请联系管理员。");
        var existing = await db.Orders.AsNoTracking().Where(x => x.UserId == userId && x.PlanId == plan.Id
                && (x.Status == "pending" || x.Status == "review_requested") && x.CreatedAt > Now - 86400
                && x.AmountMinor == plan.PriceMinor && x.DurationDays == plan.DurationDays
                && x.GrantsMembership == plan.GrantsMembership && x.ConfigurationSessions == plan.ConfigurationSessions)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing != null) return existing;
        var order = new Order
        {
            UserId = userId, PlanId = plan.Id, PlanName = plan.Name, DurationDays = plan.DurationDays,
            ConfigurationSessions = plan.ConfigurationSessions,
            GrantsMembership = plan.GrantsMembership,
            AmountMinor = plan.PriceMinor, Currency = plan.Currency, CreatedAt = Now,
            Status = "pending", Provider = "manual"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task SubmitForReviewAsync(string userId, string orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId && x.UserId == userId, ct)
            ?? throw new ApiException(404, "order_not_found", "订单不存在。");
        if (order.Status == "review_requested") return;
        if (order.Status != "pending" || order.Provider != "manual")
            throw new ApiException(409, "order_not_pending", "这笔订单不能提交核对。");
        order.Status = "review_requested";
        db.AuditEntries.Add(new AuditEntry { Actor = userId, Action = "order.review_requested", UserId = userId,
            Reason = "用户申请核对人工付款", BeforeJson = JsonSerializer.Serialize(new { OrderId = order.Id, Status = "pending" }),
            AfterJson = JsonSerializer.Serialize(new { OrderId = order.Id, order.Status }), CreatedAt = Now });
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmAsync(string orderId, string? reference, string? reason, string actor, CancellationToken ct = default)
    {
        reference = reference?.Trim(); reason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(reference) || reference.Length is < 4 or > 120)
            throw new ApiException(400, "invalid_reference", "请输入收款平台的真实交易流水号（4 至 120 字）。");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length is < 3 or > 500)
            throw new ApiException(400, "invalid_reason", "请输入 3 至 500 字的核对说明。");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new ApiException(404, "order_not_found", "订单不存在。");
        if (order.Status is not ("pending" or "review_requested") || order.Provider != "manual")
            throw new ApiException(409, "order_not_pending", "订单已处理，不能重复开通。");
        if (await db.Orders.AnyAsync(x => x.Provider == "manual" && x.ProviderTransactionId == reference, ct))
            throw new ApiException(409, "reference_used", "该收款流水号已用于其他订单。");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == order.UserId, ct)
            ?? throw new ApiException(404, "user_not_found", "账户不存在。");
        var previous = user.ProExpiresAt;
        if (order.DurationDays is < 0 or > 3650 || order.ConfigurationSessions is < 0 or > 100 ||
            (!order.GrantsMembership && (order.DurationDays != 0 || order.ConfigurationSessions < 1)))
            throw new ApiException(409, "invalid_plan", "订单权益不完整，请联系管理员。");
        var expiry = !order.GrantsMembership ? previous : order.DurationDays == 0 || previous == MembershipPlans.LifetimeExpiry
            ? MembershipPlans.LifetimeExpiry
            : checked(Math.Max(previous ?? 0, Now) + order.DurationDays * 86400L);
        if (expiry > MembershipPlans.LifetimeExpiry) throw new ApiException(409, "invalid_expiry", "会员有效期超出支持范围。");
        var priorStatus = order.Status;
        order.Status = "paid";
        order.PaidAt = Now;
        order.ConfigurationServiceStatus = order.ConfigurationSessions > 0 ? "pending" : "none";
        order.ProviderTransactionId = reference;
        user.ProExpiresAt = expiry;
        db.PaymentEvents.Add(new PaymentEvent { Provider = "manual", TransactionId = reference,
            OrderId = order.Id, ReceivedAt = Now, Status = "confirmed" });
        if (order.GrantsMembership) db.MembershipChanges.Add(new MembershipChange { UserId = user.Id, PreviousExpiresAt = previous,
            NewExpiresAt = expiry!.Value, Reason = reason, Actor = actor, Source = "manual-order:" + order.Id, CreatedAt = Now });
        db.AuditEntries.Add(new AuditEntry { Actor = actor, Action = "order.confirm", UserId = user.Id,
            Reason = reason, BeforeJson = JsonSerializer.Serialize(new { Status = priorStatus, ProExpiresAt = previous }),
            AfterJson = JsonSerializer.Serialize(new { OrderId = order.Id, order.Status, Reference = reference, ProExpiresAt = expiry }),
            CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task CompleteConfigurationAsync(string orderId, string? reason, string actor, int expectedUsed = 0)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
            throw new ApiException(400, "invalid_reason", "请填写服务完成说明（3至500字）。");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId)
            ?? throw new ApiException(404, "order_not_found", "订单不存在。");
        if (order.Status != "paid" || order.ConfigurationSessionsUsed != expectedUsed || expectedUsed < 0 ||
            order.ConfigurationSessions <= expectedUsed || order.ConfigurationServiceStatus != "pending")
            throw new ApiException(409, "service_not_pending", "服务次数已更新或没有剩余指导次数，请刷新订单。");
        var used = expectedUsed + 1;
        var status = used == order.ConfigurationSessions ? "completed" : "pending";
        var changed = await db.Orders.Where(x => x.Id == orderId && x.Status == "paid" && x.ConfigurationServiceStatus == "pending"
                && x.ConfigurationSessionsUsed == expectedUsed && x.ConfigurationSessions > expectedUsed)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ConfigurationSessionsUsed, used).SetProperty(x => x.ConfigurationServiceStatus, status));
        if (changed != 1) throw new ApiException(409, "service_not_pending", "服务次数已更新，请刷新订单。");
        db.AuditEntries.Add(new AuditEntry { Actor = actor, Action = "order.configuration_completed", UserId = order.UserId,
            Reason = reason.Trim(), BeforeJson = JsonSerializer.Serialize(new { order.Id, ConfigurationSessionsUsed = expectedUsed, order.ConfigurationSessions }),
            AfterJson = JsonSerializer.Serialize(new { order.Id, ConfigurationSessionsUsed = used, ConfigurationServiceStatus = status }), CreatedAt = Now });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

}
