using System.Net;
using System.Net.Mail;
using System.Text;

namespace Mu.Server.Services;

public sealed class SmtpVerificationEmailSender(IConfiguration config, ILogger<SmtpVerificationEmailSender> logger)
    : IVerificationEmailSender
{
    public async Task SendAsync(string email, string code, string purpose, string requestId,
        CancellationToken cancellationToken = default)
    {
        var host = config["Smtp:Host"];
        var from = config["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)
            || !int.TryParse(config["Smtp:Port"] ?? "587", out var port) || port is < 1 or > 65535)
            throw Unavailable();

        var tls = config.GetValue("Smtp:StartTls", true);
        var username = config["Smtp:Username"];
        // Cleartext submission is opt-in and only for our isolated Docker network.
        if (!tls && (!config.GetValue("Smtp:AllowPrivatePlaintext", false) || !string.IsNullOrEmpty(username)))
            throw Unavailable();

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = tls,
                UseDefaultCredentials = false,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (!string.IsNullOrEmpty(username))
                client.Credentials = new NetworkCredential(username, config["Smtp:Password"]);
            var action = purpose == "register" ? "注册账号" : "重置密码";
            using var message = new MailMessage(new MailAddress(from), new MailAddress(email))
            {
                Subject = $"MU {action}验证码",
                Body = $"您的 MU {action}验证码是 {code}，10 分钟内有效。请勿向他人透露验证码。如果并非您本人操作，请忽略此邮件。",
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false
            };
            // SMTP acceptance is queue acceptance, not proof of inbox delivery. No automatic resend on timeout.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await client.SendMailAsync(message, timeout.Token);
        }
        catch (Exception error) when (error is SmtpException or OperationCanceledException or FormatException or ArgumentException)
        {
            logger.LogWarning("SMTP verification email submission failed ({ErrorType})", error.GetType().Name);
            throw Unavailable();
        }
    }

    private static ApiException Unavailable() => new(503, "email_unavailable", "验证码暂时发送失败，请稍后重试。");
}
