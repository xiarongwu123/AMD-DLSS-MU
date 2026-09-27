using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed record PaymentCheckout(string Provider, string CheckoutUrl);

public interface IPaymentProvider
{
    bool Enabled { get; }
    Task<PaymentCheckout> CreateCheckoutAsync(Order order, CancellationToken cancellationToken = default);
}

public sealed class DisabledPaymentProvider : IPaymentProvider
{
    public bool Enabled => false;
    public Task<PaymentCheckout> CreateCheckoutAsync(Order order, CancellationToken cancellationToken = default) =>
        throw new ApiException(503, "payments_unavailable", "支付尚未开放，暂时无法购买。");
}
